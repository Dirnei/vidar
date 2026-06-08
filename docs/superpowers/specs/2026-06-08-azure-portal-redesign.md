# Vidar UI Redesign — Azure Portal Style + Application Architecture

## Context

The current UI was built incrementally — pages bolted on one by one. With 70+ devices across 3 protocols (Shelly, Zigbee2MQTT, UniFi), the flat lists and basic filters don't scale. More importantly, the "Communication Node" concept is too narrow. Vidar needs an **Application** model where:

- **Provider Applications** produce devices (Shelly, Z2M, UniFi — the current comm nodes)
- **Consumer Applications** react to device events (automations, rules, scenes, notifications — future)

Each application has a lifecycle (enabled/disabled/configured/running), its own settings, and reports status. The current Integrations page becomes the **Applications** page. The current "discovered devices" become devices produced by a specific Provider Application.

## Design Direction

Azure Portal-inspired: left filter/facet panel + main content area. The filter panel provides hierarchical faceted navigation (application → device type → capabilities). The main area shows a searchable, sortable list of items.

## Layout

### Global Shell

```
┌──────────┬─────────────────┬────────────────────────────────┐
│ Sidebar  │ Filter Panel     │ Main Content                   │
│ (nav)    │ (context-aware)  │ (list / detail / config)       │
│          │                  │                                │
│ vidar    │ Filters change   │ Content changes per page       │
│          │ per page:        │                                │
│ ○ Rooms  │ - Setup: protocol│                                │
│ ○ All    │   + device type  │                                │
│ ○ Setup  │ - Rooms: room    │                                │
│ ○ Integ  │   filter         │                                │
│          │ - Devices: cap   │                                │
│          │   + room filter  │                                │
│ Expert   │                  │                                │
└──────────┴─────────────────┴────────────────────────────────┘
```

- **Sidebar** (existing): 200px, navigation + expert mode toggle
- **Filter Panel** (new): 220px, collapsible on mobile, context-aware per page
- **Main Content**: fills remaining space

On mobile: filter panel becomes a slide-out drawer triggered by a filter icon button.

### Filter Panel Structure

Each filter section is a collapsible group with checkboxes and counts:

```
PROTOCOL
☑ zigbee2mqtt (17)
☑ unifi (53)
☐ shelly (2)

DEVICE TYPE
☑ Light (5)
☑ Sensor (8)
☑ Remote (3)
☑ Contact (4)
☑ Client (42)
☑ Network Device (16)
☑ Cover (3)
☑ Power Monitor (2)

CAPABILITIES
☑ Temperature
☑ Humidity
☑ Presence
☑ Battery

[Clear All Filters]
```

Counts update dynamically based on current filters (faceted search — selecting a protocol updates type/capability counts).

### Device Type Derivation

Device types are derived from capabilities — no backend change needed:

| Primary Capability | Device Type |
|---|---|
| Light | Light |
| Cover | Cover |
| Switch (without Light) | Switch |
| Temperature or Humidity (without Switch/Light/Cover) | Sensor |
| Motion (without Switch/Light) | Motion Sensor |
| Contact | Contact Sensor |
| Action | Remote |
| Presence | Client |
| Power or Energy (without Switch/Light/Cover) | Power Monitor |
| Network switch (UniFi) | Network Switch |
| Network routing (UniFi) | Network Router |
| Network access-point (UniFi) | Network AccessPoint |

The derivation happens in the frontend from the device's capabilities and metadata.

## Pages

### Setup Page (Discovered Devices)

- Filter panel: Protocol + Device Type + search
- Main content: device cards (existing design, already good)
- "Add Shelly Device by IP" card stays at top
- Pagination or virtual scrolling for 70+ devices

### All Devices Page

- Filter panel: Room + Capability + Protocol + Online/Offline
- Main content: device rows (existing)

### Rooms Page

- Filter panel: Room list as clickable items (instead of grid cards?)
- Or keep the grid cards and add a filter for room name search
- This page might not need the filter panel — the grid already works

### Applications Page (replaces Integrations)

The central management page for all Vidar applications.

#### Application Model

```
Application {
  id: string              // "shelly", "zigbee2mqtt", "unifi", "automations"
  name: string            // "Shelly", "Zigbee2MQTT", "UniFi Network", "Automations"
  type: "provider" | "consumer"
  enabled: boolean
  status: "running" | "stopped" | "error" | "unconfigured"
  settings: Record<string, string>
  deviceCount?: number    // providers only
  description: string
}
```

#### Provider Applications (produce devices)

**Shelly**
- Type: provider
- Settings: none (discovery is manual via IP on Setup page)
- Shows count of discovered/configured Shelly devices

**Zigbee2MQTT**
- Type: provider
- Settings: MQTT broker host/port, username/password, base topic
- Shows connection status + device count

**UniFi Network** (already implemented)
- Type: provider
- Settings: Host, API Key, Site ID, Poll Interval
- Shows connection status + device count

#### Consumer Applications (react to device events — future)

**Automations** (future)
- Type: consumer
- Listens to device state changes, executes rules
- "When motion detected in hallway AND it's dark → turn on lights"

**Scenes** (future)
- Type: consumer
- Named state snapshots that can be activated
- "Movie mode: dim living room to 20%, close covers, turn off kitchen"

**Notifications** (future)
- Type: consumer
- Push/email/webhook when conditions are met
- "Alert when garage door opens after 22:00"

#### Card Layout

Each application card shows:
- Application icon + name
- Type badge (Provider / Consumer)
- Status indicator (green dot = running, red = error, gray = disabled)
- Device count (providers) or rule count (consumers)
- Expand arrow for settings
- Enable/Disable toggle

The Setup page only shows devices from **enabled Provider applications**.

### Device Detail Page

- No filter panel needed — keep current layout
- Expert mode panel stays

## Technical Changes

### Backend

1. **Rename IntegrationConfig → ApplicationConfig**: The model already works, just rename for clarity. The `type` field gets added ("provider" or "consumer").

2. **ApplicationStatusActor** on the Host: Subscribes to `application-status.{id}` topic. Each comm node periodically publishes its status (running, device count, error info). The Host aggregates into a queryable state.

3. **Applications API**:
```
GET    /api/applications              — list all applications with status
GET    /api/applications/{id}         — single application with config + status
PUT    /api/applications/{id}         — update config (enable/disable, settings)
GET    /api/applications/{id}/devices — devices produced by this application
```

Status response:
```json
{
  "id": "zigbee2mqtt",
  "name": "Zigbee2MQTT",
  "type": "provider",
  "enabled": true,
  "status": "running",
  "deviceCount": 17,
  "settings": { "mqttHost": "10.220.220.10", "baseTopic": "smarthome/z2m" }
}
```

4. **Z2M as configurable application**: Move MQTT broker config from env vars to ApplicationConfig. Env vars become fallback defaults.

5. **Shelly as configurable application**: Enable/disable only. Device discovery stays manual (IP-based on Setup page).

6. **Discovered devices API**: Add `deviceType` and `applicationId` fields to the response for filtering.

### Frontend

1. **FilterPanel component**: Reusable left panel with collapsible sections, checkboxes, counts, search. Used across Setup, Devices pages.

2. **Layout update**: The `main-content` area splits into filter panel + content when the page provides filters.

3. **Setup page rewrite**: Uses FilterPanel for application/type filtering. Virtual scrolling for large lists.

4. **Applications page** (replaces Integrations): Application cards for all providers and consumers. Settings, status, enable/disable.

5. **Sidebar update**: "Integrations" → "Applications" (icon: grid or puzzle piece).

6. **Mobile**: Filter panel slides in as a drawer from the left (triggered by filter button).

## Migration

- Z2M and Shelly env vars stay as fallback — if no IntegrationConfig exists, use env vars
- Existing devices/state unaffected
- The filter panel is additive — existing pages keep working, just gain the panel

## Implementation Order

1. **Application model + API** — rename IntegrationConfig, add type/status, new endpoints
2. **Applications page** — replace Integrations page with provider/consumer cards
3. **FilterPanel component** — reusable faceted filter with checkboxes and counts
4. **Setup page rewrite** — filter panel + device list with application/type/capability filtering
5. **All Devices page** — add filter panel with room/capability/application filters
6. **Z2M configurable** — move broker config to ApplicationConfig
7. **Mobile filter drawer** — slide-out panel for mobile

## Out of Scope (for now)

- Consumer applications (automations, scenes, notifications) — the model supports them but implementation is future work
- WebSocket/real-time filter updates (polling is fine)
- Drag-and-drop device organization
- Dashboard/overview page
