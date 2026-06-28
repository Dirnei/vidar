# dyson2mqtt

A small standalone bridge that connects Dyson cloud devices (AWS IoT) and republishes their
state to a normal MQTT broker — in the spirit of [zigbee2mqtt](https://www.zigbee2mqtt.io/).

Newer Dyson devices (e.g. the 358K) only talk to the cloud over AWS IoT (MQTT over WebSocket
with a custom authorizer). In practice the `paho` MQTT client is the only one that reliably
publishes to that endpoint, so this tool owns the cloud connection and bridges it to a broker
your other tools (Vidar, Home Assistant, …) can consume.

## What it does

For each device on the account it:

1. exchanges the account token for short-lived AWS IoT credentials,
2. connects to AWS IoT over WSS and subscribes to `<product>/<serial>/status/#`,
3. requests `CURRENT-STATE` on an interval,
4. republishes every device message to `<base>/<serial>` on the target broker (retained), and
5. forwards anything published to `<base>/<serial>/set` back to the device's command topic.

## Configuration (environment variables)

Everything is configurable; nothing is hard-coded.

### Target MQTT broker (where state is published)

| Var | Default | Notes |
| --- | --- | --- |
| `MQTT_HOST` | `emqx` | broker host |
| `MQTT_PORT` | `1883` | broker port |
| `MQTT_USERNAME` | – | optional |
| `MQTT_PASSWORD` | – | optional |
| `MQTT_BASE_TOPIC` | `dyson2mqtt` | topic prefix |

### Dyson account / cloud

| Var | Default | Notes |
| --- | --- | --- |
| `DYSON_TOKEN` | – | MyDyson account token. If unset, read from Mongo (below). |
| `DYSON_DEVICES` | – | optional explicit list `serial:product,serial:product` (skips discovery) |
| `DYSON_API_BASE` | `https://appapi.cp.dyson.com` | region API host |
| `DYSON_POLL_INTERVAL` | `30` | seconds between `CURRENT-STATE` requests |

### Mongo (only used when `DYSON_TOKEN` is unset)

Lets the tool "just work" inside the Vidar stack by reading the token + device manifest that
Vidar's onboarding already stored.

| Var | Default |
| --- | --- |
| `MONGO_URI` | `mongodb://mongodb:27017` |
| `MONGO_DB` | `vidar` |

## Run

```bash
# standalone, against any broker
docker build -t dyson2mqtt tools/dyson2mqtt
docker run --rm \
  -e DYSON_TOKEN=... \
  -e DYSON_DEVICES=X6P-EU-XXXXXXX:358K \
  -e MQTT_HOST=192.168.1.10 -e MQTT_PORT=1883 \
  dyson2mqtt
```

Inside the Vidar `docker-compose` stack it needs no Dyson config — it reads the token and
device list from Mongo and publishes to the bundled `emqx` broker by default.

## Topics

- State: `dyson2mqtt/<serial>` — the raw device JSON (`CURRENT-STATE`, `ENVIRONMENTAL-…`, …)
- Command: `dyson2mqtt/<serial>/set` — JSON forwarded to the device (e.g. a `STATE-SET` message)
