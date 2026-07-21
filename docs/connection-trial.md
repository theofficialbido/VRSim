# Connection trial — testing the engine link on the headset

How to prove, on real hardware, that the Quest can reach the engine on your PC
and that commands travel in both directions.

Everything here runs against the **mock engine**. There is no real drilling
engine yet; the mock speaks the same protocol, so anything that works here will
work against the real one when it implements [protocol v1](protocol.md).

---

## What the trial proves

A green panel is not the point. The panel is built to distinguish four things
that are easy to confuse:

| What you see | What it means |
|---|---|
| `Connecting` / `Discovering` | The headset cannot reach the PC yet |
| `Connected` + numbers moving | Engine → VR works |
| `self-test  n/n accepted` climbing | **VR → engine works** — this is requirement 4 |
| `STALE — no update for Ns` in red | Connected, but data stopped arriving |

That last row is the one worth dwelling on. A frozen gauge showing a plausible
number is more dangerous than one that admits it has lost contact, so the client
calls staleness out loudly rather than letting the last value sit there looking
alive.

---

## 1. Start the mock engine on the PC

```bash
cd tools/mock_engine
pip install -r requirements.txt
python mock_engine.py
```

It prints the addresses a headset can reach it on:

```
discovery responder listening on udp/8769
mock engine (protocol v1) listening on ws://0.0.0.0:8765 at 20 Hz
  reachable from the LAN at ws://192.168.0.194:8765
```

Note that address. If it says `bound to 127.0.0.1 -- NOT reachable from a
headset`, you passed `--host 127.0.0.1`; drop it.

**Windows Firewall will prompt on first run. Allow it on _private_ networks.**
If you dismiss that prompt the headset will never connect, and the symptom is
an indefinite `Connecting` with no error — the packets are dropped, not refused.

## 2. Build the test scene

In Unity: **VRSIM → Build Connection Test Scene**.

This generates `Assets/Scenes/Connection Test.unity` and makes it first in Build
Settings, so an APK boots straight into it. It is generated rather than
hand-authored deliberately: it has no dependency on the 5,700-object main scene,
so if the trial fails you know the cause is the network or the build, not the rig.

The scene contains an XR rig and one `Engine Link` object carrying
`RigConnection` + `ConnectionHud`.

## 3. Try it in the Editor first

Press Play. The panel should reach `Connected` within a few seconds and start
showing live numbers. Fixing problems here is far faster than through a
20-minute APK cycle.

## 4. Build and run on the headset

`File → Build And Run` with the Quest attached and developer mode on.

Put the headset on. The panel follows your gaze. Within ~5 s you should see
`Connected`, numbers moving, and the self-test counter climbing.

---

## If it does not connect

Work down this list; it is ordered by how often each one is the cause.

**Both devices on the same network.** Most homes and offices have separate 2.4/5
GHz SSIDs or a guest network. The headset and PC must be on the same subnet.
Guest networks usually enable client isolation, which blocks this entirely and
cannot be worked around from the app.

**Firewall.** Re-check the prompt from step 1. To verify quickly, browse to
`http://<pc-ip>:8765` from the headset's browser — a WebSocket server will
reject the request, but *reaching* it proves the port is open. A timeout means
the firewall is still blocking.

**Discovery blocked.** Plenty of networks drop UDP broadcast. Discovery is a
convenience, never a dependency: select `Engine Link` and set **host** to the
PC's IP from step 1, then rebuild. The manual address is the supported path when
broadcast is unavailable.

**Read the panel, then the log.** The panel states the reason for a failure
rather than just spinning. For more, `adb logcat -s Unity` shows the
`[RigConnection]` lines; turn on **verbose logging** on the component first.

**"Another headset has control".** The engine serves one client at a time by
design — two people commanding one well is a safety problem, not a feature. An
Editor Play session still holding the socket will lock out the headset. Stop it.

---

## Testing VR → engine

### The quick one: aim and pull the trigger

A large button floats at eye level, labelled **`diag.test_button`**. Point a
controller at it — you will see a ray, which turns **green** when it is on the
button — and **pull the trigger**.

On the PC, the engine prints:

```
==========================================================
  BUTTON PRESS RECEIVED FROM VR   (press #1)
==========================================================
```

That banner is the proof you asked for: the press left the headset, crossed the
network, and was executed on the PC.

The button then turns green in VR once the engine's press counter comes back
increased — so you see both halves of the round trip, from both ends.

It is verified by a **counter increasing** rather than by matching a value, so
every press is provable on its own. A toggle that happened to already be in the
requested position would confirm instantly and prove nothing.

Trigger, grip and A/X are all bound. A diagnostic that failed because the wrong
button was bound would look exactly like a network fault, which is the one
confusion worth spending three bindings to avoid. If the ray never turns green,
the problem is aim or tracking, not the connection.

### The rest: four rig controls

Four smaller buttons on a stand exercise real controls. These respond to the
trigger too, and also to simply **touching them with a controller**.

They also activate by proximity because that needs no input binding at all, so
between the two methods a dead button is unambiguous.

| Button | Sends | Watch |
|---|---|---|
| `console.pump_1` | `on` / `off` | Pump dot lights, flow climbs |
| `console.pump_2` | `on` / `off` | Second dot, more flow |
| `console.auto_drill` | `on` / `off` | WOB rises, bit depth starts advancing |
| `bop.annular_1` | `closed` / `open` | **Stays amber ~2.5 s**, then greens |

### What the colours mean

| Colour | Meaning |
|---|---|
| Grey | Idle |
| **Amber** | Command sent, waiting — includes "accepted but not done yet" |
| **Green** | Rig state actually reports the value you asked for |
| **Red** | Rejected, or state never changed |

The button turns green **only when state comes back showing the change** — not
when the ack arrives. That distinction is the whole point. An ack means the
engine took the command; it does not mean the world changed.

The annular button demonstrates this best. Press it and the sequence is:

```
press      -> amber, "sent closed"
~instantly -> amber, "accepted, awaiting state"
~instantly -> amber, "engine says moving…"
after 2.5s -> green, "confirmed in 2530 ms"
```

A client that treated the ack as success would have shown that ram closed for
two and a half seconds before it actually was. On a well-control trainer that is
the difference between a correct display and a dangerous one.

The label also reports the measured round-trip in milliseconds, which is a
useful read on your Wi-Fi.

### Watching from the PC

The mock engine logs every command as it arrives:

```
13:41:07  INFO    command c-4f2a91b0 bop.annular_1=closed -> accepted
13:41:12  INFO    command c-88de1a34 console.pump_1=on -> accepted
```

Run it with `--verbose` for more. If lines appear here, the headset is reaching
the engine — so anything still wrong is on the return path.

### Testing a rejection

Press `bop.annular_1` while the master valve is closed and the engine refuses
it. The button goes red and shows `master valve closed` — the engine's own
words, surfaced in the headset rather than swallowed into a log.

---

## Exercising the failure paths

Worth doing once, because these are the behaviours that matter in a real session
and they are invisible if you only ever test the happy path.

| Do this | Expect |
|---|---|
| Stop the mock engine while connected | Red `STALE`, then `Reconnecting` with a backing-off retry |
| Start it again | Reconnects on its own and rebuilds state from a fresh snapshot |
| `python mock_engine.py --tick-hz 1` | Numbers step slowly; nothing breaks |
| Connect a second client while the headset is on | Second one refused, saying another headset has control |

State is never merged across a reconnect — the client discards everything and
rebuilds from the new snapshot, because anything that changed while it was away
would otherwise persist looking current.

---

## Then put it back

The test scene is first in Build Settings. When you are done:
**VRSIM → Make Main Scene First In Build**.
