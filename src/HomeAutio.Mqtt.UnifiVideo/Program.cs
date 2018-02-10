using System.Configuration;
using I8Beef.UniFi.Video;
using NLog;
using Topshelf;

namespace HomeAutio.Mqtt.UnifiVideo
{
    /// <summary>
    /// Main program entrypoint.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Main method.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        static void Main(string[] args)
        {
            var log = LogManager.GetCurrentClassLogger();

            var brokerIp = ConfigurationManager.AppSettings["brokerIp"];
            var brokerPort = int.Parse(ConfigurationManager.AppSettings["brokerPort"]);
            var brokerUsername = ConfigurationManager.AppSettings["brokerUsername"];
            var brokerPassword = ConfigurationManager.AppSettings["brokerPassword"];

            var nvrName = ConfigurationManager.AppSettings["nvrName"];
            var nvrRereshInterval = int.Parse(ConfigurationManager.AppSettings["nvrRereshInterval"]) * 1000;
            var nvrHost = ConfigurationManager.AppSettings["nvrHost"];
            var nvrDisableSslCheck = bool.Parse(ConfigurationManager.AppSettings["nvrDisableSslCheck"]);
            var nvrUsername = ConfigurationManager.AppSettings["nvrUsername"];
            var nvrPassword = ConfigurationManager.AppSettings["nvrPassword"];

            var client = new Client(nvrHost, nvrUsername, nvrPassword, nvrDisableSslCheck);
            HostFactory.Run(x =>
            {
                x.UseNLog();
                x.OnException((ex) => { log.Error(ex); });

                x.Service<UniFiVideoMqttService>(s =>
                {
                    s.ConstructUsing(name => new UniFiVideoMqttService(client, nvrName, nvrRereshInterval, brokerIp, brokerPort, brokerUsername, brokerPassword));
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });

                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(0);
                    r.RestartService(0);
                    r.RestartService(0);
                });

                x.RunAsLocalSystem();
                x.UseAssemblyInfoForServiceInfo();
            });
        }
    }
}
