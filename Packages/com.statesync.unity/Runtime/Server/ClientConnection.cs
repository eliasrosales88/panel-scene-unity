using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StateSync.Server
{
    /// <summary>
    /// Wraps a single accepted WebSocket. Serializes <see cref="SendCoalescedAsync"/>
    /// and <see cref="SendDirectAsync"/> via a per-connection semaphore — the BCL
    /// requires no concurrent SendAsync calls on the same socket.
    /// </summary>
    internal sealed class ClientConnection : IDisposable
    {
        public Guid Id { get; }
        public WebSocket Socket { get; }
        public BroadcastCoalescer Coalescer { get; } = new BroadcastCoalescer();

        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        int _disposed;

        public ClientConnection(Guid id, WebSocket socket)
        {
            Id = id;
            Socket = socket;
        }

        /// <summary>
        /// Submits a payload through the coalescer, then drains pending until empty.
        /// If another caller is already the drain driver, this submit returns quickly
        /// and the existing driver picks up the newest payload.
        /// </summary>
        public async Task SendCoalescedAsync(string payload, CancellationToken ct)
        {
            bool drainerResponsibility = Coalescer.Submit(payload);
            if (!drainerResponsibility) return;

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    string next = Coalescer.TakePending();
                    if (next == null) break;
                    if (Socket.State != WebSocketState.Open) return;

                    byte[] bytes = Encoding.UTF8.GetBytes(next);
                    await Socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// One-shot send that bypasses the coalescer entirely. Used for the
        /// initial snapshot sent before the client enters the broadcast set.
        /// </summary>
        public async Task SendDirectAsync(string payload, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Socket.State != WebSocketState.Open) return;
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                await Socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _sendLock.Dispose(); } catch { /* swallow */ }
        }
    }
}
