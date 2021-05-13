using Microsoft.Extensions.Configuration;
using Serilog;
using System.Threading.Tasks;

namespace Linuxtesting
{
    class Program
    {
        static async Task Main(string[] args)
        {
            SetupStaticLogger();
            //WriteLogs();

            await SARotate.DoWork(args);
        }

        //public static void WriteLogs()
        //{
        //    var simpleData = "This is a string.";

        //    // Use the static Serilog.Log class for logging.
        //    Log.Verbose("Here's a Verbose message.");
        //    Log.Debug("Here's a Debug message. Only Public Properties (not fields) are shown on structured data. Structured data: {@sampleData}. Simple data: {simpleData}.", "123", simpleData);
        //    Log.Information(new Exception("Exceptions can be put on all log levels"), "Here's an Info message.");
        //    Log.Warning("Here's a Warning message.");
        //    Log.Error(new Exception("This is an exception."), "Here's an Error message.");
        //    Log.Fatal("Here's a Fatal message.");
        //}

        private static void SetupStaticLogger()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            Log.Information("SARotate vSickAssRotater9000 started");
        }
    }
}
