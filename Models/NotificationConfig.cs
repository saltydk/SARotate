using System.Collections.Generic;

namespace SARotate.Models
{
    public class NotificationConfig
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "errors_only")]
        public bool AppriseNotificationsErrorsOnly { get; set; } = true;
        [YamlDotNet.Serialization.YamlMember(Alias = "apprise")]
        public List<string> AppriseServices { get; set; } = new List<string>();
        [YamlDotNet.Serialization.YamlMember(Alias = "include_hostname")]
        public bool AppriseNotificationsIncludeHostname { get; set; } = false;
    }
}