using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SARotate.Infrastructure;
using SARotate.Models;
using Serilog;
using Serilog.Events;

namespace SARotate
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

            SARotateConfig? saRotateConfig = SARotateConfig.ParseSARotateYamlConfig(configAbsolutePath);

            if (saRotateConfig == null)
            {
                Environment.Exit(-1);
            }

            return Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());

                    string logPath = _configuration["Serilog:WriteTo:0:Args:configure:1:Args:path"] ?? cwd + "/sarotate.log";
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
            return rollingInterval.ToLower() switch
            {
                "infinite" => RollingInterval.Infinite,
                "year" => RollingInterval.Year,
                "month" => RollingInterval.Month,
                "day" => RollingInterval.Day,
                "hour" => RollingInterval.Hour,
                "minute" => RollingInterval.Minute,
                _ => RollingInterval.Day
            };
        }

        private static LogEventLevel ConvertMinimumLogLevelConfigToLogEventLevel(string minimumLogLevel)
        {
            return minimumLogLevel.ToLower() switch
            {
                "verbose" => LogEventLevel.Verbose,
                "debug" => LogEventLevel.Debug,
                "information" => LogEventLevel.Information,
                "error" => LogEventLevel.Error,
                "fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information,
            };
        }
    }
}
