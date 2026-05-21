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

        void Start()
        {
            Vector3 e = transform.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = Normalize(e.x);
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.isPressed) return;
            if (mouse.rightButton.isPressed || mouse.middleButton.isPressed) return;

            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * sensitivity;
            _pitch -= delta.y * sensitivity;
            _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        static float Normalize(float angle)
        {
            angle %= 360f;
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
