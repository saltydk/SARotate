using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SARotate.Infrastructure;
using SARotate.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SARotate
{
    class Program
    {
        public static IConfiguration _configuration;

        public class Options
        {
            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }
            [Option('c', "config", Required = false, HelpText = "Set path to config.")]
            public string? Config { get; set; }
            [Option('l', "logfile", Required = false, HelpText = "Set path for log file.")]
            public string? LogFile { get; set; }
        }

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
            string cwd = Directory.GetCurrentDirectory();

            // Build configuration
            _configuration = new ConfigurationBuilder()
                .SetBasePath(cwd)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddCommandLine(args)
                .Build();

            (string? configAbsolutePath, string? logFilePath, bool verboseFlagExists) = ParseArguments(args);

            SARotateConfig? saRotateConfig = SARotateConfig.ParseSARotateYamlConfig(configAbsolutePath ?? cwd + "/config.yaml");

            if (saRotateConfig == null)
            {
                Environment.Exit(-1);
            }

            Logger logger = CreateLogger(cwd, logFilePath, verboseFlagExists);

            return Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<SARotate>();
                    services.AddSingleton(saRotateConfig);
                    services.AddSingleton(_configuration);
                })
                .UseSerilog(logger);
        }

        private static (string? configAbsolutePath, string? logFilePath, bool verboseFlagExists) ParseArguments(string[] args)
        {
            bool verboseFlagExists = false;
            string? configAbsolutePath = null;
            string? logFilePath = null;

            Parser.Default
                .ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    configAbsolutePath = o.Config;
                    logFilePath = o.LogFile;
                    verboseFlagExists = o.Verbose;
                    Console.WriteLine("config flag is " + o.Config);
                    Console.WriteLine("verbose flag is " + o.Verbose);
                })
                .WithNotParsed(o =>
                {
                    foreach (Error error in o)
                    {
                        Console.WriteLine("argument parsing error: " + error);
                    }

                    Console.WriteLine("Passed in unknown flag, exiting.");
                    Environment.Exit(-1);
                });

            return (configAbsolutePath, logFilePath, verboseFlagExists);
        }

        private static Logger CreateLogger(string cwd, string? logFilePath, bool verboseFlagExists)
        {
            string logPath = logFilePath ?? _configuration["Serilog:WriteTo:0:Args:configure:1:Args:path"] ?? cwd + "/sarotate.log";
            string minimumLogLevelConfig = verboseFlagExists ? "Verbose" : _configuration["Serilog:WriteTo:0:Args:configure:1:Args:restrictedToMinimumLevel"] ?? "Information";
            string rollingIntervalConfig = _configuration["Serilog:WriteTo:0:Args:configure:1:Args:rollingInterval"] ?? "Day";
            int fileSizeLimitBytes = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:1:Args:fileSizeLimitBytes"] ?? "5000000");
            int retainedFileCountLimit = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:1:Args:retainedFileCountLimit"] ?? "5");

            LogEventLevel minimumLogEventLevel = ConvertMinimumLogLevelConfigToLogEventLevel(minimumLogLevelConfig);
            RollingInterval rollingInterval = ConvertRollingIntervalConfigValueToEnum(rollingIntervalConfig);

            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "SARotate")
                .Enrich.With<GenericLogEnricher>()
                .MinimumLevel.ControlledBy(new LoggingLevelSwitch(minimumLogEventLevel))
                .WriteTo.File(logPath,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    rollingInterval: rollingInterval,
                    retainedFileCountLimit: retainedFileCountLimit)
                .CreateLogger();

            Log.Logger = logger;

            return logger;
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
