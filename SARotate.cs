using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SARotate.Models;
using SARotate.Models.Enums;
using SARotate.Models.Google;
using SARotate.Models.RCloneCommands;
using LogLevel = SARotate.Models.Enums.LogLevel;

namespace SARotate
{
    // ReSharper disable once InconsistentNaming
    public class SARotate : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SARotate> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        // ReSharper disable once InconsistentNaming
        private readonly SARotateConfig _SARotateConfig;
        private readonly IHttpClientFactory _httpClientFactory;


        private static int _minimumMajorVersion = 1;
        private static int _minimumMinorVersion = 55;
        private static int _minimumPatchVersion = 0;
        private static string _minimumVersionString = "v" + _minimumMajorVersion + "." + _minimumMinorVersion + "." + _minimumPatchVersion;

        public SARotate(IConfiguration configuration, ILogger<SARotate> logger, CancellationTokenSource cancellationTokenSource, 
            SARotateConfig SARotateConfig, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _SARotateConfig = SARotateConfig;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _cancellationTokenSource = cancellationTokenSource;
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


                await RunSwappingService(_SARotateConfig, serviceAccountUsageOrderByGroup, cancellationToken);
            }
            catch (Exception e)
            {
                await SendAppriseNotification(_SARotateConfig, e.Message, LogLevel.Error);

                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    LogMessage($"Fatal error, shutting down. Error: {e.Message}", LogLevel.Critical);
                    _cancellationTokenSource.Cancel();
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            return Task.CompletedTask;
        }

        private async Task<Dictionary<string, List<ServiceAccount>>?> GenerateServiceAccountUsageOrderByGroup(SARotateConfig yamlConfigContent)
        {
            var serviceAccountUsageOrderByGroup = new Dictionary<string, List<ServiceAccount>>();

            foreach ((string serviceAccountsDirectoryAbsolutePath, Dictionary<string, RemoteInfo> remotes) in yamlConfigContent.RemoteConfig)
            {

                foreach (string remote in remotes.Keys)
                {
                    string? remoteRcloneHost = remotes[remote].Address;

                    if (!remoteRcloneHost.ToLower().Contains("http"))
                    {
                        remoteRcloneHost = "http://" + remoteRcloneHost;
                    }

                    var remoteVersionUri = new Uri($"{remoteRcloneHost}/core/version");
                    bool validRcloneVersion = await CheckValidRcloneVersion(remoteVersionUri, remote, remotes[remote]);

                    if (!validRcloneVersion)
                    {
                        LogMessage("Ignoring remote: " + remote);
                        LogMessage("Rclone versions below " + _minimumVersionString + " are unsupported.");
                        remotes.Remove(remote);
                    }

                }

                List<ServiceAccount>? svcAccts = await ParseSvcAccts(serviceAccountsDirectoryAbsolutePath);

                if (svcAccts == null || !svcAccts.Any())
                {
                    return null;
                }

                List<ServiceAccount> svcAcctsUsageOrder = OrderServiceAccountsForUsage(svcAccts);
                ServiceAccount? earliestSvcAcctUsed = null;
                ServiceAccount? latestSvcAcctUsed = null;

                foreach (string remote in remotes.Keys)
                {
                    string? previousServiceAccountUsed = await FindPreviousServiceAccountUsedForRemote(remote, remotes[remote]);

                    if (string.IsNullOrEmpty(previousServiceAccountUsed))
                    {
                        LogMessage("unable to find previous service account used for remote " + remote);
                        continue;
                    }

                    ServiceAccount? serviceAccount = svcAcctsUsageOrder.FirstOrDefault(sa => sa.FileName == previousServiceAccountUsed);

                    if (serviceAccount == null)
                    {
                        LogMessage("unable to find local file " + previousServiceAccountUsed);
                        LogMessage("group accounts " + string.Join(",", svcAcctsUsageOrder.Select(sa => sa.FilePath)));
                        continue;
                    }

                    if (earliestSvcAcctUsed == null || latestSvcAcctUsed == null)
                    {
                        earliestSvcAcctUsed = serviceAccount;
                        latestSvcAcctUsed = serviceAccount;
                    }
                    else
                    {
                        int indexOfSvcAcct = svcAcctsUsageOrder.IndexOf(serviceAccount);
                        int indexOfCurrentEarliestSvcAcct = svcAcctsUsageOrder.IndexOf(earliestSvcAcctUsed);
                        int indexOfCurrentLatestSvcAcct = svcAcctsUsageOrder.IndexOf(latestSvcAcctUsed);

                        if (indexOfSvcAcct < indexOfCurrentEarliestSvcAcct)
                        {
                            earliestSvcAcctUsed = serviceAccount;
                        }

                        if (indexOfCurrentLatestSvcAcct < indexOfSvcAcct)
                        {
                            latestSvcAcctUsed = serviceAccount;
                        }
                    }
                }

                if (earliestSvcAcctUsed != null && latestSvcAcctUsed != null)
                {
                    int indexOfEarliestSvcAcct = svcAcctsUsageOrder.IndexOf(earliestSvcAcctUsed);
                    int indexOfLatestSvcAcct = svcAcctsUsageOrder.IndexOf(latestSvcAcctUsed);

                    bool serviceAccountListLooped = remotes.Keys.Count < indexOfLatestSvcAcct - indexOfEarliestSvcAcct;

                    var svcAcctsToReEnqueue = new List<ServiceAccount>();

                    int indexOfCutoffForReEnqueue = serviceAccountListLooped ? indexOfEarliestSvcAcct + 1 : indexOfLatestSvcAcct + 1;

                    List<ServiceAccount>? accountsToRemove = svcAcctsUsageOrder.GetRange(0, indexOfCutoffForReEnqueue);

                    svcAcctsToReEnqueue.AddRange(accountsToRemove);

                    svcAcctsUsageOrder.RemoveRange(0, indexOfCutoffForReEnqueue);
                    svcAcctsUsageOrder.AddRange(svcAcctsToReEnqueue);
                }                

                serviceAccountUsageOrderByGroup.Add(serviceAccountsDirectoryAbsolutePath, svcAcctsUsageOrder);
            }

            return serviceAccountUsageOrderByGroup;
        }

        private async Task<bool> CheckValidRcloneVersion(Uri rcloneVersionEndpoint, string remote, RemoteInfo remoteInfo)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, rcloneVersionEndpoint);

            if (!string.IsNullOrEmpty(remoteInfo.User) && !string.IsNullOrEmpty(remoteInfo.Pass))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(remoteInfo.User + ":" + remoteInfo.Pass)));
                LogMessage("Adding user and password to RClone api request");
            }

            HttpClient client = _httpClientFactory.CreateClient();

            HttpResponseMessage response = await client.SendAsync(request);

            string resultContent = await response.Content.ReadAsStringAsync();

            LogMessage("resultFromVersion:::: " + resultContent);

            dynamic? versionResponse = JsonConvert.DeserializeObject(resultContent);
            dynamic? decomposed = versionResponse?.decomposed;
            int majorVersion = decomposed != null ? decomposed[0] : -1;
            int minorVersion = decomposed != null ? decomposed[1] : -1;
            int patchVersion = decomposed != null ? decomposed[2] : -1;

            LogMessage($"Version from RClone endpoint of remote {remote} is {majorVersion + "." + minorVersion + "." + patchVersion}", LogLevel.Information);

            return majorVersion == _minimumMajorVersion && minorVersion >= _minimumMinorVersion && patchVersion >= _minimumPatchVersion;
        }

        private async Task<string?> FindPreviousServiceAccountUsedForRemote(string remote, RemoteInfo remoteInfo)
        {
            string rcloneApiUri = remoteInfo.Address;
            rcloneApiUri += rcloneApiUri.EndsWith("/") ? "backend/command" : "/backend/command";

            var request = new HttpRequestMessage(HttpMethod.Post, rcloneApiUri);

            if (!string.IsNullOrEmpty(remoteInfo.User) && !string.IsNullOrEmpty(remoteInfo.Pass))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(remoteInfo.User + ":" + remoteInfo.Pass)));
                LogMessage("Adding user and password to RClone api request");
            }

            var command = new RCloneServiceAccountCommand
            {
                command = "get",
                fs = remote+":",
                opt = new Opt
                {
                    service_account_file = ""
                }
            };

            request.Content = new StringContent(JsonConvert.SerializeObject(command));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add("Accept", "*/*");

            HttpClient client = _httpClientFactory.CreateClient();

            HttpResponseMessage response = await client.SendAsync(request);

            dynamic? resultContent = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());

            string? serviceAccountFile = resultContent?.result?.service_account_file;

            LogMessage("serviceaccountfile - " + resultContent);

            if (string.IsNullOrEmpty(serviceAccountFile))
            {
                LogMessage("could not find service_account_file line");
                return null;
            }

            string? serviceAccount = serviceAccountFile.Split("/").LastOrDefault();

            if (!string.IsNullOrEmpty(serviceAccount))
            {
                LogMessage(serviceAccount);

                return serviceAccount;
            }

            LogMessage("could not find service_account_file SA name");
            return null;
        }

        private List<ServiceAccount> OrderServiceAccountsForUsage(IEnumerable<ServiceAccount> svcaccts)
        {
            var svcAcctsUsageOrder = new List<ServiceAccount>();
            List<IGrouping<string, ServiceAccount>> serviceAccountsByProject = svcaccts
                .OrderBy(c => c.ClientEmail)
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

        private async Task<List<ServiceAccount>?> ParseSvcAccts(string serviceAccountDirectory)
        {
            var accountCollections = new List<ServiceAccount>();

            if (!Directory.Exists(serviceAccountDirectory))
            {
                return null;
            }

            IEnumerable<string> filePaths = Directory.EnumerateFiles(serviceAccountDirectory, "*", new EnumerationOptions() { RecurseSubdirectories = true });

            foreach (string filePath in filePaths)
            {
                if (!filePath.ToLower().EndsWith(".json"))
                {
                    continue;
                }

                try
                {
                    using var streamReader = new StreamReader(filePath);

                    string fileJson = await streamReader.ReadToEndAsync();

                    ServiceAccount account = JsonConvert.DeserializeObject<ServiceAccount>(fileJson) ?? throw new ArgumentException("service account file structure is bad");
                    account.FilePath = filePath;

                    accountCollections.Add(account);

                }
                catch (Exception)
                {
                    const string logMessage = "service account json file {filePath} is invalid";
                    LogMessage(logMessage, LogLevel.Error, filePath);
                }                
            }

            return accountCollections;
        }

        private async Task RunSwappingService(
            SARotateConfig yamlConfigContent,
            Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup,
            CancellationToken cancellationToken)
        {
            var swapServiceAccounts = true;
            while (swapServiceAccounts)
            {
                swapServiceAccounts &= !cancellationToken.IsCancellationRequested;

                foreach ((string serviceAccountGroupAbsolutePath, List<ServiceAccount> serviceAccountsForGroup) in serviceAccountUsageOrderByGroup)
                {
                    if (!swapServiceAccounts)
                    {
                        return;
                    }

                    Dictionary<string, RemoteInfo> remoteConfig = yamlConfigContent.RemoteConfig[serviceAccountGroupAbsolutePath];

                    foreach (string remote in remoteConfig.Keys)
                    {
                        ServiceAccount nextServiceAccount = serviceAccountsForGroup.First();
                        string rcloneApiUri = remoteConfig[remote].Address;

                        if (!rcloneApiUri.ToLower().Contains("http"))
                        {
                            rcloneApiUri = "http://" + rcloneApiUri;
                        }

                        rcloneApiUri += rcloneApiUri.EndsWith("/") ? "backend/command" : "/backend/command";

                        var request = new HttpRequestMessage(HttpMethod.Post, rcloneApiUri);

                        if (!string.IsNullOrEmpty(remoteConfig[remote].User) && !string.IsNullOrEmpty(remoteConfig[remote].Pass))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(remoteConfig[remote].User + ":" + remoteConfig[remote].Pass)));
                            LogMessage("Adding user and password to RClone api request");
                        }

                        var command = new RCloneServiceAccountCommand
                        {
                            command = "set",
                            fs = remote + ":",
                            opt = new Opt
                            {
                                service_account_file = nextServiceAccount.FilePath ?? throw new ArgumentNullException()
                            }
                        };

                        request.Content = new StringContent(JsonConvert.SerializeObject(command));
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                        request.Headers.Add("Accept", "*/*");

                        HttpClient client = _httpClientFactory.CreateClient();

                        HttpResponseMessage response = await client.SendAsync(request);

                        dynamic? resultContent = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());

                        string? currentAccountFile = resultContent?.result?.service_account_file.current;
                        string? previousAccountFile = resultContent?.result?.service_account_file.previous;

                        LogMessage("serviceaccountfile - " + resultContent);

                        if (string.IsNullOrEmpty(currentAccountFile))
                        {
                            await SendAppriseNotification(yamlConfigContent, $"Could not swap service account for remote {remote}", LogLevel.Error);
                        }
                        else
                        {
                            serviceAccountsForGroup.Remove(nextServiceAccount);
                            serviceAccountsForGroup.Add(nextServiceAccount);

                            await LogRCloneServiceAccountSwapResult(yamlConfigContent, remote, Convert.ToString(resultContent), previousAccountFile, currentAccountFile);
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

        private async Task LogRCloneServiceAccountSwapResult(
            SARotateConfig yamlConfigContent, 
            string remote, 
            string responseMessage, 
            string previousServiceAccount, 
            string currentServiceAccount)
        {
            string? currentFile = currentServiceAccount.Split("/").LastOrDefault();
            string? previousFile = previousServiceAccount.Split("/").LastOrDefault();

            string logMessage = $"Switching remote {remote} from service account {previousFile} to {currentFile} for {yamlConfigContent.RCloneConfig.SleepTime} seconds";
            LogMessage(logMessage, LogLevel.Information);
            LogMessage(responseMessage);
            await SendAppriseNotification(yamlConfigContent, logMessage);
        }

        private async Task SendAppriseNotification(SARotateConfig yamlConfigContent, string logMessage, LogLevel logLevel = LogLevel.Debug)
        {
            if (yamlConfigContent.NotificationConfig.AppriseNotificationsErrorsOnly && yamlConfigContent.NotificationConfig.AppriseServices.Any() && logLevel < LogLevel.Error)
            {
                LogMessage($"apprise notification not sent due to errors_only notifications: {logMessage}", logLevel);
            }
            else if (yamlConfigContent.NotificationConfig.AppriseServices.Any(svc => !string.IsNullOrWhiteSpace(svc)))
            {
                string appriseCommand = $"apprise -vv ";
                string escapedLogMessage = logMessage.Replace("'", "´").Replace("\"", "´");
                string hostName = yamlConfigContent.NotificationConfig.AppriseNotificationsIncludeHostname ? $" on { System.Net.Dns.GetHostName()}" : "";

                appriseCommand += $"-b '{escapedLogMessage}{hostName}' ";

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
