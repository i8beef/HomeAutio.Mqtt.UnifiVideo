using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using HomeAutio.Mqtt.Core;
using HomeAutio.Mqtt.Core.Utilities;
using I8Beef.UniFi.Video;
using I8Beef.UniFi.Video.Protocol.Camera;
using I8Beef.UniFi.Video.Protocol.Common;
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

        private readonly IClient _client;
        private readonly string _nvrName;

        private readonly IDictionary<string, string> _currentMotionStates = new Dictionary<string, string>();
        private IDictionary<string, Camera> _cameraInfo = new Dictionary<string, Camera>();

        private bool _disposed = false;
        private System.Timers.Timer _refresh;
        private System.Timers.Timer _detectMotionRefresh;
        private int _refreshInterval;
        private int _detectMotionRefreshInterval;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniFiVideoMqttService"/> class.
        /// </summary>
        /// <param name="logger">Logging instance.</param>
        /// <param name="nvrClient">The UniFi Video client.</param>
        /// <param name="nvrName">The target NVR name.</param>
        /// <param name="refreshInterval">The refresh interval.</param>
        /// <param name="detectMotionRefreshInterval">The detect motion refresh interval.</param>
        /// <param name="brokerSettings">MQTT broker settings.</param>
        public UniFiVideoMqttService(
            ILogger<UniFiVideoMqttService> logger,
            IClient nvrClient,
            string nvrName,
            int refreshInterval,
            int detectMotionRefreshInterval,
            BrokerSettings brokerSettings)
            : base(logger, brokerSettings, "unifi/video/" + nvrName)
        {
            _log = logger;
            _refreshInterval = refreshInterval * 1000;
            _detectMotionRefreshInterval = detectMotionRefreshInterval * 1000;
            SubscribedTopics.Add(TopicRoot + "/camera/+/+/set");

            _client = nvrClient;
            _nvrName = nvrName;
        }

        #region Service implementation

        /// <inheritdoc />
        protected override async Task StartServiceAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await GetInitialStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            // Enable refresh
            if (_refresh != null)
            {
                _refresh.Dispose();
            }

            _refresh = new System.Timers.Timer();
            _refresh.Elapsed += RefreshAsync;
            _refresh.Interval = _refreshInterval;
            _refresh.Start();

            // Enable refresh
            if (_detectMotionRefresh != null)
            {
                _detectMotionRefresh.Dispose();
            }

            _detectMotionRefresh = new System.Timers.Timer();
            _detectMotionRefresh.Elapsed += DetectMotionAsync;
            _detectMotionRefresh.Interval = _detectMotionRefreshInterval;
            _detectMotionRefresh.Start();
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
            var message = e.ApplicationMessage.ConvertPayloadToString();
            _log.LogInformation("MQTT message received for topic " + e.ApplicationMessage.Topic + ": " + message);

            // Parse topic out
            var topicWithoutRoot = e.ApplicationMessage.Topic.Substring(TopicRoot.Length + 8);
            var cameraName = topicWithoutRoot.Substring(0, topicWithoutRoot.IndexOf('/'));
            if (_cameraInfo.Values.Any(x => x.Name.Sluggify() == cameraName))
            {
                var cameraId = _cameraInfo.FirstOrDefault(x => x.Value.Name.Sluggify() == cameraName).Key;
                var cameraTopic = topicWithoutRoot.Substring(topicWithoutRoot.IndexOf('/') + 1);

                switch (cameraTopic)
                {
                    case "recordMode/set":
                        await HandleRecordModeCommandAsync(cameraId, message)
                            .ConfigureAwait(false);
                        break;
                }
            }
        }

        /// <summary>
        /// Handles a record mode command.
        /// </summary>
        /// <param name="cameraId">Camera id.</param>
        /// <param name="value">Record mode value.</param>
        /// <returns>An awaitable <see cref="Task"/>.</returns>
        private async Task HandleRecordModeCommandAsync(string cameraId, string value)
        {
            var recordMode = RecordingMode.None;
            switch (value)
            {
                case "always":
                    recordMode = RecordingMode.FullTime;
                    break;
                case "motion":
                    recordMode = RecordingMode.Motion;
                    break;
                case "none":
                default:
                    recordMode = RecordingMode.None;
                    break;
            }

            await _client.SetRecordModeAsync(cameraId, recordMode)
                .ConfigureAwait(false);
        }

        #endregion

        #region UniFi Video implementation

        /// <summary>
        /// Refresh camera state.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void RefreshAsync(object sender, ElapsedEventArgs e)
        {
            // Get camera states
            var cameraInfo = (await _client.CameraAsync().ConfigureAwait(false)).ToDictionary(k => k.Id, v => v);

            // Publish and changes in camera state
            foreach (var cameraId in cameraInfo.Keys)
            {
                var cameraName = cameraInfo[cameraId].Name.Sluggify();

                // Record mode
                if (!_cameraInfo.ContainsKey(cameraId) ||
                    _cameraInfo[cameraId].RecordingSettings.FullTimeRecordEnabled != cameraInfo[cameraId].RecordingSettings.FullTimeRecordEnabled ||
                    _cameraInfo[cameraId].RecordingSettings.MotionRecordEnabled != cameraInfo[cameraId].RecordingSettings.MotionRecordEnabled)
                {
                    var recordMode = "none";
                    if (cameraInfo[cameraId].RecordingSettings.FullTimeRecordEnabled)
                        recordMode = "always";

                    if (cameraInfo[cameraId].RecordingSettings.MotionRecordEnabled)
                        recordMode = "motion";

                    await MqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic($"{TopicRoot}/camera/{cameraName}/recordMode")
                        .WithPayload(recordMode)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build())
                        .ConfigureAwait(false);
                }

                _cameraInfo[cameraId] = cameraInfo[cameraId];
            }
        }

        /// <summary>
        /// Refresh camera state.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void DetectMotionAsync(object sender, ElapsedEventArgs e)
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
                // Motion state
                var currentMotionState = "close";
                if (inProgressRecordings.Any(x => x.Cameras[0] == cameraId))
                    currentMotionState = "open";

                if (!_currentMotionStates.ContainsKey(cameraId) || _currentMotionStates[cameraId] != currentMotionState)
                {
                    await MqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic($"{TopicRoot}/camera/{_cameraInfo[cameraId].Name.Sluggify()}/motion")
                        .WithPayload(currentMotionState)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build())
                        .ConfigureAwait(false);

                    _currentMotionStates[cameraId] = currentMotionState;
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
                if (_detectMotionRefresh != null)
                {
                    _detectMotionRefresh.Stop();
                    _detectMotionRefresh.Dispose();
                }

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
