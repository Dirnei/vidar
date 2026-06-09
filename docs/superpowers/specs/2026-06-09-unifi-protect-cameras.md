# UniFi Protect Camera Integration

## Context

Vidar already has a UniFi Network provider that discovers switches, APs, and WiFi clients. The UniFi Protect API (same host, same API key) exposes cameras. This spec adds camera discovery and snapshot viewing to the existing UniFi communication node.

## API

Base: `https://{host}/proxy/protect/integration/v1` (same host + API key as Network API)

| Method | Path | Returns |
|--------|------|---------|
| GET | `/cameras` | List of all cameras (basic info) |
| GET | `/cameras/{id}` | Camera details |
| GET | `/cameras/{id}/rtsps-stream` | RTSP stream URLs |
| POST | `/cameras/{id}/snapshot` | JPEG snapshot image bytes |

Auth: `X-API-Key` header (same key as Network API).

## Design

### Shared Config

The Protect integration reuses the existing UniFi application's host + API key. No new application — cameras are discovered by the same `UniFi Network` provider. A new boolean setting `protectEnabled` (default `true`) controls whether camera polling happens.

### New Capability: Camera

Add `Camera = 13` to `CapabilityType`. State value is a string — the RTSP URL (or empty if unavailable). The snapshot is fetched on-demand via a proxy endpoint, not stored in state.

### Backend Changes

**`UniFiProtectApiClient`** — New API client class in `Vidar.Communication.UniFi`:
- `GetCamerasAsync()` → `List<UniFiCamera>` (id, name, model, state, firmwareVersion, host, type)
- `GetCameraRtspStreamAsync(cameraId)` → RTSP URL string
- `GetCameraSnapshotAsync(cameraId)` → `byte[]` JPEG image

**`UniFiProtectModels`** — Camera response model:
```
UniFiCamera { Id, Name, Model, State, FirmwareVersion, Host, Type, MacAddress }
```

**`UniFiBridgeActor`** — Extend polling:
- After polling network devices/clients, also poll Protect cameras (if protectEnabled)
- Each camera → `DeviceDiscovered` with communicationType=`unifi`, capabilities=[Camera, Extras]
- For configured cameras: fetch RTSP URL, publish `DeviceStateUpdate` with Camera=rtspUrl and Extras={model, firmware, resolution, etc.}
- NativeId: camera MAC address (prefixed with `protect-` to avoid collision with network device MACs)

**Snapshot proxy** — New endpoint on the Host:
```
GET /api/devices/{id}/snapshot
```
- Looks up the device's communicationType + nativeId
- Sends a request to the UniFi bridge actor via Pub/Sub to fetch the snapshot
- Returns the JPEG bytes with `Content-Type: image/jpeg`
- Message flow: Controller → Pub/Sub `snapshot-request.unifi` → UniFiBridgeActor → Protect API → response back

Alternatively (simpler): the Host proxies directly to the Protect API using the stored application config (host + apiKey). This avoids the actor round-trip. The Host already has `IApplicationConfigRepository` — read the unifi config, build the URL, fetch, return bytes.

**Go with the simpler direct-proxy approach.**

### Frontend Changes

**New capability card** in `DeviceDetailPage` for `Camera`:
- Shows a snapshot image (`<img src="/api/devices/{id}/snapshot" />`)
- "Refresh" button that reloads the image (append `?t={timestamp}` cache buster)
- RTSP URL shown as a copyable monospace pill (same pattern as IP address)

**`DeviceRow`** — For Camera devices, show a small camera icon and "Online"/"Offline" status. No inline snapshot in the list view.

**`CapabilityIcon`** — Add a camera icon for the Camera capability.

### Device Type Derivation

In `frontend/src/utils/deviceType.ts`, add:
```
Camera capability → "Camera"
```
This should be checked before the Network Switch/Router/AP metadata checks since cameras are also UniFi devices.

## Discovery Flow

1. UniFi bridge polls Protect API `/cameras`
2. Each camera → `DeviceDiscovered` (nativeId=`protect-{mac}`, capabilities=[Camera, Extras])
3. User configures camera on Setup page (assigns name + room)
4. Bridge fetches RTSP URL, publishes state updates on each poll cycle
5. User views camera detail → snapshot loads via proxy endpoint

## Out of Scope

- Live RTSP/WebRTC streaming in the browser (future — discuss go2rtc or similar)
- Motion event subscriptions from Protect WebSocket
- PTZ camera control
- Recording/playback
