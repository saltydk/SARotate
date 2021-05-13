namespace Linuxtesting
{
    public class RCloneConfig
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "rclone_config")]
        public string ConfigAbsolutePath { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "rc_user")]
        public string User { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "rc_pass")]
        public string Pass { get; set; }
    }
}