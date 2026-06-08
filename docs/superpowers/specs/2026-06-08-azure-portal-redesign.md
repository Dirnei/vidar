# Vidar UI Redesign — Azure Portal Style

## Context

The current UI was built incrementally — pages bolted on one by one. With 70+ devices across 3 protocols (Shelly, Zigbee2MQTT, UniFi), the flat lists and basic filters don't scale. The Setup page needs hierarchical filtering, and all three communication protocols need to be configurable as plugins from the Integrations page.

## Design Direction

Azure Portal-inspired: left filter/facet panel + main content area. The filter panel provides hierarchical faceted navigation (protocol → device type → capabilities). The main area shows a searchable, sortable list of items.

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
| Features: switching/routing/access-point (UniFi) | Network Device |

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

### Integrations Page

All three protocols as configurable plugins:

**Shelly**
- Enable/Disable toggle
- No config needed (discovery is manual via IP)
- Shows count of discovered/configured Shelly devices

**Zigbee2MQTT**
- Enable/Disable toggle
- MQTT broker host/port
- MQTT username/password
- Base topic (default: zigbee2mqtt)
- Shows connection status + device count

**UniFi** (already implemented)
- Enable/Disable toggle
- Host, API Key, Site ID, Poll Interval
- Shows connection status + device count

Each plugin card shows:
- Protocol icon + name
- Status badge (Connected / Disconnected / Disabled)
- Device count
- Expand for settings

### Device Detail Page

- No filter panel needed — keep current layout
- Expert mode panel stays

## Technical Changes

### Backend

1. **Z2M as configurable plugin**: Move MQTT broker config from env vars to IntegrationConfig (like UniFi). The Z2M comm node reads config from the Integrations API on startup. Env vars become fallback defaults.

2. **Shelly as configurable plugin**: Simpler — just enable/disable. The Shelly node has no broker to configure (HTTP polling is device-specific). The integration config just tracks whether the node should be active.

3. **Plugin status API**: New endpoint `GET /api/integrations/status` that returns each plugin's connection state:
```json
[
  { "id": "zigbee2mqtt", "enabled": true, "connected": true, "deviceCount": 17 },
  { "id": "unifi", "enabled": true, "connected": true, "deviceCount": 69 },
  { "id": "shelly", "enabled": true, "connected": true, "deviceCount": 8 }
]
```

The comm nodes periodically report status via Pub/Sub, the Host aggregates it.

4. **Discovered devices API enhancement**: Add `deviceType` field to the response (derived server-side from capabilities for consistent filtering).

### Frontend

1. **FilterPanel component**: Reusable left panel with collapsible sections, checkboxes, counts, search. Used across Setup, Devices pages.

2. **Layout update**: The `main-content` area splits into filter panel + content when the page provides filters.

3. **Setup page rewrite**: Uses FilterPanel for protocol/type filtering. Virtual scrolling for large lists.

4. **Integrations page rewrite**: All three plugins as configurable cards with status badges.

5. **Mobile**: Filter panel slides in as a drawer from the left (triggered by filter button).

## Migration

- Z2M and Shelly env vars stay as fallback — if no IntegrationConfig exists, use env vars
- Existing devices/state unaffected
- The filter panel is additive — existing pages keep working, just gain the panel

## Out of Scope

- WebSocket/real-time filter updates (polling is fine for now)
- Drag-and-drop device organization
- Dashboard/overview page (future)
