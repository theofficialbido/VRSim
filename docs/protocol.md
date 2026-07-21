# VRSIM Protocol v1

The contract between the **VR client** (Unity, running on the Quest headset) and
the **PC** (the Pansim engine plus a small supervising agent), communicating over
the local network.

This document is the single source of truth. The Unity client, the mock engine
and the real engine are all written against *this*, not against each other. When
they disagree, this file is right and the code is wrong.

---

## 1. Topology

```
   QUEST (Android APK)                    PC on the same LAN
   ──────────────────                     ──────────────────
                          UDP 8769  ┌──────────────────────┐
   discovery broadcast ─────────────►                      │
                       ◄────────────┤   VRSIM Agent        │  always running
                          reply     │   (supervisor)       │
                                    │                      │
   agent control ──── WS 8770 ──────►                      │
                                    └──────────┬───────────┘
                                               │ spawns / supervises
                                               ▼
                                    ┌──────────────────────┐
   telemetry + commands ─ WS 8765 ──►   Pansim engine      │  started on demand
                                    └──────────────────────┘
```

Two WebSocket endpoints with different lifetimes:

| Endpoint | Port | Lifetime | Purpose |
|---|---|---|---|
| Agent | 8770 | Always up | Start/stop/monitor the engine |
| Engine | 8765 | Only while the engine runs | Telemetry and control |

They are separate because the agent must survive the engine crashing — it is
what restarts it. A single endpoint would go down with the thing it supervises.

**Roles.** The PC always listens; the headset always connects. Neither the agent
nor the engine ever initiates a connection to the headset. This matters because
the headset's IP is unpredictable and it may sleep or roam between access points.

---

## 2. Discovery

Typing an IP address on a VR keyboard is miserable, so the headset finds the PC
itself. Manual entry stays available as a fallback and an override.

1. Headset UDP-broadcasts to `255.255.255.255:8769`:

   ```json
   { "v": 1, "type": "discover", "client": "vrsim-quest" }
   ```

2. Every listening agent replies directly to the sender:

   ```json
   {
     "v": 1,
     "type": "discover.reply",
     "host": "RIG-PC-01",
     "agent_port": 8770,
     "engine_port": 8765,
     "engine_running": false,
     "protocol": [1]
   }
   ```

`protocol` lists every version the PC can speak, so a client can decide
compatibility before opening a socket.

The headset broadcasts every 2 s until it gets a reply, then stops. If more than
one agent replies, the client presents the list rather than guessing — a training
room may have several rigs on one network.

---

## 3. Envelope

Every message on both WebSocket endpoints is a single JSON object in a text
frame:

```jsonc
{
  "v": 1,              // protocol major version — integer, required
  "type": "state.delta",
  "seq": 1042,         // per-sender monotonic counter from 1, required
  "ts": 1721558400.123,// sender's Unix time, seconds, float. Diagnostics only.
  "payload": { }       // type-specific, may be omitted when empty
}
```

`ts` is for diagnostics and latency estimation only. Never use it for
simulation timing — the two clocks are not synchronised.

`seq` is per-sender and per-connection, resetting to 1 on each new connection.
It exists so the receiver can detect a gap; see §6.

### Version negotiation

`v` is the **major** version. A receiver that sees a `v` it does not implement
must send an `error` (see below) and then close with code `4400`.

### Errors must be sent as messages, not only as close codes

**Before closing with any code other than 1000, the engine must first send an
`error` message.** The close code alone is not sufficient.

```jsonc
{ "v":1, "type":"error", "seq":1, "ts":…,
  "payload": {
    "code": "unsupported_version",   // machine-readable, see the table in §6
    "message": "engine speaks v1, client sent v99",
    "close_code": 4400
  } }
```

This is not belt-and-braces. Most WebSocket client libraries do not surface
application close codes in the 4000–4999 range: Unity's NativeWebSocket maps
every code outside 1000–1015 to a single `Undefined` value, so a client that
depended on the close code could not tell "wrong protocol version" from
"another headset has control" — two situations needing opposite responses, one
"stop and tell the user", the other "wait and retry".

Clients must therefore treat the last `error` payload received as the
authoritative reason for a disconnect, and the close code as a hint.

Adding an optional field is not a version bump. Removing a field, renaming one,
or changing its meaning or units is.

The client surfaces a version mismatch **in the headset**, as readable text. It
must never fail silently — the failure mode this replaces is a documented port
of 8766 against a configured port of 8765, with nothing anywhere reporting that
they disagreed.

---

## 4. Agent endpoint (port 8770)

### Client → Agent

| Type | Payload | Meaning |
|---|---|---|
| `hello` | `{ "client": "vrsim-quest", "app_version": "…" }` | First message. Always. |
| `engine.start` | `{ "profile": "default" }` | Start the engine if not running |
| `engine.stop` | `{}` | Stop it |
| `engine.restart` | `{}` | Stop then start |
| `engine.status` | `{}` | Request an immediate status |
| `ping` | `{}` | Liveness |

### Agent → Client

| Type | Payload |
|---|---|
| `hello.ack` | `{ "host": "…", "protocol": [1], "engine_port": 8765 }` |
| `engine.status` | see below |
| `error` | `{ "code": "…", "message": "…" }` |
| `pong` | `{}` |

`engine.status` is sent in reply to a request **and unsolicited whenever the
state changes**, so a crash reaches the headset without polling:

```jsonc
{
  "state": "running",        // stopped | starting | running | stopping | crashed
  "pid": 24180,              // null unless running
  "uptime_s": 412.5,
  "engine_port": 8765,
  "last_exit_code": null,    // set when state is "crashed"
  "last_error": null,        // human-readable, shown in-headset
  "restarts": 0              // since the agent started
}
```

`starting` and `stopping` are explicit states rather than gaps between
`stopped` and `running`. The engine takes seconds to boot; without them the UI
cannot distinguish "starting" from "failed to start", and the user presses the
button again.

---

## 5. Engine endpoint (port 8765)

### 5.1 Handshake

Client sends `hello`; engine replies `hello.ack` and then **immediately** a
`state.snapshot`. The client is not considered connected until it holds a
snapshot — there is no window in which it is connected but showing nothing.

```jsonc
// client → engine
{ "v":1, "type":"hello", "seq":1, "ts":…,
  "payload": { "client":"vrsim-quest", "app_version":"0.2.0" } }

// engine → client
{ "v":1, "type":"hello.ack", "seq":1, "ts":…,
  "payload": { "engine_version":"…", "tick_hz":20, "controls":[ … ] } }
```

`controls` is the engine's **declared controllable surface**: every control it
will accept a command for. The client uses it to validate its own bindings at
startup and log any control it can drive that the engine does not recognise —
catching desynchronised builds immediately rather than on first use in a
training session.

```jsonc
{ "id": "bop.annular_1", "kind": "valve",  "states": ["open","closed"] }
{ "id": "console.pump_1", "kind": "switch", "states": ["on","off"] }
{ "id": "swaco.choke",    "kind": "analog", "min": 0.0, "max": 1.0 }
{ "id": "tds.throttle",   "kind": "axis",   "min": -1.0, "max": 1.0 }
```

### 5.2 State

`state.snapshot` carries the complete state. `state.delta` carries only what
changed, as an **RFC 7386 JSON Merge Patch** against the last known state:
present keys replace, `null` deletes, absent keys are untouched.

```jsonc
{
  "drilling": {
    "bit_depth_ft": 10432.5,
    "hole_depth_ft": 10440.0,
    "rop_fph": 42.7,
    "wob_klbs": 25.0,
    "hookload_klbs": 200.0,
    "torque_ftlb": 8200.0,
    "rpm": 120.0,
    "tds_position_ft": 85.0,
    "tds_movement": "stop",        // up | down | stop
    "auto_drill": false
  },
  "pumps": {
    "pump_1": { "active": true,  "spm": 65.0, "pressure_psi": 2850.0 },
    "pump_2": { "active": false, "spm": 0.0,  "pressure_psi": 0.0 },
    "pump_3": { "active": false, "spm": 0.0,  "pressure_psi": 0.0 },
    "total_flow_gpm": 620.0
  },
  "bop": {
    "components": {
      "annular_1":      "open",     // open | closed | moving | fault
      "pipe_ram_1":     "open",
      "pipe_ram_2":     "closed",
      "pipe_ram_3":     "open",
      "blind_shear_ram":"closed",
      "kill_line":      "closed",
      "choke_line":     "open"
    },
    "pressures_psi": { "annular": 1500.0, "manifold": 1529.0,
                       "accumulator": 3000.0, "air": 125.0 },
    "master_valve": "open"
  },
  "swaco": {
    "standpipe_psi": 2850.0,
    "casing_psi": 400.0,
    "choke_position": 0.35,
    "total_strokes": 14820.0,
    "total_volume_bbl": 512.4
  },
  "alarms": []
}
```

**Naming rules**, because the old system's `"Annular-1"`, `"Pipe Ram-1"` and
`"Blind/Shear Ram"` string keys forced hand-written brace-scanning parsers and
substring matching on display labels:

- Identifiers are `snake_case` ASCII. No spaces, no `/`, no `-`.
- Units are in the field name (`_ft`, `_psi`, `_gpm`, `_klbs`, `_fph`, `_ftlb`).
  A bare number with no unit suffix is dimensionless.
- Enumerations are lowercase and closed. Receivers must tolerate an unknown
  value by holding the previous one and logging, never by crashing.

### 5.3 Commands

```jsonc
// client → engine
{ "v":1, "type":"command", "seq":88, "ts":…,
  "payload": {
    "cmd_id": "c-7f3a91",       // client-unique, echoed in the ack
    "control": "bop.annular_1",
    "action": "set",
    "value": "closed"
  } }

// engine → client
{ "v":1, "type":"ack", "seq":512, "ts":…,
  "payload": {
    "cmd_id": "c-7f3a91",
    "status": "accepted",       // accepted | rejected | superseded
    "reason": null              // required when rejected
  } }
```

**Commands are requests, not assertions.** The client never mutates simulation
state locally. It sends intent; the engine decides; the result arrives as a
`state.delta`. One authority, so the two halves cannot silently diverge.

`accepted` means the engine has taken the command, **not** that the world has
finished changing — a ram takes seconds to close. The visible result arrives as
state, and only state.

`rejected` requires a `reason` written for a human, because it will be read in a
headset by someone holding a lever: `"master valve closed"`, not `"EINVAL"`.

`superseded` is for a newer command on the same control arriving first;
the client should drop the older one silently rather than reporting an error.

An engine must ack every command it receives. A client that gets no ack within
its timeout treats it as failed — see §6.

### 5.4 Events

Discrete occurrences that are not state, and would be missed if the client only
sampled state at tick rate:

```jsonc
{ "v":1, "type":"event", "seq":513, "ts":…,
  "payload": {
    "kind": "alarm.raised",     // alarm.raised | alarm.cleared | kick.detected
                                // | limit.exceeded | engine.shutdown
    "severity": "warning",      // info | warning | critical
    "message": "Standpipe pressure above limit",
    "data": { "value_psi": 4200.0, "limit_psi": 4000.0 }
  } }
```

Events are advisory. Anything an event describes that also matters to the world
must be reflected in state as well; a client that missed an event must never end
up with a wrong picture of the rig.

---

## 6. Failure and recovery

The headset is on Wi-Fi. It will drop. Every rule below exists because the
alternative is a display that quietly lies about a live well.

| Situation | Required behaviour |
|---|---|
| Sequence gap detected | Send `resync`; the engine replies with a full `state.snapshot`. Deltas cannot be applied across a gap. |
| No message for 5 s | Client sends `ping`. No `pong` within 3 s ⇒ treat as disconnected. |
| Disconnected | Reconnect with exponential backoff: 0.5 s, doubling, capped at 10 s, with jitter. Retry forever; a trainee should not have to restart the app. |
| Reconnected | Full handshake again. Discard all local state and rebuild from the new snapshot — never merge across a disconnect. |
| Command un-acked after 2 s | Mark failed. Spring the control's visual back to last confirmed state and show it failed. |
| Engine stops or crashes | Agent pushes `engine.status`. Client marks state **stale** and says so visibly. |
| Version mismatch | Close `4400`, show both versions in-headset. |

**Stale is not the same as disconnected, and both must be visible.** A frozen
gauge showing a plausible number is more dangerous than one that admits it has
lost contact. When state is stale the client must visually mark every affected
readout.

### Close codes and error codes

Every non-normal close is preceded by an `error` message carrying the matching
`code`. The client keys its behaviour off that `code`, because close codes in
the 4000-4999 range do not survive most client libraries (see §3).

| Close | `error.code` | Meaning | Client should |
|---|---|---|---|
| 1000 | — | Normal | Reconnect normally |
| 4400 | `unsupported_version` | Protocol version mismatch | **Stop.** Show both versions. Do not retry — retrying just loops. |
| 4401 | `malformed_message` | Unparseable frame | Stop and log; this is a client bug |
| 4409 | `already_controlled` | Another client holds control (§7) | Retry slowly; show "another headset has control" |
| 4500 | `internal_error` | Engine fault | Retry with backoff |

---

## 7. Control arbitration

**VR is the only input.** There are no physical joysticks; the headset is the
sole source of operator intent, so the engine has exactly one commanding client
and no arbitration between input devices is needed.

What remains is arbitration between *headsets*:

- The engine accepts commands from **at most one** client at a time. A second
  client receives an `error` with code `already_controlled` and is closed
  `4409`. Two people commanding one well is a safety problem, not a feature.
- A refused client should retry slowly and say plainly that another headset has
  control, rather than appearing broken.

---

## 8. Rates and limits

| | |
|---|---|
| `state.delta` | Engine tick rate, declared in `hello.ack` as `tick_hz`. Target 20 Hz. |
| `state.snapshot` | On connect, on `resync`, and every 30 s as a correctness backstop |
| Commands | Client-side rate limit 20/s per control; the engine may reject bursts |
| Max frame | 256 KB. Larger ⇒ close `4401`. |

20 Hz is chosen deliberately: fast enough that interpolated gauge needles look
continuous, slow enough to be tolerant of Wi-Fi. The client **interpolates
between deltas** rather than snapping, so the tick rate is not visible.

---

## 9. Conformance

An implementation is conformant when it:

1. Sends an `error` carrying a machine-readable `code` before every non-normal
   close, and rejects a mismatched `v` rather than proceeding.
2. Sends `hello` first and, as an engine, follows `hello.ack` with a snapshot.
3. Detects a `seq` gap and recovers via `resync` rather than applying the delta.
4. Acks every command exactly once, with a human-readable `reason` on rejection.
5. Applies deltas as RFC 7386 merge patches, including `null` deletion.
6. Never presents stale state as live.

The mock engine in `tools/mock_engine/` is the reference implementation and the
conformance target for the Unity client.

---

## Changelog

| Version | Notes |
|---|---|
| 1 | Initial. Supersedes the ad-hoc `BridgeData` format, which was receive-only, had no versioning, and required hand-written JSON scanning because `JsonUtility` cannot deserialise a `Dictionary`. |
