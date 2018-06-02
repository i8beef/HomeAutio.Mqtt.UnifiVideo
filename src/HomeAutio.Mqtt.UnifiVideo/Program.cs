using System;
using System.Threading.Tasks;
using I8Beef.UniFi.Video;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HomeAutio.Mqtt.UnifiVideo
{
    /// <summary>
    /// Main program entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args">Arguments.</param>
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Awaitable <see cref="Task" />.</returns>
        public static async Task MainAsync(string[] args)
        {
            // Setup logging
            Log.Logger = new LoggerConfiguration()
              .Enrich.FromLogContext()
              .WriteTo.Console()
              .WriteTo.RollingFile(@"logs/HomeAutio.Mqtt.UnifiVideo.log")
              .CreateLogger();

            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.SetBasePath(Environment.CurrentDirectory);
                    config.AddJsonFile("appsettings.json", optional: false);
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddSerilog();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Setup client
                    services.AddScoped<Client>(serviceProvider =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        return new Client(
                            configuration.GetValue<string>("unifiHost"),
                            configuration.GetValue<string>("unifiUsername"),
                            configuration.GetValue<string>("unifiPassword"),
                            configuration.GetValue<bool>("unifiDisableSslCheck"));
                    });

                    // Setup service instance
                    services.AddScoped<IHostedService, UniFiVideoMqttService>(serviceProvider =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        return new UniFiVideoMqttService(
                            serviceProvider.GetRequiredService<IApplicationLifetime>(),
                            serviceProvider.GetRequiredService<ILogger<UniFiVideoMqttService>>(),
                            serviceProvider.GetRequiredService<Client>(),
                            configuration.GetValue<string>("unifiName"),
                            configuration.GetValue<int>("refreshInterval"),
                            configuration.GetValue<string>("brokerIp"),
                            configuration.GetValue<int>("brokerPort"),
                            configuration.GetValue<string>("brokerUsername"),
                            configuration.GetValue<string>("brokerPassword"));
                    });
                });

            await hostBuilder.RunConsoleAsync();
        }
    }
}
