using Newtonsoft.Json;

namespace Linuxtesting.Models.Google
{
    internal class ServiceAccount
    {
        [JsonProperty("project_id")]
        public string ProjectId { get; set; }
        [JsonProperty("client_email")]
        public string ClientEmail { get; set; }
        [JsonIgnore]
        public string FilePath { get; set; }
    }
}
