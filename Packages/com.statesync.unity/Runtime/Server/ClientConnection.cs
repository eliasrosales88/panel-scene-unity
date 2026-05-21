using System;
using System.Threading;
using System.Threading.Tasks;

namespace StateSync.Server
{
    /// <summary>
    /// Wraps a single accepted <see cref="WsConnection"/> with a per-client
    /// semaphore that serializes sends — even though our WsConnection has its
    /// own send lock, the coalescer loop below benefits from the explicit
    /// outer guard so the drain-driver pattern remains correct under all
    /// scheduling orders.
    /// </summary>
    internal sealed class ClientConnection : IDisposable
    {
        public Guid Id { get; }
        public WsConnection Connection { get; }
        public BroadcastCoalescer Coalescer { get; } = new BroadcastCoalescer();

        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        int _disposed;

        public ClientConnection(Guid id, WsConnection connection)
        {
            Id = id;
            Connection = connection;
        }

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
                    if (!Connection.IsOpen) return;
                    await Connection.SendTextAsync(next, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task SendDirectAsync(string payload, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!Connection.IsOpen) return;
                await Connection.SendTextAsync(payload, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _sendLock.Dispose(); } catch { }
            try { Connection?.Dispose(); } catch { }
        }
    }
}
