# State Sync wire protocol

**Version:** v1
**Transport:** WebSocket, UTF-8 text frames
**Encoding:** one JSON object per WS frame, terminated by `\n`
**Bind:** `ws://127.0.0.1:<port>/` (loopback only by default)

## Common envelope

Every message MUST carry these three fields:

| Field | Type | Required | Description |
|---|---|---|---|
| `v` | int | yes | Major schema version. Receivers MUST reject mismatched major versions. v1 supports `snapshot` and `rotation`. |
| `type` | string | yes | Discriminator: one of `"snapshot"`, `"rotation"`. Unknown types MUST be ignored (forward compatibility). |
| `t` | float | yes | Sender's monotonic timestamp in seconds since process start. Receivers use **arrival time**, not `t`, for interpolation alignment. `t` is reserved for diagnostics and future ordering checks. |

## Message types

### `rotation`

Sent by the **host** when the local Transform's rotation changes, throttled per the rules in [Lifecycle](#lifecycle).

Also valid in the **client → server** direction: a follower (or external driver) may push rotations into a server.

```jsonc
{ "v": 1, "type": "rotation", "yaw": 12.34, "pitch": -5.10, "roll": 0.00, "t": 1.234 }
```

| Field | Type | Range | Units |
|---|---|---|---|
| `yaw` | float | `(-360, 360]` | degrees, rotation around Y axis |
| `pitch` | float | `[-85, 85]` | degrees, rotation around X axis (clamped to avoid gimbal lock) |
| `roll` | float | `(-360, 360]` | degrees, rotation around Z axis |

**Reconstruction:** `Quaternion.Euler(pitch, yaw, roll)` (Unity Euler order X, Y, Z).

### `snapshot`

Sent by the server **immediately** after accepting a new client. Identical payload schema to `rotation`. Distinguished only by `type` so receivers can:
- Seed their interpolation buffer with a known starting pose.
- Distinguish "initial state" from "incremental change" if they care.

```jsonc
{ "v": 1, "type": "snapshot", "yaw": 12.34, "pitch": -5.10, "roll": 0.00, "t": 1.234 }
```

There is exactly **one** snapshot per connection, sent before any `rotation` deltas.

## Lifecycle

```
client                              server
  │                                   │
  │  GET / HTTP/1.1                   │
  │  Upgrade: websocket               │
  ├──────────────────────────────────▶│
  │                                   │
  │  101 Switching Protocols          │
  │◀──────────────────────────────────┤
  │                                   │
  │              snapshot             │
  │◀──────────────────────────────────┤
  │                                   │
  │              rotation             │
  │◀──────────────────────────────────┤
  │              rotation             │
  │◀──────────────────────────────────┤
  │              ...                  │
  │                                   │
  │       (optional, follower role)   │
  │              rotation             │
  ├──────────────────────────────────▶│
  │                                   │
  │  Close (1000 NormalClosure)       │
  │◀─────────────────────────────────▶│
```

1. Client connects.
2. Server sends one `snapshot`.
3. Server broadcasts `rotation` on change, with these rate rules:
   - **Throttle**: at most **30 Hz** between consecutive sends.
   - **Threshold**: only send when `|deltaAngle| ≥ 0.05°` from last sent value.
   - **Heartbeat**: if neither throttle nor threshold has emitted in **1 s**, send a heartbeat `rotation` with the current value. Prevents drift between sender and follower.
4. Followers MAY send `rotation` messages to drive the server's Transform. Hosts MUST ignore inbound `rotation`.
5. Either side closes with `WebSocketCloseStatus.NormalClosure` (1000).

## Send-rate optimization rationale

A naive 60 Hz emit at full float precision saturates SignalR / proxy hubs and burns mobile bandwidth on the parent process side. The defaults aim for:

- **Idle**: ≤1 msg/s (heartbeat only).
- **Active manipulation**: ≤30 msg/s, ~60 B/msg → ~1.8 KB/s.
- **Receiver smoothness**: 100 ms interpolation buffer at the receiver hides up to 3–4 dropped samples; extrapolation clamped to +50 ms past the newest sample.

If your needs are different, the publisher/receiver components expose all these as serialized fields.

## Evolution policy

- **Additive within v1**: new optional fields, new `type` values. Clients MUST ignore unknown fields and unknown types.
- **Breaking changes**: rename/remove a field, change semantics, change units. These bump `v` to 2.
- Receivers MUST fail loud (warn + close connection) when `v` doesn't match their supported major.
- Senders MUST NOT downgrade `v` — always send the major they implement.

## Out of scope

- **Authentication** / origin validation: the default bind is loopback, so the trust boundary is the local user. Cross-machine deployment requires adding an auth layer.
- **Reliability**: WebSocket TCP guarantees in-order delivery while the connection is up. After a reconnect, the receiver re-seeds via the new `snapshot`. There is no replay log.
- **Backpressure protocol**: the server may silently drop intermediate messages for slow clients (coalescing). Receivers should not rely on receiving every single delta.
- **Multi-stream**: one stream per WebSocket connection. Multiplexing different state streams requires opening multiple sockets or extending the envelope with a `stream` field (post v1).
