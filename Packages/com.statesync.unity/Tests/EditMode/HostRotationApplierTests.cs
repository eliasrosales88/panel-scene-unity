using NUnit.Framework;
using StateSync.Sync;
using UnityEngine;

namespace StateSync.Tests
{
    public class HostRotationApplierTests
    {
        const float Tolerance = 0.001f;

        [Test]
        public void ComputeRotation_MatchesProtocolEulerOrder()
        {
            var expected = Quaternion.Euler(-5.1f, 12.34f, 30f);
            var actual = HostRotationApplier.ComputeRotation(12.34f, -5.1f, 30f, 85f);
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance));
        }

        [Test]
        public void ComputeRotation_ClampsPitchAboveLimit()
        {
            var expected = Quaternion.Euler(85f, 0f, 0f);
            var actual = HostRotationApplier.ComputeRotation(0f, 120f, 0f, 85f);
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance));
        }

        [Test]
        public void ComputeRotation_ClampsPitchBelowNegativeLimit()
        {
            var expected = Quaternion.Euler(-85f, 0f, 0f);
            var actual = HostRotationApplier.ComputeRotation(0f, -120f, 0f, 85f);
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance));
        }

        [Test]
        public void ComputeRotation_PassesYawAndRollThroughUnclamped()
        {
            var expected = Quaternion.Euler(0f, 270f, -170f);
            var actual = HostRotationApplier.ComputeRotation(270f, 0f, -170f, 85f);
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance));
        }
    }
}
