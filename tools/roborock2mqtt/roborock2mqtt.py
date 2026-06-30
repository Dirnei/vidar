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
    """Return (user_data_dict, [manifest dicts]) from vidar.integrations/_id:'roborock'."""
    try:
        from pymongo import MongoClient
    except ImportError:
        return None, []
    try:
        client = MongoClient(MONGO_URI, serverSelectionTimeoutMS=5000)
        cfg = client[MONGO_DB]["integrations"].find_one({"_id": "roborock"})
    except Exception as e:  # noqa: BLE001
        log("Mongo read failed:", e)
        return None, []
    if not cfg:
        return None, []
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
    return user_data, devices


def map_status_to_payload(status, rooms, transport):
    out = dict(status)
    out["_transport"] = transport
    out["_rooms"] = rooms or []
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

    def __init__(self, user_data, manifest, target):
        self.user_data = user_data
        self.duid = manifest["duid"]
        self.name = manifest.get("name", self.duid)
        self.model = manifest.get("model", "")
        self.local_key = manifest["localKey"]
        self.ip = manifest.get("ip")
        self.target = target
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
        from roborock.containers import DeviceData, HomeDataDevice
        from roborock.version_1_apis import RoborockLocalClientV1, RoborockMqttClientV1

        device = HomeDataDevice(duid=self.duid, name=self.name, local_key=self.local_key,
                                fv="", pv="1.0")
        device_data = DeviceData(device=device, model=self.model, host=self.ip)

        try:
            # local-first
            try:
                if not self.ip:
                    raise RuntimeError("no local ip")
                self.client = RoborockLocalClientV1(device_data)
                await self.client.async_connect()
                self.transport = "local"
            except Exception as e:  # noqa: BLE001
                log(f"[{self.duid}] local failed ({e}); falling back to cloud")
                self.client = RoborockMqttClientV1(self.user_data, device_data)
                await self.client.async_connect()
                self.transport = "cloud"

            log(f"[{self.duid}] connected ({self.transport})")
            await self._refresh_rooms()
            while not self._stop.is_set():
                status = await self.client.get_status()
                payload = map_status_to_payload(
                    status.as_dict() if hasattr(status, "as_dict") else dict(status),
                    self.rooms, self.transport)
                self.target.publish(self.state_topic, json.dumps(payload), qos=0, retain=True)
                await asyncio.sleep(POLL_INTERVAL)
        finally:
            await self._disconnect_client()

    async def _disconnect_client(self):
        client, self.client = self.client, None
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
        try:
            mapping = await self.client.get_room_mapping()  # [[segment_id, iot_id], ...]
            self.rooms = [{"id": int(seg), "name": str(iot)} for seg, iot in (mapping or [])]
        except Exception as e:  # noqa: BLE001
            log(f"[{self.duid}] room mapping failed: {e}")
            self.rooms = []

    def _publish_offline(self):
        self.target.publish(self.state_topic,
                            json.dumps({"_transport": "offline", "_rooms": self.rooms}),
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


def main():
    import paho.mqtt.client as mqtt
    from roborock.containers import UserData

    user_data_dict, manifest = load_manifest_from_mongo()
    if not user_data_dict or not manifest:
        log("FATAL: no Roborock account/manifest in Mongo (complete onboarding first)")
        sys.exit(2)
    user_data = UserData.from_dict(user_data_dict)
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
    target.connect(MQTT_HOST, MQTT_PORT, keepalive=30)
    target.loop_start()

    for m in manifest:
        b = DeviceBridge(user_data, m, target)
        bridges[m["duid"]] = b
        b.start()

    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        for b in bridges.values():
            b.stop()


if __name__ == "__main__":
    main()
