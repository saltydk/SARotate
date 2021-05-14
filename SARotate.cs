using Linuxtesting.Models.Google;
using Newtonsoft.Json;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Linuxtesting
{
    public static class SARotate
    {
        public static async Task<(Dictionary<string, List<ServiceAccount>> serviceAccountUsageOrderByGroup, string rCloneCommand)> InitializeRCloneCommand(SARotateConfig yamlConfigContent)
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

        private static async Task<Dictionary<string, List<ServiceAccount>>> GenerateServiceAccountUsageOrderByGroup(SARotateConfig yamlConfigContent)
        {
            var serviceAccountUsageOrderByGroup = new Dictionary<string, List<ServiceAccount>>();

            foreach (var serviceAccountFolder in yamlConfigContent.MainConfig)
            {
                string serviceAccountsDirectoryAbsolutePath = serviceAccountFolder.Key;

                List<ServiceAccount> svcacctsJsons = (await ParseSvcAccts(serviceAccountsDirectoryAbsolutePath))
               .OrderBy(c => c.ClientEmail)
               .ToList();

                List<ServiceAccount> svcAcctsUsageOrder = OrderServiceAccountsForUsage(svcacctsJsons);

                foreach (var remote in yamlConfigContent.MainConfig[serviceAccountFolder.Key].Keys)
                {
                    int remoteIndexForServiceAccountToUse = await FindPreviousServiceAccountUsedForRemote(svcAcctsUsageOrder, remote);

                    if (remoteIndexForServiceAccountToUse != -1)
                    {
                        var serviceAccountPreviouslyUsed = svcAcctsUsageOrder.ElementAt(remoteIndexForServiceAccountToUse);
                        svcAcctsUsageOrder.Remove(serviceAccountPreviouslyUsed);
                        svcAcctsUsageOrder.Add(serviceAccountPreviouslyUsed);
                    }
                }

                serviceAccountUsageOrderByGroup.Add(serviceAccountFolder.Key, svcAcctsUsageOrder);
            }

            return serviceAccountUsageOrderByGroup;
        }

        private static async Task<int> FindPreviousServiceAccountUsedForRemote(List<ServiceAccount> svcAcctsUsageOrder, string remote)
        {
            var bashResult = await $"rclone config show {remote}:".Bash();

            var lines = bashResult.Split("\n");

            var svcAccountLine = lines.FirstOrDefault(l => l.Contains("service_account_file"));

            if (string.IsNullOrEmpty(svcAccountLine))
            {
                return 0;
            }
            else
            {
                var serviceAccount = svcAccountLine.Split("/").LastOrDefault();

                if (string.IsNullOrEmpty(serviceAccount))
                {
                    return 0;
                }

                int indexOfServiceAccount = svcAcctsUsageOrder.FindIndex(sa => sa.FilePath.Contains(serviceAccount));

                return indexOfServiceAccount == -1 ? 0 : indexOfServiceAccount;
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

        private static async Task<List<ServiceAccount>> ParseSvcAccts(string serviceAccountDirectory)
        {
#if DEBUG
            serviceAccountDirectory = "/home/shadowspy/svcaccts";
#endif
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
