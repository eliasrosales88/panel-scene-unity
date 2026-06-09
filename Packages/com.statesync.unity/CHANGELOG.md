# Changelog

All notable changes to `com.statesync.unity` are documented in this file. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the package uses semantic versioning.

## [1.0.0] — Unreleased

### Added
- WebSocket server (`WsServer`) on `ws://127.0.0.1:<port>/` over `System.Net.Sockets.TcpListener` with a hand-rolled RFC 6455 handshake + framing (`WsConnection`). Mono in Unity Standalone Player throws `NotImplementedException` from `HttpListener.AcceptWebSocketAsync`, so the implementation deliberately avoids `System.Net.WebSockets` on the server side and depends only on raw TCP. Zero native plugins.
- `ConnectionConfig` static — CLI args (`--ws-mode`, `--ws-port`) with `EditorPrefs`-backed Editor override.
- `MainThreadDispatcher` — `ConcurrentQueue<Action>` drained on `Update()`, capped at 64 actions/frame.
- `JsonCodec` — hand-rolled, thread-safe serialize/parse for `snapshot` and `rotation` messages.
- `InterpolationBuffer` — Slerp between received samples with 50 ms extrapolation clamp.
- `TransformRotationPublisher` (host only) — 30 Hz throttle, 0.05° threshold, 1 s heartbeat.
- `TransformRotationReceiver` (follower only) — 100 ms interpolation delay.
- `NetStatusOverlay` — DEV-build on-screen status (role, port, clients, sent/recv/errs).
- `ConfigMenu` — `Tools/State Sync/Mode|Port` Editor menu items.
- Wire-protocol contract documented in [PROTOCOL.md](PROTOCOL.md).
- EditMode tests for `InterpolationBuffer`, `JsonCodec`, `ConnectionConfig`.
