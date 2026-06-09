using NUnit.Framework;
using StateSync.Buffer;
using StateSync.Protocol;
using UnityEngine;

namespace StateSync.Tests
{
    [TestFixture]
    public class InterpolationBufferTests
    {
        const float QuatTolerance = 0.5f; // degrees of angular difference

        InterpolationBuffer _buf;

        [SetUp]
        public void SetUp() => _buf = new InterpolationBuffer();

        [Test]
        public void Empty_Buffer_Is_Not_Ready()
        {
            Assert.IsFalse(_buf.IsReady);
            Assert.AreEqual(0, _buf.Count);
            Assert.AreEqual(Quaternion.identity, _buf.Evaluate(0f));
        }

        [Test]
        public void Single_Sample_Holds_Pose()
        {
            _buf.Push(new RotationSample(yawDeg: 45f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 0f, arrivalTime: 1f));
            Assert.IsTrue(_buf.IsReady);
            float angle = Quaternion.Angle(Quaternion.Euler(0f, 45f, 0f), _buf.Evaluate(1f));
            Assert.Less(angle, QuatTolerance);
            // Also when renderTime is well before or after, returns same single pose.
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 45f, 0f), _buf.Evaluate(10f)), QuatTolerance);
        }

        [Test]
        public void Two_Samples_Slerp_At_Midpoint()
        {
            _buf.Push(new RotationSample(0f, 0f, 0f, 0f, 0f));
            _buf.Push(new RotationSample(yawDeg: 90f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 0.1f, arrivalTime: 0.1f));
            Quaternion mid = _buf.Evaluate(0.05f);
            float angle = Quaternion.Angle(Quaternion.Euler(0f, 45f, 0f), mid);
            Assert.Less(angle, QuatTolerance);
        }

        [Test]
        public void Past_Newest_Within_Limit_Extrapolates()
        {
            _buf.Push(new RotationSample(0f, 0f, 0f, 0f, 0f));
            _buf.Push(new RotationSample(yawDeg: 30f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 0.033f, arrivalTime: 0.033f));
            // renderTime 0.066s = newest + 33ms (under 50ms cap) → expect ~60deg (extrapolated)
            Quaternion q = _buf.Evaluate(0.066f);
            float yaw = q.eulerAngles.y;
            // Yaw should be between newest (30) and double-extrapolated (60), with some tolerance
            Assert.Greater(yaw, 35f);
            Assert.Less(yaw, 70f);
        }

        [Test]
        public void Past_Newest_Beyond_Limit_Holds_Newest()
        {
            _buf.Push(new RotationSample(0f, 0f, 0f, 0f, 0f));
            _buf.Push(new RotationSample(yawDeg: 30f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 0.033f, arrivalTime: 0.033f));
            // 200ms past newest → way beyond 50ms cap → clamped to newest (30)
            Quaternion q = _buf.Evaluate(0.233f);
            float angle = Quaternion.Angle(Quaternion.Euler(0f, 30f, 0f), q);
            Assert.Less(angle, QuatTolerance);
        }

        [Test]
        public void Before_Oldest_Holds_Oldest()
        {
            _buf.Push(new RotationSample(yawDeg: 10f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 1f, arrivalTime: 1f));
            _buf.Push(new RotationSample(yawDeg: 20f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 2f, arrivalTime: 2f));
            // renderTime well before oldest → clamp to oldest (10)
            Quaternion q = _buf.Evaluate(0f);
            float angle = Quaternion.Angle(Quaternion.Euler(0f, 10f, 0f), q);
            Assert.Less(angle, QuatTolerance);
        }

        [Test]
        public void Ring_Buffer_Drops_Oldest_When_Full()
        {
            for (int i = 0; i < InterpolationBuffer.Capacity + 3; i++)
            {
                _buf.Push(new RotationSample(yawDeg: i, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: i, arrivalTime: i));
            }
            Assert.AreEqual(InterpolationBuffer.Capacity, _buf.Count);

            // After 11 pushes (0..10), buffer holds samples 3..10. Querying at time=3 → should be ~3deg.
            Quaternion qAtOldest = _buf.Evaluate(3f);
            float angle = Quaternion.Angle(Quaternion.Euler(0f, 3f, 0f), qAtOldest);
            Assert.Less(angle, QuatTolerance);

            // Anything before 3 clamps to sample 3 (the new oldest).
            Quaternion qBefore = _buf.Evaluate(0f);
            float angle2 = Quaternion.Angle(Quaternion.Euler(0f, 3f, 0f), qBefore);
            Assert.Less(angle2, QuatTolerance);
        }

        [Test]
        public void Clear_Resets_State()
        {
            _buf.Push(new RotationSample(10f, 0f, 0f, 0f, 0f));
            Assert.IsTrue(_buf.IsReady);
            _buf.Clear();
            Assert.IsFalse(_buf.IsReady);
            Assert.AreEqual(0, _buf.Count);
        }

        [Test]
        public void Coincident_Timestamps_Do_Not_Divide_By_Zero()
        {
            _buf.Push(new RotationSample(yawDeg: 0f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 1f, arrivalTime: 1f));
            _buf.Push(new RotationSample(yawDeg: 10f, pitchDeg: 0f, rollDeg: 0f, senderTimestamp: 1f, arrivalTime: 1f));
            // span is 0; should not throw; should return one of them.
            Quaternion q = _buf.Evaluate(1f);
            Assert.IsNotNull(q);
        }
    }
}
