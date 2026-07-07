#!/usr/bin/env python3
"""loxone2mqtt - bridge one or more Loxone Miniservers to a plain MQTT broker.

Standalone tool, in the spirit of zigbee2mqtt. A Miniserver speaks a token-authenticated
WebSocket protocol (RSA/AES handshake + binary event tables); that protocol is owned by
`pyloxone-api` via `loxone_client`. This sidecar republishes each control's flattened state to
<base>/<serial>/<uuid> (retained), publishes the structure + rooms manifests, forwards commands
published to <base>/<serial>/<uuid>/set, and re-publishes the structure on change (re-sync).

Everything is configured by environment variables (12-factor); nothing is hard-coded.
"""
import colorsys
import json
import os
import threading
import time

BASE_TOPIC = os.environ.get("MQTT_BASE_TOPIC", "loxone2mqtt").strip("/")
MQTT_HOST = os.environ.get("MQTT_HOST", "emqx")
MQTT_PORT = int(os.environ.get("MQTT_PORT", "1883"))
MQTT_USERNAME = os.environ.get("MQTT_USERNAME") or None
MQTT_PASSWORD = os.environ.get("MQTT_PASSWORD") or None
MONGO_URI = os.environ.get("MONGO_URI", "mongodb://mongodb:27017")
MONGO_DB = os.environ.get("MONGO_DB", "vidar")
POLL_INTERVAL = int(os.environ.get("LOXONE_POLL_INTERVAL", "30"))


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


# ── Pure structure / state / command helpers (unit-tested) ─────────────────────
# These own the Loxone→Vidar contract. Field names/types mirror LoxoneStructureParser,
# LoxoneStateMapper, and LoxoneCommandBuilder on the worker side. Verified at live E2E.

_PHASE_A_TYPES = {"Switch", "Pushbutton", "Dimmer", "LightControllerV2",
                  "PresenceDetector", "SmokeAlarm", "Touch"}

# Phase B: ColorPickerV2 (split into ColorPickerRGBW/ColorPickerTunableWhite/pre-split Dimmers by
# classify_colorpicker below) and IRoomControllerV2 (normalized to RoomControllerV2).
_SUPPORTED_TYPES = _PHASE_A_TYPES | {"ColorPickerV2", "IRoomControllerV2"}


def hex_to_hsv(hex_str: str):
    """"#RRGGBB" -> (h, s, v) with h in 0..360, s/v in 0..100 (standard HSV, colorsys-backed)."""
    s = hex_str.lstrip("#")
    r, g, b = (int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    h, sat, val = colorsys.rgb_to_hsv(r, g, b)
    return h * 360.0, sat * 100.0, val * 100.0


def hsv_to_hex(h, s, v):
    """(h in 0..360, s/v in 0..100) -> "#RRGGBB"."""
    r, g, b = colorsys.hsv_to_rgb((h % 360) / 360.0, s / 100.0, v / 100.0)
    return "#{:02X}{:02X}{:02X}".format(round(r * 255), round(g * 255), round(b * 255))


def classify_colorpicker(control: dict) -> str:
    """Decide a ColorPickerV2's mode from LoxAPP3 details: "ColorPickerRGBW" |
    "ColorPickerTunableWhite" | "single" (single-channel, pre-split into Dimmers by the caller).

    LIVE-CAPTURE: the exact detail key Loxone uses to mark a ColorPickerV2's capability is
    confirmed at E2E (Task 13-equivalent for this sidecar) -- this is the single correction point
    for that discovery. Best-effort heuristic reading `details.pickerType`/`details.colorMode`;
    defaults to ColorPickerRGBW (the most common case) when ambiguous."""
    details = control.get("details", {}) or {}
    picker = str(details.get("pickerType", details.get("colorMode", ""))).lower()
    if "temp" in picker or "white" in picker or "tunable" in picker or "lumitech" in picker:
        return "ColorPickerTunableWhite"
    if "single" in picker or "channel" in picker:
        return "single"
    return "ColorPickerRGBW"


def build_structure(loxapp3: dict, serial: str) -> dict:
    rooms = [{"uuid": uid, "name": (r or {}).get("name", uid)}
             for uid, r in (loxapp3.get("rooms") or {}).items()]
    controls = []
    for uid, c in (loxapp3.get("controls") or {}).items():
        ctype = c.get("type")
        if ctype not in _SUPPORTED_TYPES:
            continue  # present-fields: skip unsupported types
        if ctype == "IRoomControllerV2":
            ctype = "RoomControllerV2"
        elif ctype == "ColorPickerV2":
            mode = classify_colorpicker(c)
            if mode == "single":
                # Single-channel picker: pre-split into 4 independent Dimmer controls so the
                # worker needs no split logic of its own (uuid "<uuid>/r|g|b|w").
                for ch in ("r", "g", "b", "w"):
                    controls.append({"uuid": f"{uid}/{ch}",
                                     "name": f"{c.get('name', uid)} {ch.upper()}",
                                     "type": "Dimmer", "room": c.get("room")})
                continue
            ctype = mode
        entry = {"uuid": uid, "name": c.get("name", uid), "type": ctype, "room": c.get("room")}
        moods = (((c.get("details") or {}).get("moodList")) or [])
        if moods:
            entry["moods"] = [{"id": m.get("id"), "name": m.get("name", str(m.get("id")))}
                              for m in moods if "id" in m]
        controls.append(entry)
    return {"serial": serial, "controls": controls, "rooms": rooms}


def flatten_control_state(control_type: str, states: dict) -> dict:
    """Map a control's resolved state values to the named fields the worker expects.
    `states` is already {state_name: value} (loxone_client resolves state uuids to names)."""
    if control_type in ("Switch", "Pushbutton"):
        return _pick(states, {"active": "active"})
    if control_type == "Dimmer":
        return _pick(states, {"active": "active", "position": "position"})
    if control_type == "LightControllerV2":
        out = {}
        moods = states.get("activeMoods")
        if isinstance(moods, list) and moods:
            out["activeMood"] = moods[0]
        elif "activeMood" in states:
            out["activeMood"] = states["activeMood"]
        if "active" in states:
            out["active"] = states["active"]
        return out
    if control_type == "PresenceDetector":
        return _pick(states, {"active": "active", "brightness": "brightness"})
    if control_type == "SmokeAlarm":
        return _pick(states, {"active": "active", "battery": "battery", "tamper": "tamper"})
    if control_type == "Touch":
        return _pick(states, {"action": "action"})
    if control_type == "ColorPickerRGBW":
        return _pick(states, {"active": "active", "position": "position",
                              "color": "color", "white": "white"})
    if control_type == "ColorPickerTunableWhite":
        return _pick(states, {"active": "active", "position": "position", "colortemp": "colortemp"})
    if control_type == "RoomControllerV2":
        return _pick(states, {"tempActual": "tempActual", "tempTarget": "tempTarget",
                              "mode": "mode", "valve": "valve"})
    return {}


def _pick(states: dict, mapping: dict) -> dict:
    return {dst: states[src] for src, dst in mapping.items() if src in states}


def command_url(uuid: str, command: str) -> str:
    # The worker emits the full command verb: "on"/"off", a bare brightness percent, or
    # "changeTo/<moodId>" for a LightControllerV2 mood. All forms map straight into the Loxone
    # command URL — the worker owns the verb, the sidecar just wraps it.
    return f"jdev/sps/io/{uuid}/{command}"


# ── Miniserver bridge (one LoxoneClient per Miniserver) ────────────────────────
# NOTE: LoxoneClient (loxone_client.py) wraps `pyloxone-api` and is imported lazily inside
# MiniserverBridge.start(), never at module top-level, so `import loxone2mqtt` succeeds even
# when pyloxone-api isn't installed (as in this pytest environment).

class MiniserverBridge:
    """Owns one Miniserver's LoxoneClient connection and republishes its structure/state to the
    target broker, keyed by serial. Mirrors dreo2mqtt.AccountBridge's lifecycle (start/stop on a
    background thread, reconnect with backoff), but per-Miniserver rather than per-account."""

    def __init__(self, serial, host, user, password, target):
        self.serial = serial
        self.host = host
        self.user = user
        self.password = password
        self.target = target
        self.client = None
        self._stop = threading.Event()
        # Guards handoff of `self.client` between _run (owns the connect/reconnect loop) and
        # stop()/forward_command (may be called from other threads) so exactly one caller closes
        # any given LoxoneClient instance, never zero and never twice.
        self._client_lock = threading.Lock()
        # uuid -> control type, learned from the last structure fetch. Needed to flatten each
        # state callback (flatten_control_state is keyed by control type).
        self._control_types = {}
        self._types_lock = threading.Lock()

    def start(self):
        threading.Thread(target=self._run, name=f"loxone-{self.serial}", daemon=True).start()

    def stop(self):
        self._stop.set()
        self._close_client()

    def _close_client(self):
        """Detach and close the current client, if any. Safe to call from multiple threads and
        multiple times: `self.client` is swapped out under a lock so only the caller that wins
        the swap actually invokes `.close()` on a given instance."""
        with self._client_lock:
            client, self.client = self.client, None
        if client is not None:
            try:
                client.close()
            except Exception:  # noqa: BLE001
                pass

    def _run(self):
        from loxone_client import LoxoneClient

        backoff = 15
        while not self._stop.is_set():
            try:
                self.client = LoxoneClient(self.host, self.user, self.password)
                self.client.connect()
                log(f"[{self.serial}] connected to Miniserver at {self.host}")
                self._publish_structure()
                self.client.on_state(self._on_state)
                self.client.on_structure_changed(self._on_structure_changed)
                backoff = 15
                # LoxoneClient.run_forever() blocks until the connection ends (pyloxone-api's
                # api.start() owns its own internal reconnect retries); when it returns, fall
                # through to this bridge's own backoff+retry of connect() itself.
                self.client.run_forever()
            except Exception as e:  # noqa: BLE001
                log(f"[{self.serial}] connection error: {e}; retry in {backoff}s")
            finally:
                # Whether connect() failed outright or run_forever() returned because the
                # connection dropped, the client's event-loop thread + structure-poll thread are
                # still alive until .close() runs. Close it here, before looping back to create
                # the next LoxoneClient, so a dropped connection never leaks those threads.
                self._close_client()
            if self._stop.is_set():
                break
            self._stop.wait(backoff)
            backoff = min(backoff * 2, 120)

    def _publish_structure(self):
        loxapp3 = self.client.get_structure()
        structure = build_structure(loxapp3, self.serial)
        with self._types_lock:
            self._control_types = {c["uuid"]: c["type"] for c in structure["controls"]}
        self.target.publish(f"{BASE_TOPIC}/{self.serial}/structure", json.dumps(structure),
                            qos=0, retain=True)
        self.target.publish(f"{BASE_TOPIC}/{self.serial}/rooms", json.dumps(structure["rooms"]),
                            qos=0, retain=True)
        log(f"[{self.serial}] published structure "
            f"({len(structure['controls'])} controls, {len(structure['rooms'])} rooms)")

    def _on_structure_changed(self):
        log(f"[{self.serial}] structure changed; re-fetching + republishing (re-sync)")
        try:
            self._publish_structure()
        except Exception as e:  # noqa: BLE001
            log(f"[{self.serial}] structure re-fetch failed: {e}")

    def _on_state(self, control_uuid, control_type, states):
        with self._types_lock:
            self._control_types[control_uuid] = control_type
        flat = flatten_control_state(control_type, states)
        if not flat:
            return
        self.target.publish(f"{BASE_TOPIC}/{self.serial}/{control_uuid}", json.dumps(flat),
                            qos=0, retain=True)

    def forward_command(self, uuid, command):
        with self._types_lock:
            known = uuid in self._control_types
        client = self.client
        if not known:
            log(f"[{self.serial}] command for unknown control {uuid} dropped")
            return
        if not client or not client.is_connected:
            log(f"[{self.serial}] command for {uuid} dropped (client not connected)")
            return
        try:
            client.send_command(uuid, command_url(uuid, command))
            log(f"[{self.serial}] sent {uuid} -> {command}")
        except Exception as e:  # noqa: BLE001
            log(f"[{self.serial}] command failed: {e}")


class BridgeManager:
    """Owns the target MQTT client and the current {serial: MiniserverBridge} set. Mirrors
    dreo2mqtt.BridgeManager: both the Mongo poll loop (cold start) and a future onboarding
    handler (Task 9, live re-onboard) can call ensure_started/replace so a manifest change takes
    effect without a container restart. All swaps are serialized by a lock."""

    def __init__(self, target):
        self.target = target
        self._lock = threading.Lock()
        self.bridges = {}  # serial -> MiniserverBridge

    def ensure_started(self, miniservers):
        """Cold-start path: start a bridge for every miniserver not already running. Returns the
        list of serials newly started."""
        started = []
        with self._lock:
            for ms in miniservers:
                serial = ms["serial"]
                if serial in self.bridges:
                    continue
                bridge = MiniserverBridge(serial, ms["host"], ms.get("user", ""),
                                          ms.get("password", ""), self.target)
                self.bridges[serial] = bridge
                bridge.start()
                started.append(serial)
        return started

    def replace(self, miniservers):
        """Live re-onboard path: stop every running bridge and start fresh ones for the given
        manifest. Called after a successful onboarding call (Task 9) so new credentials take
        effect immediately."""
        with self._lock:
            old = dict(self.bridges)
            self.bridges = {}
            for ms in miniservers:
                serial = ms["serial"]
                bridge = MiniserverBridge(serial, ms["host"], ms.get("user", ""),
                                          ms.get("password", ""), self.target)
                self.bridges[serial] = bridge
                bridge.start()
        for bridge in old.values():
            bridge.stop()
        log(f"bridges (re)started for {len(self.bridges)} Miniserver(s)")

    def replace_one(self, ms):
        """Live re-onboard path for a SINGLE Miniserver (used by the onboarding HTTP handler,
        Task 9): stop and replace just that serial's bridge, leaving every other running
        Miniserver bridge untouched. Deliberately narrower than `replace()`/dreo2mqtt's
        single-account `BridgeManager.replace` -- a Vidar install can bridge several Miniservers
        at once, and a POST /auth/login validating one of them must not tear down the others."""
        serial = ms["serial"]
        bridge = MiniserverBridge(serial, ms["host"], ms.get("user", ""),
                                  ms.get("password", ""), self.target)
        with self._lock:
            old = self.bridges.get(serial)
            self.bridges[serial] = bridge
            bridge.start()
        if old is not None:
            old.stop()
        log(f"[{serial}] bridge (re)started from onboarding")

    def forward_command(self, serial, uuid, command):
        bridge = self.bridges.get(serial)
        if bridge is not None:
            bridge.forward_command(uuid, command)
        else:
            log(f"[{serial}] command for {uuid} dropped (no active bridge)")


# ── Onboarding (validate a Miniserver + live-(re)start its bridge) ─────────────
# Runs in-process as an HTTP server the Vidar host calls at http://loxone2mqtt:<port>, gated by
# LOXONE_ONBOARD_HTTP=1. Mirrors dreo2mqtt's onboarding server, adapted from "one cloud account"
# to "one Miniserver per POST" (see BridgeManager.replace_one above for why that's scoped
# per-serial rather than reusing the full-fleet `replace`).

ONBOARD_PORT = int(os.environ.get("LOXONE_ONBOARD_PORT", "8897"))


def probe_miniserver(host, user, password) -> dict:
    """Connect to a Miniserver, fetch its structure, and return the summary the host shows during
    onboarding: {"serial", "controlCount", "roomCount"}. Raises on connection/handshake failure
    (mapped to an HTTP status by `_auth_status_code`); the client is always closed, whether the
    probe succeeds or fails, so a failed probe never leaks the connection's background threads."""
    from loxone_client import LoxoneClient

    client = LoxoneClient(host, user, password)
    try:
        client.connect()
        loxapp3 = client.get_structure()
        serial = (loxapp3.get("msInfo") or {}).get("serialNr", "")
        structure = build_structure(loxapp3, serial)
        return {"serial": serial, "controlCount": len(structure["controls"]),
                "roomCount": len(structure["rooms"])}
    finally:
        client.close()


def _auth_status_code(exc):
    """Map a Miniserver probe failure to an HTTP status the host understands, mirroring
    dreo2mqtt._auth_status_code. pyloxone-api's HTTP calls (the LoxAPP3.json fetch and the
    public-key/token exchange during the handshake) run over httpx; an httpx.HTTPStatusError with
    a 401/403 response means the Miniserver rejected the credentials outright -> 401.

    Everything else is treated as an upstream connectivity problem (502), NOT a credentials
    problem -- including the plain `ConnectionError` that `LoxoneClient.connect()` itself raises
    when the RSA/AES handshake fails for ANY reason. That conflation is a genuine library
    limitation, not a guess papered over: pyloxone-api's `async_init()` returns a bare bool with
    no separate signal for "bad password" vs. "handshake failed some other way", so narrowing a
    bare ConnectionError to 401 would be inventing a distinction the library doesn't expose.
    Confirming (or replacing) this mapping against a real Miniserver's actual rejection response
    is Task 13's E2E boundary, not this one's."""
    try:
        import httpx
    except ImportError:
        return 502
    if isinstance(exc, httpx.HTTPStatusError) and exc.response is not None:
        if exc.response.status_code in (401, 403):
            return 401
    return 502


def start_onboarding_server(manager=None):
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

    class OnboardHandler(BaseHTTPRequestHandler):
        def _send(self, code, obj):
            body = json.dumps(obj).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def _read_body(self):
            te = (self.headers.get("Transfer-Encoding") or "").lower()
            if "chunked" in te:
                chunks = []
                while True:
                    line = self.rfile.readline().strip()
                    if not line:
                        continue
                    size = int(line.split(b";")[0], 16)
                    if size == 0:
                        self.rfile.readline()  # trailing CRLF
                        break
                    chunks.append(self.rfile.read(size))
                    self.rfile.readline()  # CRLF after each chunk
                return b"".join(chunks)
            length = int(self.headers.get("Content-Length", 0) or 0)
            return self.rfile.read(length) if length else b""

        def do_POST(self):
            raw = self._read_body() or b"{}"
            try:
                req = json.loads(raw or b"{}")
            except Exception:  # noqa: BLE001
                self._send(400, {"error": "invalid json"})
                return
            path = self.path.rstrip("/")
            try:
                if path == "/auth/login":
                    host, user, password = req["host"], req["user"], req["password"]
                    summary = probe_miniserver(host, user, password)
                    self._send(200, summary)
                    # Push: (re)start THIS Miniserver's bridge immediately so re-onboarding takes
                    # effect without a container restart. A failure here must NOT fail the login
                    # response -- the host still persists to Mongo and a restart (cold-start
                    # path) recovers.
                    if manager is not None:
                        try:
                            manager.replace_one({"serial": summary["serial"], "host": host,
                                                 "user": user, "password": password})
                        except Exception as e:  # noqa: BLE001
                            log(f"bridge (re)start after login failed: {e}")
                else:
                    self._send(404, {"error": "not found"})
            except Exception as e:  # noqa: BLE001
                code = _auth_status_code(e)
                log(f"onboard {path} -> {code}: {e}")
                self._send(code, {"error": str(e)})

        def log_message(self, *a):  # silence default request logging
            pass

    srv = ThreadingHTTPServer(("0.0.0.0", ONBOARD_PORT), OnboardHandler)
    threading.Thread(target=srv.serve_forever, name="onboard-http", daemon=True).start()
    log(f"onboarding HTTP server listening on :{ONBOARD_PORT}")


# ── Mongo manifest (cold start) ─────────────────────────────────────────────────

def load_miniservers_from_mongo():
    """Return the list of miniserver dicts ({serial, host, user, password}) from
    vidar.integrations/_id:'loxone' -> Settings["miniservers"] (a JSON array, mirroring
    LoxoneBridgeActor.ParseManifest on the worker side)."""
    try:
        from pymongo import MongoClient
    except ImportError:
        return []
    try:
        client = MongoClient(MONGO_URI, serverSelectionTimeoutMS=5000)
        cfg = client[MONGO_DB]["integrations"].find_one({"_id": "loxone"})
    except Exception as e:  # noqa: BLE001
        log("Mongo read failed:", e)
        return []
    if not cfg:
        return []
    settings = cfg.get("Settings", {}) or {}
    try:
        miniservers = json.loads(settings.get("miniservers", "[]"))
    except (ValueError, TypeError):
        return []
    return [m for m in miniservers if m.get("serial") and m.get("host")]


# ── Main: poll Mongo and bridge every configured Miniserver ─────────────────────

def main():
    import paho.mqtt.client as mqtt

    target = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="loxone2mqtt")
    if MQTT_USERNAME:
        target.username_pw_set(MQTT_USERNAME, MQTT_PASSWORD)

    manager = BridgeManager(target)

    if os.environ.get("LOXONE_ONBOARD_HTTP") == "1":
        # Started early and unconditionally: onboarding must work even before any Mongo manifest
        # exists, mirroring dreo2mqtt's crash-loop-before-onboarding fix. Passing `manager` lets a
        # successful /auth/login (re)start that Miniserver's bridge immediately -- no container
        # restart needed to re-onboard.
        start_onboarding_server(manager)

    def on_connect(cl, u, flags, rc, props=None):
        log(f"target broker connected ({rc})")
        cl.subscribe(f"{BASE_TOPIC}/+/+/set")

    def on_message(cl, u, msg):
        parts = msg.topic.split("/")
        # <base>/<serial>/<uuid>/set
        if len(parts) >= 4 and parts[-1] == "set":
            serial, uuid = parts[-3], parts[-2]
            manager.forward_command(serial, uuid, msg.payload.decode(errors="replace"))

    target.on_connect = on_connect
    target.on_message = on_message

    # EMQX may not be ready the instant this starts; retry the broker connect.
    while True:
        try:
            target.connect(MQTT_HOST, MQTT_PORT, keepalive=30)
            break
        except Exception as e:  # noqa: BLE001
            log(f"broker connect failed ({e}); retry in 5s")
            time.sleep(5)
    target.loop_start()

    # Cold-start path: if the container comes up with an already-onboarded manifest in Mongo,
    # start a bridge for every miniserver not yet running. Live re-onboarding (Task 9) is pushed
    # by the onboarding handler (manager.replace), so this loop only needs to cover "not running
    # yet" additions.
    logged_waiting = False
    while True:
        miniservers = load_miniservers_from_mongo()
        if miniservers:
            started = manager.ensure_started(miniservers)
            if started:
                log(f"started bridge(s) for {started}")
            logged_waiting = False
        elif not logged_waiting:
            log("no Loxone manifest in Mongo yet; waiting for onboarding")
            logged_waiting = True
        time.sleep(POLL_INTERVAL)


if __name__ == "__main__":
    main()
