#!/usr/bin/env python3
"""roborock2mqtt - bridge a Roborock account's vacuums to a plain MQTT broker.

Owns the Roborock protocol (cloud auth + local-key bootstrap, local-first/cloud-fallback
transport) via python-roborock and republishes each device's status to <base>/<duid>
(retained). Commands published to <base>/<duid>/set are forwarded to the device.
"""
import json, os, sys, time

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


if __name__ == "__main__":
    ud, devs = load_manifest_from_mongo()
    log(f"roborock2mqtt: {len(devs)} device(s); broker {MQTT_HOST}:{MQTT_PORT} base '{BASE_TOPIC}'")
    if not ud or not devs:
        log("FATAL: no Roborock account/manifest in Mongo (complete onboarding first)")
        sys.exit(2)
