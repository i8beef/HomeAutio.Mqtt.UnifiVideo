using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using HomeAutio.Mqtt.Core;
using HomeAutio.Mqtt.Core.Utilities;
using I8Beef.UniFi.Video;
using I8Beef.UniFi.Video.Protocol.Camera;
using I8Beef.UniFi.Video.Protocol.Recording;
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace HomeAutio.Mqtt.UnifiVideo
{
    /// <summary>
    /// UniFi Video MQTT Topshelf service class.
    /// </summary>
    public class UniFiVideoMqttService : ServiceBase
    {
        private readonly ILogger<UniFiVideoMqttService> _log;

        private readonly Client _client;
        private readonly string _nvrName;

        private readonly IDictionary<string, string> _currentMotionStates = new Dictionary<string, string>();
        private IDictionary<string, Camera> _cameraInfo = new Dictionary<string, Camera>();

        private bool _disposed = false;
        private System.Timers.Timer _refresh;
        private int _refreshInterval;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniFiVideoMqttService"/> class.
        /// </summary>
        /// <param name="logger">Logging instance.</param>
        /// <param name="nvrClient">The UniFi Video client.</param>
        /// <param name="nvrName">The target NVR name.</param>
        /// <param name="refreshInterval">The refresh interval.</param>
        /// <param name="brokerIp">MQTT broker IP.</param>
        /// <param name="brokerPort">MQTT broker port.</param>
        /// <param name="brokerUsername">MQTT broker username.</param>
        /// <param name="brokerPassword">MQTT broker password.</param>
        public UniFiVideoMqttService(ILogger<UniFiVideoMqttService> logger, Client nvrClient, string nvrName, int refreshInterval, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(logger, brokerIp, brokerPort, brokerUsername, brokerPassword, "unifi/video/" + nvrName)
        {
            _log = logger;
            _refreshInterval = refreshInterval * 1000;
            SubscribedTopics.Add(TopicRoot + "/camera/+/+/set");

            _client = nvrClient;
            _nvrName = nvrName;
        }

        #region Service implementation

        /// <inheritdoc />
        protected override async Task StartServiceAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await GetInitialStatusAsync(cancellationToken).ConfigureAwait(false);

            // Enable refresh
            if (_refresh != null)
            {
                _refresh.Dispose();
            }

            _refresh = new System.Timers.Timer();
            _refresh.Elapsed += RefreshAsync;
            _refresh.Interval = _refreshInterval;
            _refresh.Start();
        }

        /// <inheritdoc />
        protected override Task StopServiceAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.CompletedTask;
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the UniFi Video published to MQTT.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        protected override async void Mqtt_MqttMsgPublishReceived(object sender, MqttApplicationMessageReceivedEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            _log.LogDebug("MQTT message received for topic " + e.ApplicationMessage.Topic + ": " + message);
        }

        #endregion

        #region UniFi Video implementation

        /// <summary>
        /// Heartbeat ping. Failure will result in the heartbeat being stopped, which will
        /// make any future calls throw an exception as the heartbeat indicator will be disabled.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void RefreshAsync(object sender, ElapsedEventArgs e)
        {
            var now = e.SignalTime;

            // Get all recordings in the last half hour
            var recordingsTimespan = 60 * 30;
            var recordings = await _client.RecordingAsync(now.AddSeconds(0 - recordingsTimespan), now, _cameraInfo.Keys, new List<RecordingEventType> { RecordingEventType.MotionRecording })
                .ConfigureAwait(false);

            // Determine if there are any recordings still in progress
            var inProgressRecordings = recordings.Where(x => x.InProgress == true);

            // Publish and changes in camera state
            foreach (var cameraId in _cameraInfo.Keys)
            {
                var currentState = "close";
                if (inProgressRecordings.Any(x => x.Cameras[0] == cameraId))
                    currentState = "open";

                // If this is a new state, publish
                if (!_currentMotionStates.ContainsKey(cameraId) || _currentMotionStates[cameraId] != currentState)
                {
                    await MqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic($"{TopicRoot}/camera/{_cameraInfo[cameraId].Name.Sluggify()}/motion")
                        .WithPayload(currentState)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build()).ConfigureAwait(false);

                    _currentMotionStates[cameraId] = currentState;
                }
            }
        }

        /// <summary>
        /// Get, cache, and publish initial states.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An awaitable <see cref="Task"/>.</returns>
        private async Task GetInitialStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            _cameraInfo = (await _client.CameraAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(k => k.Id, v => v);
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Dispose implementation.
        /// </summary>
        /// <param name="disposing">Indicates if disposing.</param>
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_refresh != null)
                {
                    _refresh.Stop();
                    _refresh.Dispose();
                }

                if (_client != null)
                {
                    _client.Dispose();
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        #endregion
    }
}
