using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SARotate.Models;
using SARotate.Models.Enums;
using SARotate.Models.Google;
using LogLevel = SARotate.Models.Enums.LogLevel;

namespace SARotate
{
    // ReSharper disable once InconsistentNaming
    public class SARotate : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SARotate> _logger;
        // ReSharper disable once InconsistentNaming
        private readonly SARotateConfig _SARotateConfig;

        public SARotate(IConfiguration configuration, ILogger<SARotate> logger, SARotateConfig SARotateConfig)
        {
            _configuration = configuration;
            _SARotateConfig = SARotateConfig;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                Dictionary<string, List<ServiceAccount>>? serviceAccountUsageOrderByGroup = await GenerateServiceAccountUsageOrderByGroup(_SARotateConfig);

                if (serviceAccountUsageOrderByGroup == null)
                {
                    throw new ArgumentException("Service accounts not found");
                }

                string rCloneCommand = InitializeRCloneCommand(_SARotateConfig);

                await RunSwappingService(_SARotateConfig, serviceAccountUsageOrderByGroup, rCloneCommand, cancellationToken);
            }
            catch (Exception e)
            {
                await SendAppriseNotification(_SARotateConfig, e.Message, LogLevel.Error);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public static string InitializeRCloneCommand(SARotateConfig yamlConfigContent)
        {
            string rcloneCommand = "rclone rc";

            bool rcloneConfigUserExists = !string.IsNullOrEmpty(yamlConfigContent.RCloneConfig.User) && !string.IsNullOrEmpty(yamlConfigContent.RCloneConfig.Pass);
            if (rcloneConfigUserExists)
            {
                rcloneCommand += $" --rc-user={yamlConfigContent.RCloneConfig.User} --rc-pass={yamlConfigContent.RCloneConfig.Pass}";
            }

            bool rcloneConfigOverriden = !string.IsNullOrEmpty(yamlConfigContent.RCloneConfig.ConfigAbsolutePath);
            if (rcloneConfigOverriden)
            {
                rcloneCommand += $" --config={yamlConfigContent.RCloneConfig.ConfigAbsolutePath}";
            }

            return rcloneCommand;
        }

        private async Task<Dictionary<string, List<ServiceAccount>>?> GenerateServiceAccountUsageOrderByGroup(SARotateConfig yamlConfigContent)
        {
            var serviceAccountUsageOrderByGroup = new Dictionary<string, List<ServiceAccount>>();

            foreach ((string serviceAccountsDirectoryAbsolutePath, Dictionary<string, string> remotes) in yamlConfigContent.RemoteConfig)
            {
                List<ServiceAccount>? svcAccts = await ParseSvcAccts(serviceAccountsDirectoryAbsolutePath);

                if (svcAccts == null || !svcAccts.Any())
                {
                    return null;
                }

                List<ServiceAccount> svcAcctsUsageOrder = OrderServiceAccountsForUsage(svcAccts.OrderBy(c => c.ClientEmail).ToList());

                foreach (string remote in remotes.Keys)
                {
                    string previousServiceAccountUsed = await FindPreviousServiceAccountUsedForRemote(remote);

                    if (string.IsNullOrEmpty(previousServiceAccountUsed))
                    {
                        continue;
                    }

                    ServiceAccount? serviceAccount = svcAcctsUsageOrder.FirstOrDefault(sa => sa.FilePath.Contains(previousServiceAccountUsed));

                    if (serviceAccount == null)
                    {
                        continue;
                    }

                    svcAcctsUsageOrder.Remove(serviceAccount);
                    svcAcctsUsageOrder.Add(serviceAccount);
                }

                serviceAccountUsageOrderByGroup.Add(serviceAccountsDirectoryAbsolutePath, svcAcctsUsageOrder);
            }

            return serviceAccountUsageOrderByGroup;
        }

        private async Task<string> FindPreviousServiceAccountUsedForRemote(string remote)
        {
            (string result, int exitCode) = await $"rclone config show {remote}:".Bash();

            LogMessage(result);

            if (exitCode != (int)ExitCode.Success)
            {
                throw new Exception(result);
            }

            string[] lines = result.Split("\n");

            string? svcAccountLine = lines.FirstOrDefault(l => l.Contains("service_account_file"));

            if (string.IsNullOrEmpty(svcAccountLine))
            {
                return "";
            }

            string? serviceAccount = svcAccountLine.Split("/").LastOrDefault();

            return string.IsNullOrEmpty(serviceAccount) ? "" : serviceAccount;
        }

        private List<ServiceAccount> OrderServiceAccountsForUsage(IEnumerable<ServiceAccount> svcaccts)
        {
            var svcAcctsUsageOrder = new List<ServiceAccount>();
            List<IGrouping<string, ServiceAccount>>? serviceAccountsByProject = svcaccts
                            .GroupBy(c => c.ProjectId)
                            .ToList();

            int largestNumberOfServiceAccounts = GetMaxNumberOfServiceAccountsForProject(serviceAccountsByProject);

            for (var i = 0; i < largestNumberOfServiceAccounts; i++)
            {
                foreach (var projectWithServiceAccounts in serviceAccountsByProject)
                {
                    ServiceAccount? account = projectWithServiceAccounts.ElementAtOrDefault(i);
                    if (account != null)
                    {
                        svcAcctsUsageOrder.Add(account);
                    }
                }
            }

            return svcAcctsUsageOrder;
        }

        private int GetMaxNumberOfServiceAccountsForProject(IEnumerable<IGrouping<string, ServiceAccount>> serviceAccountsByProject)
        {
            List<(IGrouping<string, ServiceAccount> project, int noServiceAccounts)> counts = serviceAccountsByProject
                .Select(project => (project, project.Count()))
                .ToList();

            int largestNoServiceAccounts = counts.Max(x => x.noServiceAccounts);
            IEnumerable<string> projectsWithLessServiceAccounts = counts.Where(c => c.noServiceAccounts < largestNoServiceAccounts).Select(a => a.project.Key);
            
            if (projectsWithLessServiceAccounts.Any())
            {
                IEnumerable<string> projectsWithMostServiceAccounts = counts
                    .Where(c => c.noServiceAccounts == largestNoServiceAccounts)
                    .Select(a => a.project.Key);
                const string logMessage = "amount of service accounts in projects {projects1} is lower than projects {projects2}";
                LogMessage(logMessage, LogLevel.Warning, projectsWithLessServiceAccounts, projectsWithMostServiceAccounts);
            }

            return largestNoServiceAccounts;
        }

        private static async Task<List<ServiceAccount>?> ParseSvcAccts(string serviceAccountDirectory)
        {
            var accountCollections = new List<ServiceAccount>();

            if (!Directory.Exists(serviceAccountDirectory))
            {
                return null;
            }

            IEnumerable<string> fileNames = Directory.EnumerateFiles(serviceAccountDirectory, "*", new EnumerationOptions() { RecurseSubdirectories = true });

            foreach (string fileName in fileNames)
            {
                if (!fileName.ToLower().EndsWith(".json"))
                {
                    continue;
                }

                using (var streamReader = new StreamReader(fileName))
                {
                    string fileJson = await streamReader.ReadToEndAsync();

                    ServiceAccount account = JsonConvert.DeserializeObject<ServiceAccount>(fileJson) ?? throw new ArgumentException("service account file structure is bad");
                    account.FilePath = fileName;

                    accountCollections.Add(account);
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
            var swapServiceAccounts = true;
            while (swapServiceAccounts)
            {
                swapServiceAccounts &= !cancellationToken.IsCancellationRequested;

                foreach ((string key, List<ServiceAccount> value) in serviceAccountUsageOrderByGroup)
                {
                    if (!swapServiceAccounts)
                    {
                        return;
                    }

                    Dictionary<string, string> remoteConfig = yamlConfigContent.RemoteConfig[key];

                    foreach (var remote in remoteConfig.Keys)
                    {
                        string addressForRemote = remoteConfig.Values.First(); //only 1 value, but need dictionary to parse yaml
                        ServiceAccount nextServiceAccount = value.First();

                        var rcCommandAddressParameter = $" --rc-addr={addressForRemote}";
                        var rcCommandBackendCommandParameter = $" backend/command command=set fs=\"{remote}:\": -o service_account_file=\"{nextServiceAccount.FilePath}\"";

                        string commandForCurrentServiceAccountGroupRemote = rCloneCommand + rcCommandAddressParameter + rcCommandBackendCommandParameter;

                        (string result, int exitCode) = await commandForCurrentServiceAccountGroupRemote.Bash();

                        LogMessage($"rclone stdout/err: {result}");

                        if (exitCode != (int)ExitCode.Success)
                        {
                            await SendAppriseNotification(yamlConfigContent, $"Could not swap service account for remote {remote}", LogLevel.Error);
                        }
                        else
                        {
                            value.Remove(nextServiceAccount);
                            value.Add(nextServiceAccount);

                            string stdoutputJson = result
                            .Split("STDOUT:")
                            .Last();

                            RCloneRCCommandResult rCloneCommandResult = JsonConvert.DeserializeObject<RCloneRCCommandResult>(stdoutputJson) ?? throw new ArgumentException("rclone output bad format");
                            await LogRCloneServiceAccountSwapResult(yamlConfigContent, remote, stdoutputJson, rCloneCommandResult);
                        }
                    }
                }

                int timeoutMs = yamlConfigContent.RCloneConfig.SleepTime * 1000;

                try
                {
                    await Task.Delay(timeoutMs, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    //added to catch exception from ctrl+c program cancellation
                }
            }
        }

        private async Task LogRCloneServiceAccountSwapResult(SARotateConfig yamlConfigContent, string remote, string stdoutputJson, RCloneRCCommandResult rcloneCommandResult)
        {
            string? currentFile = rcloneCommandResult.Result.ServiceAccountFile.Current.Split("/").LastOrDefault();
            string? previousFile = rcloneCommandResult.Result.ServiceAccountFile.Previous.Split("/").LastOrDefault();

            string logMessage = $"Switching remote {remote} from service account {previousFile} to {currentFile} for {yamlConfigContent.RCloneConfig.SleepTime} seconds";
            LogMessage(logMessage, LogLevel.Information);
            LogMessage(stdoutputJson);
            await SendAppriseNotification(yamlConfigContent, logMessage);
        }

        private async Task SendAppriseNotification(SARotateConfig yamlConfigContent, string logMessage, LogLevel logLevel = LogLevel.Debug)
        {
            if (yamlConfigContent.NotificationConfig.AppriseNotificationsErrorsOnly && logLevel < LogLevel.Error)
            {
                LogMessage($"apprise notification not sent due to errors_only notifications: {logMessage}", logLevel);
            }
            else if (yamlConfigContent.NotificationConfig.AppriseServices.Any(svc => !string.IsNullOrWhiteSpace(svc)))
            {
                string appriseCommand = $"apprise -vv ";
                string escapedLogMessage = logMessage.Replace("'", "´").Replace("\"", "´");
                appriseCommand += $"-b '{escapedLogMessage}' ";

                foreach (var appriseService in yamlConfigContent.NotificationConfig.AppriseServices.Where(svc => !string.IsNullOrWhiteSpace(svc)))
                {
                    appriseCommand += $"'{appriseService}' ";
                }

                (string result, int exitCode) = await appriseCommand.Bash();

                if (exitCode != (int)ExitCode.Success)
                {
                    LogMessage($"Unable to send apprise notification: {logMessage}", LogLevel.Error);
                    LogMessage($"Apprise failure: {result}");
                }
                else
                {
                    LogMessage($"sent apprise notification: {logMessage}");
                }
            }
        }

        private void LogMessage(string message, LogLevel level = LogLevel.Debug, params object[] args)
        {
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
    }
}
