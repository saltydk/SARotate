using System.Linq;
using Newtonsoft.Json;

namespace SARotate.Models
{
    public class RemoteInfo
    {
        private string _address;

        [YamlDotNet.Serialization.YamlMember(Alias = "address")]
        public string Address 
        {
            get => _address; 
            set
            {
                if (!value.ToLower().Contains("http"))
                {
                    _address = "http://" + value;
                }
                else
                {
                    _address = value;
                }
            }
        }
        [YamlDotNet.Serialization.YamlMember(Alias = "user")]
        public string User { get; set; } = "";
        [YamlDotNet.Serialization.YamlMember(Alias = "pass")]
        public string Pass { get; set; } = "";
    }
}
