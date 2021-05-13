using Linuxtesting.Models.Google;
using Newtonsoft.Json;
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
            string serviceAccountsDirectoryAbsolutePath = "/home/shadowspy/svcaccts";
            string configAbsolutePath = args.Skip(1).FirstOrDefault();

#if DEBUG
            configAbsolutePath ??= "/home/shadowspy/config2.yaml";
#endif

            SARotateConfig yamlConfigContent = await ParseYamlConfig(configAbsolutePath);

            List<ServiceAccount> svcacctsJsons = await ParseSvcAccts(serviceAccountsDirectoryAbsolutePath);

            var res = await $"id -u".Bash();

            Console.WriteLine(res);
        }

        private static async Task<SARotateConfig?> ParseYamlConfig(string configAbsolutePath)
        {
            if (string.IsNullOrEmpty(configAbsolutePath))
            {
                Console.WriteLine("configAbsolutePath missing as argument");
                throw new ArgumentException("Config file not found");
            }

            IDeserializer deserializer = new DeserializerBuilder()
               .WithNamingConvention(UnderscoredNamingConvention.Instance)
               .IgnoreUnmatchedProperties()
               .Build();

            using (var streamReader = new StreamReader(configAbsolutePath))
            {
                if (File.Exists(configAbsolutePath))
                {
                    string fileContent = await streamReader.ReadToEndAsync();

                    try
                    {
                        var rotateConfig = deserializer.Deserialize<SARotateConfig>(fileContent);

                        return rotateConfig;
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
