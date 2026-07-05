# dreo2mqtt

A small standalone bridge that connects a Dreo cloud account (ceiling fans, etc.) and
republishes device state to a normal MQTT broker — in the spirit of
[zigbee2mqtt](https://www.zigbee2mqtt.io/).

Dreo devices only talk to Dreo's cloud: a REST login/device-list API plus a realtime
WebSocket. This tool owns that cloud protocol (via `dreocloud.py`'s pure REST/WS helpers)
and bridges it to a broker your other tools (Vidar, Home Assistant, …) can consume.

## What it does

For the account's devices it:

1. logs in to Dreo's cloud (`POST /api/oauth/login`) and fetches the device list,
2. connects a single realtime WebSocket per account (`dreocloud.ws_url`),
3. republishes each device's flattened reported state to `<base>/<serial>` on the target
   broker (retained), and
4. forwards anything published to `<base>/<serial>/set` to that device via
   `dreocloud.control_envelope`.

It also serves a small onboarding HTTP endpoint (gated by `DREO_ONBOARD_HTTP=1`) that the
Vidar host calls during account linking, so Dreo credentials never need to reach the host
directly.

## Configuration (environment variables)

Everything is configurable; nothing is hard-coded.

### Target MQTT broker (where state is published)

| Var | Default | Notes |
| --- | --- | --- |
| `MQTT_HOST` | `emqx` | broker host |
| `MQTT_PORT` | `1883` | broker port |
| `MQTT_USERNAME` | – | optional |
| `MQTT_PASSWORD` | – | optional |
| `MQTT_BASE_TOPIC` | `dreo2mqtt` | topic prefix |

### Dreo account / cloud

| Var | Default | Notes |
| --- | --- | --- |
| `DREO_DEFAULT_REGION` | `us` | region used for the first login attempt; if the login response names a different region, the tool re-logs in against it once |
| `DREO_ONBOARD_HTTP` | – | set to `1` to enable the onboarding HTTP server |
| `DREO_ONBOARD_PORT` | `8896` | onboarding HTTP port |
| `DREO_POLL_INTERVAL` | `30` | seconds between Mongo manifest polls |

### Mongo (the account token + device manifest live here, written by Vidar onboarding)

| Var | Default |
| --- | --- |
| `MONGO_URI` | `mongodb://mongodb:27017` |
| `MONGO_DB` | `vidar` |

The token and manifest are read from the `vidar.integrations` document with `_id: "dreo"`,
fields `Settings["account.token"]` (the access token) and `Settings["account.manifest"]`
(a JSON array of `{"serial", "model", "name"}`).

## Run

```bash
docker build -t dreo2mqtt tools/dreo2mqtt
docker run --rm \
  -e MQTT_HOST=192.168.1.10 -e MQTT_PORT=1883 \
  -e MONGO_URI=mongodb://192.168.1.10:27017 \
  dreo2mqtt
```

Inside the Vidar `docker-compose` stack it needs no extra config — it reads the token and
device manifest from Mongo (once onboarded) and publishes to the bundled `emqx` broker by
default.

## Topics

- State: `dreo2mqtt/<serial>` — the device's flattened reported state, as JSON (retained)
- Command: `dreo2mqtt/<serial>/set` — a JSON params object forwarded to the device as a
  Dreo `control` command

## Onboarding HTTP endpoint

Gated by `DREO_ONBOARD_HTTP=1`, listening on `DREO_ONBOARD_PORT` (default `8896`):

- `POST /auth/login` — body `{"email", "password"}`. On success: `200
  {"userDataJson": <access token>, "devices": [{"serial","model","name"}, ...]}`. On an
  upstream login/device-list failure, responds with the upstream HTTP status (401
  invalid credentials, 429 rate limited, 502 otherwise) so the host can map it.

The onboarding server starts before any Mongo manifest exists and never exits when one is
missing — the account bridge simply stays idle (logging that it's waiting) until Vidar's
onboarding flow writes the token + manifest to Mongo.
