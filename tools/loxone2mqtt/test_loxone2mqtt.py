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
    # A Dimmer reports only `position`; on/off is derived from it (position > 0).
    out = lx.flatten_control_state("Dimmer", {"position": 42})
    assert out == {"position": 42, "active": True}


def test_flatten_dimmer_zero_position_is_off():
    assert lx.flatten_control_state("Dimmer", {"position": 0}) == {"position": 0, "active": False}


def test_flatten_lightcontroller_active_mood():
    out = lx.flatten_control_state("LightControllerV2", {"activeMoods": [778]})
    assert out["activeMood"] == 778


def test_flatten_lightcontroller_position_gives_light_and_mood():
    # Brightness/on comes from the folded masterValue `position`; scene from activeMoods.
    out = lx.flatten_control_state("LightControllerV2", {"position": 70, "activeMoods": [778]})
    assert out["position"] == 70
    assert out["active"] is True
    assert out["activeMood"] == 778


def test_flatten_lightcontroller_activemoods_json_string():
    # Loxone text states can arrive as a JSON-array string rather than a list.
    out = lx.flatten_control_state("LightControllerV2", {"activeMoods": "[778]"})
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


class _RecordingTarget:
    def __init__(self):
        self.published = {}

    def publish(self, topic, payload, qos=0, retain=False):
        self.published[topic] = payload


def test_on_state_merges_fields_across_separate_batches():
    import json as _json
    t = _RecordingTarget()
    b = lx.MiniserverBridge("SER", "h", "u", "p", t)
    # mood arrives in one batch, brightness (folded masterValue position) in another
    b._on_state("u3", "LightControllerV2", {"activeMoods": [778]})
    b._on_state("u3", "LightControllerV2", {"position": 70})
    merged = _json.loads(t.published["loxone2mqtt/SER/u3"])
    assert merged["activeMood"] == 778
    assert merged["position"] == 70
    assert merged["active"] is True


def test_forward_command_redirects_brightness_to_mastervalue_but_not_verbs():
    class FakeClient:
        is_connected = True

        def __init__(self):
            self.sent = []

        def send_command(self, uuid, url):
            self.sent.append((uuid, url))

    b = lx.MiniserverBridge("SER", "h", "u", "p", _RecordingTarget())
    b.client = FakeClient()
    b._control_types = {"u3": "LightControllerV2"}
    b._master_values = {"u3": "u3/masterValue"}
    b.forward_command("u3", "55")   # bare dim level -> masterValue subcontrol
    b.forward_command("u3", "on")   # on/off verb -> parent
    b.forward_command("u3", "changeTo/778")  # scene verb -> parent
    assert b.client.sent[0][0] == "u3/masterValue"
    assert b.client.sent[1][0] == "u3"
    assert b.client.sent[2][0] == "u3"


def test_parse_mood_list_from_json_string():
    raw = '[{"id": 778, "name": "All Off", "used": true}, {"id": 5, "name": "Read"}]'
    assert lx._parse_mood_list(raw) == [{"id": 778, "name": "All Off"}, {"id": 5, "name": "Read"}]


def test_parse_mood_list_rejects_garbage():
    assert lx._parse_mood_list("not json") is None
    assert lx._parse_mood_list(123) is None


def test_moodlist_state_overlays_scene_options_on_republished_structure():
    import json as _json

    class FakeClient:
        def get_structure(self):
            return {
                "msInfo": {"serialNr": "SER"},
                "rooms": {},
                "controls": {
                    "u3": {
                        "name": "Light", "type": "LightControllerV2", "room": None,
                        "details": {"masterValue": "u3/masterValue"},
                        "subControls": {"u3/masterValue": {"name": "M", "type": "Dimmer"}},
                    },
                },
            }

    t = _RecordingTarget()
    b = lx.MiniserverBridge("SER", "h", "u", "p", t)
    b.client = FakeClient()
    b._on_state("u3", "LightControllerV2",
                {"moodList": '[{"id": 778, "name": "All On"}, {"id": 5, "name": "Read"}]'})
    struct = _json.loads(t.published["loxone2mqtt/SER/structure"])
    parent = next(c for c in struct["controls"] if c["uuid"] == "u3")
    assert parent["moods"] == [{"id": 778, "name": "All On"}, {"id": 5, "name": "Read"}]


def test_is_dim_level_distinguishes_verbs_from_levels():
    assert lx._is_dim_level("55") is True
    assert lx._is_dim_level("0") is True
    assert lx._is_dim_level("on") is False
    assert lx._is_dim_level("off") is False
    assert lx._is_dim_level("changeTo/778") is False


def test_build_structure_splits_lightcontroller_circuits_but_not_mastervalue():
    loxapp3 = {
        "msInfo": {"serialNr": "504F94A0"},
        "rooms": {"r2": {"name": "Office"}},
        "controls": {
            "u3": {
                "name": "Office Light", "type": "LightControllerV2", "room": "r2",
                "details": {"moodList": [{"id": 778, "name": "All On"}], "masterValue": "u3/masterValue"},
                "subControls": {
                    "u3/masterValue": {"name": "Master", "type": "Dimmer"},
                    "u3/AI1": {"name": "Circuit 1", "type": "Dimmer"},
                    "u3/AI2": {"name": "Circuit 2", "type": "Dimmer"},
                },
            },
        },
    }
    s = lx.build_structure(loxapp3, "504F94A0")
    uuids = {c["uuid"] for c in s["controls"]}
    # Parent light + the two circuit dimmers, but NOT the masterValue (it backs the parent).
    assert uuids == {"u3", "u3/AI1", "u3/AI2"}
    circuit = next(c for c in s["controls"] if c["uuid"] == "u3/AI1")
    assert circuit["type"] == "Dimmer"
    assert circuit["name"] == "Circuit 1"
    assert circuit["room"] == "r2"


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


# ── ColorPickerV2 mode detection, color math, climate, single-channel split (Task 4) ─

def test_hex_to_hsv_and_back_roundtrips_primary():
    assert lx.hsv_to_hex(*lx.hex_to_hsv("#FF0000")) == "#FF0000"
    assert lx.hsv_to_hex(*lx.hex_to_hsv("#00FF00")) == "#00FF00"
    assert lx.hsv_to_hex(*lx.hex_to_hsv("#0000FF")) == "#0000FF"


def test_hex_to_hsv_red():
    h, s, v = lx.hex_to_hsv("#FF0000")
    assert round(h) == 0 and round(s) == 100 and round(v) == 100


def test_build_structure_classifies_rgbw_colorpicker():
    loxapp3 = {"msInfo": {"serialNr": "S1"}, "rooms": {},
               "controls": {"u1": {"name": "Strip", "type": "ColorPickerV2", "room": "r1",
                                   "details": {"pickerType": "Rgbw"}}}}
    s = lx.build_structure(loxapp3, "S1")
    c = next(c for c in s["controls"] if c["uuid"] == "u1")
    assert c["type"] == "ColorPickerRGBW"


def test_build_structure_maps_room_controller():
    loxapp3 = {"msInfo": {"serialNr": "S1"}, "rooms": {},
               "controls": {"u2": {"name": "Climate", "type": "IRoomControllerV2", "room": "r1"}}}
    s = lx.build_structure(loxapp3, "S1")
    assert any(c["type"] == "RoomControllerV2" for c in s["controls"])


def test_flatten_room_controller_state():
    out = lx.flatten_control_state("RoomControllerV2",
        {"tempActual": 21.5, "tempTarget": 22, "mode": 1, "valve": 40})
    assert out == {"tempActual": 21.5, "tempTarget": 22, "mode": 1, "valve": 40}


def test_classify_colorpicker_tunable_white_from_details():
    assert lx.classify_colorpicker({"details": {"pickerType": "Lumitech"}}) == "ColorPickerTunableWhite"
    assert lx.classify_colorpicker({"details": {"colorMode": "tunableWhite"}}) == "ColorPickerTunableWhite"


def test_classify_colorpicker_single_channel_from_details():
    assert lx.classify_colorpicker({"details": {"pickerType": "singleChannel"}}) == "single"


def test_classify_colorpicker_defaults_to_rgbw_when_ambiguous():
    assert lx.classify_colorpicker({}) == "ColorPickerRGBW"
    assert lx.classify_colorpicker({"details": {}}) == "ColorPickerRGBW"


def test_build_structure_classifies_tunable_white_colorpicker():
    loxapp3 = {"msInfo": {"serialNr": "S1"}, "rooms": {},
               "controls": {"u1": {"name": "Panel", "type": "ColorPickerV2", "room": "r1",
                                   "details": {"pickerType": "Lumitech"}}}}
    s = lx.build_structure(loxapp3, "S1")
    c = next(c for c in s["controls"] if c["uuid"] == "u1")
    assert c["type"] == "ColorPickerTunableWhite"


def test_build_structure_presplits_single_channel_colorpicker_into_dimmers():
    loxapp3 = {"msInfo": {"serialNr": "S1"}, "rooms": {},
               "controls": {"u1": {"name": "Strip", "type": "ColorPickerV2", "room": "r1",
                                   "details": {"pickerType": "singleChannel"}}}}
    s = lx.build_structure(loxapp3, "S1")
    uuids = {c["uuid"] for c in s["controls"]}
    assert uuids == {"u1/r", "u1/g", "u1/b", "u1/w"}
    assert all(c["type"] == "Dimmer" for c in s["controls"])
    assert "u1" not in uuids


def test_flatten_colorpicker_rgbw_state():
    out = lx.flatten_control_state("ColorPickerRGBW",
        {"active": 1, "position": 80, "color": "#FF8800", "white": 30})
    assert out == {"active": 1, "position": 80, "color": "#FF8800", "white": 30}


def test_flatten_colorpicker_tunable_white_state():
    out = lx.flatten_control_state("ColorPickerTunableWhite",
        {"active": 1, "position": 60, "colortemp": 4000})
    assert out == {"active": 1, "position": 60, "colortemp": 4000}
