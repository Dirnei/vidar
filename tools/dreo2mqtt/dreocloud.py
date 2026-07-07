"""Dreo cloud REST/WebSocket helpers — pure functions, unit-testable without network.

Endpoints (verified against the community PyDreo integration):
  REST base : https://app-api-{region}.dreo-tech.com
  login     : POST /api/oauth/login   (email + password -> access_token + region)
  devices   : GET  /api/v2/user-device/device/list
  state     : GET  /api/user-device/device/state
  websocket : wss://wsb-{region}.dreo-tech.com/websocket?accessToken=...&timestamp=...
Control commands are sent over the websocket as:
  {"devicesn": <serial>, "method": "control", "params": {...}, "timestamp": <ms>}

Login is NOT a bare email/password POST: the Dreo cloud gateway 403s unless the request
carries the Android app's headers + client credentials + an MD5-hashed password + a
`timestamp` query param (no request signature is needed for login). Values below mirror the
community PyDreo integration.
"""
import hashlib
import time

# Dreo Android app credentials + identifiers (from the community PyDreo integration).
CLIENT_ID = "7de37c362ee54dcf9c4561812309347a"
CLIENT_SECRET = "32dfa0764f25451d99f94e1693498791"
HIMEI = "faede31549d649f58864093158787ec9"


def api_base(region: str) -> str:
    return f"https://app-api-{region}.dreo-tech.com"


def api_timestamp() -> str:
    """Milliseconds since epoch as a string — appended as a `timestamp` query param on every
    REST call (the gateway rejects requests without it)."""
    return str(int(time.time() * 1000))


def app_headers(token: str = None) -> dict:
    """Headers the Dreo app sends on every REST call. Missing `ua`/`user-agent` is one of the
    things that triggers a 403. `authorization` is added only once a token exists."""
    headers = {
        "ua": "dreo/2.8.2",
        "user-agent": "okhttp/4.9.1",
        "content-type": "application/json; charset=UTF-8",
        "accept-encoding": "gzip",
        "lang": "en",
    }
    if token:
        headers["authorization"] = f"Bearer {token}"
    return headers


def login_body(email: str, password: str) -> dict:
    """The login request body. Password is MD5-hex (NOT plaintext); client id/secret + himei
    identify the app to the gateway."""
    return {
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET,
        "email": email,
        "password": hashlib.md5(password.encode("utf-8")).hexdigest(),
        "grant_type": "email-password",
        "scope": "all",
        "himei": HIMEI,
        "encrypt": "ciphertext",
        "acceptLanguage": "en",
    }


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


def flatten_state(state_json: dict) -> dict:
    """Flatten a device/state REST response into {field: value}.

    Dreo returns current state under data.mixed as {field: {"state": value, "timestamp": ...}}.
    We unwrap each field's `state` so the rest of the stack sees a flat {field: value} dict
    (the same shape the C# mapper consumes)."""
    mixed = (state_json.get("data", {}) or {}).get("mixed", {}) or {}
    out = {}
    for key, val in mixed.items():
        out[key] = val.get("state") if isinstance(val, dict) else val
    return out
