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


class _FakeBridge:
    """Stand-in for AccountBridge that records lifecycle calls without threads/WS."""
    instances = []

    def __init__(self, token, region, target):
        self.token = token
        self.region = region
        self.started = False
        self.stopped = False
        _FakeBridge.instances.append(self)

    def start(self):
        self.started = True

    def stop(self):
        self.stopped = True


def _manager_with_fake_bridge(monkeypatch):
    _FakeBridge.instances = []
    monkeypatch.setattr(dreo2mqtt, "AccountBridge", _FakeBridge)
    return dreo2mqtt.BridgeManager(target=object())


def test_bridge_manager_ensure_started_is_idempotent(monkeypatch):
    mgr = _manager_with_fake_bridge(monkeypatch)
    assert mgr.ensure_started("tok", "us") is True     # first call starts a bridge
    assert mgr.ensure_started("tok", "us") is False    # already running -> no-op
    assert len(_FakeBridge.instances) == 1
    assert _FakeBridge.instances[0].started is True


def test_bridge_manager_replace_starts_new_and_stops_old(monkeypatch):
    mgr = _manager_with_fake_bridge(monkeypatch)
    mgr.ensure_started("old-tok", "us")
    mgr.replace("new-tok", "eu")                        # live re-onboard
    assert len(_FakeBridge.instances) == 2
    old, new = _FakeBridge.instances
    assert old.stopped is True                          # previous bridge torn down
    assert new.started is True and new.stopped is False
    assert mgr.account is new
    assert (mgr.account.token, mgr.account.region) == ("new-tok", "eu")


def test_bridge_manager_forward_command_delegates_to_active_bridge(monkeypatch):
    mgr = _manager_with_fake_bridge(monkeypatch)
    sent = []
    mgr.ensure_started("tok", "us")
    mgr.account.forward_command = lambda serial, payload: sent.append((serial, payload))
    mgr.forward_command("SN1", '{"poweron": true}')
    assert sent == [("SN1", '{"poweron": true}')]


def test_bridge_manager_forward_command_noop_without_bridge(monkeypatch):
    mgr = _manager_with_fake_bridge(monkeypatch)
    # No bridge started yet: must not raise, just drop the command.
    mgr.forward_command("SN1", '{"poweron": true}')
