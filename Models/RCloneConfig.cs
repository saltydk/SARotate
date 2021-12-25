namespace SARotate.Models
{
    public class RCloneConfig
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "sleeptime")]
        public int SleepTime { get; set; } = 5;
    }
}