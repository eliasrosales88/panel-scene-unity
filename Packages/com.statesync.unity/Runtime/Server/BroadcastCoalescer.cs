using System;

namespace StateSync.Server
{
    /// <summary>
    /// Per-client send coalescer. Tracks a single <c>_pending</c> payload; if a new
    /// payload arrives while another is queued, the older one is dropped and the
    /// drop counter is incremented. The server uses <see cref="ShouldDisconnect"/>
    /// to evict clients that fall too far behind.
    /// </summary>
    public sealed class BroadcastCoalescer
    {
        public const int DropWatchdogLimit = 5;
        public const float DropWatchdogWindowSec = 2f;

        readonly object _lock = new object();
        string _pending;
        int _dropsInWindow;
        DateTime _windowStart;
        long _totalDropped;

        /// <summary>
        /// Returns true if this submit transitioned the slot from empty → filled
        /// (caller becomes the drain driver). Returns false if a previous payload
        /// was already pending (it gets overwritten and counted as a drop).
        /// </summary>
        public bool Submit(string payload)
        {
            lock (_lock)
            {
                bool wasEmpty = _pending == null;
                if (!wasEmpty)
                {
                    DateTime now = DateTime.UtcNow;
                    if ((now - _windowStart).TotalSeconds > DropWatchdogWindowSec)
                    {
                        _windowStart = now;
                        _dropsInWindow = 0;
                    }
                    _dropsInWindow++;
                    _totalDropped++;
                }
                _pending = payload;
                return wasEmpty;
            }
        }

        public string TakePending()
        {
            lock (_lock)
            {
                string p = _pending;
                _pending = null;
                return p;
            }
        }

        public long TotalDropped
        {
            get { lock (_lock) return _totalDropped; }
        }

        public bool ShouldDisconnect
        {
            get
            {
                lock (_lock)
                {
                    if ((DateTime.UtcNow - _windowStart).TotalSeconds > DropWatchdogWindowSec)
                        return false;
                    return _dropsInWindow >= DropWatchdogLimit;
                }
            }
        }
    }
}
