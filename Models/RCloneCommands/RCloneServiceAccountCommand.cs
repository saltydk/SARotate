using Newtonsoft.Json;

namespace SARotate.Models.RCloneCommands
{
    public class Opt
    {
        [JsonProperty("service_account_file")]

        public string service_account_file { get; set; }
    }

    public class RCloneServiceAccountCommand
    {
        [JsonProperty("command")]
        public string command { get; set; }
        [JsonProperty("fs")]

        public string fs { get; set; }
        [JsonProperty("opt")]

        public Opt opt { get; set; }
    }
}
