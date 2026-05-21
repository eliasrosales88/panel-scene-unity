using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float orbitSensitivity = 0.3f;
        [SerializeField] float panSensitivity = 0.0015f;
        [SerializeField] float zoomSensitivity = 0.0015f;
        [SerializeField] float minRadius = 0.15f;
        [SerializeField] float maxRadius = 2.5f;
        [SerializeField] float pitchLimit = 85f;

        float _yaw;
        float _pitch;
        float _radius;
        Vector3 _panOffset;

        public Transform Target { set => target = value; }

        void Start()
        {
            if (target == null) return;
            Vector3 offset = transform.position - target.position;
            _radius = Mathf.Max(offset.magnitude, 0.05f);
            _yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Asin(Mathf.Clamp(offset.y / _radius, -1f, 1f)) * Mathf.Rad2Deg;
        }

        void LateUpdate()
        {
            if (target == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue();

            if (mouse.rightButton.isPressed)
            {
                _yaw += delta.x * orbitSensitivity;
                _pitch -= delta.y * orbitSensitivity;
                _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);
            }

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _radius *= Mathf.Exp(-wheel * zoomSensitivity);
                _radius = Mathf.Clamp(_radius, minRadius, maxRadius);
            }

            if (mouse.middleButton.isPressed)
            {
                _panOffset += (-delta.x * transform.right - delta.y * transform.up) * panSensitivity * _radius;
            }

            float yawRad = _yaw * Mathf.Deg2Rad;
            float pitchRad = _pitch * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(
                Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));

            Vector3 focus = target.position + _panOffset;
            transform.position = focus + dir * _radius;
            transform.LookAt(focus);
        }
    }
}
