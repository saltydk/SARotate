using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Linuxtesting
{
    class Program
    {
        public static IConfiguration _configuration;

        static async Task Main(string[] args)
        {
            using var host = CreateHostBuilder(args).Build();

            Log.Information("SARotate vSickAssRotater9001 started");

            await host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
           return Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());

                    // Build configuration
                    _configuration = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                        .AddCommandLine(args)
                        .Build();

                    var logger = new LoggerConfiguration()
                      .ReadFrom.Configuration(_configuration)
                      //.Enrich.
                      .CreateLogger();

                    Log.Logger = logger;
                })
                .ConfigureServices(services =>
                {
                    string configAbsolutePath = _configuration["config"] ?? Directory.GetCurrentDirectory() + "/config.yaml";

                    SARotateConfig config = ParseSARotateYamlConfig(configAbsolutePath);

                    services.AddHostedService<SARotate>();
                    services.AddSingleton(config);
                    services.AddSingleton(_configuration);
                })
                .UseSerilog();
        }

        private static SARotateConfig ParseSARotateYamlConfig(string configAbsolutePath)
        {
            if (string.IsNullOrEmpty(configAbsolutePath))
            {
                Console.WriteLine("configAbsolutePath missing as argument");
                throw new ArgumentException("Config file not found");
            }

            using (var streamReader = new StreamReader(configAbsolutePath))
            {
                if (File.Exists(configAbsolutePath))
                {
                    string fileContent = streamReader.ReadToEnd();

                    try
                    {
                        IDeserializer deserializer = new DeserializerBuilder()
                            .WithNamingConvention(UnderscoredNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();

                        return deserializer.Deserialize<SARotateConfig>(fileContent);
                    }
                    catch (Exception)
                    {
                        throw new ArgumentException("Config file invalid format");
                    }
                }
                else
                {
                    Console.WriteLine($"Config file {configAbsolutePath} does not exist");
                    throw new ArgumentException("Config file not found");
                }
            }
        }
    }
}
