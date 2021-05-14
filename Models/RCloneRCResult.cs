using Newtonsoft.Json;

namespace Linuxtesting.Models
{
    public class RCloneRCResult
    {
        [JsonProperty("service_account_file")]
        public RCloneRCResultAccount ServiceAccountFile { get; set; }
    }
}
