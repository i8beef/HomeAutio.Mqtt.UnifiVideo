using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Timers;
using HomeAutio.Mqtt.Core;
using HomeAutio.Mqtt.Core.Utilities;
using I8Beef.UniFi.Video;
using I8Beef.UniFi.Video.Protocol.Camera;
using I8Beef.UniFi.Video.Protocol.Recording;
using Newtonsoft.Json;
using NLog;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace HomeAutio.Mqtt.UnifiVideo
{
    /// <summary>
    /// UniFi Video MQTT Topshelf service class.
    /// </summary>
    public class UniFiVideoMqttService : ServiceBase
    {
        private ILogger _log = LogManager.GetCurrentClassLogger();
        private bool _disposed = false;

        private Client _client;
        private string _nvrName;

        private Timer _refresh;
        private int _refreshInterval;

        private IDictionary<string, Camera> _cameraInfo = new Dictionary<string, Camera>();
        private IDictionary<string, string> _currentMotionStates = new Dictionary<string, string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="EcobeeMqttService"/> class.
        /// </summary>
        /// <param name="nvrClient">The UniFi Video client.</param>
        /// <param name="nvrName">The target NVR name.</param>
        /// <param name="refreshInterval">The refresh interval.</param>
        /// <param name="brokerIp">MQTT broker IP.</param>
        /// <param name="brokerPort">MQTT broker port.</param>
        /// <param name="brokerUsername">MQTT broker username.</param>
        /// <param name="brokerPassword">MQTT broker password.</param>
        public UniFiVideoMqttService(Client nvrClient, string nvrName, int refreshInterval, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(brokerIp, brokerPort, brokerUsername, brokerPassword, "unifi/video/" + nvrName)
        {
            _refreshInterval = refreshInterval;
            SubscribedTopics.Add(TopicRoot + "/camera/+/+/set");

            _client = nvrClient;
            _nvrName = nvrName;
        }

        #region Service implementation

        /// <summary>
        /// Service Start action.
        /// </summary>
        protected override void StartService()
        {
            GetInitialStatus();

            // Enable refresh
            if (_refresh != null)
            {
                _refresh.Dispose();
            }

            _refresh = new Timer();
            _refresh.Elapsed += RefreshAsync;
            _refresh.Interval = _refreshInterval;
            _refresh.Start();
        }

        /// <summary>
        /// Service Stop action.
        /// </summary>
        protected override void StopService()
        {
            Dispose(true);
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Ecobee published to MQTT.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        protected override void Mqtt_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.Message);
            _log.Debug("MQTT message received for topic " + e.Topic + ": " + message);
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
            var recordings = await _client.RecordingAsync(now.AddSeconds(0 - recordingsTimespan), now, _cameraInfo.Keys, new List<RecordingEventType> { RecordingEventType.MotionRecording });

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
                    MqttClient.Publish($"{TopicRoot}/camera/{_cameraInfo[cameraId].Name.Sluggify()}/motion", Encoding.UTF8.GetBytes(currentState), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
                    _currentMotionStates[cameraId] = currentState;
                }
            }
        }

        /// <summary>
        /// Get, cache, and publish initial states.
        /// </summary>
        private void GetInitialStatus()
        {
            _cameraInfo = _client.CameraAsync().GetAwaiter().GetResult().ToDictionary(k => k.Id, v => v);
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
