using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SARotate.Models
{
    public class SARotateConfig
    {
        [YamlMember(Alias = "rclone")]
        public RCloneConfig RCloneConfig { get; set; }
        [YamlMember(Alias = "global")]
        public GlobalConfig GlobalConfig { get; set; }
        [YamlMember(Alias = "main")]
        ///svcAcctGroup absolute path -> remote -> connection info 
        public Dictionary<string, Dictionary<string, string>> MainConfig { get; set; }
        [YamlMember(Alias = "notification")]
        public NotificationConfig NotificationConfig { get; set; }

        public static SARotateConfig? ParseSARotateYamlConfig(string configAbsolutePath)
        {
            if (string.IsNullOrEmpty(configAbsolutePath))
            {
                Console.WriteLine("configAbsolutePath missing as argument");
                throw new ArgumentException("Config file path missing");
            }

            if (File.Exists(configAbsolutePath))
            {
                using (var streamReader = new StreamReader(configAbsolutePath))
                {
                    string fileContent = streamReader.ReadToEnd();

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
            }
            else
            {
                Console.WriteLine($"Could not access config file at '{configAbsolutePath}'.");
                return null;
            }
        }
    }
}