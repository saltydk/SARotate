using Linuxtesting.Models.Google;
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
    public static class SARotate
    {
        public static async Task DoWork(string[] args)
        {
            string configAbsolutePath = args.Skip(1).FirstOrDefault();

#if DEBUG
            configAbsolutePath ??= "/home/shadowspy/config2.yaml";
#endif

            SARotateConfig yamlConfigContent = await ParseSARotateYamlConfig(configAbsolutePath);
            var serviceAccountUsageOrderByGroup = new Dictionary<string, (List<ServiceAccount> svcAcctsUsageOrder, int indexForCurrent)>();

            foreach (var serviceAccountFolder in yamlConfigContent.MainConfig)
            {
                string serviceAccountsDirectoryAbsolutePath = serviceAccountFolder.Key;
#if DEBUG
                serviceAccountsDirectoryAbsolutePath = "/home/shadowspy/svcaccts";
#endif

                List<ServiceAccount> svcacctsJsons = (await ParseSvcAccts(serviceAccountsDirectoryAbsolutePath))
               .OrderBy(c => c.ClientEmail)
               .ToList();

                List<ServiceAccount> svcAcctsUsageOrder = OrderServiceAccountsForUsage(svcacctsJsons);

                int indexForServiceAccountToUse;

                foreach (var remote in yamlConfigContent.MainConfig[serviceAccountFolder.Key].Keys)
                {
                    var bashResult = await $"rclone config show {remote}:".Bash();

                    var lines = bashResult.Split("\n");

                    var svcAccountLine = lines.FirstOrDefault(l => l.Contains("service_account_file"));

                    if (string.IsNullOrEmpty(svcAccountLine))
                    {
                        indexForServiceAccountToUse = 0;
                    }
                    else
                    {
                        var serviceAccount = svcAccountLine.Split("/").LastOrDefault();

                        if (string.IsNullOrEmpty(serviceAccount))
                        {
                            indexForServiceAccountToUse = 0;
                        }
                        else
                        {
                            var indexOfServiceAccount = svcAcctsUsageOrder.FindIndex(sa => sa.FilePath.Contains(serviceAccount));

                            var indexToUseForCurrentRemote = "???";
                        }

                    }






                }

                serviceAccountUsageOrderByGroup.Add(serviceAccountFolder.Key, (svcAcctsUsageOrder, 0));


            }
        }

        private static List<ServiceAccount> OrderServiceAccountsForUsage(List<ServiceAccount> svcacctsJsons)
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

        private static int GetMaxNumberOfServiceAccountsForProject(IEnumerable<IGrouping<string, ServiceAccount>> serviceAccountsByProject)
        {
            IEnumerable<(IGrouping<string, ServiceAccount> project, int noServiceAccounts)> counts = serviceAccountsByProject
                .Select(project => (project, project.Count()));

            int largestNoServiceAccounts = counts.Max(x => x.noServiceAccounts);
            if (counts.Where(c => c.noServiceAccounts < largestNoServiceAccounts) is var asd)
            {

                var maxProjects = counts.Where(c => c.noServiceAccounts == largestNoServiceAccounts);
                Log.Warning("amount of service accounts in projects {projectsMin} is lower than projects {projectsMax}", asd.Select(a => a.project.Key), maxProjects.Select(a => a.project.Key));
            }

            return largestNoServiceAccounts;
        }

        private static async Task<SARotateConfig> ParseSARotateYamlConfig(string configAbsolutePath)
        {
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

        private static async Task<List<ServiceAccount>> ParseSvcAccts(string serviceAccountDirectory)
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

                        ServiceAccount account = JsonConvert.DeserializeObject<ServiceAccount>(fileJson);
                        account.FilePath = fileName;

                        accountCollections.Add(account);
                    }
                }
            }

            return accountCollections;
        }
    }
}
