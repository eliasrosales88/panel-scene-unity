using System;
using StateSync.Config;
using StateSync.Protocol;
using StateSync.Server;
using UnityEngine;

namespace StateSync.Sync
{
    /// <summary>
    /// Host-side control input. Subscribes to <see cref="WsServer.OnMessageReceived"/>
    /// and applies inbound <c>rotation</c> messages from external drivers (e.g. the
    /// Electron side panel) directly to the target Transform. The co-located
    /// <see cref="TransformRotationPublisher"/> then re-broadcasts the resulting pose
    /// to every connected client, keeping all UIs in sync.
    ///
    /// Self-disables in <see cref="Awake"/> when <see cref="ConnectionConfig.IsHost"/>
    /// is false — followers already drive the Transform via
    /// <see cref="TransformRotationReceiver"/>.
    /// </summary>
    public sealed class HostRotationApplier : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] WsServer server;
        [Tooltip("Inbound pitch is clamped to ±this many degrees, mirroring PanelRotator's gimbal guard.")]
        [SerializeField] float pitchLimitDeg = 85f;

        float _lastWarnTime;
        int _parseErrorsSinceWarn;

        void Awake()
        {
            if (!ConnectionConfig.IsHost) { enabled = false; return; }
            if (server == null) server = GetComponent<WsServer>();
        }

        void Start()
        {
            if (server == null)
            {
                Debug.LogError($"[StateSync] {nameof(HostRotationApplier)} on '{name}': WsServer reference missing.");
                enabled = false;
                return;
            }
            if (target == null)
            {
                Debug.LogError($"[StateSync] {nameof(HostRotationApplier)} on '{name}': target Transform missing.");
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
            if (!JsonCodec.TryParse(json, out _, out MessageType type, out float yaw, out float pitch, out float roll, out _))
            {
                _parseErrorsSinceWarn++;
                if (server != null) server.TallyParseError();
                if (Time.unscaledTime - _lastWarnTime > 1f)
                {
                    Debug.LogWarning($"[StateSync] Applier dropped {_parseErrorsSinceWarn} malformed message(s) in the last second.");
                    _parseErrorsSinceWarn = 0;
                    _lastWarnTime = Time.unscaledTime;
                }
                return;
            }

            // Snapshots are server → client only; ignore anything that isn't a rotation command.
            if (type != MessageType.Rotation) return;

            target.rotation = ComputeRotation(yaw, pitch, roll, pitchLimitDeg);
        }

        /// <summary>
        /// Builds the target rotation from wire angles, clamping pitch to ±<paramref name="pitchLimitDeg"/>.
        /// Mirrors the protocol's reconstruction rule: <c>Quaternion.Euler(pitch, yaw, roll)</c>.
        /// </summary>
        public static Quaternion ComputeRotation(float yawDeg, float pitchDeg, float rollDeg, float pitchLimitDeg)
        {
            float pitch = Mathf.Clamp(pitchDeg, -pitchLimitDeg, pitchLimitDeg);
            return Quaternion.Euler(pitch, yawDeg, rollDeg);
        }
    }
}
