# Mock engine

Reference implementation of [VRSIM protocol v1](../../docs/protocol.md).

It exists so the Unity client can be built and tested end-to-end without the
real drilling engine. It serves plausible, self-consistent drilling state,
accepts commands, and exercises every failure path the client has to handle.

It is also the **conformance target**: when the real engine arrives, it should
pass the same test suite.

## Install

```bash
pip install -r requirements.txt
```

## Run

```bash
python mock_engine.py                 # 0.0.0.0:8765 at 20 Hz
python mock_engine.py --verbose       # log every command
python mock_engine.py --tick-hz 5     # slow ticks, easier to watch
```

It binds to `0.0.0.0` by default so a Quest headset on the LAN can reach it.
`127.0.0.1` is only reachable from this machine — useful when testing in the
Unity Editor, useless from a headset.

Find the address the headset needs with `ipconfig` (the IPv4 address of the
adapter on the same network as the headset). Windows Firewall will usually
prompt on first run; it must be allowed on **private** networks.

## Verify

```bash
python conformance_test.py            # against a running engine
```

Checks the six conformance points in protocol section 9, plus control
arbitration and malformed-input handling. Exit code 0 means conformant.

```
15/15 checks passed
```

## What it models

Not a physics engine, deliberately — its job is to move believably and respond
correctly, so display and interaction code can be built and judged before the
real engine exists.

- **Drilling** — RPM spins up toward its setpoint; WOB follows when auto-drill
  is on and the string is turning; ROP derives from the two; bit depth advances
  and deepens the hole at TD. Hookload carries a slow sinusoid so a frozen
  connection is visually obvious.
- **BOP** — valves take **2.5 s to travel** and report `moving` while in
  transit. Closing the master valve interlocks the rams. This is the behaviour
  that proves the client handles "accepted" as *command taken*, not *world
  changed*.
- **Pumps** — SPM ramps rather than stepping; pressure follows with noise so a
  dead feed looks dead.
- **SWACO** — standpipe and casing pressure respond to choke position and total
  flow; totalizers integrate.
- **Emergency stop** — kills rotation and pumps and closes the annular and
  blind/shear rams, then rejects every other command until cleared.

## Deliberate behaviours worth knowing

These are not bugs. Each one exists to make the client prove something.

| Behaviour | What it forces the client to get right |
|---|---|
| Valves report `moving` for 2.5 s | A command is a request; the result arrives as state |
| Commands rejected with prose reasons | Show the reason, don't swallow it |
| Only one client at a time (close `4409`) | Handle being refused |
| Version mismatch closes `4400` | Surface it in-headset, don't retry blindly |
| Snapshot every 30 s | Deltas alone are not a correctness guarantee |
| `seq` gaps must trigger `resync` | Never apply a delta across a gap |

## Limitations

- No persistence — state resets on restart.
- Single client by design, per protocol section 7.
- No authentication. Do not expose it beyond a trusted LAN.
