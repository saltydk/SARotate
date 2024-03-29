﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
        private static HttpClient _httpClient = new HttpClient();

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

            using IHost host = CreateHostBuilder(args, cts).Build();

            var assembly = Assembly.GetExecutingAssembly();
            var informationVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

            Log.Information($"SARotate Version {informationVersion} started");

            await host.RunAsync(cts.Token);
            Log.CloseAndFlush();
        }

        private static IHostBuilder CreateHostBuilder(string[] args, CancellationTokenSource cts)
        {
            string cwd = Directory.GetCurrentDirectory();

            _configuration = new ConfigurationBuilder()
                .SetBasePath(cwd)
                .AddCommandLine(args)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
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
                    services.AddHttpClient();
                    services.AddHostedService<SARotate>();
                    services.AddSingleton(saRotateConfig);
                    services.AddSingleton(_configuration);
                    services.AddSingleton(cts);
                })
                .UseSerilog(logger);
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

            return (configAbsolutePath ?? _configuration["config"], logFilePath, verboseFlagExists);
        }

        private static Logger CreateLogger(string cwd, string? logFilePath, bool verboseFlagExists)
        {
            string logPath = logFilePath ?? _configuration["Serilog:WriteTo:0:Args:configure:0:Args:path"] ?? cwd + "/sarotate.log";
            string minimumLogLevelConfig = verboseFlagExists ? "Verbose" : _configuration["Serilog:WriteTo:0:Args:configure:0:Args:restrictedToMinimumLevel"] ?? "Information";
            string rollingIntervalConfig = _configuration["Serilog:WriteTo:0:Args:configure:0:Args:rollingInterval"] ?? "Day";
            int fileSizeLimitBytes = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:0:Args:fileSizeLimitBytes"] ?? "5000000");
            int retainedFileCountLimit = int.Parse(_configuration["Serilog:WriteTo:0:Args:configure:0:Args:retainedFileCountLimit"] ?? "5");

            LogEventLevel minimumLogEventLevel = ConvertMinimumLogLevelConfigToLogEventLevel(minimumLogLevelConfig);
            PersistentFileRollingInterval rollingInterval = ConvertRollingIntervalConfigValueToEnum(rollingIntervalConfig);

            Logger logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "SARotate")
                .Enrich.With<GenericLogEnricher>()
                .MinimumLevel.ControlledBy(new LoggingLevelSwitch(minimumLogEventLevel))
                .WriteTo.PersistentFile(logPath, 
                fileSizeLimitBytes: fileSizeLimitBytes,
                persistentFileRollingInterval: rollingInterval,
                retainedFileCountLimit: retainedFileCountLimit)
                .WriteTo.Async(a => a.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:j}{NewLine}{Exception}"))
                .CreateLogger();

            Log.Logger = logger;

            return logger;
        }

        private static PersistentFileRollingInterval ConvertRollingIntervalConfigValueToEnum(string rollingInterval)
        {
            return rollingInterval.ToLower() switch
            {
                "infinite" => PersistentFileRollingInterval.Infinite,
                "year" => PersistentFileRollingInterval.Year,
                "month" => PersistentFileRollingInterval.Month,
                "day" => PersistentFileRollingInterval.Day,
                "hour" => PersistentFileRollingInterval.Hour,
                "minute" => PersistentFileRollingInterval.Minute,
                _ => PersistentFileRollingInterval.Day
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
