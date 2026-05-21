using StateSync.Protocol;
using UnityEngine;

namespace StateSync.Buffer
{
    /// <summary>
    /// Ring buffer of <see cref="RotationSample"/>s with Slerp interpolation and
    /// clamped extrapolation. Pure C# (depends on UnityEngine.Quaternion for output
    /// only); EditMode-testable without a scene.
    /// </summary>
    public sealed class InterpolationBuffer
    {
        public const int Capacity = 8;
        public const float ExtrapolationLimitSec = 0.05f;

        readonly RotationSample[] _ring = new RotationSample[Capacity];
        int _count;
        int _start;

        public bool IsReady => _count > 0;
        public int Count => _count;

        public void Push(in RotationSample sample)
        {
            int writeIdx = (_start + _count) % Capacity;
            _ring[writeIdx] = sample;
            if (_count < Capacity)
                _count++;
            else
                _start = (_start + 1) % Capacity;
        }

        public void Clear()
        {
            _count = 0;
            _start = 0;
        }

        public Quaternion Evaluate(float renderTime)
        {
            if (_count == 0) return Quaternion.identity;

            int newestIdx = (_start + _count - 1) % Capacity;
            RotationSample newest = _ring[newestIdx];

            if (_count == 1) return ToQuat(newest);

            // Past newest sample — extrapolate or hold.
            if (renderTime >= newest.ArrivalTime)
            {
                float overshoot = renderTime - newest.ArrivalTime;
                if (overshoot > ExtrapolationLimitSec)
                    return ToQuat(newest);

                int prevIdx = (_start + _count - 2) % Capacity;
                RotationSample prev = _ring[prevIdx];
                float span = newest.ArrivalTime - prev.ArrivalTime;
                if (span < 1e-4f) return ToQuat(newest);

                float t = 1f + (overshoot / span);
                return Quaternion.SlerpUnclamped(ToQuat(prev), ToQuat(newest), t);
            }

            // Before oldest sample — hold.
            RotationSample oldest = _ring[_start];
            if (renderTime <= oldest.ArrivalTime) return ToQuat(oldest);

            // Find bracketing pair and Slerp.
            for (int i = _count - 1; i >= 1; i--)
            {
                int aIdx = (_start + i - 1) % Capacity;
                int bIdx = (_start + i) % Capacity;
                RotationSample a = _ring[aIdx];
                RotationSample b = _ring[bIdx];

                if (a.ArrivalTime <= renderTime && b.ArrivalTime >= renderTime)
                {
                    float span = b.ArrivalTime - a.ArrivalTime;
                    if (span < 1e-4f) return ToQuat(b);
                    float t = (renderTime - a.ArrivalTime) / span;
                    return Quaternion.Slerp(ToQuat(a), ToQuat(b), t);
                }
            }

            return ToQuat(newest);
        }

        static Quaternion ToQuat(in RotationSample s)
            => Quaternion.Euler(s.PitchDeg, s.YawDeg, s.RollDeg);
    }
}
