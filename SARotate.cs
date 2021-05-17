using Linuxtesting.Models;
using Linuxtesting.Models.Google;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = Linuxtesting.Models.LogLevel;

namespace Linuxtesting
{
    public class SARotate : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SARotate> _logger;
        private readonly SARotateConfig _SARotateConfig;

        public SARotate(IConfiguration configuration, ILogger<SARotate> logger, SARotateConfig SARotateConfig)
        {
            _configuration = configuration;
            _SARotateConfig = SARotateConfig;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            SARotateConfig yamlConfigContent = _SARotateConfig;

            try
            {
                (Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, string rCloneCommand) = await InitializeRCloneCommand(yamlConfigContent);

                await RunSwappingService(yamlConfigContent, serviceAccountUsageOrderByGroup, rCloneCommand, cancellationToken);
            }
            catch (Exception e)
            {
                await SendAppriseNotification(yamlConfigContent, e.Message, LogLevel.Error);
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<(Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, string rCloneCommand)> InitializeRCloneCommand(SARotateConfig yamlConfigContent)
        {
            Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup = await GenerateServiceAccountUsageOrderByGroup(yamlConfigContent);

            string rcloneCommand = "rclone rc";

            bool rcloneConfigUserExists = !string.IsNullOrEmpty(yamlConfigContent.RCloneConfig.User) && !string.IsNullOrEmpty(yamlConfigContent.RCloneConfig.Pass);
            if (rcloneConfigUserExists)
            {
                rcloneCommand += $" --rc-user={yamlConfigContent.RCloneConfig.User} --rc-pass={yamlConfigContent.RCloneConfig.Pass}";
            }

            return (serviceAccountUsageOrderByGroup, rcloneCommand);
        }

        private async Task<Dictionary<string, List<ServiceAccount>>> GenerateServiceAccountUsageOrderByGroup(SARotateConfig yamlConfigContent)
        {
            var serviceAccountUsageOrderByGroup = new Dictionary<string, List<ServiceAccount>>();

            foreach (var serviceAccountFolder in yamlConfigContent.MainConfig)
            {
                string serviceAccountsDirectoryAbsolutePath = serviceAccountFolder.Key;

                List<ServiceAccount> svcacctsJsons = (await ParseSvcAccts(serviceAccountsDirectoryAbsolutePath))
               .OrderBy(c => c.ClientEmail)
               .ToList();

                List<ServiceAccount> svcAcctsUsageOrder = OrderServiceAccountsForUsage(svcacctsJsons);

                foreach (string remote in yamlConfigContent.MainConfig[serviceAccountFolder.Key].Keys)
                {
                    string previousServiceAccountUsed = await FindPreviousServiceAccountUsedForRemote(remote);

                    if (!string.IsNullOrEmpty(previousServiceAccountUsed))
                    {
                        ServiceAccount? serviceAccount = svcAcctsUsageOrder.FirstOrDefault(sa => sa.FilePath.Contains(previousServiceAccountUsed));

                        if (serviceAccount != null)
                        {
                            svcAcctsUsageOrder.Remove(serviceAccount);
                            svcAcctsUsageOrder.Add(serviceAccount);
                        }
                    }
                }

                serviceAccountUsageOrderByGroup.Add(serviceAccountFolder.Key, svcAcctsUsageOrder);
            }

            return serviceAccountUsageOrderByGroup;
        }

        private async Task<string> FindPreviousServiceAccountUsedForRemote(string remote)
        {
            var bashResult = await $"rclone config show {remote}:".Bash();

            if (bashResult.exitCode != (int)ExitCode.Success)
            {
                throw new Exception(bashResult.result);
            }

            var lines = bashResult.result.Split("\n");

            var svcAccountLine = lines.FirstOrDefault(l => l.Contains("service_account_file"));

            if (string.IsNullOrEmpty(svcAccountLine))
            {
                return "";
            }
            else
            {
                var serviceAccount = svcAccountLine.Split("/").LastOrDefault();

                if (string.IsNullOrEmpty(serviceAccount))
                {
                    return "";
                }

                return serviceAccount;
            }
        }

        private List<ServiceAccount> OrderServiceAccountsForUsage(List<ServiceAccount> svcacctsJsons)
        {
            var svcAcctsUsageOrder = new List<ServiceAccount>();
            var serviceAccountsByProject = svcacctsJsons
                            .GroupBy(c => c.ProjectId)
                            .ToList();

            var largestNumberOfServiceAccounts = GetMaxNumberOfServiceAccountsForProject(serviceAccountsByProject);

            for (int i = 0; i < largestNumberOfServiceAccounts; i++)
            {
                foreach (var projectWithServiceAccounts in serviceAccountsByProject)
                {
                    if (projectWithServiceAccounts.ElementAtOrDefault(i) is ServiceAccount account)
                    {
                        svcAcctsUsageOrder.Add(account);
                    }
                }
            }

            return svcAcctsUsageOrder;
        }

        private int GetMaxNumberOfServiceAccountsForProject(IEnumerable<IGrouping<string, ServiceAccount>> serviceAccountsByProject)
        {
            IEnumerable<(IGrouping<string, ServiceAccount> project, int noServiceAccounts)> counts = serviceAccountsByProject
                .Select(project => (project, project.Count()));

            int largestNoServiceAccounts = counts.Max(x => x.noServiceAccounts);
            IEnumerable<string> projectsWithLessServiceAccounts = counts.Where(c => c.noServiceAccounts < largestNoServiceAccounts).Select(a => a.project.Key);
            if (projectsWithLessServiceAccounts.Any())
            {
                var projectsWithMostServiceAccounts = counts
                    .Where(c => c.noServiceAccounts == largestNoServiceAccounts)
                    .Select(a => a.project.Key);
                var logMessage = "amount of service accounts in projects {projects1} is lower than projects {projects2}";
                LogMessage(logMessage, LogLevel.Warning, projectsWithLessServiceAccounts, projectsWithMostServiceAccounts);
            }

            return largestNoServiceAccounts;
        }

        private async Task<List<ServiceAccount>> ParseSvcAccts(string serviceAccountDirectory)
        {
            var accountCollections = new List<ServiceAccount>();

            IEnumerable<string> fileNames = Directory.EnumerateFiles(serviceAccountDirectory, "*", new EnumerationOptions() { RecurseSubdirectories = true });

            foreach (string fileName in fileNames)
            {
                if (fileName.ToLower().EndsWith(".json"))
                {
                    using (var streamReader = new StreamReader(fileName))
                    {
                        string fileJson = await streamReader.ReadToEndAsync();

                        ServiceAccount account = JsonConvert.DeserializeObject<ServiceAccount>(fileJson) ?? throw new ArgumentException("service account file structure is bad");
                        account.FilePath = fileName;

                        accountCollections.Add(account);
                    }
                }
            }

            return accountCollections;
        }

        private async Task RunSwappingService(
            SARotateConfig yamlConfigContent, 
            Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, 
            string rCloneCommand, 
            CancellationToken cancellationToken)
        {
            bool swapServiceAccounts = true;
            while(swapServiceAccounts)
            {
                swapServiceAccounts &= !cancellationToken.IsCancellationRequested;

                foreach (KeyValuePair<string, List<ServiceAccount>> serviceAccountGroup in serviceAccountUsageOrderByGroup)
                {
                    if (!swapServiceAccounts)
                    {
                        return;
                    }

                    var remoteConfig = yamlConfigContent.MainConfig[serviceAccountGroup.Key];

                    foreach (var remote in remoteConfig.Keys)
                    {
                        var addressForRemote = remoteConfig.Values.First(); //only 1 value, but need dictionary to parse yaml
                        var nextServiceAccount = serviceAccountGroup.Value.First();

                        var rcCommandAddressParameter = $" --rc-addr={addressForRemote}";
                        var rcCommandBackendCommandParameter = $" backend/command command=set fs=\"{remote}:\": -o service_account_file=\"{nextServiceAccount.FilePath}\"";

                        string commandForCurrentServiceAccountGroupRemote = rCloneCommand + rcCommandAddressParameter + rcCommandBackendCommandParameter;

                        var bashResult = await commandForCurrentServiceAccountGroupRemote.Bash();

                        if (bashResult.exitCode != (int)ExitCode.Success)
                        {
                            await SendAppriseNotification(yamlConfigContent, $"Could not swap service account for remote {remote}", LogLevel.Error);
                        }
                        else
                        {
                            serviceAccountGroup.Value.Remove(nextServiceAccount);
                            serviceAccountGroup.Value.Add(nextServiceAccount);

                            var stdoutputJson = bashResult
                            .result
                            .Split("STDOUT:")
                            .Last();

                            var rcloneCommandResult = JsonConvert.DeserializeObject<RCloneRCCommandResult>(stdoutputJson) ?? throw new ArgumentException("rclone output bad format");
                            await LogRCloneServiceAccountSwapResult(yamlConfigContent, remote, stdoutputJson, rcloneCommandResult);
                        }
                    }                    
                }

                var timeoutMilliSeconds = yamlConfigContent.GlobalConfig.SleepTime * 1000;

                try
                {
                    await Task.Delay(timeoutMilliSeconds, cancellationToken);
                }
                catch
                {

                }
            }
        }

        private async Task LogRCloneServiceAccountSwapResult(SARotateConfig yamlConfigContent, string remote, string stdoutputJson, RCloneRCCommandResult rcloneCommandResult)
        {
            var currentFile = rcloneCommandResult.Result.ServiceAccountFile.Current.Split("/").LastOrDefault();
            var previousFile = rcloneCommandResult.Result.ServiceAccountFile.Previous.Split("/").LastOrDefault();

            var logMessage = $"Switching remote {remote} from service account {previousFile} to {currentFile} for {yamlConfigContent.GlobalConfig.SleepTime} seconds";
            LogMessage(logMessage, LogLevel.Information);
            LogMessage(stdoutputJson, LogLevel.Debug);
            await SendAppriseNotification(yamlConfigContent, logMessage);
        }

        private async Task SendAppriseNotification(SARotateConfig yamlConfigContent, string logMessage, LogLevel logLevel = LogLevel.Information)
        {
            if (yamlConfigContent.NotificationConfig.AppriseNotificationsErrorsOnly && logLevel < LogLevel.Error)
            {
                LogMessage($"Information log not sent via apprise notification due to errors_only notifications: {logMessage}", logLevel);
            }
            else if (yamlConfigContent.NotificationConfig.AppriseServices.Any(svc => !string.IsNullOrWhiteSpace(svc)))
            {
                string appriseCommand = $"apprise -vv ";
                appriseCommand += logLevel >= LogLevel.Error ? "-t 'ERROR!!!' " : "";
                var escapedLogMessage = logMessage.Replace("'", "´").Replace("\"", "´");
                appriseCommand += $"-b '{escapedLogMessage}' ";

                foreach (var appriseService in yamlConfigContent.NotificationConfig.AppriseServices.Where(svc => !string.IsNullOrWhiteSpace(svc)))
                {
                    appriseCommand += $"'{appriseService}' ";
                }

                await appriseCommand.Bash();

                LogMessage($"sent apprise notification: {logMessage}", logLevel);
            }
        }

        private void LogMessage(string message, LogLevel level = LogLevel.Debug, params object[] args)
        {
            Console.WriteLine(level.ToString() + " " + message);
            switch (level)
            {
                case LogLevel.Debug:
                    _logger.LogDebug(message, args);
                    break;
                case LogLevel.Information:
                    _logger.LogInformation(message, args);
                    break;
                case LogLevel.Warning:
                    _logger.LogWarning(message, args);
                    break;
                case LogLevel.Error:
                    _logger.LogError(message, args);
                    break;
                case LogLevel.Critical:
                    _logger.LogCritical(message, args);
                    break;
            }
        }
    }
}
