# Webhook Ingress — Design

**Date:** 2026-06-10
**Status:** Implemented (2026-06-10)

## Goal

External systems (UniFi Protect, UniFi Network, future senders) push webhook events to Vidar over HTTP.
Plugins (the `Vidar.Communication.*` cluster apps) register interest in named webhook routes and receive
the HTTP headers plus a reference to the stored body. Parsing the payload and reacting to it is entirely
the plugin's responsibility — the ingress is content-agnostic.

Example payloads: `docs/webhooks/unifi_protect.json` (license plate alarm, contains a large inline
base64 thumbnail), `docs/webhooks/unifi_networks.json` (WiFi client connect/disconnect event).

## Decisions (made during brainstorming)

| Topic | Decision |
|---|---|
| Routing | Named route keys: `POST /webhooks/{key}`. Each plugin registers the keys it wants. |
| Security | Configurable per registration: `None` (default), `UrlSecret` (`/webhooks/{key}/{secret}`), or `HeaderToken`. |
| Registration transport | NOT DistributedPubSub — exactly one recipient, so point-to-point: cluster singleton registry + `ClusterSingletonProxy`. |
| Plugin offline | Drop the event (still return 200 to the sender), log it. No buffering, no persistence of delivery state. |
| Large payloads | Claim-check pattern: body is streamed into MongoDB GridFS; only the payload ID + headers cross Akka remoting. |
| Payload retrieval | Plugins fetch the body over HTTP from the host (`GET /api/webhooks/payloads/{id}`). MongoDB stays a host-internal concern. |

## Architecture & Data Flow

New pieces live in existing projects; no new project is needed.

```
UniFi Protect ── POST /webhooks/unifi-protect ──► WebhooksController (Vidar.Host)
                                                      │ 1. validate route + auth against IWebhookRouteCache
                                                      │ 2. stream body → GridFS, get payloadId
                                                      │ 3. Tell registry: WebhookReceived(...)
                                                      │ 4. return 200 immediately
                                                      ▼
                                            WebhookRegistryActor (cluster singleton, host role)
                                                      │ routeKey → registered IActorRef
                                                      │ Tell (fire-and-forget; no listener → log + drop)
                                                      ▼
                                            UniFiBridgeActor (plugin)
                                                      │ GET /api/webhooks/payloads/{payloadId}
                                                      │ parse JSON, do its thing
                                                      ▼
                                            existing flow (DeviceStateUpdate → device twin, …)
```

- The controller validates the route **before** writing to GridFS — unknown route keys get `404` and
  nothing is stored, so scanners cannot fill the disk.
- Webhooks for a registered route whose plugin is currently unreachable are still accepted (`200`) and
  dropped at delivery — the sender should not retry-hammer because a plugin is restarting.
- Max accepted body size: **8 MB**, configurable.
- Payload retention: **24 h**, configurable; a recurring cleanup deletes expired GridFS payloads.

## Message Contracts

New records in `src/Vidar.Core/Messages/` (immutable, additively versionable):

```csharp
public enum WebhookAuthMode { None, UrlSecret, HeaderToken }

// Commands → WebhookRegistryActor (exactly one handler)
public record RegisterWebhookListener(
    string RouteKey,              // e.g. "unifi-protect" — lowercase, [a-z0-9-]
    IActorRef Listener,           // explicit ref; registry DeathWatches it
    WebhookAuthMode AuthMode = WebhookAuthMode.None,
    string? Secret = null,        // required for UrlSecret / HeaderToken
    string? HeaderName = null);   // required for HeaderToken, e.g. "X-Webhook-Token"

public record UnregisterWebhookListener(string RouteKey, IActorRef Listener);

// Event → registered listener (fire-and-forget Tell)
public record WebhookReceived(
    string RouteKey,
    Guid PayloadId,
    IReadOnlyDictionary<string, string> Headers,
    string ContentType,
    long ContentLength,
    DateTimeOffset ReceivedAt);
```

## Components

### WebhookRegistryActor (Vidar.Host, cluster singleton on the host role)

- State: `routeKey → (Listener, AuthMode, Secret, HeaderName)`, in-memory only.
- Registration is **idempotent and last-write-wins**: re-registering the same key refreshes it; a new
  ref replaces the old one (warning logged if the old ref was still alive).
- Plugins re-send their registration on a **60 s timer**. After a host restart the routes self-heal
  within a minute; until then unknown routes answer `404` (acceptable for home automation).
- DeathWatches every listener; on `Terminated` removes that listener's routes.
- On every state change pushes a route-table snapshot into `IWebhookRouteCache` (DI singleton in the
  host process) so the controller hot path never `Ask`s an actor.
- Receives `WebhookReceived` from the controller and forwards it to the registered listener;
  no listener → log at info level and drop.

### WebhooksController (Vidar.Host)

- `POST /webhooks/{key}` and `POST /webhooks/{key}/{secret}`
  - unknown key → `404`; auth mismatch → `404` as well (do not leak route existence)
  - body over the size limit → `413`
  - success: stream body to GridFS, `Tell` registry, return `200` immediately
- `GET /api/webhooks/payloads/{payloadId}`
  - streams the stored body with its original content type; `404` once expired/deleted
  - unauthenticated, consistent with the rest of the API

### MongoWebhookPayloadRepository (Vidar.Host)

- GridFS bucket `webhook_payloads`; file metadata: `routeKey`, `receivedAt`, `contentType`, headers.
- Cleanup: recurring job (small actor or `IHostedService`, matching the host's existing pattern for
  recurring work) deletes payloads older than the retention window via the GridFS bucket API
  (a plain TTL index would orphan the chunks collection).

### Plugin side — first consumer in Vidar.Communication.UniFi

- The bridge creates a `ClusterSingletonProxy` to the registry and registers `unifi-protect` and
  `unifi-network` on its 60 s re-registration timer.
- On `WebhookReceived`: fetch `GET {VIDAR_HOST_URL}/api/webhooks/payloads/{id}`, parse the JSON, and
  for this first iteration **log the parsed event** (e.g. "license plate 'Mama' detected on camera
  942A6FD0A26B"). Actual automations are future work.
- New plugin setting: `VIDAR_HOST_URL` (base URL of the Vidar.Host HTTP API).

## Error Handling

| Failure | Behavior |
|---|---|
| Unknown route key | `404`, nothing stored |
| Auth mismatch (wrong secret / missing header) | `404`, nothing stored |
| Body exceeds size limit | `413` |
| GridFS write fails | `500` (sender may retry) |
| No listener at delivery time | Log info, drop event (by design) |
| Listener actor terminates | DeathWatch removes its routes immediately |
| Host restarts (registry state lost) | Plugins re-register within 60 s; `404` in the gap |
| Payload expired before plugin fetched it | Plugin gets `404`, logs and skips the event |
| Malformed/unexpected payload content | Plugin's responsibility: log and skip; ingress stays content-agnostic |

## Testing

- **WebhookRegistryActor** (Akka.TestKit): register / refresh / replace / unregister, DeathWatch
  cleanup, route-cache snapshot pushes, delivery forwarding, drop-when-no-listener.
- **WebhooksController**: route validation, all three auth modes, `404`/`413` paths, payload stored
  and `WebhookReceived` sent (repository and registry faked).
- **Plugin parsing**: use `docs/webhooks/unifi_protect.json` and `docs/webhooks/unifi_networks.json`
  verbatim as test fixtures for the UniFi payload parsers.
- **Manual end-to-end**: `curl -X POST --data @docs/webhooks/unifi_protect.json http://host/webhooks/unifi-protect`
  and verify the plugin logs the parsed event.

## Out of Scope

- Buffering or persistent delivery of webhook events.
- A UI for viewing registered routes or stored payloads (could come later).
- Reacting to the UniFi events (automations) — this design only delivers and parses them.
- Senders other than POST (PUT/GET webhooks) — add when a real sender needs it.
