import dreocloud


def test_api_base():
    assert dreocloud.api_base("us") == "https://app-api-us.dreo-tech.com"


def test_ws_url_contains_token_and_region():
    url = dreocloud.ws_url("eu", "TОKEN")
    assert url.startswith("wss://wsb-eu.dreo-tech.com/websocket?")
    assert "accessToken=T" in url  # token present
    assert "timestamp=" in url


def test_parse_login():
    token, region = dreocloud.parse_login(
        {"data": {"access_token": "abc", "region": "us"}})
    assert token == "abc"
    assert region == "us"


def test_parse_devices_maps_fields():
    devices = dreocloud.parse_devices({"data": {"list": [
        {"deviceSn": "SN1", "model": "DR-HCF001S", "deviceName": "Bedroom"},
        {"sn": "SN2", "productId": "DR-HCF002S"},
    ]}})
    assert devices[0] == {"serial": "SN1", "model": "DR-HCF001S", "name": "Bedroom"}
    assert devices[1]["serial"] == "SN2"
    assert devices[1]["name"] == "SN2"  # falls back to serial


def test_control_envelope():
    env = dreocloud.control_envelope("SN1", {"poweron": True}, 1234)
    assert env == {"devicesn": "SN1", "method": "control",
                   "params": {"poweron": True}, "timestamp": 1234}


import dreo2mqtt


def test_extract_reported_prefers_reported_dict():
    serial, state = dreo2mqtt.extract_reported(
        {"devicesn": "SN1", "reported": {"poweron": True, "windlevel": 2}})
    assert serial == "SN1"
    assert state == {"poweron": True, "windlevel": 2}


def test_extract_reported_returns_none_without_serial():
    assert dreo2mqtt.extract_reported({"reported": {"poweron": True}}) is None


def test_load_manifest_from_mongo_returns_region_tuple_without_pymongo():
    # pymongo isn't installed in this test env, so the ImportError fallback path runs;
    # this pins the 3-tuple shape (token, manifest, region) the region-persistence fix relies on.
    token, manifest, region = dreo2mqtt.load_manifest_from_mongo()
    assert (token, manifest, region) == (None, [], None)
