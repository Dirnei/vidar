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


def login(email, password, region=None):
    """POST /api/oauth/login against `region` (default DEFAULT_REGION); if the response
    names a different region, re-login against it once (mirrors dreocloud's REST shape).
    Returns (token, region)."""
    import requests

    region = region or DEFAULT_REGION
    resp = requests.post(f"{dreocloud.api_base(region)}/api/oauth/login",
                          json={"email": email, "password": password}, timeout=20)
    resp.raise_for_status()
    token, resp_region = dreocloud.parse_login(resp.json())
    if resp_region and resp_region != region:
        region = resp_region
        resp = requests.post(f"{dreocloud.api_base(region)}/api/oauth/login",
                              json={"email": email, "password": password}, timeout=20)
        resp.raise_for_status()
        token, _ = dreocloud.parse_login(resp.json())
    return token, region


def fetch_devices(token, region):
    import requests

    resp = requests.get(f"{dreocloud.api_base(region)}/api/v2/user-device/device/list",
                         headers={"Authorization": f"Bearer {token}"}, timeout=20)
    resp.raise_for_status()
    return dreocloud.parse_devices(resp.json())


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

    def _on_message(self, ws, message):
        try:
            msg = json.loads(message)
        except Exception as e:  # noqa: BLE001
            log(f"bad WS message: {e}")
            return
        result = extract_reported(msg)
        if result is None:
            return
        serial, state = result
        self.target.publish(f"{BASE_TOPIC}/{serial}", json.dumps(state), qos=0, retain=True)

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


def start_onboarding_server():
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

    if os.environ.get("DREO_ONBOARD_HTTP") == "1":
        # Started FIRST and unconditionally: onboarding must work even before any Mongo
        # manifest exists, so this never sys.exit()s on a missing manifest (unlike the
        # roborock2mqtt crash-loop-before-onboarding bug this fixes).
        start_onboarding_server()

    account = None  # AccountBridge, once a manifest shows up

    target = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="dreo2mqtt")
    if MQTT_USERNAME:
        target.username_pw_set(MQTT_USERNAME, MQTT_PASSWORD)

    def on_connect(cl, u, flags, rc, props=None):
        log(f"target broker connected ({rc})")
        cl.subscribe(f"{BASE_TOPIC}/+/set")

    def on_message(cl, u, msg):
        parts = msg.topic.split("/")
        if len(parts) >= 3 and parts[-1] == "set" and account is not None:
            account.forward_command(parts[-2], msg.payload.decode(errors="replace"))

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

    # Poll Mongo for the onboarded manifest and start the account bridge once available. This
    # survives the pre-onboarding state (no exit), so onboarding HTTP can populate Mongo first.
    logged_waiting = False
    while True:
        token, manifest, persisted_region = load_manifest_from_mongo()
        if token and manifest and account is None:
            region = (persisted_region or "").strip() or DEFAULT_REGION
            serials = [m["serial"] for m in manifest if m.get("serial")]
            account = AccountBridge(token, region, target)
            account.start()
            log(f"started account bridge for {len(serials)} device(s)")
            logged_waiting = False
        elif not (token and manifest) and not logged_waiting:
            log("no Dreo manifest in Mongo yet; waiting for onboarding")
            logged_waiting = True
        time.sleep(POLL_INTERVAL)


if __name__ == "__main__":
    main()
