using Linuxtesting.Models;
using Linuxtesting.Models.Google;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Web;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Linuxtesting
{
    class Program
    {
        static async Task Main(string[] args)
        {
            SetupStaticLogger();

            string configAbsolutePath = args.Skip(1).FirstOrDefault() ?? Directory.GetCurrentDirectory()+"/config.yaml";

            SARotateConfig yamlConfigContent = await ParseSARotateYamlConfig(configAbsolutePath);

            try
            {
                (Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, string rCloneCommand) = await SARotate.InitializeRCloneCommand(yamlConfigContent);

                await RunSwappingService(yamlConfigContent, serviceAccountUsageOrderByGroup, rCloneCommand);
            }
            catch (Exception e)
            {
                await SendAppriseNotification(yamlConfigContent, e.Message, true);
                throw;
            }
        }

        private static async Task RunSwappingService(SARotateConfig yamlConfigContent, Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, string rCloneCommand)
        {
            bool swapServiceAccounts = true;
            while (swapServiceAccounts)
            {
                foreach (KeyValuePair<string, List<ServiceAccount>> serviceAccountGroup in serviceAccountUsageOrderByGroup)
                {
                    var remoteConfig = yamlConfigContent.MainConfig[serviceAccountGroup.Key];
                    var remote = remoteConfig.Keys.First(); //only 1 value, but need dictionary to parse yaml
                    var addressForRemote = remoteConfig.Values.First(); //only 1 value, but need dictionary to parse yaml
                    var nextServiceAccount = serviceAccountGroup.Value.First();

                    var rcCommandAddressParameter = $" --rc-addr={addressForRemote}";
                    var rcCommandBackendCommandParameter = $" backend/command command=set fs=\"{remote}:\": -o service_account_file=\"{nextServiceAccount.FilePath}\"";

                    string commandForCurrentServiceAccountGroupRemote = rCloneCommand + rcCommandAddressParameter + rcCommandBackendCommandParameter;

                    var bashResult = await commandForCurrentServiceAccountGroupRemote.Bash();

                    if (bashResult.exitCode != (int)ExitCode.Success)
                    {
                        await SendAppriseNotification(yamlConfigContent, $"Could not swap service account for remote {remote}", true);
                    }
                    else
                    {
                        serviceAccountGroup.Value.Remove(nextServiceAccount);
                        serviceAccountGroup.Value.Add(nextServiceAccount);

                        var stdoutputJson = bashResult
                        .result
                        .Split("STDOUT:")
                        .Last();

                        var rcloneCommandResult = JsonConvert.DeserializeObject<RCloneRCCommandResult>(stdoutputJson);
                        await LogRCloneServiceAccountSwapResult(yamlConfigContent, remote, stdoutputJson, rcloneCommandResult);
                    }
                }

                var timeoutMilliSeconds = yamlConfigContent.GlobalConfig.SleepTime * 1000;
                await Task.Delay(timeoutMilliSeconds);
            }
        }

        private static async Task LogRCloneServiceAccountSwapResult(SARotateConfig yamlConfigContent, string remote, string stdoutputJson, RCloneRCCommandResult rcloneCommandResult)
        {
            var currentFile = rcloneCommandResult.Result.ServiceAccountFile.Current.Split("/").LastOrDefault();
            var previousFile = rcloneCommandResult.Result.ServiceAccountFile.Previous.Split("/").LastOrDefault();

            var logMessage = $"Switching remote {remote} from service account {previousFile} to {currentFile} for {yamlConfigContent.GlobalConfig.SleepTime} seconds\n";
            LogInformation(logMessage, stdoutputJson);
            await SendAppriseNotification(yamlConfigContent, logMessage);
        }

        private static async Task SendAppriseNotification(SARotateConfig yamlConfigContent, string logMessage, bool error = false)
        {
            if (yamlConfigContent.NotificationConfig.AppriseServices.Any())
            {
                string appriseCommand = $"apprise -vv ";
                appriseCommand += error ? "-t 'ERROR!!!' " : "";
                var escapedLogMessage = logMessage.Replace("'", "´").Replace("\"", "´");
                appriseCommand += $"-b '{escapedLogMessage}' ";

                foreach (var appriseService in yamlConfigContent.NotificationConfig.AppriseServices)
                {
                    appriseCommand += $"'{appriseService}' ";
                }

                await appriseCommand.Bash();

                LogInformation($"sent apprise notification: {logMessage}");
            }
        }

        private static void LogInformation(string logMessage, string debugMessage = null)
        {
            Console.WriteLine(logMessage);
            Log.Information(logMessage);
            if (!string.IsNullOrEmpty(debugMessage))
            {
                Log.Debug("\n" + debugMessage);
            }
        }

        private static async Task<SARotateConfig> ParseSARotateYamlConfig(string configAbsolutePath)
        {
#if DEBUG
            configAbsolutePath ??= "/home/shadowspy/config2.yaml";
#endif
            if (string.IsNullOrEmpty(configAbsolutePath))
            {
                Console.WriteLine("configAbsolutePath missing as argument");
                throw new ArgumentException("Config file not found");
            }

            using (var streamReader = new StreamReader(configAbsolutePath))
            {
                if (File.Exists(configAbsolutePath))
                {
                    string fileContent = await streamReader.ReadToEndAsync();

                    try
                    {
                        IDeserializer deserializer = new DeserializerBuilder()
                            .WithNamingConvention(UnderscoredNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();

                        return deserializer.Deserialize<SARotateConfig>(fileContent);
                    }
                    catch (Exception)
                    {
                        throw new ArgumentException("Config file invalid format");
                    }
                }
                else
                {
                    Console.WriteLine($"Config file {configAbsolutePath} does not exist");
                    throw new ArgumentException("Config file not found");
                }
            }
        }

        private static void SetupStaticLogger()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            Log.Information("SARotate vSickAssRotater9000 started");
        }
    }
}
