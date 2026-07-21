"""
Conformance test for VRSIM protocol v1.

Checks an engine implementation against docs/protocol.md section 9. Point it at
the mock engine to verify the mock, or at the real engine later to verify that.

    python conformance_test.py [--host 127.0.0.1] [--port 8765]

Exit code 0 = conformant, 1 = one or more checks failed.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
import time
from typing import Any

import websockets
from websockets.exceptions import ConnectionClosed

PROTOCOL_VERSION = 1

results: list[tuple[bool, str, str]] = []


def check(ok: bool, name: str, detail: str = "") -> None:
    results.append((ok, name, detail))
    print(f"  {'PASS' if ok else 'FAIL'}  {name}" + (f"  -- {detail}" if detail else ""))


def apply_merge_patch(target: Any, patch: Any) -> Any:
    """RFC 7386. The client must implement exactly this."""
    if not isinstance(patch, dict):
        return patch
    if not isinstance(target, dict):
        target = {}
    for key, value in patch.items():
        if value is None:
            target.pop(key, None)
        else:
            target[key] = apply_merge_patch(target.get(key), value)
    return target


async def recv_typed(ws, wanted: str, timeout: float = 5.0) -> dict:
    """Read until a message of the wanted type arrives."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        raw = await asyncio.wait_for(ws.recv(), timeout=max(0.1, deadline - time.monotonic()))
        msg = json.loads(raw)
        if msg.get("type") == wanted:
            return msg
    raise asyncio.TimeoutError(f"no '{wanted}' within {timeout}s")


async def send(ws, msg_type: str, payload: dict | None = None, seq: int = 1, v: int = PROTOCOL_VERSION):
    frame = {"v": v, "type": msg_type, "seq": seq, "ts": round(time.time(), 3)}
    if payload is not None:
        frame["payload"] = payload
    await ws.send(json.dumps(frame))


async def test_version_rejection(url: str) -> None:
    """Conformance 1: a mismatched v is closed with 4400, not tolerated."""
    try:
        async with websockets.connect(url) as ws:
            await send(ws, "hello", {"client": "conformance"}, v=99)
            try:
                while True:
                    await asyncio.wait_for(ws.recv(), timeout=3.0)
            except ConnectionClosed as exc:
                check(exc.code == 4400, "rejects unsupported version with 4400",
                      f"got close code {exc.code}")
                return
            except asyncio.TimeoutError:
                check(False, "rejects unsupported version with 4400", "connection stayed open")
    except Exception as exc:  # noqa: BLE001
        check(False, "rejects unsupported version with 4400", repr(exc))


async def test_handshake_and_state(url: str) -> None:
    """Conformance 2, 4, 5 and the resync half of 3."""
    try:
        async with websockets.connect(url) as ws:
            await send(ws, "hello", {"client": "conformance", "app_version": "test"})

            ack = await recv_typed(ws, "hello.ack")
            p = ack.get("payload", {})
            check("controls" in p and isinstance(p["controls"], list) and p["controls"],
                  "hello.ack declares the controllable surface",
                  f"{len(p.get('controls', []))} controls")
            check(isinstance(p.get("tick_hz"), (int, float)) and p["tick_hz"] > 0,
                  "hello.ack declares tick_hz", f"{p.get('tick_hz')} Hz")

            snap = await recv_typed(ws, "state.snapshot", timeout=3.0)
            state = snap["payload"]
            check(all(k in state for k in ("drilling", "pumps", "bop", "swaco")),
                  "snapshot follows hello.ack and is complete",
                  f"top-level keys: {sorted(state)}")

            # Conformance 5: deltas apply as merge patches and seq is monotonic.
            seqs, applied = [], json.loads(json.dumps(state))
            deadline = time.monotonic() + 4.0
            deltas = 0
            while time.monotonic() < deadline and deltas < 8:
                msg = json.loads(await asyncio.wait_for(ws.recv(), timeout=3.0))
                seqs.append(msg["seq"])
                if msg["type"] == "state.delta":
                    applied = apply_merge_patch(applied, msg["payload"])
                    deltas += 1
            check(deltas > 0, "streams state.delta", f"{deltas} deltas received")
            check(seqs == sorted(seqs) and len(set(seqs)) == len(seqs),
                  "seq is monotonic and gapless on the wire")
            check(isinstance(applied.get("drilling", {}).get("rpm"), (int, float)),
                  "merge-patched state stays well-formed")

            # Conformance 3: resync returns a full snapshot.
            await send(ws, "resync", {}, seq=2)
            resync = await recv_typed(ws, "state.snapshot", timeout=3.0)
            check(all(k in resync["payload"] for k in ("drilling", "pumps", "bop", "swaco")),
                  "resync returns a full snapshot")

            # Conformance 4: valid command is acked exactly once.
            await send(ws, "command",
                       {"cmd_id": "c-test-1", "control": "console.pump_1",
                        "action": "set", "value": "on"}, seq=3)
            ack1 = await recv_typed(ws, "ack", timeout=3.0)
            check(ack1["payload"]["cmd_id"] == "c-test-1"
                  and ack1["payload"]["status"] == "accepted",
                  "valid command is accepted and ack echoes cmd_id",
                  str(ack1["payload"]))

            # ...and the command actually changes the world.
            changed, deadline = False, time.monotonic() + 4.0
            while time.monotonic() < deadline and not changed:
                msg = json.loads(await asyncio.wait_for(ws.recv(), timeout=3.0))
                if msg["type"] in ("state.delta", "state.snapshot"):
                    applied = apply_merge_patch(applied, msg["payload"])
                    if applied["pumps"]["pump_1"]["active"] is True:
                        changed = True
            check(changed, "accepted command is reflected in subsequent state")

            # Conformance 4: rejection carries a human-readable reason.
            await send(ws, "command",
                       {"cmd_id": "c-test-2", "control": "bop.nonexistent",
                        "action": "set", "value": "open"}, seq=4)
            ack2 = await recv_typed(ws, "ack", timeout=3.0)
            rp = ack2["payload"]
            check(rp["status"] == "rejected" and isinstance(rp.get("reason"), str) and rp["reason"],
                  "unknown control is rejected with a readable reason",
                  repr(rp.get("reason")))

            # Out-of-range analog is rejected, not silently clamped.
            await send(ws, "command",
                       {"cmd_id": "c-test-3", "control": "swaco.choke",
                        "action": "set", "value": 5.0}, seq=5)
            ack3 = await recv_typed(ws, "ack", timeout=3.0)
            check(ack3["payload"]["status"] == "rejected",
                  "out-of-range analog value is rejected",
                  repr(ack3["payload"].get("reason")))

            # ping/pong liveness.
            await send(ws, "ping", {}, seq=6)
            await recv_typed(ws, "pong", timeout=3.0)
            check(True, "responds to ping with pong")

    except Exception as exc:  # noqa: BLE001
        check(False, "handshake and state exchange", repr(exc))


async def test_single_controller(url: str) -> None:
    """Protocol section 7: a second controlling client is refused with 4409."""
    try:
        async with websockets.connect(url) as first:
            await send(first, "hello", {"client": "conformance-a"})
            await recv_typed(first, "state.snapshot", timeout=3.0)
            try:
                async with websockets.connect(url) as second:
                    await send(second, "hello", {"client": "conformance-b"})
                    try:
                        while True:
                            await asyncio.wait_for(second.recv(), timeout=3.0)
                    except ConnectionClosed as exc:
                        check(exc.code == 4409, "second client refused with 4409",
                              f"got close code {exc.code}")
                        return
                    except asyncio.TimeoutError:
                        check(False, "second client refused with 4409", "second client was served")
            except ConnectionClosed as exc:
                check(exc.code == 4409, "second client refused with 4409",
                      f"got close code {exc.code}")
    except Exception as exc:  # noqa: BLE001
        check(False, "second client refused with 4409", repr(exc))


async def test_malformed(url: str) -> None:
    """Malformed input closes 4401 rather than crashing the engine."""
    try:
        async with websockets.connect(url) as ws:
            await ws.send("this is not json")
            try:
                while True:
                    await asyncio.wait_for(ws.recv(), timeout=3.0)
            except ConnectionClosed as exc:
                check(exc.code == 4401, "malformed frame closed with 4401",
                      f"got close code {exc.code}")
                return
            except asyncio.TimeoutError:
                check(False, "malformed frame closed with 4401", "connection stayed open")
    except Exception as exc:  # noqa: BLE001
        check(False, "malformed frame closed with 4401", repr(exc))


async def main_async(host: str, port: int) -> int:
    url = f"ws://{host}:{port}"
    print(f"VRSIM protocol v{PROTOCOL_VERSION} conformance -- {url}\n")

    print("version negotiation")
    await test_version_rejection(url)
    print("\nhandshake, state and commands")
    await test_handshake_and_state(url)
    await asyncio.sleep(0.3)  # let the engine release the control slot
    print("\ncontrol arbitration")
    await test_single_controller(url)
    await asyncio.sleep(0.3)
    print("\nrobustness")
    await test_malformed(url)

    passed = sum(1 for ok, _, _ in results if ok)
    total = len(results)
    print(f"\n{passed}/{total} checks passed")
    if passed != total:
        print("\nfailed:")
        for ok, name, detail in results:
            if not ok:
                print(f"  - {name}" + (f"  ({detail})" if detail else ""))
    return 0 if passed == total else 1


def main() -> None:
    parser = argparse.ArgumentParser(description="VRSIM protocol v1 conformance test")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    sys.exit(asyncio.run(main_async(args.host, args.port)))


if __name__ == "__main__":
    main()
