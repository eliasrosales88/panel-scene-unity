using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StateSync.Server
{
    /// <summary>
    /// Server-side RFC 6455 WebSocket. Implemented over a raw TCP stream because
    /// Mono's <c>HttpListener.AcceptWebSocketAsync</c> throws
    /// <c>NotImplementedException</c> in Unity Standalone Player builds.
    ///
    /// Supports text frames (opcode 0x1), control frames (ping 0x9 / pong 0xA /
    /// close 0x8), and continuation frames (opcode 0x0). Binary frames (0x2) are
    /// received but returned as null payload.
    /// </summary>
    internal sealed class WsConnection : IDisposable
    {
        public enum State { Open, ClosePending, Closed }

        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        State _state = State.Open;
        int _disposed;

        public State CurrentState => _state;
        public bool IsOpen => _state == State.Open;

        public WsConnection(TcpClient tcp)
        {
            _tcp = tcp;
            _stream = tcp.GetStream();
        }

        // -------- Send --------

        public Task SendTextAsync(string text, CancellationToken ct)
            => SendFrameAsync(opcode: 0x1, payload: Encoding.UTF8.GetBytes(text), ct);

        public Task SendPongAsync(byte[] pingPayload, CancellationToken ct)
            => SendFrameAsync(opcode: 0xA, payload: pingPayload ?? Array.Empty<byte>(), ct);

        public async Task CloseAsync(ushort statusCode, string reason, CancellationToken ct)
        {
            if (_state == State.Closed) return;
            byte[] reasonBytes = string.IsNullOrEmpty(reason) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(reason);
            byte[] payload = new byte[2 + reasonBytes.Length];
            payload[0] = (byte)((statusCode >> 8) & 0xFF);
            payload[1] = (byte)(statusCode & 0xFF);
            if (reasonBytes.Length > 0) System.Buffer.BlockCopy(reasonBytes, 0, payload, 2, reasonBytes.Length);
            try { await SendFrameAsync(opcode: 0x8, payload: payload, ct).ConfigureAwait(false); }
            catch { /* swallow — connection may already be dead */ }
            _state = State.Closed;
        }

        async Task SendFrameAsync(int opcode, byte[] payload, CancellationToken ct)
        {
            if (_state == State.Closed) return;

            byte[] frame = BuildFrame(opcode, fin: true, payload: payload, masked: false);

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_state == State.Closed) return;
                await _stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        static byte[] BuildFrame(int opcode, bool fin, byte[] payload, bool masked)
        {
            int len = payload?.Length ?? 0;
            int headerSize = 2;
            if (len >= 126 && len <= 0xFFFF) headerSize += 2;
            else if (len > 0xFFFF) headerSize += 8;
            if (masked) headerSize += 4;

            byte[] frame = new byte[headerSize + len];
            int i = 0;
            frame[i++] = (byte)((fin ? 0x80 : 0x00) | (opcode & 0x0F));

            if (len < 126)
            {
                frame[i++] = (byte)((masked ? 0x80 : 0x00) | len);
            }
            else if (len <= 0xFFFF)
            {
                frame[i++] = (byte)((masked ? 0x80 : 0x00) | 126);
                frame[i++] = (byte)((len >> 8) & 0xFF);
                frame[i++] = (byte)(len & 0xFF);
            }
            else
            {
                frame[i++] = (byte)((masked ? 0x80 : 0x00) | 127);
                long ll = len;
                for (int k = 7; k >= 0; k--)
                    frame[i++] = (byte)((ll >> (k * 8)) & 0xFF);
            }

            // Server frames are unmasked per RFC; client frames must be masked.
            if (masked)
            {
                byte[] mk = new byte[4];
                new Random().NextBytes(mk);
                System.Buffer.BlockCopy(mk, 0, frame, i, 4);
                i += 4;
                for (int k = 0; k < len; k++)
                    frame[i + k] = (byte)(payload[k] ^ mk[k % 4]);
            }
            else if (len > 0)
            {
                System.Buffer.BlockCopy(payload, 0, frame, i, len);
            }
            return frame;
        }

        // -------- Receive --------

        public struct ReceiveResult
        {
            public ReceiveResultKind Kind;
            public string Text;
            public ushort CloseCode;
            public string CloseReason;
        }

        public enum ReceiveResultKind { Text, Closed, Error }

        /// <summary>
        /// Receives the next complete text message (across continuation frames).
        /// Handles ping/pong and close internally. Returns Closed when the peer
        /// initiated a close (we've already echoed it), Error on protocol breakage
        /// or disconnect.
        /// </summary>
        public async Task<ReceiveResult> ReceiveTextAsync(CancellationToken ct)
        {
            var collected = new MemoryStream();
            int messageOpcode = -1;

            while (true)
            {
                if (_state == State.Closed)
                    return new ReceiveResult { Kind = ReceiveResultKind.Closed };

                byte[] hdr = new byte[2];
                if (!await ReadExactlyAsync(_stream, hdr, 2, ct).ConfigureAwait(false))
                    return new ReceiveResult { Kind = ReceiveResultKind.Error };

                bool fin = (hdr[0] & 0x80) != 0;
                int opcode = hdr[0] & 0x0F;
                bool masked = (hdr[1] & 0x80) != 0;
                int len = hdr[1] & 0x7F;

                if (len == 126)
                {
                    byte[] ext = new byte[2];
                    if (!await ReadExactlyAsync(_stream, ext, 2, ct).ConfigureAwait(false))
                        return new ReceiveResult { Kind = ReceiveResultKind.Error };
                    len = (ext[0] << 8) | ext[1];
                }
                else if (len == 127)
                {
                    byte[] ext = new byte[8];
                    if (!await ReadExactlyAsync(_stream, ext, 8, ct).ConfigureAwait(false))
                        return new ReceiveResult { Kind = ReceiveResultKind.Error };
                    long ll = 0;
                    for (int k = 0; k < 8; k++) ll = (ll << 8) | ext[k];
                    if (ll > int.MaxValue) return new ReceiveResult { Kind = ReceiveResultKind.Error };
                    len = (int)ll;
                }

                byte[] mask = null;
                if (masked)
                {
                    mask = new byte[4];
                    if (!await ReadExactlyAsync(_stream, mask, 4, ct).ConfigureAwait(false))
                        return new ReceiveResult { Kind = ReceiveResultKind.Error };
                }

                byte[] payload = new byte[len];
                if (len > 0)
                {
                    if (!await ReadExactlyAsync(_stream, payload, len, ct).ConfigureAwait(false))
                        return new ReceiveResult { Kind = ReceiveResultKind.Error };
                    if (masked)
                    {
                        for (int k = 0; k < len; k++) payload[k] ^= mask[k % 4];
                    }
                }

                // Control frames
                if (opcode == 0x8) // close
                {
                    ushort code = 1005;
                    string reason = string.Empty;
                    if (len >= 2)
                    {
                        code = (ushort)((payload[0] << 8) | payload[1]);
                        if (len > 2) reason = Encoding.UTF8.GetString(payload, 2, len - 2);
                    }
                    try { await SendFrameAsync(0x8, payload, ct).ConfigureAwait(false); } catch { }
                    _state = State.Closed;
                    return new ReceiveResult { Kind = ReceiveResultKind.Closed, CloseCode = code, CloseReason = reason };
                }
                if (opcode == 0x9) // ping
                {
                    try { await SendPongAsync(payload, ct).ConfigureAwait(false); } catch { }
                    continue;
                }
                if (opcode == 0xA) // pong
                {
                    continue;
                }

                // Data frame
                if (opcode == 0x0)
                {
                    if (messageOpcode < 0) return new ReceiveResult { Kind = ReceiveResultKind.Error }; // continuation without start
                }
                else if (opcode == 0x1 || opcode == 0x2)
                {
                    if (messageOpcode >= 0) return new ReceiveResult { Kind = ReceiveResultKind.Error }; // two starts
                    messageOpcode = opcode;
                }
                else
                {
                    return new ReceiveResult { Kind = ReceiveResultKind.Error };
                }

                collected.Write(payload, 0, len);

                if (fin)
                {
                    if (messageOpcode == 0x1)
                        return new ReceiveResult { Kind = ReceiveResultKind.Text, Text = Encoding.UTF8.GetString(collected.ToArray()) };
                    // Binary or anything else → ignore and continue listening for the next message.
                    collected = new MemoryStream();
                    messageOpcode = -1;
                }
            }
        }

        static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buf, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int read;
                try { read = await stream.ReadAsync(buf, total, count - total, ct).ConfigureAwait(false); }
                catch { return false; }
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _state = State.Closed;
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
            try { _sendLock.Dispose(); } catch { }
        }
    }
}
