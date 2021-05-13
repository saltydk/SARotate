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

            await SARotate.DoWork(args);
        }

        private static void SetupStaticLogger()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
        }
    }
}
