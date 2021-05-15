using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SARotate.Infrastructure;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Linuxtesting
{
    class Program
    {
        public static IConfiguration _configuration;

        static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                Console.WriteLine("SARotate stopped");
                Log.Information("SARotate stopped");
                cts.Cancel();
            };

            using var host = CreateHostBuilder(args).Build();

            Log.Information("SARotate started");

            await host.RunAsync(cts.Token);
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            var cwd = Directory.GetCurrentDirectory();

            // Build configuration
            _configuration = new ConfigurationBuilder()
                .SetBasePath(cwd)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddCommandLine(args)
                .Build();

            string configAbsolutePath = _configuration["config"] ?? cwd + "/config.yaml";

            SARotateConfig saRotateConfig = SARotateConfig.ParseSARotateYamlConfig(configAbsolutePath);

            return Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());

                    string logPath = _configuration["Serilog:WriteTo:0:Args:configure:1:Args:path"] ?? cwd + "/log.log";
                    string minimumLogLevelConfig = _configuration["Serilog:WriteTo:0:Args:configure:1:Args:restrictedToMinimumLevel"] ?? "Verbose";
                    string rollingIntervalConfig = _configuration["Serilog:WriteTo:0:Args:configure:1:Args:rollingInterval"] ?? "Day";
                    int fileSizeLimitBytes = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:1:Args:fileSizeLimitBytes"] ?? "5000000");
                    bool rollOnFileSizeLimit = bool.Parse(_configuration["Serilog:WriteTo:0:Args:configure:1:Args:rollOnFileSizeLimit"] ?? "true");
                    int retainedFileCountLimit = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:1:Args:retainedFileCountLimit"] ?? "5");

                    LogEventLevel minimumLogEventLevel = ConvertMinimumLogLevelConfigToLogEventLevel(minimumLogLevelConfig);
                    RollingInterval rollingInterval = ConvertRollingIntervalConfigValueToEnum(rollingIntervalConfig);                   

                    var logger = new LoggerConfiguration()
                      .Enrich.FromLogContext()
                      .Enrich.WithProperty("Application", "SARotate")
                      .Enrich.With<GenericLogEnricher>()
                      .WriteTo.File(logPath,
                        restrictedToMinimumLevel: minimumLogEventLevel,
                        fileSizeLimitBytes: fileSizeLimitBytes,
                        rollingInterval: rollingInterval,
                        retainedFileCountLimit: retainedFileCountLimit)
                      .CreateLogger();

                    Log.Logger = logger;
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<SARotate>();
                    services.AddSingleton(saRotateConfig);
                    services.AddSingleton(_configuration);
                })
                .UseSerilog();
        }

        private static RollingInterval ConvertRollingIntervalConfigValueToEnum(string rollingInterval)
        {
            switch (rollingInterval.ToLower())
            {
                case "infinite":
                    return RollingInterval.Infinite;
                case "year":
                    return RollingInterval.Year;
                case "month":
                    return RollingInterval.Month;
                case "day":
                    return RollingInterval.Day;
                case "hour":
                    return RollingInterval.Hour;
                case "minute":
                    return RollingInterval.Minute;
                default:
                    return RollingInterval.Day;
            }
        }

        private static LogEventLevel ConvertMinimumLogLevelConfigToLogEventLevel(string minimumLogLevel)
        {
            switch (minimumLogLevel.ToLower())
            {
                case "verbose":
                    return LogEventLevel.Verbose;
                case "debug":
                    return LogEventLevel.Debug;
                case "information":
                    return LogEventLevel.Information;
                case "error":
                    return LogEventLevel.Error;
                case "fatal":
                    return LogEventLevel.Fatal;
                default:
                    return LogEventLevel.Information;
            }
        }
    }
}
