using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StateSync.Config;
using StateSync.Threading;
using UnityEngine;

namespace StateSync.Server
{
    /// <summary>
    /// Lightweight WebSocket server bound to <c>127.0.0.1:&lt;port&gt;</c>.
    ///
    /// Implemented over <see cref="TcpListener"/> with a hand-rolled RFC 6455
    /// handshake + framing (see <see cref="WsConnection"/>). We do NOT use
    /// <see cref="System.Net.WebSockets"/> on the server side because Mono in
    /// Unity Standalone Player throws <c>NotImplementedException</c> from
    /// <c>HttpListener.AcceptWebSocketAsync</c>.
    ///
    /// All consumer-facing events (<see cref="OnClientConnected"/>,
    /// <see cref="OnClientDisconnected"/>, <see cref="OnMessageReceived"/>) fire
    /// on Unity's main thread via <see cref="MainThreadDispatcher"/>.
    ///
    /// Snapshot semantics: when a client connects, the server invokes
    /// <see cref="SnapshotProvider"/> on the main thread and sends the result
    /// to that client BEFORE admitting it into the broadcast set. The snapshot
    /// is always the client's first received message.
    /// </summary>
    public sealed class WsServer : MonoBehaviour
    {
        public enum ServerState { Idle, Starting, Listening, Stopping, Stopped }

        const string WsMagic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        [Tooltip("If > 0, overrides ConnectionConfig.WsPort. Lets multiple instances on one machine pick distinct ports.")]
        [SerializeField] int portOverride = 0;

        readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new ConcurrentDictionary<Guid, ClientConnection>();
        TcpListener _listener;
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
        /// Invoked on Unity's main thread when a client connects. Return the
        /// initial state payload (e.g., a serialized snapshot message). The
        /// returned text is sent directly before the client enters the
        /// broadcast set.
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

        // -------- Lifecycle --------

        void StartServer()
        {
            int prev = Interlocked.CompareExchange(ref _stateInt, (int)ServerState.Starting, (int)ServerState.Idle);
            if (prev != (int)ServerState.Idle)
            {
                prev = Interlocked.CompareExchange(ref _stateInt, (int)ServerState.Starting, (int)ServerState.Stopped);
                if (prev != (int)ServerState.Stopped) return;
            }

            Port = portOverride > 0 ? portOverride : ConnectionConfig.WsPort;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                Task.Run(() => AcceptLoop(_cts.Token));
                Interlocked.Exchange(ref _stateInt, (int)ServerState.Listening);
                Debug.Log($"[StateSync] WsServer listening on ws://127.0.0.1:{Port}/");
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _stateInt, (int)ServerState.Stopped);
                Debug.LogError($"[StateSync] Failed to start WsServer on 127.0.0.1:{Port}: {ex.Message}");
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

            try { _cts?.Cancel(); } catch { }

            foreach (var kv in _clients)
            {
                _ = TryCloseClientAsync(kv.Value);
            }
            _clients.Clear();

            try { _listener?.Stop(); } catch { }
            _listener = null;
            try { _cts?.Dispose(); } catch { }
            _cts = null;

            Interlocked.Exchange(ref _stateInt, (int)ServerState.Stopped);
            Debug.Log("[StateSync] WsServer stopped.");
        }

        async Task TryCloseClientAsync(ClientConnection client)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.Connection.CloseAsync(1000, "server shutdown", cts.Token).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                client.Dispose();
            }
        }

        // -------- Accept loop --------

        async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (ct.IsCancellationRequested) return; else continue; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested) OnError?.Invoke(ex);
                    continue;
                }

                _ = HandleConnection(tcp, ct);
            }
        }

        async Task HandleConnection(TcpClient tcp, CancellationToken ct)
        {
            NetworkStream stream = null;
            try
            {
                tcp.NoDelay = true;
                stream = tcp.GetStream();

                // Read and parse HTTP request headers.
                var headers = await ReadHttpHeadersAsync(stream, ct).ConfigureAwait(false);
                if (headers == null)
                {
                    SafeClose(tcp);
                    return;
                }

                // Validate WebSocket upgrade.
                if (!TryValidateUpgrade(headers, out string wsKey))
                {
                    await WriteResponseAsync(stream, "400 Bad Request", null, ct).ConfigureAwait(false);
                    SafeClose(tcp);
                    return;
                }

                // Write 101 Switching Protocols response.
                string acceptKey = ComputeAcceptKey(wsKey);
                string response =
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Accept: {acceptKey}\r\n" +
                    "\r\n";
                byte[] respBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(respBytes, 0, respBytes.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                // Hand off to WsConnection.
                var ws = new WsConnection(tcp);
                Guid id = Guid.NewGuid();
                var client = new ClientConnection(id, ws);

                // Send snapshot BEFORE admitting client to the broadcast set.
                string snapshot = await BuildSnapshotAsync(id, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(snapshot) && ws.IsOpen)
                {
                    try { await ws.SendTextAsync(snapshot, ct).ConfigureAwait(false); }
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
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Debug.LogWarning($"[StateSync] HandleConnection error: {ex.Message}");
                SafeClose(tcp);
            }
        }

        async Task ReceiveLoop(ClientConnection client, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && client.Connection.IsOpen)
            {
                WsConnection.ReceiveResult result;
                try { result = await client.Connection.ReceiveTextAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (Exception) { return; }

                if (result.Kind == WsConnection.ReceiveResultKind.Closed) return;
                if (result.Kind == WsConnection.ReceiveResultKind.Error) return;

                TotalReceived++;
                var local = OnMessageReceived;
                if (local != null)
                {
                    Guid clientId = client.Id;
                    string text = result.Text;
                    MainThreadDispatcher.Enqueue(() => local(clientId, text));
                }
            }
        }

        // -------- HTTP handshake helpers --------

        static async Task<Dictionary<string, string>> ReadHttpHeadersAsync(Stream stream, CancellationToken ct)
        {
            var buffer = new MemoryStream(2048);
            var chunk = new byte[1024];
            const string terminator = "\r\n\r\n";
            int bytesRead = 0;
            const int maxHeader = 16 * 1024;

            while (bytesRead < maxHeader)
            {
                int n;
                try { n = await stream.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false); }
                catch { return null; }
                if (n <= 0) return null;
                buffer.Write(chunk, 0, n);
                bytesRead += n;
                string seen = Encoding.ASCII.GetString(buffer.ToArray());
                int end = seen.IndexOf(terminator, StringComparison.Ordinal);
                if (end >= 0)
                {
                    string headerSection = seen.Substring(0, end);
                    return ParseHeaders(headerSection);
                }
            }
            return null;
        }

        static Dictionary<string, string> ParseHeaders(string text)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return headers;
            // First line: "GET / HTTP/1.1" — keep under a special key for diagnostics.
            headers["__request_line__"] = lines[0];
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                headers[name] = value;
            }
            return headers;
        }

        static bool TryValidateUpgrade(Dictionary<string, string> headers, out string wsKey)
        {
            wsKey = null;
            if (!headers.TryGetValue("Upgrade", out string upgrade)) return false;
            if (!upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)) return false;
            if (!headers.TryGetValue("Connection", out string connection)) return false;
            if (connection.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!headers.TryGetValue("Sec-WebSocket-Version", out string ver) || ver != "13") return false;
            if (!headers.TryGetValue("Sec-WebSocket-Key", out wsKey) || string.IsNullOrEmpty(wsKey)) return false;
            return true;
        }

        static string ComputeAcceptKey(string clientKey)
        {
            using var sha1 = SHA1.Create();
            byte[] combined = Encoding.UTF8.GetBytes(clientKey + WsMagic);
            byte[] hash = sha1.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }

        static async Task WriteResponseAsync(Stream stream, string status, string body, CancellationToken ct)
        {
            try
            {
                string b = body ?? "";
                string http =
                    $"HTTP/1.1 {status}\r\n" +
                    $"Content-Type: text/plain\r\n" +
                    $"Content-Length: {Encoding.UTF8.GetByteCount(b)}\r\n" +
                    "Connection: close\r\n" +
                    "\r\n" +
                    b;
                byte[] bytes = Encoding.UTF8.GetBytes(http);
                await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch { }
        }

        static void SafeClose(TcpClient tcp)
        {
            try { tcp.Close(); } catch { }
        }

        // -------- Snapshot --------

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

        // -------- Broadcast --------

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
            try { await client.SendCoalescedAsync(payload, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
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
