import roborock2mqtt as r


def test_map_status_attaches_transport_and_rooms():
    raw = {"state": 5, "battery": 87, "fan_power": 102, "error_code": 0}
    rooms = [{"id": 16, "name": "Kitchen"}]
    out = r.map_status_to_payload(raw, rooms, "local")
    assert out["state"] == 5
    assert out["battery"] == 87
    assert out["_transport"] == "local"
    assert out["_rooms"] == rooms


def test_translate_simple_commands():
    assert r.translate_command({"capability": "vacuum.start", "value": True})[0] == "APP_START"
    assert r.translate_command({"capability": "vacuum.stop", "value": True})[0] == "APP_STOP"
    assert r.translate_command({"capability": "vacuum.pause", "value": True})[0] == "APP_PAUSE"
    assert r.translate_command({"capability": "vacuum.dock", "value": True})[0] == "APP_CHARGE"
    assert r.translate_command({"capability": "vacuum.locate", "value": True})[0] == "FIND_ME"


def test_translate_fan_power():
    name, params = r.translate_command({"capability": "vacuum.fanPower", "value": 104})
    assert name == "SET_CUSTOM_MODE"
    assert params == [104]


def test_translate_clean_segments():
    name, params = r.translate_command({"capability": "vacuum.cleanSegments", "value": "16,17"})
    assert name == "APP_SEGMENT_CLEAN"
    assert params == [{"segments": [16, 17], "repeat": 1}]


def test_map_status_attaches_scenes():
    out = r.map_status_to_payload({"state": 8}, [{"id": 16, "name": "Kitchen"}], "cloud",
                                  scenes=[{"id": 1234, "name": "After dinner"}])
    assert out["_scenes"] == [{"id": 1234, "name": "After dinner"}]
    assert out["_rooms"] == [{"id": 16, "name": "Kitchen"}]

def test_map_status_scenes_default_empty():
    out = r.map_status_to_payload({"state": 8}, [], "cloud")
    assert out["_scenes"] == []
