"""Dreo cloud REST/WebSocket helpers — pure functions, unit-testable without network.

Endpoints (verified against the community PyDreo integration):
  REST base : https://app-api-{region}.dreo-tech.com
  login     : POST /api/oauth/login   (email + password -> access_token + region)
  devices   : GET  /api/v2/user-device/device/list
  state     : GET  /api/user-device/device/state
  websocket : wss://wsb-{region}.dreo-tech.com/websocket?accessToken=...&timestamp=...
Control commands are sent over the websocket as:
  {"devicesn": <serial>, "method": "control", "params": {...}, "timestamp": <ms>}
"""
import time


def api_base(region: str) -> str:
    return f"https://app-api-{region}.dreo-tech.com"


def ws_url(region: str, access_token: str) -> str:
    ts = int(time.time() * 1000)
    return (f"wss://wsb-{region}.dreo-tech.com/websocket"
            f"?accessToken={access_token}&timestamp={ts}")


def parse_login(resp_json: dict) -> tuple:
    data = resp_json.get("data", {}) or {}
    return data.get("access_token"), data.get("region")


def parse_devices(resp_json: dict) -> list:
    data = resp_json.get("data", {}) or {}
    out = []
    for d in data.get("list", []) or []:
        serial = d.get("deviceSn") or d.get("sn")
        if not serial:
            continue
        out.append({
            "serial": serial,
            "model": d.get("model") or d.get("productId") or "",
            "name": d.get("deviceName") or serial,
        })
    return out


def control_envelope(serial: str, params: dict, ts_ms: int) -> dict:
    return {"devicesn": serial, "method": "control",
            "params": params, "timestamp": ts_ms}
