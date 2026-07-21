using StateSync.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Runtime
{
    [DisallowMultipleComponent]
    public sealed class PanelRotator : MonoBehaviour
    {
        [SerializeField] float sensitivity = 0.3f;
        [SerializeField] float pitchLimit = 85f;

        float _yaw;
        float _pitch;
        float _roll;

        void Awake()
        {
            // Followers don't drive the panel locally — the receiver does.
            if (!ConnectionConfig.IsHost) { enabled = false; return; }

            SyncFromTransform();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.isPressed) return;
            if (mouse.rightButton.isPressed || mouse.middleButton.isPressed) return;

            // External drivers (e.g. HostRotationApplier fed by the Electron panel)
            // may have rotated the panel since the last drag — re-seed from the
            // actual transform when a new drag begins so the panel doesn't snap back.
            if (mouse.leftButton.wasPressedThisFrame) SyncFromTransform();

            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * sensitivity;
            _pitch -= delta.y * sensitivity;
            _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, _roll);
        }

        void SyncFromTransform()
        {
            Vector3 e = transform.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = Normalize(e.x);
            _roll = Normalize(e.z);
        }

        static float Normalize(float angle)
        {
            angle %= 360f;
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
