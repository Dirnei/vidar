#!/usr/bin/env python3
"""dyson2mqtt - bridge Dyson cloud devices (AWS IoT) to a plain MQTT broker.

Standalone tool, in the spirit of zigbee2mqtt. Dyson's newer devices (e.g. 358K) only
speak to the cloud over AWS IoT (MQTT-over-WebSocket with a custom authorizer). The only
client proven to work against that endpoint is paho, so this tool owns the cloud side and
republishes everything to a normal broker that the rest of your stack can consume.

For each device it:
  - exchanges the account token for short-lived AWS IoT credentials,
  - connects to AWS IoT (WSS) and subscribes to <product>/<serial>/status/#,
  - periodically requests CURRENT-STATE,
  - republishes every device message to <base>/<serial> on the target broker, and
  - forwards commands published to <base>/<serial>/set back to the device.

Everything is configured by environment variables (12-factor); nothing is hard-coded:

  Target MQTT broker (where state is published - fully configurable):
    MQTT_HOST            default "emqx"
    MQTT_PORT            default "1883"
    MQTT_USERNAME        optional
    MQTT_PASSWORD        optional
    MQTT_BASE_TOPIC      default "dyson2mqtt"

  Dyson account / cloud:
    DYSON_TOKEN          account token (from MyDyson login / Vidar onboarding).
                         If unset, the token + device list are read from Mongo (below).
    DYSON_DEVICES        optional explicit device list "serial:product,serial:product"
                         (overrides Mongo / API discovery)
    DYSON_API_BASE       default "https://appapi.cp.dyson.com"
    DYSON_POLL_INTERVAL  seconds between CURRENT-STATE requests, default "30"

  Mongo (only used when DYSON_TOKEN is unset - lets it "just work" in the Vidar stack):
    MONGO_URI            default "mongodb://mongodb:27017"
    MONGO_DB             default "vidar"
"""
import json
import os
import sys
import threading
import time

import paho.mqtt.client as mqtt
import requests

API_BASE = os.environ.get("DYSON_API_BASE", "https://appapi.cp.dyson.com").rstrip("/")
POLL_INTERVAL = int(os.environ.get("DYSON_POLL_INTERVAL", "30"))
BASE_TOPIC = os.environ.get("MQTT_BASE_TOPIC", "dyson2mqtt").strip("/")

MQTT_HOST = os.environ.get("MQTT_HOST", "emqx")
MQTT_PORT = int(os.environ.get("MQTT_PORT", "1883"))
MQTT_USERNAME = os.environ.get("MQTT_USERNAME") or None
MQTT_PASSWORD = os.environ.get("MQTT_PASSWORD") or None

UA = {"User-Agent": "android client"}


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


def load_account():
    """Return (token, [(serial, product), ...]). Token from env or Mongo; devices from
    env override, Mongo manifest, or the Dyson account API."""
    token = os.environ.get("DYSON_TOKEN")
    devices = parse_devices_env()

    if not token or not devices:
        token_m, devices_m = load_from_mongo()
        token = token or token_m
        devices = devices or devices_m

    if not token:
        log("FATAL: no Dyson token (set DYSON_TOKEN or provide Mongo with a dyson config)")
        sys.exit(2)

    if not devices:
        devices = fetch_devices_from_api(token)

    if not devices:
        log("FATAL: no Dyson devices found")
        sys.exit(2)
    return token, devices


def parse_devices_env():
    raw = os.environ.get("DYSON_DEVICES", "").strip()
    if not raw:
        return []
    out = []
    for part in raw.split(","):
        if ":" in part:
            serial, product = part.split(":", 1)
            out.append((serial.strip(), product.strip()))
    return out


def load_from_mongo():
    try:
        from pymongo import MongoClient
    except ImportError:
        return None, []
    uri = os.environ.get("MONGO_URI", "mongodb://mongodb:27017")
    db = os.environ.get("MONGO_DB", "vidar")
    try:
        client = MongoClient(uri, serverSelectionTimeoutMS=5000)
        cfg = client[db]["integrations"].find_one({"_id": "dyson"})
    except Exception as e:  # noqa: BLE001
        log("Mongo read failed:", e)
        return None, []
    if not cfg:
        return None, []
    settings = cfg.get("Settings", {}) or {}
    token = settings.get("account.token")
    devices = []
    try:
        for d in json.loads(settings.get("account.manifest", "[]")):
            serial = d.get("serial")
            product = d.get("productType")
            if serial and product:
                devices.append((serial, product))
    except (ValueError, TypeError):
        pass
    return token, devices


def fetch_devices_from_api(token):
    try:
        r = requests.get(f"{API_BASE}/v2/provisioningservice/manifest",
                         headers={"Authorization": f"Bearer {token}", **UA}, timeout=20)
        if r.status_code != 200:
            log("device manifest fetch failed:", r.status_code)
            return []
        out = []
        for d in r.json():
            serial = d.get("Serial") or d.get("serial")
            product = d.get("ProductType") or d.get("productType")
            if serial and product:
                out.append((serial, product))
        return out
    except Exception as e:  # noqa: BLE001
        log("device manifest fetch error:", e)
        return []


def get_iot_credentials(token, serial):
    r = requests.post(f"{API_BASE}/v2/authorize/iot-credentials",
                      headers={"Authorization": f"Bearer {token}", **UA},
                      json={"Serial": serial}, timeout=20)
    r.raise_for_status()
    data = r.json()
    c = data["IoTCredentials"]
    return data["Endpoint"], c


class DeviceBridge:
    """Owns one device's AWS IoT connection and bridges it to the target broker."""

    def __init__(self, token, serial, product, target):
        self.token = token
        self.serial = serial
        self.product = product
        self.target = target  # the shared target-broker paho client
        self.status_topic = f"{product}/{serial}/status/#"
        self.command_topic = f"{product}/{serial}/command"
        self.cloud = None
        self._stop = threading.Event()
        self._connected = threading.Event()
        self._backoff = 15

    def start(self):
        threading.Thread(target=self._run, name=f"dev-{self.serial}", daemon=True).start()

    def stop(self):
        self._stop.set()
        if self.cloud:
            try:
                self.cloud.disconnect()
            except Exception:  # noqa: BLE001
                pass

    def _run(self):
        while not self._stop.is_set():
            try:
                self._open()
                # Wait for the CONNACK (on_connect) before monitoring — otherwise
                # is_connected() is still False and we would busy-reconnect.
                if not self._connected.wait(timeout=15):
                    raise TimeoutError("connect timed out")
                self._backoff = 15
                # Stay connected; re-request CURRENT-STATE on an interval.
                while not self._stop.is_set() and self.cloud.is_connected():
                    if self._stop.wait(POLL_INTERVAL):
                        break
                    self._request_state()
            except requests.HTTPError as e:
                code = e.response.status_code if e.response is not None else 0
                # Rate-limit safety: never hammer the credentials endpoint.
                delay = max(60, self._backoff) if code == 429 else self._backoff
                log(f"[{self.serial}] cloud auth HTTP {code}; retry in {delay}s")
                self._stop.wait(delay)
                self._backoff = min(self._backoff * 2, 120)
            except Exception as e:  # noqa: BLE001
                log(f"[{self.serial}] cloud error: {e}; retry in {self._backoff}s")
                self._stop.wait(self._backoff)
                self._backoff = min(self._backoff * 2, 120)
            finally:
                self._close()

    def _open(self):
        endpoint, c = get_iot_credentials(self.token, self.serial)
        self._connected.clear()
        cl = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2,
                         client_id=c["ClientId"], transport="websockets")
        cl.ws_set_options(path="/mqtt", headers={
            c["TokenKey"]: c["TokenValue"],
            "X-Amz-CustomAuthorizer-Name": c["CustomAuthorizerName"],
            "X-Amz-CustomAuthorizer-Signature": c["TokenSignature"],
        })
        cl.tls_set()
        cl.on_connect = self._on_cloud_connect
        cl.on_message = self._on_cloud_message
        cl.on_disconnect = self._on_cloud_disconnect
        self.cloud = cl
        log(f"[{self.serial}] connecting AWS IoT {endpoint}")
        cl.connect(endpoint, 443, keepalive=45)
        cl.loop_start()

    def _close(self):
        cl, self.cloud = self.cloud, None
        self._connected.clear()
        if cl:
            try:
                cl.loop_stop()
            except Exception:  # noqa: BLE001
                pass
            try:
                cl.disconnect()
            except Exception:  # noqa: BLE001
                pass

    def _on_cloud_connect(self, cl, u, flags, rc, props=None):
        log(f"[{self.serial}] AWS IoT connected ({rc})")
        cl.subscribe(self.status_topic)
        self._connected.set()
        self._request_state()

    def _on_cloud_disconnect(self, cl, u, flags, rc, props=None):
        self._connected.clear()
        log(f"[{self.serial}] AWS IoT disconnected ({rc})")

    def _on_cloud_message(self, cl, u, msg):
        # Republish the raw device payload to the target broker under <base>/<serial>.
        try:
            payload = msg.payload.decode()
        except Exception:  # noqa: BLE001
            payload = msg.payload.decode(errors="replace")
        self.target.publish(f"{BASE_TOPIC}/{self.serial}", payload, qos=0, retain=True)

    def _request_state(self):
        if self.cloud and self.cloud.is_connected():
            now = time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())
            self.cloud.publish(self.command_topic,
                               json.dumps({"msg": "REQUEST-CURRENT-STATE", "time": now}))

    def forward_command(self, payload):
        """Forward a command received on <base>/<serial>/set to the device."""
        if self.cloud and self.cloud.is_connected():
            self.cloud.publish(self.command_topic, payload)
            log(f"[{self.serial}] forwarded command -> device")
        else:
            log(f"[{self.serial}] command dropped (cloud not connected)")


def main():
    token, devices = load_account()
    log(f"dyson2mqtt: {len(devices)} device(s); broker {MQTT_HOST}:{MQTT_PORT} "
        f"base '{BASE_TOPIC}'")

    bridges = {}

    target = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="dyson2mqtt")
    if MQTT_USERNAME:
        target.username_pw_set(MQTT_USERNAME, MQTT_PASSWORD)

    def on_target_connect(cl, u, flags, rc, props=None):
        log(f"target broker connected ({rc})")
        cl.subscribe(f"{BASE_TOPIC}/+/set")

    def on_target_message(cl, u, msg):
        # <base>/<serial>/set -> forward to that device
        parts = msg.topic.split("/")
        if len(parts) >= 3 and parts[-1] == "set":
            serial = parts[-2]
            b = bridges.get(serial)
            if b:
                b.forward_command(msg.payload.decode(errors="replace"))

    target.on_connect = on_target_connect
    target.on_message = on_target_message
    target.connect(MQTT_HOST, MQTT_PORT, keepalive=30)
    target.loop_start()

    for serial, product in devices:
        b = DeviceBridge(token, serial, product, target)
        bridges[serial] = b
        b.start()

    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        for b in bridges.values():
            b.stop()


if __name__ == "__main__":
    main()
