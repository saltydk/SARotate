using Serilog.Core;
using Serilog.Events;
using System;

namespace SARotate.Infrastructure
{
    public class GenericLogEnricher : ILogEventEnricher
    {
        public const string OSVersionPropertyName = "OSVersion";
        public const string BaseDirectoryPropertyName = "BaseDirectory";
        public const string MachineNamePropertyName = "MachineName";

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException("logEvent");
            }

            AddProperty(logEvent, OSVersionPropertyName, GetOSVersion());
            AddProperty(logEvent, MachineNamePropertyName, GetMachineName());
            AddProperty(logEvent, BaseDirectoryPropertyName, GetBaseDirectory());
        }

        private void AddProperty(LogEvent logEvent, string propertyName, string value)
        {
            var property = new LogEventProperty(propertyName, new ScalarValue(value));
            logEvent.AddPropertyIfAbsent(property);
        }

        public string GetOSVersion()
        {
            return Environment.OSVersion.ToString();
        }

        public string GetMachineName()
        {
            return Environment.MachineName;
        }

        public string GetBaseDirectory()
        {
            try
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                return string.Empty;

            }
        }
    }
}
