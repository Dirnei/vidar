import sys
import types

import pytest

import loxone2mqtt as lx


def test_build_structure_extracts_controls_rooms_moods():
    loxapp3 = {
        "msInfo": {"serialNr": "504F94A0"},
        "rooms": {"r1": {"name": "OG Kitchen"}, "r2": {"name": "Living"}},
        "controls": {
            "u1": {"name": "Kitchen Relay", "type": "Switch", "room": "r1"},
            "u3": {"name": "Living Light", "type": "LightControllerV2", "room": "r2",
                   "details": {"moodList": [{"id": 1, "name": "Off"}, {"id": 778, "name": "All On"}]}},
        },
    }
    s = lx.build_structure(loxapp3, "504F94A0")
    assert s["serial"] == "504F94A0"
    assert {c["uuid"] for c in s["controls"]} == {"u1", "u3"}
    lc = next(c for c in s["controls"] if c["uuid"] == "u3")
    assert lc["moods"] == [{"id": 1, "name": "Off"}, {"id": 778, "name": "All On"}]
    assert {r["uuid"] for r in s["rooms"]} == {"r1", "r2"}


def test_flatten_switch_state():
    assert lx.flatten_control_state("Switch", {"active": 1}) == {"active": 1}


def test_flatten_dimmer_state():
    out = lx.flatten_control_state("Dimmer", {"active": 1, "position": 42})
    assert out == {"active": 1, "position": 42}


def test_flatten_lightcontroller_active_mood():
    out = lx.flatten_control_state("LightControllerV2", {"activeMoods": [778]})
    assert out["activeMood"] == 778


def test_command_url():
    assert lx.command_url("0f12", "on") == "jdev/sps/io/0f12/on"
    assert lx.command_url("0f12", "changeTo/778") == "jdev/sps/io/0f12/changeTo/778"


def test_build_structure_skips_unsupported_control_type():
    loxapp3 = {
        "msInfo": {"serialNr": "504F94A0"},
        "rooms": {"r1": {"name": "OG Kitchen"}},
        "controls": {
            "u1": {"name": "Kitchen Relay", "type": "Switch", "room": "r1"},
            "u2": {"name": "Kitchen Blinds", "type": "Jalousie", "room": "r1"},
        },
    }
    s = lx.build_structure(loxapp3, "504F94A0")
    assert {c["uuid"] for c in s["controls"]} == {"u1"}
    assert "u2" not in {c["uuid"] for c in s["controls"]}


def test_flatten_presence_detector_state():
    out = lx.flatten_control_state("PresenceDetector", {"active": 1, "brightness": 250})
    assert out == {"active": 1, "brightness": 250}


def test_flatten_smoke_alarm_state():
    out = lx.flatten_control_state("SmokeAlarm", {"active": 0, "battery": 95, "tamper": 0})
    assert out == {"active": 0, "battery": 95, "tamper": 0}


def test_flatten_touch_state():
    assert lx.flatten_control_state("Touch", {"action": 1}) == {"action": 1}


def test_flatten_lightcontroller_active_mood_and_active_together():
    out = lx.flatten_control_state("LightControllerV2", {"activeMoods": [778], "active": 1})
    assert out["activeMood"] == 778
    assert out["active"] == 1


# ── Onboarding: probe_miniserver / _auth_status_code (Task 9) ───────────────────
# LoxoneClient is imported lazily inside probe_miniserver (`from loxone_client import
# LoxoneClient`), so a fake `loxone_client` module is installed into sys.modules for the
# duration of each test -- no real WS/pyloxone-api is touched.

class _FakeLoxoneClient:
    """Stand-in for loxone_client.LoxoneClient. Class-level knobs (reset per test) configure
    connect() to raise, and what get_structure() returns."""
    connect_error = None
    structure = {
        "msInfo": {"serialNr": "504F94A0"},
        "rooms": {"r1": {"name": "Kitchen"}},
        "controls": {"u1": {"name": "Relay", "type": "Switch", "room": "r1"}},
    }
    last_instance = None

    def __init__(self, host, user, password):
        self.host, self.user, self.password = host, user, password
        self.closed = False
        _FakeLoxoneClient.last_instance = self

    def connect(self):
        if _FakeLoxoneClient.connect_error is not None:
            raise _FakeLoxoneClient.connect_error

    def get_structure(self):
        return _FakeLoxoneClient.structure

    def close(self):
        self.closed = True


def _install_fake_loxone_client(monkeypatch, connect_error=None, structure=None):
    _FakeLoxoneClient.connect_error = connect_error
    _FakeLoxoneClient.structure = structure if structure is not None else {
        "msInfo": {"serialNr": "504F94A0"},
        "rooms": {"r1": {"name": "Kitchen"}},
        "controls": {"u1": {"name": "Relay", "type": "Switch", "room": "r1"}},
    }
    fake_module = types.ModuleType("loxone_client")
    fake_module.LoxoneClient = _FakeLoxoneClient
    monkeypatch.setitem(sys.modules, "loxone_client", fake_module)


def test_probe_miniserver_returns_summary(monkeypatch):
    _install_fake_loxone_client(monkeypatch)
    summary = lx.probe_miniserver("10.0.0.5", "admin", "secret")
    assert summary == {"serial": "504F94A0", "controlCount": 1, "roomCount": 1}
    assert _FakeLoxoneClient.last_instance.closed is True  # always closed, even on success


def test_probe_miniserver_closes_client_even_on_connect_failure(monkeypatch):
    _install_fake_loxone_client(monkeypatch, connect_error=ConnectionError("handshake failed"))
    with pytest.raises(ConnectionError):
        lx.probe_miniserver("10.0.0.5", "admin", "wrong")
    assert _FakeLoxoneClient.last_instance.closed is True  # never leak the client on failure


def test_auth_status_code_maps_generic_connect_failure_to_502():
    # LoxoneClient.connect() raises a bare ConnectionError for ANY handshake failure (bad
    # credentials or otherwise) -- pyloxone-api gives no separate signal, so this can't safely be
    # narrowed to 401 without a real Miniserver (Task 13); treated as an upstream problem.
    assert lx._auth_status_code(ConnectionError("Loxone handshake failed for 10.0.0.5")) == 502
    assert lx._auth_status_code(OSError("host unreachable")) == 502
    assert lx._auth_status_code(TimeoutError()) == 502


def test_auth_status_code_maps_http_401_403_to_401(monkeypatch):
    fake_httpx = types.ModuleType("httpx")

    class _Resp:
        def __init__(self, status_code):
            self.status_code = status_code

    class HTTPStatusError(Exception):
        def __init__(self, response):
            self.response = response

    fake_httpx.HTTPStatusError = HTTPStatusError
    monkeypatch.setitem(sys.modules, "httpx", fake_httpx)

    assert lx._auth_status_code(HTTPStatusError(_Resp(401))) == 401
    assert lx._auth_status_code(HTTPStatusError(_Resp(403))) == 401
    assert lx._auth_status_code(HTTPStatusError(_Resp(500))) == 502


# ── BridgeManager.replace_one (Task 9: scoped live re-onboard) ──────────────────

class _FakeMiniserverBridge:
    """Stand-in for MiniserverBridge that records lifecycle calls without threads/WS."""
    instances = []

    def __init__(self, serial, host, user, password, target):
        self.serial, self.host, self.user, self.password = serial, host, user, password
        self.started = False
        self.stopped = False
        _FakeMiniserverBridge.instances.append(self)

    def start(self):
        self.started = True

    def stop(self):
        self.stopped = True


def _manager_with_fake_bridge(monkeypatch):
    _FakeMiniserverBridge.instances = []
    monkeypatch.setattr(lx, "MiniserverBridge", _FakeMiniserverBridge)
    return lx.BridgeManager(target=object())


def test_bridge_manager_replace_one_leaves_other_bridges_running(monkeypatch):
    mgr = _manager_with_fake_bridge(monkeypatch)
    mgr.ensure_started([
        {"serial": "A", "host": "10.0.0.1", "user": "u", "password": "p"},
        {"serial": "B", "host": "10.0.0.2", "user": "u", "password": "p"},
    ])
    mgr.replace_one({"serial": "A", "host": "10.0.0.9", "user": "u2", "password": "p2"})

    assert mgr.bridges["B"].stopped is False  # untouched by the single-serial replace

    a_instances = [b for b in _FakeMiniserverBridge.instances if b.serial == "A"]
    assert len(a_instances) == 2
    assert a_instances[0].stopped is True                 # old A bridge torn down
    assert a_instances[1].stopped is False                # new A bridge live
    assert a_instances[1].host == "10.0.0.9"
    assert mgr.bridges["A"] is a_instances[1]


def test_bridge_manager_replace_one_starts_bridge_when_none_running(monkeypatch):
    mgr = _manager_with_fake_bridge(monkeypatch)
    mgr.replace_one({"serial": "A", "host": "10.0.0.9", "user": "u", "password": "p"})
    assert mgr.bridges["A"].started is True
    assert mgr.bridges["A"].stopped is False
