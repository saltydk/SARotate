namespace Linuxtesting
{
    public class GlobalConfig
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "log_file")]
        public string LogFile { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "sleeptime")]
        public string SleepTime { get; set; }
    }
}