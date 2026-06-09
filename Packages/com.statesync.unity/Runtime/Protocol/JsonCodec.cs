using System;
using System.Globalization;
using System.Text;

namespace StateSync.Protocol
{
    /// <summary>
    /// Thread-safe hand-rolled JSON codec for the State Sync wire protocol (v1).
    /// Replaces <c>JsonUtility</c> which is not thread-safe and cannot be called
    /// from WebSocket worker threads.
    /// </summary>
    public static class JsonCodec
    {
        public const int Version = 1;

        public static string Serialize(MessageType type, float yawDeg, float pitchDeg, float rollDeg, float senderTimestamp)
        {
            string typeStr = ToWire(type);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(96);
            sb.Append("{\"v\":").Append(Version);
            sb.Append(",\"type\":\"").Append(typeStr).Append('"');
            sb.Append(",\"yaw\":").Append(yawDeg.ToString("F2", inv));
            sb.Append(",\"pitch\":").Append(pitchDeg.ToString("F2", inv));
            sb.Append(",\"roll\":").Append(rollDeg.ToString("F2", inv));
            sb.Append(",\"t\":").Append(senderTimestamp.ToString("F3", inv));
            sb.Append('}');
            return sb.ToString();
        }

        public static bool TryParse(string json,
                                    out int version,
                                    out MessageType type,
                                    out float yawDeg,
                                    out float pitchDeg,
                                    out float rollDeg,
                                    out float senderTimestamp)
        {
            version = 0; type = MessageType.Unknown;
            yawDeg = 0f; pitchDeg = 0f; rollDeg = 0f; senderTimestamp = 0f;

            if (string.IsNullOrEmpty(json)) return false;
            if (!TryGetInt(json, "v", out version)) return false;
            if (version != Version) return false;

            if (!TryGetString(json, "type", out string typeStr)) return false;
            type = FromWire(typeStr);
            if (type == MessageType.Unknown) return false;

            if (!TryGetFloat(json, "yaw", out yawDeg)) return false;
            if (!TryGetFloat(json, "pitch", out pitchDeg)) return false;
            if (!TryGetFloat(json, "roll", out rollDeg)) return false;
            if (!TryGetFloat(json, "t", out senderTimestamp)) return false;

            return true;
        }

        static string ToWire(MessageType t) => t switch
        {
            MessageType.Rotation => "rotation",
            MessageType.Snapshot => "snapshot",
            _ => throw new ArgumentException($"Unsupported MessageType for serialization: {t}", nameof(t))
        };

        static MessageType FromWire(string s) => s switch
        {
            "rotation" => MessageType.Rotation,
            "snapshot" => MessageType.Snapshot,
            _ => MessageType.Unknown
        };

        static int FindValueStart(string json, string fieldName)
        {
            string key = "\"" + fieldName + "\":";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return -1;
            int i = idx + key.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            return i;
        }

        static int ScanNumber(string json, int start)
        {
            int i = start;
            if (i < json.Length && (json[i] == '-' || json[i] == '+')) i++;
            while (i < json.Length)
            {
                char c = json[i];
                bool ok = (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+';
                if (!ok) break;
                i++;
            }
            return i;
        }

        static bool TryGetInt(string json, string name, out int value)
        {
            value = 0;
            int start = FindValueStart(json, name);
            if (start < 0) return false;
            int end = ScanNumber(json, start);
            if (end <= start) return false;
            return int.TryParse(json.Substring(start, end - start),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out value);
        }

        static bool TryGetFloat(string json, string name, out float value)
        {
            value = 0f;
            int start = FindValueStart(json, name);
            if (start < 0) return false;
            int end = ScanNumber(json, start);
            if (end <= start) return false;
            return float.TryParse(json.Substring(start, end - start),
                                  NumberStyles.Float,
                                  CultureInfo.InvariantCulture,
                                  out value);
        }

        static bool TryGetString(string json, string name, out string value)
        {
            value = null;
            int start = FindValueStart(json, name);
            if (start < 0 || start >= json.Length || json[start] != '"') return false;
            int end = json.IndexOf('"', start + 1);
            if (end < 0) return false;
            value = json.Substring(start + 1, end - start - 1);
            return true;
        }
    }
}
