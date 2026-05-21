using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StateSync.Config;
using StateSync.Threading;
using UnityEngine;

namespace StateSync.Server
{
    /// <summary>
    /// Lightweight WebSocket server bound to <c>http://127.0.0.1:&lt;port&gt;/</c>.
    /// All consumer-facing events (<see cref="OnClientConnected"/>,
    /// <see cref="OnClientDisconnected"/>, <see cref="OnMessageReceived"/>) fire on
    /// Unity's main thread via <see cref="MainThreadDispatcher"/>.
    ///
    /// Snapshot semantics: when a client connects, the server invokes
    /// <see cref="SnapshotProvider"/> on the main thread, sends the result
    /// directly to that client, and only then admits the client into the
    /// broadcast set. This guarantees ordering: the snapshot is always the
    /// client's first received message.
    /// </summary>
    public sealed class WsServer : MonoBehaviour
    {
        public enum ServerState { Idle, Starting, Listening, Stopping, Stopped }

        [Tooltip("If > 0, overrides ConnectionConfig.WsPort. Useful for running multiple instances on one machine.")]
        [SerializeField] int portOverride = 0;

        readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new ConcurrentDictionary<Guid, ClientConnection>();
        HttpListener _listener;
        CancellationTokenSource _cts;
        int _stateInt = (int)ServerState.Idle;

        public ServerState State => (ServerState)_stateInt;
        public int Port { get; private set; }
        public int ClientCount => _clients.Count;
        public long TotalSent { get; private set; }
        public long TotalReceived { get; private set; }
        public long TotalParseErrors { get; private set; }

        public event Action<Guid> OnClientConnected;
        public event Action<Guid> OnClientDisconnected;
        public event Action<Guid, string> OnMessageReceived;
        public event Action<Exception> OnError;

        /// <summary>
        /// Optional. Returns the snapshot payload for a new client, invoked on the
        /// Unity main thread. Set by the publisher; the snapshot is sent before
        /// the client enters the broadcast set to guarantee delivery order.
        /// </summary>
        public Func<Guid, string> SnapshotProvider { get; set; }

        public void TallyParseError() { TotalParseErrors++; }

        void Awake()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopServer;
#endif
        }

        void OnEnable() => StartServer();
        void OnDisable() => StopServer();

        void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopServer;
#endif
            StopServer();
        }

        // ----------------------------------------------------------------------
        // Lifecycle

        void StartServer()
        {
            int prev = Interlocked.CompareExchange(ref _stateInt, (int)ServerState.Starting, (int)ServerState.Idle);
            if (prev != (int)ServerState.Idle)
            {
                prev = Interlocked.CompareExchange(ref _stateInt, (int)ServerState.Starting, (int)ServerState.Stopped);
                if (prev != (int)ServerState.Stopped) return;
            }

            Port = portOverride > 0 ? portOverride : ConnectionConfig.WsPort;
            string prefix = $"http://127.0.0.1:{Port}/";

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                _cts = new CancellationTokenSource();
                Task.Run(() => AcceptLoop(_cts.Token));
                Interlocked.Exchange(ref _stateInt, (int)ServerState.Listening);
                Debug.Log($"[StateSync] WsServer listening on {prefix}");
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _stateInt, (int)ServerState.Stopped);
                Debug.LogError($"[StateSync] Failed to start WsServer on {prefix}: {ex.Message}");
                OnError?.Invoke(ex);
            }
        }

        void StopServer()
        {
            int prev = Interlocked.Exchange(ref _stateInt, (int)ServerState.Stopping);
            if (prev == (int)ServerState.Idle || prev == (int)ServerState.Stopped)
            {
                Interlocked.Exchange(ref _stateInt, (int)ServerState.Stopped);
                return;
            }

            try { _cts?.Cancel(); } catch { /* ignore */ }

            foreach (var kv in _clients)
            {
                _ = TryCloseClientAsync(kv.Value);
            }
            _clients.Clear();

            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }
            _listener = null;
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;

            Interlocked.Exchange(ref _stateInt, (int)ServerState.Stopped);
            Debug.Log("[StateSync] WsServer stopped.");
        }

        async Task TryCloseClientAsync(ClientConnection client)
        {
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                {
                    using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await client.Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "server shutdown",
                        ctsTimeout.Token).ConfigureAwait(false);
                }
            }
            catch { /* ignore */ }
            finally
            {
                client.Dispose();
            }
        }

        // ----------------------------------------------------------------------
        // Accept

        async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException e) when (e.ErrorCode == 995) { return; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Debug.LogWarning($"[StateSync] Accept failed: {ex.Message}");
                        OnError?.Invoke(ex);
                    }
                    continue;
                }

                if (!ctx.Request.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    continue;
                }

                _ = HandleWebSocketContext(ctx, ct);
            }
        }

        async Task HandleWebSocketContext(HttpListenerContext ctx, CancellationToken ct)
        {
            WebSocketContext wsContext;
            try
            {
                wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StateSync] WebSocket upgrade failed: {ex.Message}");
                try { ctx.Response.Close(); } catch { /* ignore */ }
                return;
            }

            var id = Guid.NewGuid();
            var client = new ClientConnection(id, wsContext.WebSocket);

            // Send snapshot BEFORE admitting client to broadcast set, to guarantee ordering.
            string snapshot = await BuildSnapshotAsync(id, ct).ConfigureAwait(false);
            if (snapshot != null && client.Socket.State == WebSocketState.Open)
            {
                try { await client.SendDirectAsync(snapshot, ct).ConfigureAwait(false); }
                catch (Exception ex) { Debug.LogWarning($"[StateSync] Snapshot send failed: {ex.Message}"); }
            }

            _clients.TryAdd(id, client);
            MainThreadDispatcher.Enqueue(() => OnClientConnected?.Invoke(id));

            try
            {
                await ReceiveLoop(client, ct).ConfigureAwait(false);
            }
            finally
            {
                _clients.TryRemove(id, out _);
                client.Dispose();
                MainThreadDispatcher.Enqueue(() => OnClientDisconnected?.Invoke(id));
            }
        }

        async Task<string> BuildSnapshotAsync(Guid clientId, CancellationToken ct)
        {
            var provider = SnapshotProvider;
            if (provider == null) return null;

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            MainThreadDispatcher.Enqueue(() =>
            {
                try { tcs.TrySetResult(provider(clientId)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });

            var winner = await Task.WhenAny(tcs.Task, Task.Delay(2000, ct)).ConfigureAwait(false);
            if (winner != tcs.Task) return null;
            try { return await tcs.Task.ConfigureAwait(false); }
            catch { return null; }
        }

        // ----------------------------------------------------------------------
        // Receive

        async Task ReceiveLoop(ClientConnection client, CancellationToken ct)
        {
            byte[] buffer = new byte[4096];
            var sb = new StringBuilder(128);

            while (!ct.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                try
                {
                    do
                    {
                        result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        if (result.Count > 0)
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                }
                catch (OperationCanceledException) { return; }
                catch (WebSocketException) { return; }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[StateSync] Receive error from {client.Id}: {ex.Message}");
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Text) continue;

                string msg = sb.ToString();
                TotalReceived++;
                var local = OnMessageReceived;
                if (local != null)
                {
                    Guid clientId = client.Id;
                    MainThreadDispatcher.Enqueue(() => local(clientId, msg));
                }
            }
        }

        // ----------------------------------------------------------------------
        // Send

        /// <summary>
        /// Broadcasts a text payload to every admitted client. Uses each client's
        /// coalescer so a slow client drops intermediate messages instead of
        /// stalling the publisher. Clients hitting the watchdog threshold are
        /// disconnected.
        /// </summary>
        public void Broadcast(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            if (State != ServerState.Listening) return;

            foreach (var kv in _clients)
            {
                ClientConnection client = kv.Value;
                if (client.Coalescer.ShouldDisconnect)
                {
                    Debug.LogWarning($"[StateSync] Disconnecting slow client {client.Id}.");
                    _clients.TryRemove(client.Id, out _);
                    _ = TryCloseClientAsync(client);
                    continue;
                }
                _ = SendCoalescedAsync(client, payload);
            }
            TotalSent++;
        }

        async Task SendCoalescedAsync(ClientConnection client, string payload)
        {
            try
            {
                await client.SendCoalescedAsync(payload, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StateSync] Send to {client.Id} failed: {ex.Message}");
                if (_clients.TryRemove(client.Id, out _))
                {
                    client.Dispose();
                    MainThreadDispatcher.Enqueue(() => OnClientDisconnected?.Invoke(client.Id));
                }
            }
        }
    }
}
