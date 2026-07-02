# Ecowitt GW3001 → Vidar (MQTT) setup

Vidar consumes the gateway's **native MQTT** publications from the built-in EMQX
broker. Configure the gateway once via the **Ecowitt WS View Plus** app (or the
gateway's web UI), then enable the integration in Vidar.

## 1. Point the gateway at EMQX

In WS View Plus → your GW3001 → **More** → **MQTT** (custom broker):

| Setting | Value |
|---|---|
| Broker / Host | the host running EMQX, reachable from the gateway's LAN |
| Port | `1883` |
| Client ID | anything stable, e.g. `gw3001` |
| Topic | `ecowitt` (must match the Vidar `topic` setting) |
| Username / Password | only if EMQX auth is enabled (match the Vidar settings) |
| Upload interval | e.g. 60 s |

If EMQX is not running in anonymous mode, create a broker user for the gateway
and use the same credentials in Vidar.

## 2. Enable the integration in Vidar

Open **Applications → Ecowitt Weather**, toggle it on, and set:

| Key | Default | Notes |
|---|---|---|
| `mqttHost` | `emqx` | EMQX hostname on the Vidar network |
| `mqttPort` | `1883` | |
| `mqttUser` | (empty) | optional |
| `mqttPassword` | (empty) | optional |
| `topic` | `ecowitt` | must match the gateway's topic |
| `staleAfterSeconds` | `300` | status → `degraded` if no message within this window |

Within one upload interval the station appears as a device with outdoor/indoor
temperature & humidity, pressure, wind, rain, solar, UV, and the outdoor sensor
battery flag.
