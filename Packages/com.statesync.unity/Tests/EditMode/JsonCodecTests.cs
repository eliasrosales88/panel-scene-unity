using NUnit.Framework;
using StateSync.Protocol;

namespace StateSync.Tests
{
    [TestFixture]
    public class JsonCodecTests
    {
        const float Tolerance = 0.005f; // F2 precision → max 0.005° error per field

        [Test]
        public void Serializes_Rotation_With_Stable_Format()
        {
            string json = JsonCodec.Serialize(MessageType.Rotation, 12.345f, -5.10f, 0.0f, 1.234f);
            StringAssert.Contains("\"v\":1", json);
            StringAssert.Contains("\"type\":\"rotation\"", json);
            StringAssert.Contains("\"yaw\":12.35", json); // F2 rounds
            StringAssert.Contains("\"pitch\":-5.10", json);
            StringAssert.Contains("\"roll\":0.00", json);
            StringAssert.Contains("\"t\":1.234", json);
        }

        [Test]
        public void Round_Trip_Rotation()
        {
            string json = JsonCodec.Serialize(MessageType.Rotation, 42.21f, -3.14f, 8.50f, 7.890f);
            Assert.IsTrue(JsonCodec.TryParse(json, out int v, out MessageType t, out float y, out float p, out float r, out float ts));
            Assert.AreEqual(1, v);
            Assert.AreEqual(MessageType.Rotation, t);
            Assert.AreEqual(42.21f, y, Tolerance);
            Assert.AreEqual(-3.14f, p, Tolerance);
            Assert.AreEqual(8.50f, r, Tolerance);
            Assert.AreEqual(7.890f, ts, 0.001f);
        }

        [Test]
        public void Round_Trip_Snapshot()
        {
            string json = JsonCodec.Serialize(MessageType.Snapshot, 1.0f, 2.0f, 3.0f, 0.5f);
            Assert.IsTrue(JsonCodec.TryParse(json, out _, out MessageType t, out _, out _, out _, out _));
            Assert.AreEqual(MessageType.Snapshot, t);
        }

        [Test]
        public void Rejects_Mismatched_Version()
        {
            const string json = "{\"v\":2,\"type\":\"rotation\",\"yaw\":0,\"pitch\":0,\"roll\":0,\"t\":0}";
            Assert.IsFalse(JsonCodec.TryParse(json, out _, out _, out _, out _, out _, out _));
        }

        [Test]
        public void Rejects_Unknown_Type()
        {
            const string json = "{\"v\":1,\"type\":\"position\",\"yaw\":0,\"pitch\":0,\"roll\":0,\"t\":0}";
            Assert.IsFalse(JsonCodec.TryParse(json, out _, out _, out _, out _, out _, out _));
        }

        [Test]
        public void Rejects_Malformed_Json()
        {
            Assert.IsFalse(JsonCodec.TryParse("not json", out _, out _, out _, out _, out _, out _));
            Assert.IsFalse(JsonCodec.TryParse("", out _, out _, out _, out _, out _, out _));
            Assert.IsFalse(JsonCodec.TryParse(null, out _, out _, out _, out _, out _, out _));
        }

        [Test]
        public void Rejects_Missing_Field()
        {
            const string json = "{\"v\":1,\"type\":\"rotation\",\"yaw\":0,\"pitch\":0,\"t\":0}"; // no roll
            Assert.IsFalse(JsonCodec.TryParse(json, out _, out _, out _, out _, out _, out _));
        }

        [Test]
        public void Parses_With_Whitespace_After_Colon()
        {
            const string json = "{\"v\": 1,\"type\": \"rotation\",\"yaw\": 5.00,\"pitch\": 0.00,\"roll\": 0.00,\"t\": 0.000}";
            Assert.IsTrue(JsonCodec.TryParse(json, out _, out MessageType type, out float yaw, out _, out _, out _));
            Assert.AreEqual(MessageType.Rotation, type);
            Assert.AreEqual(5.0f, yaw, Tolerance);
        }

        [Test]
        public void Parses_Negative_Values()
        {
            const string json = "{\"v\":1,\"type\":\"rotation\",\"yaw\":-90.50,\"pitch\":-45.25,\"roll\":-180.00,\"t\":12.345}";
            Assert.IsTrue(JsonCodec.TryParse(json, out _, out _, out float y, out float p, out float r, out float ts));
            Assert.AreEqual(-90.5f, y, Tolerance);
            Assert.AreEqual(-45.25f, p, Tolerance);
            Assert.AreEqual(-180.0f, r, Tolerance);
            Assert.AreEqual(12.345f, ts, 0.001f);
        }

        [Test]
        public void Uses_Invariant_Decimal_Separator_Not_Locale()
        {
            // Even if the test runs under a locale that uses comma, the codec must emit dots.
            string json = JsonCodec.Serialize(MessageType.Rotation, 1.5f, 0f, 0f, 0f);
            StringAssert.Contains("1.50", json);
            StringAssert.DoesNotContain("1,50", json);
        }
    }
}
