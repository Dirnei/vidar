"""loxone_client - thin synchronous wrapper over `pyloxone-api` (pinned surface: 0.2.4).

Every `pyloxone-api` call lives in this file so the rest of the sidecar (loxone2mqtt.py) stays
library-agnostic and importable without it installed. Confirmed by reading pyloxone-api==0.2.4
source directly (`api.py`, `message.py`) — the library is asyncio-only (`LoxAPI` has no sync
surface) and its own `httpx<0.20.0` pin fails to import on Python 3.13+ (`import cgi`, removed
by PEP 594), so it could not be exercised end-to-end in this sandbox. The exact call sequence,
message shapes, and reconnect behavior below are believed correct from source but are UNVERIFIED
against a real Miniserver until Task 13 (the documented E2E boundary for this file) — treat any
detail here as a best-effort reading of the library, not a confirmed contract.

Relevant pyloxone_api.api.LoxAPI surface this wrapper drives:
  - LoxAPI(host, port, user, password, use_tls, token_persist_filename) -- construct
  - await api.getJson()        -- HTTP: fetches LoxAPP3.json (-> api.json), the public key, and
                                   the serial (api.snr)
  - await api.async_init()     -- RSA/AES key exchange + token acquisition/refresh; opens the WS
                                   (api._ws). Returns True on success.
  - await api.start()          -- runs the WS listen + keepalive + token-refresh loop together;
                                   BLOCKS until disconnected, and internally retries/reconnects
                                   (connect_retries / connect_delay) before giving up. There is no
                                   separate "run once" call - this owns the connection's lifetime.
  - api.message_call_back      -- an ASYNC callable(dict); awaited once per decoded binary event
                                   batch. The dict is flat: {state_uuid: value} (see
                                   pyloxone_api.message.ValueStatesTable/TextStatesTable.as_dict).
  - await api.send_websocket_command(uuid, value) -- sends "jdev/sps/io/<uuid>/<value>" verbatim
                                   (unencrypted, over the already-authenticated WS).
  - await api.stop()            -- closes the WS.

pyloxone-api is fully asyncio; the rest of this sidecar (MiniserverBridge) is a plain thread,
mirroring dreo2mqtt's `websocket.WebSocketApp` thread. So `LoxoneClient` runs one dedicated
asyncio event loop per Miniserver on a background thread, and exposes a synchronous surface by
scheduling coroutines onto that loop (`asyncio.run_coroutine_threadsafe`).
"""
import asyncio
import threading

# loxone2mqtt.py owns the pure color math (hex_to_hsv/hsv_to_hex) and the ColorPickerV2 mode
# classifier (classify_colorpicker) and stays pyloxone-api-free; this module is the one place
# that turns those values into (and back out of) real Loxone command/state strings. loxone2mqtt
# has no heavy top-level imports (colorsys is stdlib), so importing it here at module scope
# doesn't compromise "import loxone2mqtt works without pyloxone-api/paho/pymongo" -- the reverse
# direction (loxone2mqtt importing this file) stays lazy, inside functions, as before.
from loxone2mqtt import classify_colorpicker, hex_to_hsv, hsv_to_hex

# How often to re-fetch LoxAPP3.json and diff it for a structure change, since pyloxone-api
# 0.2.4 does not expose a distinct "structure changed" push event (unlike per-control state
# updates). Best-effort/heuristic — confirmed or replaced with a real push signal at Task 13.
STRUCTURE_POLL_SECONDS = 300


class LoxoneClient:
    """One Miniserver connection. Not thread-safe to call concurrently from multiple threads;
    MiniserverBridge owns exactly one of these per Miniserver and calls it from its own thread
    (except send_command, which may be called from the MQTT client's thread - each call is
    independently scheduled onto this client's own event loop, so that's safe)."""

    def __init__(self, host, user, password, port=80, use_tls=False):
        self._host = host
        self._user = user
        self._password = password
        self._port = port
        self._use_tls = use_tls

        self._api = None
        self._loop = None
        self._loop_thread = None
        self._start_future = None
        # True once async_init() (the RSA/AES handshake + token auth) has succeeded, until
        # close() runs. Lets MiniserverBridge.forward_command check liveness without reaching
        # into pyloxone-api internals.
        self._connected = False

        self._state_cb = None
        self._structure_cb = None
        self._structure_last_modified = None
        self._structure_poll_stop = threading.Event()

        # state_uuid -> (control_uuid, control_type, state_name); rebuilt from LoxAPP3.json so
        # incoming {state_uuid: value} event batches can be resolved to the named per-control
        # fields flatten_control_state() expects. control_type here is already normalized
        # (ColorPickerRGBW/ColorPickerTunableWhite/RoomControllerV2) to match build_structure()'s
        # manifest, so flatten_control_state() sees the same type string either way.
        self._state_index = {}

        # control_uuid -> last-known brightness/position (0..100). Needed to translate a bare
        # "temp/<kelvin>" verb (Task 3) into Loxone's temp(<brightness>,<kelvin>) command, which
        # requires a brightness even though the worker only sends the kelvin change.
        self._last_position = {}

    # ── Connection lifecycle ────────────────────────────────────────────────

    def connect(self):
        """Start the background event loop, run the connect sequence (HTTP getJson + RSA/AES
        handshake + token auth) on it and block for the result, then start the persistent
        listen/keepalive task in the background. Raises on handshake failure."""
        from pyloxone_api import LoxAPI

        self._loop = asyncio.new_event_loop()
        self._loop_thread = threading.Thread(target=self._loop.run_forever,
                                              name="loxone-client-loop", daemon=True)
        self._loop_thread.start()

        self._api = LoxAPI(host=self._host, port=self._port, user=self._user,
                           password=self._password, use_tls=self._use_tls)
        self._api.message_call_back = self._on_message

        self._call(self._api.getJson())
        ok = self._call(self._api.async_init())
        if not ok:
            raise ConnectionError(f"Loxone handshake failed for {self._host}")
        self._connected = True

        self._rebuild_state_index()
        self._structure_last_modified = (self._api.json or {}).get("lastModified")

        # api.start() owns the WS listen/keepalive/token-refresh loop and BLOCKS until
        # disconnected (with its own internal reconnect retries) - run it as a background task
        # on this client's loop, not on the calling thread.
        self._start_future = asyncio.run_coroutine_threadsafe(self._api.start(), self._loop)

        threading.Thread(target=self._poll_structure, name="loxone-structure-poll",
                         daemon=True).start()

    def run_forever(self):
        """Block the calling thread until this connection ends (api.start() returns or raises -
        it owns its own reconnect retries internally). Lets MiniserverBridge mirror dreo2mqtt's
        blocking `ws.run_forever()` pattern despite pyloxone-api being asyncio under the hood.
        Not part of the pyloxone-api surface - added here to bridge the sync/async gap."""
        if self._start_future is not None:
            try:
                self._start_future.result()
            except Exception:  # noqa: BLE001
                pass

    def close(self):
        self._connected = False
        self._structure_poll_stop.set()
        if self._api is not None:
            try:
                self._call(self._api.stop(), timeout=10)
            except Exception:  # noqa: BLE001
                pass
        if self._loop is not None:
            self._loop.call_soon_threadsafe(self._loop.stop)

    @property
    def is_connected(self) -> bool:
        """True once connect() has completed the handshake successfully, until close() runs."""
        return self._connected

    def _call(self, coro, timeout=30):
        """Run `coro` on this client's event loop and block the calling thread for the result."""
        return asyncio.run_coroutine_threadsafe(coro, self._loop).result(timeout)

    # ── Structure ────────────────────────────────────────────────────────────

    def get_structure(self) -> dict:
        """Return the raw LoxAPP3.json dict fetched by getJson() during connect()."""
        return self._api.json or {}

    def _rebuild_state_index(self):
        for uuid_, control in (self._api.json or {}).get("controls", {}).items():
            ctype = self._normalize_control_type(control)
            for name, state_uuid in (control.get("states") or {}).items():
                if not isinstance(state_uuid, str):
                    continue  # some states map to a list of uuids; not expected for Phase A types
                self._state_index[state_uuid] = (uuid_, ctype, name)

    @staticmethod
    def _normalize_control_type(control: dict) -> str:
        """Mirror loxone2mqtt.build_structure()'s type normalization so the state callback
        reports the same normalized type string the worker's manifest uses (flatten_control_state
        is keyed by it). IRoomControllerV2 -> RoomControllerV2; ColorPickerV2 -> ColorPickerRGBW /
        ColorPickerTunableWhite via the shared classify_colorpicker heuristic.

        Known gap (E2E boundary, same as build_structure's single-channel split): a
        single-channel ColorPickerV2 is pre-split into 4 Dimmer *controls* in the manifest, but
        this state index still has only the ONE raw control uuid/type from LoxAPP3.json -- there
        is no confirmed mapping from that single control's state uuids to the split r/g/b/w
        Dimmer uuids until a real Miniserver capture confirms whether Loxone even exposes
        independent per-channel state uuids. Until then such a control's raw type
        ("ColorPickerV2") is left unnormalized so it visibly falls through
        flatten_control_state() as unhandled, rather than silently mis-tagged."""
        raw_type = control.get("type")
        if raw_type == "IRoomControllerV2":
            return "RoomControllerV2"
        if raw_type == "ColorPickerV2":
            mode = classify_colorpicker(control)
            return mode if mode != "single" else raw_type
        return raw_type

    def on_structure_changed(self, callback):
        """Register `callback()` (no args), invoked when a re-fetched LoxAPP3.json's
        `lastModified` differs from the one seen at connect()/the last change. The caller
        (MiniserverBridge) re-fetches via get_structure() and republishes."""
        self._structure_cb = callback

    def _poll_structure(self):
        while not self._structure_poll_stop.wait(STRUCTURE_POLL_SECONDS):
            try:
                self._call(self._api.getJson())
            except Exception:  # noqa: BLE001
                continue
            modified = (self._api.json or {}).get("lastModified")
            if modified != self._structure_last_modified:
                self._structure_last_modified = modified
                self._rebuild_state_index()
                if self._structure_cb is not None:
                    try:
                        self._structure_cb()
                    except Exception:  # noqa: BLE001
                        pass

    # ── State ────────────────────────────────────────────────────────────────

    def on_state(self, callback):
        """Register `callback(control_uuid, control_type, states_dict)`, invoked once per
        control that appears in an incoming event batch, with only the fields that changed."""
        self._state_cb = callback

    async def _on_message(self, parsed_data):
        # `api.message_call_back` is awaited by pyloxone-api, so this must be a coroutine.
        # `parsed_data` is a flat {state_uuid: value} dict - group by owning control and invoke
        # the state callback once per control with just the fields present in this batch.
        if not self._state_cb or not isinstance(parsed_data, dict):
            return
        by_control = {}
        for state_uuid, value in parsed_data.items():
            hit = self._state_index.get(state_uuid)
            if not hit:
                continue
            control_uuid, ctype, name = hit
            states = by_control.setdefault(control_uuid, (ctype, {}))[1]
            if ctype in ("ColorPickerRGBW", "ColorPickerTunableWhite") and name == "color":
                # LIVE-CAPTURE: convert the raw ColorPickerV2 "color" state value into the
                # normalized fields flatten_control_state() expects, instead of passing the raw
                # Loxone string straight through.
                states.update(_parse_colorpicker_value(value))
            else:
                states[name] = value
        for control_uuid, (ctype, states) in by_control.items():
            if "position" in states:
                # Remembered so a later bare "temp/<kelvin>" command (which carries no brightness
                # of its own) can be translated into Loxone's temp(<brightness>,<kelvin>).
                self._last_position[control_uuid] = states["position"]
            try:
                self._state_cb(control_uuid, ctype, states)
            except Exception:  # noqa: BLE001
                pass

    # ── Commands ─────────────────────────────────────────────────────────────

    def send_command(self, uuid, command):
        """`command` is the full jdev command string built by `command_url()`, e.g.
        "jdev/sps/io/<uuid>/on". `LoxAPI.send_websocket_command(uuid, value)` re-builds that exact
        same "jdev/sps/io/<uuid>/<value>" string from `value` alone, so passing it the full string
        would double the prefix. Recover the verb by stripping the known prefix, translate the
        normalized worker verb (Task 3) into the real Loxone command where the two differ
        (ColorPickerV2/IRoomControllerV2), and delegate to the public method rather than reaching
        into the library's private websocket."""
        prefix = f"jdev/sps/io/{uuid}/"
        verb = command[len(prefix):] if command.startswith(prefix) else command
        verb = self._translate_verb(uuid, verb)
        self._call(self._api.send_websocket_command(uuid, verb))

    def _translate_verb(self, uuid, verb: str) -> str:
        """Translate a normalized worker verb into the real Loxone command string for the Phase B
        types. Every Loxone-format string returned here is the LIVE-CAPTURE / E2E-confirmed
        boundary for this file (Task 13-equivalent for loxone2mqtt): each is a best-effort reading
        of community Loxone integrations (openHAB/ioBroker Loxone bindings), not yet exercised
        against a real Miniserver. `on`/`off`/a bare brightness percent/`changeTo/<id>` (Phase A)
        pass through unchanged."""
        if verb.startswith("color/"):
            hex_value = verb[len("color/"):]
            h, s, v = hex_to_hsv(hex_value)
            # E2E-confirmed: ColorPickerV2 accepts "hsv(<h 0..360>,<s 0..100>,<v 0..100>)" as its
            # color-set command.
            return f"hsv({h:.0f},{s:.0f},{v:.0f})"
        if verb.startswith("white/"):
            # E2E-confirmed: best-effort -- lacking a confirmed dedicated white-channel command
            # for a ColorPickerV2 in RGBW mode, pass the percentage through as a plain brightness
            # set (mirrors the Dimmer verb). Correct/replace at E2E if Loxone exposes a distinct
            # white-channel command.
            return verb[len("white/"):]
        if verb.startswith("temp/"):
            kelvin = verb[len("temp/"):]
            brightness = self._last_position.get(uuid, 100)
            # E2E-confirmed: ColorPickerV2 in tunable-white mode accepts
            # "temp(<brightness 0..100>,<kelvin>)"; the worker's temp/<kelvin> verb carries no
            # brightness of its own, so the last-known position is reused (100 if never seen).
            return f"temp({brightness:.0f},{kelvin})"
        if verb.startswith("settemp/"):
            value = verb[len("settemp/"):]
            # E2E-confirmed: IRoomControllerV2 target-temperature setpoint command.
            return f"setpoint/{value}"
        if verb.startswith("climatemode/"):
            mode = verb[len("climatemode/"):]
            # E2E-confirmed: IRoomControllerV2 operating-mode command.
            return f"mode/{mode}"
        return verb  # on/off/bare brightness/changeTo/<id>: Phase A pass-through, unchanged


def _parse_colorpicker_value(raw) -> dict:
    """Convert a ColorPickerV2 raw "color" state value into the normalized fields
    flatten_control_state() expects. Loxone reports the live color as a string:
    "hsv(<h>,<s>,<v>)" in RGBW mode, "temp(<brightness>,<kelvin>)" in tunable-white mode.
    LIVE-CAPTURE: this exact string format is a best-effort reading of community Loxone
    integrations, unconfirmed against a real Miniserver until E2E. Returns {} for anything that
    doesn't match either shape, so an unrecognized value is dropped rather than mis-published."""
    if not isinstance(raw, str):
        return {}
    text = raw.strip()
    if text.startswith("hsv(") and text.endswith(")"):
        try:
            h, s, v = (float(x) for x in text[len("hsv("):-1].split(","))
        except ValueError:
            return {}
        return {"color": hsv_to_hex(h, s, v), "position": v}
    if text.startswith("temp(") and text.endswith(")"):
        try:
            brightness, kelvin = (float(x) for x in text[len("temp("):-1].split(","))
        except ValueError:
            return {}
        return {"colortemp": kelvin, "position": brightness}
    return {}
