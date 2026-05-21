using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace StateSync.Threading
{
    /// <summary>
    /// Marshals work from background threads (WebSocket I/O) to Unity's main thread.
    /// Drained in <see cref="Update"/>, capped at <see cref="MaxActionsPerFrame"/>
    /// to absorb burst-resume after main-thread pauses (Editor inspector drag, breakpoints).
    ///
    /// Auto-created on first access via <see cref="Instance"/>; survives scene loads.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        public const int MaxActionsPerFrame = 64;

        static MainThreadDispatcher _instance;
        static readonly object _instanceLock = new object();

        readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_instanceLock)
                {
                    if (_instance != null) return _instance;
                    var go = new GameObject("StateSync.MainThreadDispatcher");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MainThreadDispatcher>();
                    return _instance;
                }
            }
        }

        public static bool HasInstance => _instance != null;

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            // Force lazy creation if accessed before any scene component spawned us.
            Instance._queue.Enqueue(action);
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        void Update()
        {
            int drained = 0;
            while (drained < MaxActionsPerFrame && _queue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogException(ex); }
                drained++;
            }
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
