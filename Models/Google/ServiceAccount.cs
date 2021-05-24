using System.Linq;
using Newtonsoft.Json;

namespace SARotate.Models.Google
{
    public class ServiceAccount
    {
        [JsonProperty("project_id")]
        public string ProjectId { get; set; }
        [JsonProperty("client_email")]
        public string ClientEmail { get; set; }
        [JsonIgnore]
        public string? FilePath { get; set; }
        [JsonIgnore]
        public string? FileName => FilePath?.Split("/").Last();
    }
}
