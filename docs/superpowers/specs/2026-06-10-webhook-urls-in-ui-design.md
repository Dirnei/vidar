# Webhook URLs in the UI — Design

**Date:** 2026-06-10
**Status:** Draft, pending review

## Goal

The Applications page shows, per integration entry, the live webhook URLs that external
systems must be configured with (e.g. UniFi entry shows
`http://<host>:5280/webhooks/unifi-protect` and `.../webhooks/unifi-network`), each with a
copy-to-clipboard button.

## Decision (from brainstorming)

**Live + tagged (Approach A):** plugins tag their webhook registrations with the owning
integration id; the host exposes the currently registered routes via a new API endpoint;
the UI renders only what is actually registered. New plugins appear automatically with no
frontend changes. Trade-off: after a host restart the URLs are absent for up to ~60 s until
plugins re-register — acceptable, and doubles as a health signal.

## Backend

### 1. Message contract (Vidar.Core)

`RegisterWebhookListener` gains a new optional field, appended last (additive, versionable):

```csharp
public sealed record RegisterWebhookListener(
    string RouteKey,
    IActorRef Listener,
    WebhookAuthMode AuthMode = WebhookAuthMode.None,
    string? Secret = null,
    string? HeaderName = null,
    string? IntegrationId = null);   // NEW — e.g. "unifi"; ties the route to an application entry
```

### 2. Route cache (Vidar.Host.Webhooks)

`WebhookRouteInfo` gains `IntegrationId`:

```csharp
public sealed record WebhookRouteInfo(
    WebhookAuthMode AuthMode, string? Secret, string? HeaderName, string? IntegrationId);
```

`IWebhookRouteCache` gains an enumeration method for the API:

```csharp
IReadOnlyDictionary<string, WebhookRouteInfo> Snapshot();
```

(`WebhookRouteCache` returns the current dictionary reference — already immutable-by-convention.)

`WebhookRegistryActor.PushRouteCache()` maps `IntegrationId` into the snapshot.

### 3. New API endpoint (WebhooksController)

```
GET /api/webhooks/routes
→ 200 [
    { "routeKey": "unifi-protect", "integrationId": "unifi", "authMode": "None",
      "path": "/webhooks/unifi-protect", "headerName": null },
    ...
  ]
```

- `path` is computed server-side: `/webhooks/{key}` or `/webhooks/{key}/{secret}` for
  `UrlSecret` routes. The UI never implements auth logic; it prepends `window.location.origin`.
- `headerName` is included so the UI can hint "send token in header X" for `HeaderToken`
  routes. The raw `Secret` value is never returned on its own — it only appears embedded in
  `path` for `UrlSecret`, where it is part of the URL by design. (API is LAN-trusted like the
  rest of the host API.)
- Sorted by routeKey for stable rendering.

### 4. UniFi bridge (Vidar.Communication.UniFi)

Registers with the integration id:

```csharp
_webhookRegistry.Tell(new RegisterWebhookListener("unifi-protect", Self, IntegrationId: "unifi"));
_webhookRegistry.Tell(new RegisterWebhookListener("unifi-network", Self, IntegrationId: "unifi"));
```

## Frontend (frontend/, React + TS)

### 5. API client + types

- `types/index.ts`: `WebhookRoute { routeKey, integrationId, authMode, path, headerName }`
- `api/client.ts`: `getWebhookRoutes(): Promise<WebhookRoute[]>` → `GET /api/webhooks/routes`

### 6. ApplicationsPage

- Page fetches routes once alongside applications (refresh together on save/reload).
- Each `ApplicationCard` receives `webhookRoutes` filtered by `integrationId === app.id`.
- When non-empty, the card shows a **Webhooks** section (always visible, not only when
  settings are expanded — the URL is the point of this feature) listing per route:
  - the full URL: `window.location.origin + route.path`
  - a copy-to-clipboard button (`navigator.clipboard.writeText`, brief "copied" feedback)
  - for `HeaderToken` routes, a muted hint: `requires token in header <headerName>`
- Styling follows the existing card sections (same patterns as the settings rows).

### 7. Build

`npm run build` in `frontend/` outputs to `src/Vidar.Host/wwwroot/` (committed like the
existing wwwroot artifacts, matching the current workflow).

## Error handling

| Case | Behavior |
|---|---|
| No routes registered (plugin down / first 60 s after host restart) | Webhooks section hidden; nothing breaks |
| Route registered without IntegrationId | Returned by the API with `integrationId: null`; not shown on any card (invisible until tagged) |
| Clipboard API unavailable (non-HTTPS remote origin) | Fallback: select-on-click text, copy button hidden if `navigator.clipboard` is undefined |

## Testing

- **WebhookRegistryActorTests**: IntegrationId flows into the cache snapshot.
- **WebhooksControllerTests**: GET /api/webhooks/routes returns routeKey/integrationId/path;
  UrlSecret route path embeds the secret; no standalone secret field in response.
- **UniFiBridgeActorWebhookTests**: registrations carry `IntegrationId == "unifi"`.
- **Frontend**: `npm run build` passes (type-checks); manual verification on the
  Applications page against the running docker stack.

## Out of scope

- Authenticating the routes API (consistent with the rest of the LAN API).
- Showing webhook delivery history/statistics.
- Editing webhook auth config from the UI (registrations are plugin-defined).
