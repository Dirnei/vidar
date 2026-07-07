#!/usr/bin/env python3
"""dreo2mqtt - bridge a Dreo cloud account's devices to a plain MQTT broker.

Standalone tool, in the spirit of zigbee2mqtt. Dreo devices (ceiling fans etc.) only speak
to Dreo's cloud (REST login/device-list + a realtime WebSocket); this sidecar owns that cloud
protocol via `dreocloud` (Task 8's pure helpers) and republishes each device's reported state
to a normal broker the rest of the stack can consume.

For each onboarded device it:
  - holds one shared WebSocket connection per account (`dreocloud.ws_url`),
  - republishes each device's flattened reported state to <base>/<serial> (retained), and
  - forwards commands published to <base>/<serial>/set to the device via `control_envelope`.

It also serves a small onboarding HTTP endpoint (gated by DREO_ONBOARD_HTTP=1) that the Vidar
host calls during account linking:
  POST /auth/login -> {"userDataJson": <token>, "region": <resolved_region>, "devices": [...]}.

Everything is configured by environment variables (12-factor); nothing is hard-coded.
"""
import json
import os
import threading
import time

import dreocloud

BASE_TOPIC = os.environ.get("MQTT_BASE_TOPIC", "dreo2mqtt").strip("/")
MQTT_HOST = os.environ.get("MQTT_HOST", "emqx")
MQTT_PORT = int(os.environ.get("MQTT_PORT", "1883"))
MQTT_USERNAME = os.environ.get("MQTT_USERNAME") or None
MQTT_PASSWORD = os.environ.get("MQTT_PASSWORD") or None
MONGO_URI = os.environ.get("MONGO_URI", "mongodb://mongodb:27017")
MONGO_DB = os.environ.get("MONGO_DB", "vidar")
DEFAULT_REGION = os.environ.get("DREO_DEFAULT_REGION", "us")
POLL_INTERVAL = int(os.environ.get("DREO_POLL_INTERVAL", "30"))


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


# ── Reported-state extraction ─────────────────────────────────────────────────
# Kept isolated in this one small pure function so the exact WS envelope (confirmed only at
# live E2E) can be corrected in one place without touching the rest of the bridge.

def extract_reported(msg: dict):
    """Return (serial, flattened_state_dict) from an inbound WS message, or None if the
    message doesn't identify a device. Tries msg["reported"], then msg["data"]["reported"],
    then msg["data"], in that order."""
    if not isinstance(msg, dict):
        return None
    serial = msg.get("devicesn")
    if not serial:
        return None
    if isinstance(msg.get("reported"), dict):
        return serial, msg["reported"]
    data = msg.get("data")
    if isinstance(data, dict):
        if isinstance(data.get("reported"), dict):
            return serial, data["reported"]
        return serial, data
    return None


# ── Account / manifest ─────────────────────────────────────────────────────────

def load_manifest_from_mongo():
    """Return (token, [manifest dicts], region) from vidar.integrations/_id:'dreo'."""
    try:
        from pymongo import MongoClient
    except ImportError:
        return None, [], None
    try:
        client = MongoClient(MONGO_URI, serverSelectionTimeoutMS=5000)
        cfg = client[MONGO_DB]["integrations"].find_one({"_id": "dreo"})
    except Exception as e:  # noqa: BLE001
        log("Mongo read failed:", e)
        return None, [], None
    if not cfg:
        return None, [], None
    settings = cfg.get("Settings", {}) or {}
    token = settings.get("account.token")
    devices = []
    try:
        devices = json.loads(settings.get("account.manifest", "[]"))
    except (ValueError, TypeError):
        pass
    region = settings.get("account.region")
    return token, devices, region


def _oauth_login(email, password, region):
    import requests

    resp = requests.post(f"{dreocloud.api_base(region)}/api/oauth/login",
                         params={"timestamp": dreocloud.api_timestamp()},
                         json=dreocloud.login_body(email, password),
                         headers=dreocloud.app_headers(), timeout=20)
    resp.raise_for_status()
    return resp.json()


def login(email, password, region=None):
    """POST /api/oauth/login against `region` (default DEFAULT_REGION) with the Dreo app
    headers/credentials + MD5 password (see dreocloud); if the response names a different
    region, re-login against it once. Returns (token, region)."""
    region = region or DEFAULT_REGION
    token, resp_region = dreocloud.parse_login(_oauth_login(email, password, region))
    if resp_region and resp_region != region:
        region = resp_region
        token, _ = dreocloud.parse_login(_oauth_login(email, password, region))
    return token, region


def fetch_devices(token, region):
    import requests

    resp = requests.get(f"{dreocloud.api_base(region)}/api/v2/user-device/device/list",
                        params={"timestamp": dreocloud.api_timestamp()},
                        headers=dreocloud.app_headers(token), timeout=20)
    resp.raise_for_status()
    return dreocloud.parse_devices(resp.json())


def fetch_state(token, region, serial):
    """GET the current device state and flatten data.mixed into {field: value}. The realtime WS
    only pushes CHANGES, so this snapshot is what populates the twin on (re)connect."""
    import requests

    resp = requests.get(f"{dreocloud.api_base(region)}/api/user-device/device/state",
                        params={"deviceSn": serial, "timestamp": dreocloud.api_timestamp()},
                        headers=dreocloud.app_headers(token), timeout=20)
    resp.raise_for_status()
    return dreocloud.flatten_state(resp.json())


# ── Realtime bridge (single WebSocket per account) ─────────────────────────────

class AccountBridge:
    """Owns the account's single realtime WebSocket and republishes every device's
    reported state to the target broker, keyed by serial."""

    def __init__(self, token, region, target):
        self.token = token
        self.region = region
        self.target = target
        self.ws = None
        self._stop = threading.Event()
        # Full flat state per serial. The WS pushes only CHANGED fields, so we merge them into
        # the last snapshot and always publish the COMPLETE state (retained) — otherwise each
        # single-field publish would overwrite the retained topic and consumers would lose the
        # rest of the state on reconnect.
        self._states = {}
        self._states_lock = threading.Lock()

    def start(self):
        threading.Thread(target=self._run, name="dreo-ws", daemon=True).start()

    def stop(self):
        self._stop.set()
        if self.ws:
            try:
                self.ws.close()
            except Exception:  # noqa: BLE001
                pass

    def _run(self):
        import websocket

        backoff = 15
        while not self._stop.is_set():
            try:
                url = dreocloud.ws_url(self.region, self.token)
                self.ws = websocket.WebSocketApp(
                    url,
                    on_open=self._on_open,
                    on_message=self._on_message,
                    on_error=lambda ws, err: log(f"WS error: {err}"),
                )
                log("connecting to Dreo realtime WS")
                self.ws.run_forever()
                backoff = 15
            except Exception as e:  # noqa: BLE001
                log(f"WS error: {e}; retry in {backoff}s")
            if self._stop.is_set():
                break
            self._stop.wait(backoff)
            backoff = min(backoff * 2, 120)

    def _on_open(self, ws):
        log("Dreo realtime WS connected")
        # Dreo keepalive is APPLICATION-level: send the text frame "2" every 15s or the server
        # drops the socket (~60s). This is NOT a protocol ping frame. One ping thread per
        # connection; it exits when the socket closes (send raises) or on shutdown.
        def ping_loop():
            while not self._stop.is_set():
                try:
                    ws.send("2")
                except Exception:  # noqa: BLE001
                    return
                if self._stop.wait(15):
                    return
        threading.Thread(target=ping_loop, name="dreo-ws-ping", daemon=True).start()
        # The WS only pushes changes, so publish a REST state snapshot per device on (re)connect.
        threading.Thread(target=self._publish_initial_state, name="dreo-init-state",
                         daemon=True).start()

    def _publish_initial_state(self):
        try:
            for dev in fetch_devices(self.token, self.region):
                serial = dev["serial"]
                flat = fetch_state(self.token, self.region, serial)
                if flat:
                    self._merge_and_publish(serial, flat)
        except Exception as e:  # noqa: BLE001
            log(f"initial-state fetch failed: {e}")

    def _merge_and_publish(self, serial, fields):
        """Merge changed fields into the device's full state and publish the COMPLETE snapshot
        (retained), so the retained topic always holds the whole state."""
        with self._states_lock:
            state = self._states.setdefault(serial, {})
            state.update(fields)
            snapshot = dict(state)
        self.target.publish(f"{BASE_TOPIC}/{serial}", json.dumps(snapshot), qos=0, retain=True)
        log(f"published {BASE_TOPIC}/{serial} ({len(snapshot)} fields): {json.dumps(fields)[:200]}")

    def _on_message(self, ws, message):
        # Ignore the keepalive echo (server may reply to our "2" ping with a short token).
        if isinstance(message, str) and message.strip() in ("2", "3", ""):
            return
        try:
            msg = json.loads(message)
        except Exception as e:  # noqa: BLE001
            log(f"bad WS message: {e}")
            return
        result = extract_reported(msg)
        if result is None:
            return
        serial, reported = result
        # control-reply frames echo command errors ({error_msg, error_code}); those are not
        # device state — never merge them into the snapshot.
        if "error_code" in reported or "error_msg" in reported:
            log(f"[{serial}] control error: {reported}")
            return
        self._merge_and_publish(serial, reported)

    def forward_command(self, serial, payload):
        try:
            params = json.loads(payload)
        except Exception as e:  # noqa: BLE001
            log(f"[{serial}] bad command: {e}")
            return
        if not self.ws or not getattr(self.ws, "sock", None):
            log(f"[{serial}] command dropped (WS not connected)")
            return
        env = dreocloud.control_envelope(serial, params, int(time.time() * 1000))
        try:
            self.ws.send(json.dumps(env))
            log(f"[{serial}] sent {params}")
        except Exception as e:  # noqa: BLE001
            log(f"[{serial}] send failed: {e}")


class BridgeManager:
    """Owns the target MQTT client and the current AccountBridge, and lets BOTH the Mongo
    poll loop (cold start) and the onboarding HTTP handler (live re-onboard) start or replace
    the bridge — so re-onboarding with a new token/region takes effect without a container
    restart. All bridge swaps are serialized by a lock."""

    def __init__(self, target):
        self.target = target
        self._lock = threading.Lock()
        self.account = None  # the live AccountBridge, once one is running

    def ensure_started(self, token, region):
        """Cold-start path: start the bridge only if none is running yet. Returns True if it
        started one, False if a bridge was already running."""
        with self._lock:
            if self.account is not None:
                return False
            self.account = AccountBridge(token, region, self.target)
            self.account.start()
        return True

    def replace(self, token, region):
        """Live re-onboard path: start a fresh bridge and stop the previous one. Called after a
        successful /auth/login so new credentials take effect immediately."""
        with self._lock:
            old = self.account
            self.account = AccountBridge(token, region, self.target)
            self.account.start()
        if old is not None:
            old.stop()
        log("account bridge (re)started from onboarding")

    def forward_command(self, serial, payload):
        acct = self.account
        if acct is not None:
            acct.forward_command(serial, payload)
        else:
            log(f"[{serial}] command dropped (no active account bridge)")


# ── Onboarding (cloud auth via dreocloud REST helpers) ─────────────────────────
# Runs in-process as an HTTP server the Vidar host calls at http://dreo2mqtt:<port>.

ONBOARD_PORT = int(os.environ.get("DREO_ONBOARD_PORT", "8896"))


def _auth_status_code(exc):
    """Map an upstream login/device-list failure to an HTTP status the host understands."""
    import requests

    if isinstance(exc, requests.HTTPError) and exc.response is not None:
        status = exc.response.status_code
        if status in (401, 429):
            return status
        return 502
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
                    token, region = login(req["email"], req["password"])
                    devices = fetch_devices(token, region)
                    self._send(200, {"userDataJson": token, "region": region, "devices": devices})
                    # Push: (re)start the live bridge immediately so re-onboarding takes effect
                    # without a container restart. A failure here must NOT fail the login response
                    # — the host still persists to Mongo and a restart (cold-start path) recovers.
                    if manager is not None:
                        try:
                            manager.replace(token, region)
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


# ── Main: serve onboarding, then poll Mongo and bridge the account ─────────────

def main():
    import paho.mqtt.client as mqtt

    target = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="dreo2mqtt")
    if MQTT_USERNAME:
        target.username_pw_set(MQTT_USERNAME, MQTT_PASSWORD)

    manager = BridgeManager(target)

    if os.environ.get("DREO_ONBOARD_HTTP") == "1":
        # Started early and unconditionally: onboarding must work even before any Mongo manifest
        # exists, so this never sys.exit()s on a missing manifest (unlike the roborock2mqtt
        # crash-loop-before-onboarding bug this fixes). Passing `manager` lets a successful
        # /auth/login (re)start the live bridge immediately — no container restart to re-onboard.
        start_onboarding_server(manager)

    def on_connect(cl, u, flags, rc, props=None):
        log(f"target broker connected ({rc})")
        cl.subscribe(f"{BASE_TOPIC}/+/set")

    def on_message(cl, u, msg):
        parts = msg.topic.split("/")
        if len(parts) >= 3 and parts[-1] == "set":
            manager.forward_command(parts[-2], msg.payload.decode(errors="replace"))

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
    # start the bridge from it. Live re-onboarding is pushed by the onboarding handler
    # (manager.replace), so this loop only needs to cover the "no bridge running yet" case.
    logged_waiting = False
    while True:
        token, manifest, persisted_region = load_manifest_from_mongo()
        if token and manifest and manager.account is None:
            region = (persisted_region or "").strip() or DEFAULT_REGION
            serials = [m["serial"] for m in manifest if m.get("serial")]
            if manager.ensure_started(token, region):
                log(f"started account bridge for {len(serials)} device(s)")
            logged_waiting = False
        elif not (token and manifest) and not logged_waiting:
            log("no Dreo manifest in Mongo yet; waiting for onboarding")
            logged_waiting = True
        time.sleep(POLL_INTERVAL)


if __name__ == "__main__":
    main()
