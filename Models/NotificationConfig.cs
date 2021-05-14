using System.Collections.Generic;

namespace Linuxtesting
{
    public class NotificationConfig
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "errors_only")]
        public bool AppriseNotificationsErrorsOnly { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "apprise")]
        public List<string> AppriseServices { get; set; }        
    }
}