using System.Collections.Generic;

namespace Linuxtesting
{
    public class SARotateConfig
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "rclone")]
        public RCloneConfig RCloneConfig { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "global")]
        public GlobalConfig GlobalConfig { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "main")]
        ///svcAcctGroup absolute path -> remote -> connection info 
        public Dictionary<string, Dictionary<string, string>> MainConfig { get; set; }
    }
}