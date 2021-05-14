using Linuxtesting.Models;
using Linuxtesting.Models.Google;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Linuxtesting
{
    class Program
    {
        static async Task Main(string[] args)
        {
            SetupStaticLogger();

            string configAbsolutePath = args.Skip(1).FirstOrDefault();

            SARotateConfig yamlConfigContent = await ParseSARotateYamlConfig(configAbsolutePath);

            (Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, string rCloneCommand) = await SARotate.InitializeRCloneCommand(yamlConfigContent);

            foreach (KeyValuePair<string, List<ServiceAccount>> serviceAccountGroup in serviceAccountUsageOrderByGroup)
            {
                bool swapRemoteServiceAccount = true;
                if (swapRemoteServiceAccount)
                {

                    var remoteConfig = yamlConfigContent.MainConfig[serviceAccountGroup.Key];
                    var addressForRemote = remoteConfig.Values.First();
                    var remote = remoteConfig.Keys.First();
                    var nextServiceAccount = serviceAccountGroup.Value.First();

                    serviceAccountGroup.Value.Remove(nextServiceAccount);
                    serviceAccountGroup.Value.Add(nextServiceAccount);

                    var rcCommandAddressParameter = $" --rc-addr={addressForRemote}";
                    var rcCommandBackendCommandParameter = $" backend/command command=set fs=\"{remote}:\": -o service_account_file=\"{nextServiceAccount.FilePath}\"";

                    string commandForCurrentServiceAccountGroupRemote = rCloneCommand + rcCommandAddressParameter + rcCommandBackendCommandParameter;

                    var stdoutputJson = (await commandForCurrentServiceAccountGroupRemote.Bash())
                        .Split("STDOUT:")
                        .Last();

                    var rcloneCommandResult = JsonConvert.DeserializeObject<RCloneRCCommandResult>(stdoutputJson);
                    var currentFile = rcloneCommandResult.Result.ServiceAccountFile.Current.Split("/").LastOrDefault();
                    var previousFile = rcloneCommandResult.Result.ServiceAccountFile.Previous.Split("/").LastOrDefault();


                    var logMessage = $"Switching remote {remote} from service account {previousFile} to {currentFile} for 5m\n";
                    Console.WriteLine(logMessage);
                    Log.Information(logMessage);
                    Log.Debug("\n" + stdoutputJson);
                }                
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
