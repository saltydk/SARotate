using System;
using System.Collections.Generic;
using System.IO;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SARotate.Models
{
    public class SARotateConfig
    {
        [YamlMember(Alias = "rclone")]
        public RCloneConfig RCloneConfig { get; set; }
        
        [YamlMember(Alias = "remotes")]
        public Dictionary<string, Dictionary<string, RemoteInfo>> RemoteConfig { get; set; }

        /// <summary>
        /// svcAcctGroup absolute path -> remote -> connection info 
        /// </summary>
        [YamlMember(Alias = "notification")] public NotificationConfig NotificationConfig { get; set; }

        // ReSharper disable once InconsistentNaming
        public static SARotateConfig? ParseSARotateYamlConfig(string configAbsolutePath)
        {
            if (!File.Exists(configAbsolutePath))
            {
                Console.WriteLine($"Could not access config file at '{configAbsolutePath}'.");
                return null;
            }

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
                    Console.WriteLine("Config file invalid format. Check https://github.com/saltydk/SARotate/blob/main/README.md");
                    return null;
                }
            }
        }
    }
}