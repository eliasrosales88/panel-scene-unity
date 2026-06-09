using System;
using StateSync.Buffer;
using StateSync.Config;
using StateSync.Protocol;
using StateSync.Server;
using UnityEngine;

namespace StateSync.Sync
{
    /// <summary>
    /// Follower-side receiver. Subscribes to <see cref="WsServer.OnMessageReceived"/>,
    /// parses incoming <c>rotation</c> / <c>snapshot</c> messages, and feeds them
    /// into an <see cref="InterpolationBuffer"/> which is sampled each frame to
    /// drive the target Transform's rotation.
    ///
    /// Self-disables in <see cref="Awake"/> when <see cref="ConnectionConfig.IsHost"/>
    /// is true.
    /// </summary>
    public sealed class TransformRotationReceiver : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] WsServer server;
        [Tooltip("How far behind real time to render, in seconds. Larger = smoother but more visible latency.")]
        [SerializeField] float interpolationDelaySec = 0.1f;

        readonly InterpolationBuffer _buffer = new InterpolationBuffer();
        float _lastWarnTime;
        int _parseErrorsSinceWarn;

        void Awake()
        {
            if (ConnectionConfig.IsHost) { enabled = false; return; }
            if (server == null) server = GetComponent<WsServer>();
        }

        void Start()
        {
            if (server == null)
            {
                Debug.LogError($"[StateSync] {nameof(TransformRotationReceiver)} on '{name}': WsServer reference missing.");
                enabled = false;
                return;
            }
            if (target == null)
            {
                Debug.LogError($"[StateSync] {nameof(TransformRotationReceiver)} on '{name}': target Transform missing.");
                enabled = false;
                return;
            }
            server.OnMessageReceived += HandleMessage;
        }

        void OnDisable()
        {
            if (server != null) server.OnMessageReceived -= HandleMessage;
        }

        // Runs on Unity's main thread (server marshals via MainThreadDispatcher).
        void HandleMessage(Guid clientId, string json)
        {
            if (!JsonCodec.TryParse(json, out _, out MessageType type, out float yaw, out float pitch, out float roll, out float t))
            {
                _parseErrorsSinceWarn++;
                if (server != null) server.TallyParseError();
                if (Time.unscaledTime - _lastWarnTime > 1f)
                {
                    Debug.LogWarning($"[StateSync] Receiver dropped {_parseErrorsSinceWarn} malformed message(s) in the last second.");
                    _parseErrorsSinceWarn = 0;
                    _lastWarnTime = Time.unscaledTime;
                }
                return;
            }

            if (type != MessageType.Rotation && type != MessageType.Snapshot) return;

            var sample = new RotationSample(yaw, pitch, roll, t, Time.unscaledTime);
            _buffer.Push(in sample);
        }

        void Update()
        {
            if (target == null || !_buffer.IsReady) return;
            float renderTime = Time.unscaledTime - interpolationDelaySec;
            target.rotation = _buffer.Evaluate(renderTime);
        }
    }
}
