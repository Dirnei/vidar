#!/usr/bin/env python3
"""roborock2mqtt - bridge a Roborock account's vacuums to a plain MQTT broker.

Owns the Roborock protocol (cloud auth + local-key bootstrap, local-first/cloud-fallback
transport) via python-roborock and republishes each device's status to <base>/<duid>
(retained). Commands published to <base>/<duid>/set are forwarded to the device.
"""
import asyncio, json, os, sys, threading, time

BASE_TOPIC = os.environ.get("MQTT_BASE_TOPIC", "roborock2mqtt").strip("/")
MQTT_HOST = os.environ.get("MQTT_HOST", "emqx")
MQTT_PORT = int(os.environ.get("MQTT_PORT", "1883"))
MQTT_USERNAME = os.environ.get("MQTT_USERNAME") or None
MQTT_PASSWORD = os.environ.get("MQTT_PASSWORD") or None
POLL_INTERVAL = int(os.environ.get("ROBOROCK_POLL_INTERVAL", "30"))
MONGO_URI = os.environ.get("MONGO_URI", "mongodb://mongodb:27017")
MONGO_DB = os.environ.get("MONGO_DB", "vidar")


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


def load_manifest_from_mongo():
    """Return (user_data_dict, [manifest dicts], email) from vidar.integrations/_id:'roborock'."""
    try:
        from pymongo import MongoClient
    except ImportError:
        return None, [], None
    try:
        client = MongoClient(MONGO_URI, serverSelectionTimeoutMS=5000)
        cfg = client[MONGO_DB]["integrations"].find_one({"_id": "roborock"})
    except Exception as e:  # noqa: BLE001
        log("Mongo read failed:", e)
        return None, [], None
    if not cfg:
        return None, [], None
    settings = cfg.get("Settings", {}) or {}
    user_data = None
    try:
        user_data = json.loads(settings.get("account.userData", "null"))
    except (ValueError, TypeError):
        pass
    devices = []
    try:
        devices = json.loads(settings.get("account.manifest", "[]"))
    except (ValueError, TypeError):
        pass
    email = settings.get("account.email")
    return user_data, devices, email


def map_status_to_payload(status, rooms, transport, scenes=None):
    out = dict(status)
    out["_transport"] = transport
    out["_rooms"] = rooms or []
    out["_scenes"] = scenes or []
    return out


def _parse_segments(value):
    if isinstance(value, (list, tuple)):
        return [int(v) for v in value]
    return [int(p) for p in str(value).split(",") if p.strip()]


def translate_command(cmd):
    cap = cmd.get("capability")
    val = cmd.get("value")
    if cap == "vacuum.start":
        return "APP_START", []
    if cap == "vacuum.stop":
        return "APP_STOP", []
    if cap == "vacuum.pause":
        return "APP_PAUSE", []
    if cap == "vacuum.dock":
        return "APP_CHARGE", []
    if cap == "vacuum.locate":
        return "FIND_ME", []
    if cap == "vacuum.fanPower":
        return "SET_CUSTOM_MODE", [int(val)]
    if cap == "vacuum.cleanSegments":
        return "APP_SEGMENT_CLEAN", [{"segments": _parse_segments(val), "repeat": 1}]
    raise ValueError(f"unknown capability {cap!r}")


class DeviceBridge:
    """Owns one vacuum: local-first/cloud-fallback transport, republished to the broker."""

    def __init__(self, user_data, manifest, target, home_device, room_names=None, email=None):
        self.user_data = user_data
        self.duid = manifest["duid"]
        self.name = manifest.get("name", self.duid)
        self.model = manifest.get("model", "")
        self.ip = manifest.get("ip")
        self.target = target
        # The real HomeDataDevice from Roborock's home data — python-roborock's DeviceData
        # requires it (it has product_id, pv/protocol version, local_key, etc.); a hand-built
        # one is missing required fields and picks the wrong protocol.
        self.home_device = home_device
        # iot room id -> friendly name (from home data at onboarding/startup)
        self.room_names = room_names or {}
        self.email = email
        self.scenes = []
        self.state_topic = f"{BASE_TOPIC}/{self.duid}"
        self.transport = "offline"
        self.rooms = []
        self.client = None
        self.loop = None
        self._stop = threading.Event()

    def start(self):
        threading.Thread(target=self._run, name=f"dev-{self.duid}", daemon=True).start()

    def stop(self):
        self._stop.set()

    def _run(self):
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        backoff = 15
        try:
            while not self._stop.is_set():
                try:
                    self.loop.run_until_complete(self._connect_and_poll())
                    backoff = 15
                except Exception as e:  # noqa: BLE001
                    log(f"[{self.duid}] error: {e}; retry in {backoff}s")
                    self.transport = "offline"
                    self._publish_offline()
                    self._stop.wait(backoff)
                    backoff = min(backoff * 2, 120)
        finally:
            self.loop.close()

    async def _connect_and_poll(self):
        from roborock.containers import DeviceData

        device_data = DeviceData(device=self.home_device, model=self.model, host=self.ip)
        try:
            status = await self._select_transport(device_data)
            log(f"[{self.duid}] connected ({self.transport})")
            await self._refresh_rooms()
            self.scenes = await self._fetch_scenes()
            while not self._stop.is_set():
                if status is None:
                    status = await self.client.get_status()
                payload = map_status_to_payload(
                    status.as_dict() if hasattr(status, "as_dict") else dict(status),
                    self.rooms, self.transport, self.scenes)
                self.target.publish(self.state_topic, json.dumps(payload), qos=0, retain=True)
                status = None
                await asyncio.sleep(POLL_INTERVAL)
        finally:
            await self._disconnect_client()

    async def _select_transport(self, device_data):
        """Local-first, cloud-fallback. Newer devices (e.g. Qrevo) connect locally but return an
        empty status, so local is only used if get_status actually yields state. Returns the first
        status so the poll loop can publish it without a second round-trip."""
        from roborock.version_1_apis import RoborockLocalClientV1, RoborockMqttClientV1

        if self.ip:
            try:
                c = RoborockLocalClientV1(device_data)
                await c.async_connect()
                st = await c.get_status()
                if getattr(st, "state", None) is not None:
                    self.client, self.transport = c, "local"
                    return st
                log(f"[{self.duid}] local connected but returned no state; falling back to cloud")
                await self._disconnect(c)
            except Exception as e:  # noqa: BLE001
                log(f"[{self.duid}] local failed ({e}); falling back to cloud")

        c = RoborockMqttClientV1(self.user_data, device_data)
        await c.async_connect()
        st = await c.get_status()
        self.client, self.transport = c, "cloud"
        return st

    async def _disconnect_client(self):
        client, self.client = self.client, None
        await self._disconnect(client)

    async def _disconnect(self, client):
        if client is None:
            return
        for method in ("async_disconnect", "async_release", "disconnect"):
            fn = getattr(client, method, None)
            if fn is None:
                continue
            try:
                result = fn()
                if asyncio.iscoroutine(result):
                    await result
            except Exception as e:  # noqa: BLE001
                log(f"[{self.duid}] disconnect ({method}) failed: {e}")
            return

    async def _refresh_rooms(self):
        # get_room_mapping() returns list[RoomMapping] with .segment_id and .iot_id (NOT tuples).
        # iot_id joins to a HomeDataRoom.id -> friendly name via self.room_names.
        try:
            mapping = await self.client.get_room_mapping()
            rooms = []
            for rm in (mapping or []):
                seg = getattr(rm, "segment_id", None)
                iot = getattr(rm, "iot_id", None)
                if seg is None:
                    continue
                name = (self.room_names.get(iot)
                        or self.room_names.get(str(iot))
                        or str(iot))
                rooms.append({"id": int(seg), "name": name})
            self.rooms = rooms
        except Exception as e:  # noqa: BLE001
            log(f"[{self.duid}] room mapping failed: {e}")
            self.rooms = []

    async def _fetch_scenes(self):
        if not self.email:
            return []
        try:
            from roborock.web_api import RoborockApiClient
            scenes = await RoborockApiClient(self.email).get_scenes(self.user_data, self.duid)
            return [{"id": s.id, "name": s.name} for s in (scenes or [])]
        except Exception as e:  # noqa: BLE001
            log(f"[{self.duid}] scene fetch failed: {e}")
            return []

    def _publish_offline(self):
        self.target.publish(self.state_topic,
                            json.dumps({"_transport": "offline",
                                        "_rooms": self.rooms, "_scenes": self.scenes}),
                            qos=0, retain=True)

    def forward_command(self, payload):
        from roborock import RoborockCommand

        try:
            cmd = json.loads(payload)
            name, params = translate_command(cmd)
        except Exception as e:  # noqa: BLE001
            log(f"[{self.duid}] bad command: {e}")
            return
        if not self.client or not self.loop:
            log(f"[{self.duid}] command dropped (not connected)")
            return
        rcmd = getattr(RoborockCommand, name)
        fut = asyncio.run_coroutine_threadsafe(
            self.client.send_command(rcmd, params), self.loop)
        try:
            fut.result(timeout=15)
            log(f"[{self.duid}] sent {name} {params}")
        except Exception as e:  # noqa: BLE001
            log(f"[{self.duid}] command {name} failed: {e}")


# ── Onboarding (cloud auth via python-roborock) ──────────────────────────────
# Runs in-process as an HTTP server the Vidar host calls at http://roborock2mqtt:8895.
# python-roborock is the only client that speaks Roborock's auth, so onboarding lives here.

ONBOARD_PORT = int(os.environ.get("ROBOROCK_ONBOARD_PORT", "8895"))


def _run_async(coro):
    """Run a coroutine to completion in a throwaway event loop (for the HTTP thread)."""
    loop = asyncio.new_event_loop()
    try:
        return loop.run_until_complete(coro)
    finally:
        loop.close()


async def _assemble_account(api, user_data):
    """From an authenticated api+user_data, build the manifest devices + room list."""
    from roborock.containers import DeviceData
    from roborock.version_1_apis import RoborockMqttClientV1

    try:
        home = await api.get_home_data_v3(user_data)
    except Exception:  # noqa: BLE001 — older accounts / API variance
        home = await api.get_home_data(user_data)

    products = {p.id: p for p in (home.products or [])}
    all_devices = list(home.devices or []) + list(getattr(home, "received_devices", None) or [])
    rooms = [{"id": r.id, "name": r.name} for r in (home.rooms or [])]

    devices = []
    for d in all_devices:
        model = products[d.product_id].model if d.product_id in products else ""
        ip = ""
        try:
            mc = RoborockMqttClientV1(user_data, DeviceData(device=d, model=model))
            await mc.async_connect()
            net = await mc.get_networking()
            ip = (getattr(net, "ip", "") if net else "") or ""
            await mc.async_disconnect()
        except Exception as e:  # noqa: BLE001 — ip is best-effort; local falls back to cloud
            log(f"networking for {d.duid} failed: {e}")
        devices.append({"duid": d.duid, "name": d.name, "model": model,
                        "localKey": d.local_key, "ip": ip})
    return devices, rooms


async def _home_devices(email, user_data):
    """Fetch the real {duid: HomeDataDevice} for this account (needed to build DeviceData)."""
    from roborock.web_api import RoborockApiClient
    api = RoborockApiClient(email)
    try:
        home = await api.get_home_data_v3(user_data)
    except Exception:  # noqa: BLE001
        home = await api.get_home_data(user_data)
    devs = list(home.devices or []) + list(getattr(home, "received_devices", None) or [])
    room_names = {r.id: r.name for r in (getattr(home, "rooms", None) or [])}
    return {d.duid: d for d in devs}, room_names


async def _login_password(email, password):
    from roborock.web_api import RoborockApiClient
    api = RoborockApiClient(email)
    user_data = await api.pass_login(password)
    devices, rooms = await _assemble_account(api, user_data)
    return {"userDataJson": json.dumps(user_data.as_dict()), "devices": devices, "rooms": rooms}


# Roborock binds an emailed code to the requesting client's header id, which is a hash of
# username + a per-instance random device_identifier. request_code and code_login therefore
# MUST present the SAME device_identifier, so we persist it per email across the two HTTP calls.
_ONBOARD_IDENTIFIERS = {}


async def _request_code(email):
    from roborock.web_api import RoborockApiClient
    api = RoborockApiClient(email)
    _ONBOARD_IDENTIFIERS[email] = api._device_identifier
    await api.request_code()


async def _code_login(email, code):
    from roborock.web_api import RoborockApiClient
    api = RoborockApiClient(email)
    ident = _ONBOARD_IDENTIFIERS.get(email)
    if ident:
        api._device_identifier = ident  # reuse the id the code was issued against
    user_data = await api.code_login(code)
    devices, rooms = await _assemble_account(api, user_data)
    return {"userDataJson": json.dumps(user_data.as_dict()), "devices": devices, "rooms": rooms}


def _auth_status_code(exc):
    """Map a python-roborock auth failure to an HTTP status the host understands."""
    name = type(exc).__name__.lower()
    msg = str(exc).lower()
    if any(k in name for k in ("credential", "auth", "email", "login")) \
            or any(k in msg for k in ("password", "credential", "unauthorized", "invalid")):
        return 401
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
            # .NET's PostAsJsonAsync sends the body chunked (no Content-Length); handle both.
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
                    self._send(200, _run_async(_login_password(req["email"], req["password"])))
                elif path == "/auth/request-code":
                    _run_async(_request_code(req["email"]))
                    self._send(200, {"sent": True})
                elif path == "/auth/code-login":
                    self._send(200, _run_async(_code_login(req["email"], req["code"])))
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


# ── Main: serve onboarding, then poll Mongo and bridge each device ────────────

def main():
    import paho.mqtt.client as mqtt
    from roborock.containers import UserData

    if os.environ.get("ROBOROCK_ONBOARD_HTTP") == "1":
        start_onboarding_server()

    bridges = {}
    target = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="roborock2mqtt")
    if MQTT_USERNAME:
        target.username_pw_set(MQTT_USERNAME, MQTT_PASSWORD)

    def on_connect(cl, u, flags, rc, props=None):
        log(f"target broker connected ({rc})")
        cl.subscribe(f"{BASE_TOPIC}/+/set")

    def on_message(cl, u, msg):
        parts = msg.topic.split("/")
        if len(parts) >= 3 and parts[-1] == "set":
            b = bridges.get(parts[-2])
            if b:
                b.forward_command(msg.payload.decode(errors="replace"))

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

    # Poll Mongo for the onboarded manifest and start a bridge per new device. This survives the
    # pre-onboarding state (no exit), so the onboarding HTTP endpoints can populate Mongo first.
    logged_waiting = False
    while True:
        user_data_dict, manifest, email = load_manifest_from_mongo()
        if user_data_dict and manifest and email:
            user_data = UserData.from_dict(user_data_dict)
            new = [m for m in manifest if m["duid"] not in bridges]
            if new:
                try:
                    home_devices, room_names = _run_async(_home_devices(email, user_data))
                except Exception as e:  # noqa: BLE001
                    log(f"home data fetch failed ({e}); retry next poll")
                    home_devices, room_names = {}, {}
                for m in new:
                    hd = home_devices.get(m["duid"])
                    if hd is None:
                        log(f"device {m['duid']} not found in home data yet; will retry")
                        continue
                    b = DeviceBridge(user_data, m, target, hd, room_names, email)
                    bridges[m["duid"]] = b
                    b.start()
                    log(f"started bridge for {m['duid']} ({m.get('name')})")
            logged_waiting = False
        elif not logged_waiting:
            log("no Roborock manifest in Mongo yet; waiting for onboarding")
            logged_waiting = True
        time.sleep(POLL_INTERVAL)


if __name__ == "__main__":
    main()
