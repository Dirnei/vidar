#!/usr/bin/env python3
"""loxone2mqtt - bridge one or more Loxone Miniservers to a plain MQTT broker.

Standalone tool, in the spirit of zigbee2mqtt. A Miniserver speaks a token-authenticated
WebSocket protocol (RSA/AES handshake + binary event tables); that protocol is owned by
`pyloxone-api` via `loxone_client`. This sidecar republishes each control's flattened state to
<base>/<serial>/<uuid> (retained), publishes the structure + rooms manifests, forwards commands
published to <base>/<serial>/<uuid>/set, and re-publishes the structure on change (re-sync).

Everything is configured by environment variables (12-factor); nothing is hard-coded.
"""
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


def build_structure(loxapp3: dict, serial: str) -> dict:
    rooms = [{"uuid": uid, "name": (r or {}).get("name", uid)}
             for uid, r in (loxapp3.get("rooms") or {}).items()]
    controls = []
    for uid, c in (loxapp3.get("controls") or {}).items():
        ctype = c.get("type")
        if ctype not in _PHASE_A_TYPES:
            continue  # present-fields: skip unsupported types
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
        # uuid -> control type, learned from the last structure fetch. Needed to flatten each
        # state callback (flatten_control_state is keyed by control type).
        self._control_types = {}
        self._types_lock = threading.Lock()

    def start(self):
        threading.Thread(target=self._run, name=f"loxone-{self.serial}", daemon=True).start()

    def stop(self):
        self._stop.set()
        if self.client:
            try:
                self.client.close()
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
        if not self.client or not known:
            log(f"[{self.serial}] command for unknown control {uuid} dropped")
            return
        try:
            self.client.send_command(uuid, command_url(uuid, command))
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

    def forward_command(self, serial, uuid, command):
        bridge = self.bridges.get(serial)
        if bridge is not None:
            bridge.forward_command(uuid, command)
        else:
            log(f"[{serial}] command for {uuid} dropped (no active bridge)")


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
