# Vidar MVP — Design Specification

Vidar is an autonomous home orchestration platform. It is not a dashboard — it is a residential operating system that manages the home according to the homeowner's goals. The MVP establishes the foundation: device integration, digital twins, basic controls, and the plugin architecture.

## Scope

### In Scope

- Akka.NET actor system with cluster sharding (single node, designed for multi-node)
- Device digital twins as sharded entities (actors and sensors)
- Room configuration via MongoDB
- Two Communication nodes: Shelly (HTTP) and Zigbee2MQTT (MQTT)
- REST API for commands and configuration
- SSE for real-time state updates to the frontend
- React/TypeScript frontend: room/device management, basic controls, device discovery
- Docker Compose deployment (Vidar.Host + Communication nodes + MongoDB + EMQX)

### Out of Scope (Future)

- Decision engine, goals, constraints, policies
- AI/prediction (occupancy, energy forecasting, comfort optimization)
- Behavioral patterns (auto-lighting, energy optimization)
- Event sourcing / decision history / explainability
- Multi-node clustering (the model supports it, but not deployed that way yet)

## Solution Structure

```
Vidar.sln
├── src/
│   ├── Vidar.Core/                          — Shared messages, interfaces, device model
│   ├── Vidar.Host/                          — ASP.NET Core: actor system, API, SSE, serves frontend
│   ├── Vidar.Communication.Shelly/          — Cluster node: Shelly HTTP integration
│   └── Vidar.Communication.Zigbee2Mqtt/     — Cluster node: Zigbee2MQTT via MQTT
├── frontend/                                — React/TypeScript SPA
├── docker-compose.yml
└── vision.md
```

### Project Dependencies

- `Vidar.Core` — standalone class library, no dependencies on other Vidar projects
- `Vidar.Host` — references `Vidar.Core`
- `Vidar.Communication.Shelly` — references `Vidar.Core`
- `Vidar.Communication.Zigbee2Mqtt` — references `Vidar.Core`

Communication projects never reference Host. Host never references Communication projects. All inter-process communication is via Akka messages defined in Core.

## Technology Stack

- .NET 10, C#
- Akka.NET 1.5.68+ (Akka.Cluster, Akka.Cluster.Sharding, Akka.Streams, Akka.Cluster.Tools)
- ASP.NET Core (Kestrel)
- MongoDB (via MongoDB.Driver)
- React 19, TypeScript
- Docker, Docker Compose
- EMQX (MQTT broker for Zigbee2MQTT, includes management dashboard)
- HiveMQtt (MQTT client library for .NET)

## Device Model

### Two Categories

- **Actors** — devices you control: lights, shades/covers, heating, wallbox, AC, audio receiver, switches, dimmers
- **Sensors** — devices you read: temperature, motion, solar production, energy meters, humidity

A single physical device can expose both actor and sensor capabilities (e.g., a Shelly Plug has switch + power + energy).

### Capabilities

Capabilities define what a device can do or report:

- `Switch` — on/off (actor)
- `Dimmer` — brightness 0-100% (actor)
- `Cover` — position 0-100% (actor)
- `Temperature` — degrees Celsius (sensor)
- `Motion` — detected/clear (sensor)
- `Power` — watts (sensor)
- `Energy` — kWh (sensor)
- `Humidity` — percentage (sensor)

This list grows as new device types are integrated. Each capability defines its value type and whether it is controllable (actor) or read-only (sensor).

### Identity

Each device twin is identified by a GUID, assigned at discovery or configuration time. Human-readable names and room assignments are stored in MongoDB configuration, not in the identity.

## Actor Architecture

### Akka.NET Cluster

- Single seed node: `vidar-host`
- Communication nodes join the cluster by pointing to the seed node
- Akka.Management for health checks
- Split-brain resolver configured but not critical for single-node deployment

### Device Twins as Sharded Entities

Each device is a sharded entity in Akka.Cluster.Sharding. Entity ID is the device's GUID (string representation).

```
Cluster Sharding (entity type: "device-twin")
  /device-twin/{guid-1}     — Shelly Plug (capabilities: Switch, Power, Energy)
  /device-twin/{guid-2}     — Zigbee Motion Sensor (capabilities: Motion, Temperature)
  /device-twin/{guid-3}     — Zigbee Dimmer (capabilities: Dimmer)
  ...
```

No actor hierarchy. Rooms are a configuration concept in MongoDB, not an actor topology concern. This keeps the actor model flat and simple.

### Device Twin Responsibilities

- Hold current state in memory (capability → value map)
- Persist state to MongoDB on change
- Publish state change events via Akka Distributed Pub/Sub
- Receive commands and forward them to the appropriate Communication node via Distributed Pub/Sub (topic per communication type, e.g., `commands.shelly`)

### Communication Node Responsibilities

1. **Discovery** — find devices on the network and publish `DeviceDiscovered` messages to the Host
2. **Ingest** — translate protocol-specific state into `DeviceStateUpdate` messages and send to device twins via cluster sharding
3. **Execute** — subscribe to a Distributed Pub/Sub topic (e.g., `commands.shelly`), receive `DeviceCommand` messages, translate to protocol-specific calls

Communication nodes are stateless bridges. They translate between vendor protocols and the Vidar message contract.

## Core Message Contract

All messages live in `Vidar.Core`. These are the only types that cross process boundaries.

### State Updates (Communication → Device Twin)

```csharp
record DeviceStateUpdate(Guid DeviceId, CapabilityType Capability, object Value);
```

Sent by Communication nodes when a device's state changes. Routed to the device twin via cluster sharding.

### Commands (Device Twin → Communication)

```csharp
record DeviceCommand(Guid DeviceId, string CommunicationType, string NativeId, CapabilityType Capability, object Value);
```

The device twin knows its `CommunicationType` and `NativeId` from its configuration (loaded from MongoDB on startup). It includes both in the command so the Communication node can route to the correct physical device. Published to Distributed Pub/Sub topic `commands.{communicationType}`.

### Discovery (Communication → Host)

```csharp
record DeviceDiscovered(
    Guid DeviceId,
    string CommunicationType,
    string NativeId,
    IReadOnlyList<CapabilityType> Capabilities,
    Dictionary<string, string> Metadata);
```

Sent when a Communication node discovers a new device. Host stores it as an unconfigured device in MongoDB.

### Capability Type

```csharp
enum CapabilityType
{
    Switch,
    Dimmer,
    Cover,
    Temperature,
    Motion,
    Power,
    Energy,
    Humidity
}
```

## Communication Nodes

### Vidar.Communication.Shelly

- Discovers Shelly devices via mDNS on the local network
- Polls device state via Shelly HTTP API (Gen1 and Gen2)
- Translates Shelly state into `DeviceStateUpdate` messages
- Executes commands by calling Shelly HTTP endpoints
- Akka.Streams pipeline: Source (poll/mDNS) → Flow (parse) → Sink (shard region)

### Vidar.Communication.Zigbee2Mqtt

Uses HiveMQtt as the MQTT client library.

**Zigbee2MQTT MQTT API** is comprehensive — the Communication node leverages it fully:

#### Discovery & Device Metadata

- Subscribes to `zigbee2mqtt/bridge/devices` (retained) — receives a full array of all paired devices with IEEE address, friendly name, supported status, power source, interview state, and crucially the **`exposes`** structure describing every capability
- Subscribes to `zigbee2mqtt/bridge/event` — receives `device_joined`, `device_announce`, `device_interview`, `device_leave` events for real-time discovery
- Subscribes to `zigbee2mqtt/bridge/state` — bridge online/offline status
- Subscribes to `zigbee2mqtt/bridge/groups` — Zigbee group definitions

#### Zigbee2MQTT Exposes System

Zigbee2MQTT describes device capabilities via a structured `exposes` JSON on each device. This maps directly to Vidar's capability model:

**Generic types:** `binary`, `numeric`, `enum`, `text`, `composite`, `list` — each with an `access` bitmask (bit 1: readable, bit 2: settable via `/set`, bit 3: retrievable via `/get`)

**Specific types** (domain capabilities containing `features` arrays of generic types):
- `light` — state, brightness, color_temp, color_xy, color_hs
- `switch` — state (ON/OFF)
- `cover` — state (OPEN/CLOSE), position (0-100), tilt (0-100)
- `climate` — system_mode, local_temperature, heating/cooling setpoints, fan_mode, preset
- `fan` — state, mode
- `lock` — state (LOCK/UNLOCK), lock_state

The Communication node parses `exposes` at discovery time to populate `DeviceDiscovered.Capabilities`. The access bitmask determines whether a capability maps to an actor (settable, bit 2) or sensor (read-only, bit 1 only).

#### State Ingestion

- Subscribes to `zigbee2mqtt/+` (wildcard for all device state topics)
- Each device publishes JSON state to `zigbee2mqtt/{friendly_name}`, e.g., `{"temperature": 22.4, "humidity": 44.7}` or `{"state": "ON", "brightness": 215}`
- Subscribes to `zigbee2mqtt/+/availability` for device online/offline status
- Translates JSON properties into `DeviceStateUpdate` messages using the known exposes mapping

#### Command Execution

- Publishes commands to `zigbee2mqtt/{friendly_name}/set` as JSON, e.g., `{"state": "ON", "brightness": 200}`
- Supports partial updates — only changed properties need to be sent
- Supports `zigbee2mqtt/{friendly_name}/get` with `{"state": ""}` to request current state

#### Group Support

- Zigbee2MQTT supports device groups (controlled as one entity)
- Groups are managed via `zigbee2mqtt/bridge/request/group/add|remove|members/add|members/remove`
- Groups receive state and commands just like individual devices

#### Akka.Streams Pipeline

```
Source (HiveMQtt subscription to zigbee2mqtt/#) → Flow (parse JSON, map exposes to capabilities) → Sink (shard region)
```

## Persistence

### MongoDB Collections

**`devices`** — configured device records

```json
{
  "_id": "guid",
  "name": "Living Room Ceiling Light",
  "roomId": "guid",
  "communicationType": "shelly",
  "nativeId": "shellyplus1-abc123",
  "capabilities": ["Switch", "Power", "Energy"],
  "settings": {}
}
```

**`rooms`** — room definitions

```json
{
  "_id": "guid",
  "name": "Living Room"
}
```

**`deviceState`** — latest state snapshot per device, written by twins

```json
{
  "_id": "guid",
  "states": {
    "Switch": true,
    "Power": 45.2,
    "Energy": 12.34
  },
  "lastUpdated": "2026-06-04T10:30:00Z"
}
```

**`discoveredDevices`** — unconfigured devices waiting for user assignment

```json
{
  "_id": "guid",
  "communicationType": "zigbee2mqtt",
  "nativeId": "0x00158d0001a2b3c4",
  "capabilities": ["Motion", "Temperature"],
  "metadata": { "model": "RTCGQ11LM", "vendor": "Xiaomi" },
  "discoveredAt": "2026-06-04T10:00:00Z"
}
```

### Data Flow

- Device twins write their own state to `deviceState` on every state change
- The API reads from MongoDB for all queries (no ask pattern on actors)
- Configuration changes (room assignment, naming) go through the API → MongoDB → actor restarts if needed

## API

REST API hosted on `Vidar.Host` via ASP.NET Core.

### Endpoints

```
GET    /api/rooms                       — list all rooms
POST   /api/rooms                       — create a room
PUT    /api/rooms/{id}                  — update a room
DELETE /api/rooms/{id}                  — delete a room
GET    /api/rooms/{id}/devices          — devices in a room

GET    /api/devices                     — all configured devices with current state
GET    /api/devices/{id}                — single device with state
POST   /api/devices/{id}/command        — send command to device
PUT    /api/devices/{id}                — update device config (name, room)
DELETE /api/devices/{id}                — remove device

GET    /api/devices/discovered          — unconfigured devices
POST   /api/devices/discovered/{id}/configure  — assign name and room to a discovered device

GET    /api/sse/state                   — SSE stream of real-time device state changes
```

### SSE Stream

The Host subscribes to Akka Distributed Pub/Sub for device state change events. When a device twin publishes a state change, the Host pushes it to all connected SSE clients:

```
event: deviceStateChanged
data: {"deviceId": "guid", "capability": "Temperature", "value": 22.4, "timestamp": "..."}
```

## Frontend

### Technology

- React 19, TypeScript
- Built into static files, served by Kestrel from `wwwroot`
- Connects to REST API for data and commands
- Connects to SSE endpoint for real-time state updates

### Visual Design

Dark Clean Structured theme:
- Dark background (#111318), card surfaces (#1a1d24), device rows (#22252d)
- Subtle borders (#2a2d35), clean typography
- Toggle switches for actor capabilities (switch, dimmer on/off)
- Mini progress bars for range values (dimmer level, cover position)
- Dot indicators for sensor state (motion detected)
- Plain text for sensor values (temperature, power, energy)
- Device count badges per room card
- Tab navigation with underline indicator (Rooms, Devices, Discovered)

### Views

**Rooms** — grid of room cards, each showing its devices with current state and inline controls. This is the default/home view.

**Devices** — flat list of all configured devices across rooms. Filterable by capability type. Shows current state and room assignment.

**Discovered** — list of devices found by Communication nodes but not yet configured. Each entry shows communication type, native ID, detected capabilities, and a "Configure" action to assign a name and room.

**Device Detail** — accessed by clicking a device. Shows all capabilities with current values, controls for actor capabilities, and device metadata (communication type, native ID).

## Deployment

### Docker Compose

```yaml
services:
  vidar-host:
    build: ./src/Vidar.Host
    ports:
      - "5280:8080"
    depends_on:
      - mongodb
      - emqx
    environment:
      - VIDAR_CLUSTER_SEED=vidar-host:4053
      - VIDAR_MONGO_CONNECTION=mongodb://mongodb:27017
      - VIDAR_MONGO_DATABASE=vidar

  vidar-comm-shelly:
    build: ./src/Vidar.Communication.Shelly
    network_mode: host    # needs local network access for mDNS discovery
    environment:
      - VIDAR_CLUSTER_SEED=vidar-host:4053

  vidar-comm-zigbee2mqtt:
    build: ./src/Vidar.Communication.Zigbee2Mqtt
    depends_on:
      - emqx
    environment:
      - VIDAR_CLUSTER_SEED=vidar-host:4053
      - VIDAR_MQTT_BROKER=emqx:1883

  mongodb:
    image: mongo:8
    volumes:
      - vidar-mongo-data:/data/db
    ports:
      - "5281:27017"

  emqx:
    image: emqx/emqx:5
    volumes:
      - vidar-emqx-data:/opt/emqx/data
    ports:
      - "5282:1883"
      - "5283:18083"    # EMQX dashboard

volumes:
  vidar-mongo-data:
  vidar-emqx-data:
```

### Network Considerations

- `vidar-comm-shelly` uses `network_mode: host` to reach Shelly devices on the local network via mDNS
- All other services use the default Docker network
- Akka cluster communication uses port 4053 (configurable)
- The Host exposes port 5280 for the web UI and API

### Configuration

All configuration via environment variables:
- `VIDAR_CLUSTER_SEED` — seed node address for Akka cluster
- `VIDAR_MONGO_CONNECTION` — MongoDB connection string
- `VIDAR_MONGO_DATABASE` — MongoDB database name
- `VIDAR_MQTT_BROKER` — MQTT broker address (for Zigbee2MQTT communication node)

EMQX dashboard is available at port 5283 for MQTT traffic inspection and debugging.

All host ports use the 5280-5289 range to avoid conflicts with common development ports:
- `5280` — Vidar web UI / API
- `5281` — MongoDB
- `5282` — MQTT broker
- `5283` — EMQX dashboard

## Error Handling

- Device twin actors use Akka supervision: restart on transient failures, stop on persistent failures
- Communication nodes retry device polling/subscriptions with exponential backoff via Akka.Streams restart strategies
- If a Communication node crashes, Akka cluster detects it as unreachable. Device twins retain last known state. When the node recovers, it rejoins and resumes.
- MongoDB write failures in twins are logged but do not crash the actor — in-memory state remains authoritative, writes are retried

## Testing Strategy

- Unit tests for message contract serialization
- Unit tests for device twin actor behavior (state updates, command routing)
- Integration tests for Communication nodes against mock HTTP/MQTT endpoints
- End-to-end test: docker-compose up, discover a mock device, configure it, send a command, verify state update via SSE
