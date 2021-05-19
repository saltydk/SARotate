using Newtonsoft.Json;

namespace SARotate.Models
{
    public class RCloneRCResult
    {
        [JsonProperty("service_account_file")]
        public RCloneRCResultAccount ServiceAccountFile { get; set; }
    }
}
