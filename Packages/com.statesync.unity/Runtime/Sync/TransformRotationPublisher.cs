using System;
using StateSync.Config;
using StateSync.Protocol;
using StateSync.Server;
using UnityEngine;

namespace StateSync.Sync
{
    /// <summary>
    /// Host-side publisher. Watches a <see cref="Transform"/> and broadcasts its
    /// rotation as <c>rotation</c> messages, throttled by rate + delta threshold +
    /// heartbeat. Also installs itself as the server's <see cref="WsServer.SnapshotProvider"/>
    /// so newly connected clients receive the current pose as their first message.
    ///
    /// Self-disables in <see cref="Awake"/> when <see cref="ConnectionConfig.IsHost"/>
    /// is false, so the same prefab/scene works in both roles.
    /// </summary>
    public sealed class TransformRotationPublisher : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] WsServer server;
        [Tooltip("Maximum send rate (Hz). Combined with the delta threshold to determine if a send is due.")]
        [SerializeField] float maxSendHz = 30f;
        [Tooltip("Minimum angular delta (degrees) versus last sent rotation before a new message is emitted.")]
        [SerializeField] float deltaThresholdDeg = 0.05f;
        [Tooltip("If no message has been sent in this many seconds, send a heartbeat so followers stay in sync.")]
        [SerializeField] float heartbeatSec = 1f;

        Quaternion _lastSentRotation;
        float _lastSendTimeUnscaled;
        float _processStartTime;

        void Awake()
        {
            if (!ConnectionConfig.IsHost) { enabled = false; return; }
            if (server == null) server = GetComponent<WsServer>();
        }

        void Start()
        {
            if (server == null)
            {
                Debug.LogError($"[StateSync] {nameof(TransformRotationPublisher)} on '{name}': WsServer reference missing.");
                enabled = false;
                return;
            }
            if (target == null)
            {
                Debug.LogError($"[StateSync] {nameof(TransformRotationPublisher)} on '{name}': target Transform missing.");
                enabled = false;
                return;
            }

            _processStartTime = Time.realtimeSinceStartup;
            _lastSentRotation = target.rotation;
            _lastSendTimeUnscaled = Time.unscaledTime;
            server.SnapshotProvider = ProvideSnapshot;
        }

        void OnDisable()
        {
            if (server != null && server.SnapshotProvider == ProvideSnapshot)
                server.SnapshotProvider = null;
        }

        void LateUpdate()
        {
            if (target == null || server == null) return;
            if (server.State != WsServer.ServerState.Listening) return;
            if (server.ClientCount == 0) return;

            float now = Time.unscaledTime;
            float minInterval = 1f / Mathf.Max(1f, maxSendHz);
            float sinceLast = now - _lastSendTimeUnscaled;

            float deltaDeg = Quaternion.Angle(target.rotation, _lastSentRotation);
            bool deltaSatisfied = deltaDeg >= deltaThresholdDeg;
            bool heartbeatDue = sinceLast >= heartbeatSec;

            bool shouldSend = heartbeatDue || (deltaSatisfied && sinceLast >= minInterval);
            if (!shouldSend) return;

            string json = SerializeCurrent(MessageType.Rotation);
            server.Broadcast(json);
            _lastSentRotation = target.rotation;
            _lastSendTimeUnscaled = now;
        }

        string ProvideSnapshot(Guid clientId)
        {
            if (target == null) return null;
            return SerializeCurrent(MessageType.Snapshot);
        }

        string SerializeCurrent(MessageType type)
        {
            Vector3 e = target.rotation.eulerAngles;
            float yaw = NormalizeDegrees(e.y);
            float pitch = NormalizeDegrees(e.x);
            float roll = NormalizeDegrees(e.z);
            float t = Time.realtimeSinceStartup - _processStartTime;
            return JsonCodec.Serialize(type, yaw, pitch, roll, t);
        }

        static float NormalizeDegrees(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            else if (deg <= -180f) deg += 360f;
            return deg;
        }
    }
}
