"""
VRSIM mock engine -- reference implementation of protocol v1.

See docs/protocol.md. That document is the contract; this file implements it.
Where they disagree, the document is right.

Purpose: let the Unity client be developed and tested end-to-end without the
real drilling engine. It serves plausible, self-consistent drilling state,
accepts commands, and exercises every failure path the client must handle.

    python mock_engine.py [--host 0.0.0.0] [--port 8765] [--tick-hz 20]

Bind to 0.0.0.0 (the default) so a Quest headset on the LAN can reach it;
127.0.0.1 is only reachable from this machine.
"""

from __future__ import annotations

import argparse
import asyncio
import copy
import json
import logging
import math
import random
import time
from typing import Any

import websockets
from websockets.exceptions import ConnectionClosed

PROTOCOL_VERSION = 1
SNAPSHOT_INTERVAL_S = 30.0
MAX_FRAME_BYTES = 256 * 1024
DISCOVERY_PORT = 8769

# Close codes -- see docs/protocol.md section 6.
CLOSE_UNSUPPORTED_VERSION = 4400
CLOSE_MALFORMED = 4401
CLOSE_ALREADY_CONTROLLED = 4409
CLOSE_INTERNAL = 4500

log = logging.getLogger("mock_engine")


# ---------------------------------------------------------------------------
# Controllable surface, advertised in hello.ack so the client can validate its
# bindings at startup instead of discovering a mismatch mid-session.
# ---------------------------------------------------------------------------

CONTROLS: list[dict[str, Any]] = [
    {"id": "bop.annular_1", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.pipe_ram_1", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.pipe_ram_2", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.pipe_ram_3", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.blind_shear_ram", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.kill_line", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.choke_line", "kind": "valve", "states": ["open", "closed"]},
    {"id": "bop.master_valve", "kind": "valve", "states": ["open", "closed"]},
    {"id": "console.pump_1", "kind": "switch", "states": ["on", "off"]},
    {"id": "console.pump_2", "kind": "switch", "states": ["on", "off"]},
    {"id": "console.pump_3", "kind": "switch", "states": ["on", "off"]},
    {"id": "console.auto_drill", "kind": "switch", "states": ["on", "off"]},
    {"id": "console.emergency_stop", "kind": "button", "states": ["press"]},
    {"id": "swaco.choke", "kind": "analog", "min": 0.0, "max": 1.0},
    {"id": "tds.throttle", "kind": "axis", "min": -1.0, "max": 1.0},
    {"id": "tds.rpm_setpoint", "kind": "analog", "min": 0.0, "max": 250.0},
    {"id": "tds.wob_setpoint", "kind": "analog", "min": 0.0, "max": 60.0},
    # Diagnostic only: exists so a human in a headset can press something
    # and see unmistakable proof on the PC that it arrived.
    {"id": "diag.test_button", "kind": "button", "states": ["press"]},
    {"id": "swaco.hold", "kind": "switch", "states": ["on", "off"]},
    {"id": "swaco.reset", "kind": "button", "states": ["press"]},
]

CONTROLS_BY_ID = {c["id"]: c for c in CONTROLS}

# Valves take time to travel; a ram does not slam shut. The client must render
# "moving" rather than assuming a command completes instantly.
VALVE_TRAVEL_S = 2.5


async def close_with_error(ws, close_code: int, error_code: str, message: str) -> None:
    """Send an `error` frame, then close.

    The close code alone is not enough: most WebSocket clients do not surface
    application close codes in the 4000-4999 range. Unity's NativeWebSocket
    collapses every code outside 1000-1015 to a single `Undefined` value, so a
    client relying on it could not distinguish "wrong protocol version" (stop,
    tell the user) from "another headset has control" (wait and retry).
    See docs/protocol.md section 3.
    """
    frame = {
        "v": PROTOCOL_VERSION,
        "type": "error",
        "seq": 0,
        "ts": round(time.time(), 3),
        "payload": {"code": error_code, "message": message, "close_code": close_code},
    }
    try:
        await ws.send(json.dumps(frame, separators=(",", ":")))
    except ConnectionClosed:
        pass
    await ws.close(close_code, message[:120])


def _merge_patch_diff(old: Any, new: Any) -> Any:
    """RFC 7386 merge patch taking `old` to `new`.

    Returns a sentinel-free dict of only what changed. Keys removed in `new`
    become explicit nulls, which is what makes the patch reversible on the
    client without it needing to know the full schema.
    """
    if not isinstance(old, dict) or not isinstance(new, dict):
        return copy.deepcopy(new)

    patch: dict[str, Any] = {}
    for key, new_val in new.items():
        if key not in old:
            patch[key] = copy.deepcopy(new_val)
        elif isinstance(new_val, dict) and isinstance(old[key], dict):
            sub = _merge_patch_diff(old[key], new_val)
            if sub:
                patch[key] = sub
        elif old[key] != new_val:
            patch[key] = copy.deepcopy(new_val)

    for key in old:
        if key not in new:
            patch[key] = None
    return patch


class RigModel:
    """Plausible, self-consistent drilling state.

    Not a physics engine -- deliberately. Its job is to give the VR client
    something that moves believably and responds to commands, so that display
    and interaction code can be built and judged before the real engine exists.
    """

    def __init__(self) -> None:
        self.t0 = time.monotonic()
        self.rpm_setpoint = 120.0
        self.wob_setpoint = 25.0
        self.throttle = 0.0
        self.auto_drill = False
        self.emergency = False

        # Valves in transit: id -> (target_state, completes_at_monotonic)
        self._valves_moving: dict[str, tuple[str, float]] = {}
        self._demo_index = 0

        self.state: dict[str, Any] = {
            "drilling": {
                "bit_depth_ft": 10432.5,
                "hole_depth_ft": 10440.0,
                "rop_fph": 0.0,
                "wob_klbs": 0.0,
                "hookload_klbs": 200.0,
                "torque_ftlb": 0.0,
                "rpm": 0.0,
                "tds_position_ft": 85.0,
                "tds_movement": "stop",
                "auto_drill": False,
            },
            "pumps": {
                "pump_1": {"active": False, "spm": 0.0, "pressure_psi": 0.0},
                "pump_2": {"active": False, "spm": 0.0, "pressure_psi": 0.0},
                "pump_3": {"active": False, "spm": 0.0, "pressure_psi": 0.0},
                "total_flow_gpm": 0.0,
            },
            "bop": {
                "components": {
                    "annular_1": "open",
                    "pipe_ram_1": "open",
                    "pipe_ram_2": "open",
                    "pipe_ram_3": "open",
                    "blind_shear_ram": "closed",
                    "kill_line": "closed",
                    "choke_line": "open",
                },
                "pressures_psi": {
                    "annular": 1500.0,
                    "manifold": 1529.0,
                    "accumulator": 3000.0,
                    "air": 125.0,
                },
                "master_valve": "open",
            },
            "swaco": {
                "standpipe_psi": 0.0,
                "casing_psi": 400.0,
                "choke_position": 0.35,
                "total_strokes": 0.0,
                "total_volume_bbl": 0.0,
                "hold_active": False,
                "reset_active": False,
            },
            "alarms": [],
            "diag": {"button_presses": 0, "last_press_source": ""},
        }

    # -- command handling ---------------------------------------------------

    def apply_command(self, control: str, action: str, value: Any) -> tuple[bool, str | None]:
        """Returns (accepted, reason_if_rejected).

        Rejection reasons are written to be read by a person in a headset who
        is holding the lever that just did nothing.
        """
        spec = CONTROLS_BY_ID.get(control)
        if spec is None:
            return False, f"unknown control '{control}'"

        if self.emergency and control != "console.emergency_stop":
            return False, "emergency stop engaged"

        kind = spec["kind"]

        if kind in ("valve", "switch", "button"):
            if value not in spec["states"]:
                return False, f"'{value}' is not valid for {control}"
        elif kind in ("analog", "axis"):
            try:
                value = float(value)
            except (TypeError, ValueError):
                return False, f"'{value}' is not a number"
            if not (spec["min"] <= value <= spec["max"]):
                return False, f"value must be between {spec['min']} and {spec['max']}"

        if control.startswith("bop."):
            return self._apply_bop(control, value)
        if control.startswith("console.pump_"):
            self.state["pumps"][control.split(".", 1)[1]]["active"] = value == "on"
            return True, None
        if control == "console.auto_drill":
            self.auto_drill = value == "on"
            return True, None
        if control == "diag.test_button":
            self.state["diag"]["button_presses"] += 1
            n = self.state["diag"]["button_presses"]
            log.info("=" * 58)
            log.info("  BUTTON PRESS RECEIVED FROM VR   (press #%d)", n)
            log.info("=" * 58)
            return True, None
        if control == "console.emergency_stop":
            self._trigger_emergency()
            return True, None
        if control == "swaco.hold":
            self.state["swaco"]["hold_active"] = value == "on"
            return True, None
        if control == "swaco.reset":
            # Momentary: latches briefly so the lamp is visibly driven,
            # then clears on the next tick.
            self.state["swaco"]["reset_active"] = True
            self.state["swaco"]["total_strokes"] = 0.0
            self.state["swaco"]["total_volume_bbl"] = 0.0
            self._reset_clear_at = time.monotonic() + 0.75
            return True, None
        if control == "swaco.choke":
            self.state["swaco"]["choke_position"] = value
            return True, None
        if control == "tds.throttle":
            self.throttle = value
            return True, None
        if control == "tds.rpm_setpoint":
            self.rpm_setpoint = value
            return True, None
        if control == "tds.wob_setpoint":
            self.wob_setpoint = value
            return True, None

        return False, f"control '{control}' is declared but not implemented"

    def _apply_bop(self, control: str, value: str) -> tuple[bool, str | None]:
        name = control.split(".", 1)[1]
        if name == "master_valve":
            self.state["bop"]["master_valve"] = value
            return True, None

        if self.state["bop"]["master_valve"] != "open":
            return False, "master valve closed"

        components = self.state["bop"]["components"]
        if name not in components:
            return False, f"unknown BOP component '{name}'"
        if components[name] == value and name not in self._valves_moving:
            return True, None  # already there; accept idempotently

        components[name] = "moving"
        self._valves_moving[name] = (value, time.monotonic() + VALVE_TRAVEL_S)
        return True, None

    def _trigger_emergency(self) -> None:
        self.emergency = True
        self.auto_drill = False
        self.throttle = 0.0
        self.rpm_setpoint = 0.0
        for pump in ("pump_1", "pump_2", "pump_3"):
            self.state["pumps"][pump]["active"] = False
        if self.state["bop"]["master_valve"] == "open":
            for name in ("annular_1", "blind_shear_ram"):
                self.state["bop"]["components"][name] = "moving"
                self._valves_moving[name] = ("closed", time.monotonic() + VALVE_TRAVEL_S)

    def clear_emergency(self) -> None:
        self.emergency = False

    # -- tick ---------------------------------------------------------------

    def tick(self, dt: float) -> list[dict[str, Any]]:
        """Advance the model. Returns events raised during this tick."""
        events: list[dict[str, Any]] = []
        now = time.monotonic()
        d = self.state["drilling"]

        # Valve travel completion is carried by state, not by an event -- a
        # client that missed an event must never end up with a wrong picture
        # of which rams are closed.
        for name, (target, done_at) in list(self._valves_moving.items()):
            if now >= done_at:
                self.state["bop"]["components"][name] = target
                del self._valves_moving[name]

        if getattr(self, "_reset_clear_at", 0) and now >= self._reset_clear_at:
            self.state["swaco"]["reset_active"] = False
            self._reset_clear_at = 0

        target_rpm = 0.0 if self.emergency else self.rpm_setpoint
        d["rpm"] += (target_rpm - d["rpm"]) * min(1.0, dt * 2.0)

        if abs(self.throttle) > 0.01 and not self.emergency:
            d["tds_position_ft"] = max(0.0, min(120.0, d["tds_position_ft"] - self.throttle * dt * 8.0))
            d["tds_movement"] = "down" if self.throttle > 0 else "up"
        else:
            d["tds_movement"] = "stop"

        d["auto_drill"] = self.auto_drill
        drilling = self.auto_drill and d["rpm"] > 10 and not self.emergency
        d["wob_klbs"] += ((self.wob_setpoint if drilling else 0.0) - d["wob_klbs"]) * min(1.0, dt * 1.5)
        d["rop_fph"] = max(0.0, d["wob_klbs"] * d["rpm"] * 0.014) if drilling else 0.0
        d["bit_depth_ft"] = min(d["hole_depth_ft"], d["bit_depth_ft"] + d["rop_fph"] * dt / 3600.0)
        if d["bit_depth_ft"] >= d["hole_depth_ft"]:
            d["hole_depth_ft"] += d["rop_fph"] * dt / 3600.0
        d["torque_ftlb"] = d["wob_klbs"] * 320.0 + d["rpm"] * 8.0
        d["hookload_klbs"] = 200.0 - d["wob_klbs"] + math.sin((now - self.t0) * 0.7) * 1.2

        pumps = self.state["pumps"]
        total_flow = 0.0
        for name in ("pump_1", "pump_2", "pump_3"):
            p = pumps[name]
            target_spm = 65.0 if p["active"] else 0.0
            p["spm"] += (target_spm - p["spm"]) * min(1.0, dt * 1.2)
            p["pressure_psi"] = p["spm"] * 44.0 + (random.uniform(-8, 8) if p["spm"] > 1 else 0.0)
            total_flow += p["spm"] * 3.2
        pumps["total_flow_gpm"] = total_flow

        sw = self.state["swaco"]
        sw["standpipe_psi"] = total_flow * 4.6 * (1.0 + (1.0 - sw["choke_position"]) * 0.35)
        sw["casing_psi"] = 400.0 + (1.0 - sw["choke_position"]) * 900.0
        sw["total_strokes"] += sum(pumps[p]["spm"] for p in ("pump_1", "pump_2", "pump_3")) * dt / 60.0
        sw["total_volume_bbl"] = sw["total_strokes"] * 0.0345

        alarms: list[str] = []
        if sw["standpipe_psi"] > 4000:
            alarms.append("standpipe_overpressure")
        if self.emergency:
            alarms.append("emergency_stop")
        if alarms != self.state["alarms"]:
            for a in set(alarms) - set(self.state["alarms"]):
                events.append(
                    {
                        "kind": "alarm.raised",
                        "severity": "critical" if a == "emergency_stop" else "warning",
                        "message": a.replace("_", " ").capitalize(),
                        "data": {"alarm": a},
                    }
                )
            for a in set(self.state["alarms"]) - set(alarms):
                events.append(
                    {
                        "kind": "alarm.cleared",
                        "severity": "info",
                        "message": f"{a.replace('_', ' ').capitalize()} cleared",
                        "data": {"alarm": a},
                    }
                )
            self.state["alarms"] = alarms

        return events

    # -- BOP light demo -------------------------------------------------

    DEMO_SEQUENCE = [
        "annular_1", "pipe_ram_1", "pipe_ram_2", "pipe_ram_3",
        "blind_shear_ram", "kill_line", "choke_line", "master_valve",
    ]

    def demo_step(self) -> str:
        """Toggle the next BOP component, for watching the panel light up.

        Walks the components in order so every lamp on the panel is exercised,
        rather than picking at random and leaving some never lit. Rams go
        through the normal travel path, so each toggle shows open -> moving ->
        closed and the amber "moving" state is visible for its full 2.5s.
        """
        name = self.DEMO_SEQUENCE[self._demo_index % len(self.DEMO_SEQUENCE)]
        self._demo_index += 1

        if name == "master_valve":
            current = self.state["bop"]["master_valve"]
            target = "closed" if current == "open" else "open"
            self.state["bop"]["master_valve"] = target
            return f"master_valve -> {target}"

        current = self.state["bop"]["components"].get(name, "open")
        target = "open" if current == "closed" else "closed"

        # Reopen the master valve if a previous step shut it, or the rams are
        # interlocked and the demo would stall with nothing moving.
        if self.state["bop"]["master_valve"] != "open":
            self.state["bop"]["master_valve"] = "open"

        self.state["bop"]["components"][name] = "moving"
        self._valves_moving[name] = (target, time.monotonic() + VALVE_TRAVEL_S)
        return f"{name} -> {target} (moving for {VALVE_TRAVEL_S}s)"

    def snapshot(self) -> dict[str, Any]:
        return copy.deepcopy(self.state)


class Session:
    """One connected VR client.

    Only one is permitted at a time (protocol section 7): two controllers of a
    single well is a safety problem, not a feature.
    """

    def __init__(self, ws, model: RigModel, tick_hz: float) -> None:
        self.ws = ws
        self.model = model
        self.tick_hz = tick_hz
        self.out_seq = 0
        self.last_sent: dict[str, Any] | None = None
        self.hello_done = False
        self.last_command_seq: dict[str, int] = {}

    async def send(self, msg_type: str, payload: dict[str, Any] | None = None) -> None:
        self.out_seq += 1
        frame = {
            "v": PROTOCOL_VERSION,
            "type": msg_type,
            "seq": self.out_seq,
            "ts": round(time.time(), 3),
        }
        if payload is not None:
            frame["payload"] = payload
        await self.ws.send(json.dumps(frame, separators=(",", ":")))

    async def send_snapshot(self) -> None:
        state = self.model.snapshot()
        await self.send("state.snapshot", state)
        self.last_sent = state

    async def send_delta(self) -> None:
        state = self.model.snapshot()
        if self.last_sent is None:
            await self.send_snapshot()
            return
        patch = _merge_patch_diff(self.last_sent, state)
        if patch:
            await self.send("state.delta", patch)
            self.last_sent = state


async def handle_client(ws, model: RigModel, tick_hz: float, holder: dict,
                        demo_interval: float = 0.0) -> None:
    peer = getattr(ws, "remote_address", ("?", 0))
    if holder.get("session") is not None:
        log.warning("rejecting %s: another client holds control", peer)
        await close_with_error(ws, CLOSE_ALREADY_CONTROLLED, "already_controlled",
                               "another client already holds control")
        return

    session = Session(ws, model, tick_hz)
    holder["session"] = session
    log.info("client connected: %s", peer)

    pump_task = asyncio.create_task(_pump(session, model, tick_hz, demo_interval))
    try:
        async for raw in ws:
            if len(raw) > MAX_FRAME_BYTES:
                await close_with_error(ws, CLOSE_MALFORMED, "malformed_message",
                                       "frame too large")
                return
            try:
                msg = json.loads(raw)
                if not isinstance(msg, dict):
                    raise ValueError("not an object")
            except (json.JSONDecodeError, ValueError) as exc:
                await close_with_error(ws, CLOSE_MALFORMED, "malformed_message",
                                       f"malformed message: {exc}")
                return

            version = msg.get("v")
            if version != PROTOCOL_VERSION:
                await close_with_error(
                    ws, CLOSE_UNSUPPORTED_VERSION, "unsupported_version",
                    f"engine speaks v{PROTOCOL_VERSION}, client sent v{version}",
                )
                return

            await _dispatch(session, model, msg, tick_hz)
    except ConnectionClosed:
        pass
    finally:
        pump_task.cancel()
        holder["session"] = None
        log.info("client disconnected: %s", peer)


async def _dispatch(session: Session, model: RigModel, msg: dict, tick_hz: float) -> None:
    msg_type = msg.get("type")
    payload = msg.get("payload") or {}

    if msg_type == "hello":
        await session.send(
            "hello.ack",
            {
                "engine_version": "mock-1.0",
                "tick_hz": tick_hz,
                "controls": CONTROLS,
            },
        )
        await session.send_snapshot()
        session.hello_done = True
        log.info("handshake complete with %s", payload.get("client", "unknown client"))
        return

    if not session.hello_done:
        await close_with_error(session.ws, CLOSE_MALFORMED, "malformed_message",
                               "hello must be the first message")
        return

    if msg_type == "ping":
        await session.send("pong")
    elif msg_type == "resync":
        log.info("client requested resync")
        await session.send_snapshot()
    elif msg_type == "command":
        await _handle_command(session, model, msg, payload)
    else:
        await session.send("error", {"code": "unknown_type", "message": f"unknown type '{msg_type}'"})


async def _handle_command(session: Session, model: RigModel, msg: dict, payload: dict) -> None:
    cmd_id = payload.get("cmd_id")
    control = payload.get("control")
    action = payload.get("action", "set")
    value = payload.get("value")

    if not cmd_id or not control:
        await session.send(
            "ack",
            {"cmd_id": cmd_id, "status": "rejected", "reason": "cmd_id and control are required"},
        )
        return

    # A newer command on the same control supersedes an older one that arrived
    # out of order. The client drops superseded acks silently.
    seq = msg.get("seq", 0)
    if seq < session.last_command_seq.get(control, 0):
        await session.send("ack", {"cmd_id": cmd_id, "status": "superseded", "reason": None})
        return
    session.last_command_seq[control] = seq

    accepted, reason = model.apply_command(control, action, value)
    await session.send(
        "ack",
        {
            "cmd_id": cmd_id,
            "status": "accepted" if accepted else "rejected",
            "reason": reason,
        },
    )
    log.info(
        "command %s %s=%s -> %s%s",
        cmd_id, control, value,
        "accepted" if accepted else "rejected",
        f" ({reason})" if reason else "",
    )
    if accepted:
        await session.send_delta()


async def _pump(session: Session, model: RigModel, tick_hz: float,
                demo_interval: float = 0.0) -> None:
    """Drive the model and stream state to this client."""
    period = 1.0 / tick_hz
    last = time.monotonic()
    last_snapshot = time.monotonic()
    last_demo = time.monotonic()
    try:
        while True:
            await asyncio.sleep(period)
            if not session.hello_done:
                continue
            now = time.monotonic()
            dt, last = now - last, now

            if demo_interval > 0 and now - last_demo >= demo_interval:
                log.info("[bop demo] %s", model.demo_step())
                last_demo = now

            for event in model.tick(dt):
                await session.send("event", event)

            if now - last_snapshot >= SNAPSHOT_INTERVAL_S:
                await session.send_snapshot()
                last_snapshot = now
            else:
                await session.send_delta()
    except (asyncio.CancelledError, ConnectionClosed):
        pass


class DiscoveryResponder(asyncio.DatagramProtocol):
    """Answers the headset's UDP broadcast so nobody types an IP in VR.

    In the full system this lives in the agent, which is always running and
    therefore always discoverable. The mock carries it too so a headset can
    find the mock engine on its own during bring-up.
    """

    def __init__(self, engine_port: int) -> None:
        self.engine_port = engine_port
        self.transport = None

    def connection_made(self, transport) -> None:
        self.transport = transport

    def datagram_received(self, data: bytes, addr) -> None:
        try:
            msg = json.loads(data.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            return
        if msg.get("type") != "discover":
            return

        import socket as _socket
        reply = {
            "v": PROTOCOL_VERSION,
            "type": "discover.reply",
            "host": _socket.gethostname(),
            "agent_port": 8770,
            "engine_port": self.engine_port,
            "engine_running": True,
            "protocol": [PROTOCOL_VERSION],
        }
        log.info("discovery request from %s -- replying", addr[0])
        self.transport.sendto(json.dumps(reply).encode("utf-8"), addr)


async def main_async(host: str, port: int, tick_hz: float, discovery: bool,
                     demo_interval: float) -> None:
    model = RigModel()
    holder: dict = {"session": None}

    async def handler(ws, *_):  # tolerate websockets<14 passing a path argument
        await handle_client(ws, model, tick_hz, holder, demo_interval)

    transport = None
    if discovery:
        try:
            loop = asyncio.get_running_loop()
            transport, _ = await loop.create_datagram_endpoint(
                lambda: DiscoveryResponder(port),
                local_addr=("0.0.0.0", DISCOVERY_PORT),
                allow_broadcast=True,
            )
            log.info("discovery responder listening on udp/%d", DISCOVERY_PORT)
        except OSError as exc:
            log.warning("discovery unavailable (%s) -- set the address manually in Unity", exc)

    try:
        async with websockets.serve(handler, host, port, max_size=MAX_FRAME_BYTES):
            log.info("mock engine (protocol v%d) listening on ws://%s:%d at %g Hz",
                     PROTOCOL_VERSION, host, port, tick_hz)
            if host == "0.0.0.0":
                for addr in _local_addresses():
                    log.info("  reachable from the LAN at ws://%s:%d", addr, port)
            else:
                log.warning("bound to %s -- NOT reachable from a headset", host)
            await asyncio.Future()
    finally:
        if transport is not None:
            transport.close()


def _local_addresses() -> list[str]:
    """Best-effort list of this machine's LAN addresses, to save an ipconfig."""
    import socket as _socket
    found = []
    try:
        for info in _socket.getaddrinfo(_socket.gethostname(), None, _socket.AF_INET):
            addr = info[4][0]
            if not addr.startswith("127.") and addr not in found:
                found.append(addr)
    except OSError:
        pass
    return found or ["<run ipconfig>"]


def main() -> None:
    parser = argparse.ArgumentParser(description="VRSIM mock drilling engine (protocol v1)")
    parser.add_argument("--host", default="0.0.0.0", help="bind address (default: all interfaces)")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--tick-hz", type=float, default=20.0)
    parser.add_argument("--demo-bop", type=float, nargs="?", const=4.0, default=0.0,
                        metavar="SECONDS",
                        help="cycle BOP components so the panel lights can be watched "
                             "(default every 4s)")
    parser.add_argument("--no-discovery", action="store_true",
                        help="do not answer UDP discovery broadcasts")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s  %(levelname)-7s %(message)s",
        datefmt="%H:%M:%S",
    )
    try:
        asyncio.run(main_async(args.host, args.port, args.tick_hz,
                           not args.no_discovery, args.demo_bop))
    except KeyboardInterrupt:
        log.info("stopped")


if __name__ == "__main__":
    main()
