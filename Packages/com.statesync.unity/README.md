# State Sync

Lightweight WebSocket-based state sync for Unity. Built on .NET BCL — **zero external dependencies**, no native plugins.

Designed for the host/follower pattern: one Unity instance drives a piece of state (e.g., a Transform's rotation), broadcasts changes over WebSocket, and other instances (followers) consume and reproduce the state with interpolated smoothing.

Ships with a Transform-rotation publisher/receiver out of the box. Layer 1 (server, dispatcher, codec, buffer) is independent of the high-level components, so you can implement your own publisher/receiver for any serializable state.

## Install

### As an embedded package (recommended for development)

Drop the `com.statesync.unity` folder into your project's `Packages/` directory. Unity auto-discovers it.

### Via UPM (manifest.json)

```jsonc
{
  "dependencies": {
    "com.statesync.unity": "file:../path/to/com.statesync.unity"
  }
}
```

Or by git URL once published:

```jsonc
"com.statesync.unity": "https://github.com/<user>/<repo>.git?path=Packages/com.statesync.unity"
```

## Quick start

1. **Configure mode at process spawn** (your parent process passes the role):
   ```
   YourGame.exe --ws-mode=host --ws-port=5050
   YourGame.exe --ws-mode=follower --ws-port=5050
   ```

2. **Wire components into your scene** (per scene, per build):
   - Create a "Network" GameObject.
   - Add `MainThreadDispatcher`, `WsServer`, `TransformRotationPublisher`, `TransformRotationReceiver`, and optionally `NetStatusOverlay`.
   - Set the `target` field of publisher and receiver to the Transform you want to sync.
   - Set their `server` field to the WsServer above.

   Both publisher and receiver self-disable in `Awake()` based on `ConnectionConfig.IsHost`. You always attach both; only one runs per role.

3. **Read on the other side** with any WebSocket client (Electron, `wscat`, browser DevTools). Messages are line-delimited JSON; see [PROTOCOL.md](PROTOCOL.md).

## Editor convenience

`Tools / State Sync / Mode / Host | Follower | Use CLI` toggles a per-machine `EditorPrefs` override so you can test follower behavior without rebuilding. Cleared by `Use CLI`.

## Architecture

Three layers, picked according to how much you reuse:

- **Layer 1 — Infrastructure**: `WsServer`, `MainThreadDispatcher`, `JsonCodec`, `InterpolationBuffer`, `ConnectionConfig`. Pipeline-agnostic, no MonoBehaviours except the server and dispatcher.
- **Layer 2 — Sync components**: `TransformRotationPublisher`, `TransformRotationReceiver`, `NetStatusOverlay`. Drop-in MonoBehaviours for Transform rotation.
- **Layer 3 — Your project**: components that wire a specific Transform / GameObject.

To sync something other than rotation (e.g., position, custom state), implement your own publisher/receiver against Layer 1 — keep using `WsServer`, `MainThreadDispatcher`, and your own `JsonCodec` variant.

## Wire protocol

See [PROTOCOL.md](PROTOCOL.md). Stable contract: every message has `{ "v": 1, "type": ..., "t": ... }` plus type-specific fields. Additive evolution within v1; breaking changes bump the major version.

## Send-rate behavior

Built-in throttle keeps bandwidth reasonable:

| Knob | Default |
|---|---|
| Max send rate | 30 Hz |
| Delta threshold | 0.05° |
| Heartbeat | 1 s (sends even if below threshold) |
| Interpolation buffer | 100 ms |
| Extrapolation clamp | 50 ms |
| JSON precision | F2 (0.01°) |

Idle bandwidth: ~60 B/s (heartbeat only). Active manipulation: ~1.8 KB/s.

## License

MIT — see [LICENSE.md](LICENSE.md).
