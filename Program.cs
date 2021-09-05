using System;
using System.Collections.Generic;
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
using SARotate.Models.Enums;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SARotate
{
    internal class Program
    {
        private static IConfiguration _configuration;

        public class Options
        {
            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }
            [Option('c', "config", Required = false, HelpText = "Set path to config.")]
            public string? Config { get; set; }
            [Option('l', "logfile", Required = false, HelpText = "Set path for log file.")]
            public string? LogFile { get; set; }
        }

        private static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                Log.Information("SARotate stopped");
                Log.CloseAndFlush();

                cts.Cancel();
            };

            bool validRcloneVersion = await CheckValidRcloneVersion();
            if (!validRcloneVersion)
            {
                Console.WriteLine("Rclone versions below v1.55 are unsupported");
                cts.Cancel();
            }

            using IHost host = CreateHostBuilder(args, cts).Build();

            Log.Information("SARotate started");

            await host.RunAsync(cts.Token);
            Log.CloseAndFlush();
        }

        private static IHostBuilder CreateHostBuilder(string[] args, CancellationTokenSource cts)
        {
            string cwd = Directory.GetCurrentDirectory();

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
                    services.AddSingleton(cts);
                })
                .UseSerilog(logger);
        }

        private static async Task<bool> CheckValidRcloneVersion()
        {
            (string result, int exitCode) = await "rclone version".Bash();

            if (exitCode != (int)ExitCode.Success)
            {
                throw new Exception(result);
            }

            string[] lines = result.Split("\n");

            string? versionLine = lines.FirstOrDefault(l => l.Contains("rclone v"));

            if (string.IsNullOrEmpty(versionLine))
            {
                Console.WriteLine("could not find rclone version line");
                return false;
            }
            int indexOfV = versionLine.IndexOf("v");

            string[]? version = versionLine.Substring(indexOfV+1).Split(".");

            bool majorValid = int.TryParse(version.First(), out int majorVersion);
            bool minorValid = int.TryParse(version.Skip(1).First(), out int minorVersion);
            bool patchValid = int.TryParse(version.Skip(2).First(), out int patchVersion);

            Console.WriteLine("version is " + majorVersion + "." + minorVersion + "." + patchVersion);

            return majorVersion == 1 && minorVersion >= 55 && patchVersion >= 0;
        }

        private static (string? configAbsolutePath, string? logFilePath, bool verboseFlagExists) ParseArguments(string[] args)
        {
            var verboseFlagExists = false;
            string? configAbsolutePath = null;
            string? logFilePath = null;

            Parser.Default
                .ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    configAbsolutePath = o.Config;
                    logFilePath = o.LogFile;
                    verboseFlagExists = o.Verbose;
                })
                .WithNotParsed(errs =>
                {
                    List<Error> errors = errs.ToList();

                    if (!errors.Any(err => err.Tag is ErrorType.HelpRequestedError or ErrorType.VersionRequestedError))
                    {
                        foreach (Error error in errors)
                        {
                            Console.WriteLine("argument parsing error: " + error);
                        }

                        Console.WriteLine("Passed in unknown flag, exiting.");
                    }

                    Environment.Exit(-1);
                });

            return (configAbsolutePath, logFilePath, verboseFlagExists);
        }

        private static Logger CreateLogger(string cwd, string? logFilePath, bool verboseFlagExists)
        {
            string logPath = logFilePath ?? _configuration["Serilog:WriteTo:0:Args:configure:0:Args:path"] ?? cwd + "/sarotate.log";
            string minimumLogLevelConfig = verboseFlagExists ? "Verbose" : _configuration["Serilog:WriteTo:0:Args:configure:0:Args:restrictedToMinimumLevel"] ?? "Information";
            string rollingIntervalConfig = _configuration["Serilog:WriteTo:0:Args:configure:0:Args:rollingInterval"] ?? "Day";
            int fileSizeLimitBytes = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:0:Args:fileSizeLimitBytes"] ?? "5000000");
            int retainedFileCountLimit = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:0:Args:retainedFileCountLimit"] ?? "5");

            LogEventLevel minimumLogEventLevel = ConvertMinimumLogLevelConfigToLogEventLevel(minimumLogLevelConfig);
            RollingInterval rollingInterval = ConvertRollingIntervalConfigValueToEnum(rollingIntervalConfig);

            Logger logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "SARotate")
                .Enrich.With<GenericLogEnricher>()
                .MinimumLevel.ControlledBy(new LoggingLevelSwitch(minimumLogEventLevel))
                .WriteTo.Async(a => a.File(logPath,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    rollingInterval: rollingInterval,
                    retainedFileCountLimit: retainedFileCountLimit))
                .WriteTo.Async(a => a.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:j}{NewLine}{Exception}"))
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
