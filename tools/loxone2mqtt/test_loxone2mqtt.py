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
