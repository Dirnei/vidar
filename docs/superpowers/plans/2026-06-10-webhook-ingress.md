# Webhook Ingress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** External systems POST webhooks to Vidar.Host; plugins register named routes with a cluster-singleton registry and receive headers plus a GridFS payload reference, which they fetch over HTTP and parse themselves.

**Architecture:** `POST /webhooks/{key}` on Vidar.Host validates the route against an in-process route cache, streams the body into MongoDB GridFS, and tells the `WebhookRegistryActor` (cluster singleton, role `host`). The registry forwards `WebhookReceived` point-to-point to the registered listener actor (DeathWatched, last-write-wins, re-registered by plugins on a 60 s timer). The UniFi plugin is the first consumer: it registers `unifi-protect` and `unifi-network`, fetches payload bodies via `GET /api/webhooks/payloads/{id}`, parses, and logs.

**Tech Stack:** ASP.NET Core controllers, Akka.NET 1.5.68 (Cluster Singleton via Akka.Cluster.Hosting), MongoDB.Driver GridFS, xunit + Akka.TestKit.Xunit2 + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-06-10-webhook-ingress-design.md`

**Conventions used throughout (match the codebase, deviating slightly from spec pseudo-code):**
- Messages are `sealed record`, one per file, in `Vidar.Core/Messages/`, using `Dictionary<string,string>` for maps (matches `DeviceDiscovered.Metadata`), not `IReadOnlyDictionary`.
- Config via environment variables read in `Program.cs` / static initializers (matches `VIDAR_MONGO_CONNECTION` pattern).
- Recurring work = actor with `IWithTimers` (no `IHostedService` exists in this codebase).
- Run all tests with: `dotnet test` from repo root. Build with `dotnet build Vidar.slnx`.

---

### Task 1: Core message contracts + singleton marker

**Files:**
- Create: `src/Vidar.Core/Messages/WebhookAuthMode.cs`
- Create: `src/Vidar.Core/Messages/RegisterWebhookListener.cs`
- Create: `src/Vidar.Core/Messages/UnregisterWebhookListener.cs`
- Create: `src/Vidar.Core/Messages/WebhookReceived.cs`
- Create: `src/Vidar.Core/Webhooks/WebhookRegistry.cs`

These are pure data contracts (no behavior), so no TDD cycle — create, build, commit.

- [ ] **Step 1: Create the enum**

`src/Vidar.Core/Messages/WebhookAuthMode.cs`:
```csharp
namespace Vidar.Core.Messages;
public enum WebhookAuthMode
{
    None,
    UrlSecret,
    HeaderToken
}
```

- [ ] **Step 2: Create the registration commands**

`src/Vidar.Core/Messages/RegisterWebhookListener.cs`:
```csharp
using Akka.Actor;
namespace Vidar.Core.Messages;
public sealed record RegisterWebhookListener(
    string RouteKey,
    IActorRef Listener,
    WebhookAuthMode AuthMode = WebhookAuthMode.None,
    string? Secret = null,
    string? HeaderName = null);
```

`src/Vidar.Core/Messages/UnregisterWebhookListener.cs`:
```csharp
using Akka.Actor;
namespace Vidar.Core.Messages;
public sealed record UnregisterWebhookListener(string RouteKey, IActorRef Listener);
```

- [ ] **Step 3: Create the delivery event**

`src/Vidar.Core/Messages/WebhookReceived.cs`:
```csharp
namespace Vidar.Core.Messages;
public sealed record WebhookReceived(
    string RouteKey,
    Guid PayloadId,
    Dictionary<string, string> Headers,
    string ContentType,
    long ContentLength,
    DateTimeOffset ReceivedAt);
```

- [ ] **Step 4: Create the singleton marker type** (used as the `ActorRegistry` key for `WithSingleton<T>`/`WithSingletonProxy<T>`, same pattern as `DeviceTwinRegion`)

`src/Vidar.Core/Webhooks/WebhookRegistry.cs`:
```csharp
namespace Vidar.Core.Webhooks;

/// <summary>Marker type for the webhook registry cluster singleton in the ActorRegistry.</summary>
public sealed class WebhookRegistry;
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Vidar.Core/Vidar.Core.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Vidar.Core
git commit -m "feat: webhook ingress message contracts"
```

---

### Task 2: Webhook route cache (host-local, lock-free reads)

**Files:**
- Create: `src/Vidar.Host/Webhooks/IWebhookRouteCache.cs`
- Create: `src/Vidar.Host/Webhooks/WebhookRouteCache.cs`
- Test: `tests/Vidar.Host.Tests/Webhooks/WebhookRouteCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Vidar.Host.Tests/Webhooks/WebhookRouteCacheTests.cs`:
```csharp
using Vidar.Core.Messages;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Webhooks;

public class WebhookRouteCacheTests
{
    private readonly WebhookRouteCache _sut = new();

    [Fact]
    public void TryGetRoute_UnknownKey_ReturnsFalse()
    {
        Assert.False(_sut.TryGetRoute("nope", out _));
    }

    [Fact]
    public void TryGetRoute_AfterUpdate_ReturnsRoute()
    {
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["unifi-protect"] = new(WebhookAuthMode.UrlSecret, "s3cret", null)
        });

        Assert.True(_sut.TryGetRoute("unifi-protect", out var route));
        Assert.Equal(WebhookAuthMode.UrlSecret, route.AuthMode);
        Assert.Equal("s3cret", route.Secret);
    }

    [Fact]
    public void UpdateRoutes_ReplacesEntireTable()
    {
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["a"] = new(WebhookAuthMode.None, null, null)
        });
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["b"] = new(WebhookAuthMode.None, null, null)
        });

        Assert.False(_sut.TryGetRoute("a", out _));
        Assert.True(_sut.TryGetRoute("b", out _));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhookRouteCacheTests`
Expected: FAIL — compile error, `WebhookRouteCache` does not exist.

- [ ] **Step 3: Implement**

`src/Vidar.Host/Webhooks/IWebhookRouteCache.cs`:
```csharp
using Vidar.Core.Messages;

namespace Vidar.Host.Webhooks;

public sealed record WebhookRouteInfo(WebhookAuthMode AuthMode, string? Secret, string? HeaderName);

public interface IWebhookRouteCache
{
    bool TryGetRoute(string routeKey, out WebhookRouteInfo route);
    void UpdateRoutes(Dictionary<string, WebhookRouteInfo> routes);
}
```

`src/Vidar.Host/Webhooks/WebhookRouteCache.cs`:
```csharp
namespace Vidar.Host.Webhooks;

/// <summary>
/// Route table snapshot written by the WebhookRegistryActor, read lock-free on the HTTP hot path.
/// Updates swap the whole dictionary reference; readers never see a partially updated table.
/// </summary>
public sealed class WebhookRouteCache : IWebhookRouteCache
{
    private volatile Dictionary<string, WebhookRouteInfo> _routes = new();

    public bool TryGetRoute(string routeKey, out WebhookRouteInfo route) =>
        _routes.TryGetValue(routeKey, out route!);

    public void UpdateRoutes(Dictionary<string, WebhookRouteInfo> routes) =>
        _routes = routes;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhookRouteCacheTests`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Vidar.Host/Webhooks tests/Vidar.Host.Tests/Webhooks
git commit -m "feat: webhook route cache for HTTP hot path"
```

---

### Task 3: WebhookRegistryActor

**Files:**
- Create: `src/Vidar.Host/Actors/WebhookRegistryActor.cs`
- Test: `tests/Vidar.Host.Tests/Actors/WebhookRegistryActorTests.cs`

The registry holds `routeKey → registration` in memory, DeathWatches listeners, pushes snapshots into `IWebhookRouteCache`, and forwards `WebhookReceived` to the registered listener. It does NOT use pub/sub, so tests need only a plain (non-cluster) TestKit.

- [ ] **Step 1: Write the failing tests**

`tests/Vidar.Host.Tests/Actors/WebhookRegistryActorTests.cs`:
```csharp
using Akka.Actor;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Host.Actors;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Actors;

public sealed class WebhookRegistryActorTests : TestKit
{
    private readonly IWebhookRouteCache _cache = Substitute.For<IWebhookRouteCache>();

    private IActorRef CreateRegistry() => Sys.ActorOf(WebhookRegistryActor.Props(_cache));

    [Fact]
    public void Register_PushesRouteToCache()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref, WebhookAuthMode.UrlSecret, "s3cret"));

        AwaitAssert(() => _cache.Received().UpdateRoutes(Arg.Is<Dictionary<string, WebhookRouteInfo>>(d =>
            d.ContainsKey("unifi-protect") &&
            d["unifi-protect"].AuthMode == WebhookAuthMode.UrlSecret &&
            d["unifi-protect"].Secret == "s3cret")));
    }

    [Fact]
    public void WebhookReceived_ForwardsToRegisteredListener()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));

        var evt = new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 42, DateTimeOffset.UtcNow);
        registry.Tell(evt);

        listener.ExpectMsg<WebhookReceived>(m => m.PayloadId == evt.PayloadId);
    }

    [Fact]
    public void WebhookReceived_NoListener_IsDroppedWithoutCrash()
    {
        var registry = CreateRegistry();

        registry.Tell(new WebhookReceived("unknown", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));

        // Actor must still be alive and functional afterwards
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("x", listener.Ref));
        registry.Tell(new WebhookReceived("x", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        listener.ExpectMsg<WebhookReceived>();
    }

    [Fact]
    public void Register_SameKeyTwice_LastWriteWins()
    {
        var registry = CreateRegistry();
        var first = CreateTestProbe();
        var second = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", first.Ref));
        registry.Tell(new RegisterWebhookListener("unifi-protect", second.Ref));

        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));

        second.ExpectMsg<WebhookReceived>();
        first.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Register_IsIdempotent_SameListenerRefreshes()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));

        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        listener.ExpectMsg<WebhookReceived>();
    }

    [Fact]
    public void ListenerTerminated_RemovesItsRoutes()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));
        registry.Tell(new RegisterWebhookListener("unifi-network", listener.Ref));

        Sys.Stop(listener.Ref);

        AwaitAssert(() => _cache.Received().UpdateRoutes(Arg.Is<Dictionary<string, WebhookRouteInfo>>(d => d.Count == 0)));
    }

    [Fact]
    public void Unregister_RemovesRoute_OnlyForOwningListener()
    {
        var registry = CreateRegistry();
        var owner = CreateTestProbe();
        var imposter = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", owner.Ref));

        // Imposter cannot unregister someone else's route
        registry.Tell(new UnregisterWebhookListener("unifi-protect", imposter.Ref));
        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        owner.ExpectMsg<WebhookReceived>();

        // Owner can
        registry.Tell(new UnregisterWebhookListener("unifi-protect", owner.Ref));
        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        owner.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhookRegistryActorTests`
Expected: FAIL — compile error, `WebhookRegistryActor` does not exist.

- [ ] **Step 3: Implement the actor**

`src/Vidar.Host/Actors/WebhookRegistryActor.cs`:
```csharp
using Akka.Actor;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Actors;

/// <summary>
/// Cluster singleton holding the webhook route table. Listeners register point-to-point
/// (no pub/sub — exactly one recipient per route). Registration is idempotent and
/// last-write-wins; listeners are DeathWatched and their routes removed on termination.
/// Every state change is mirrored into IWebhookRouteCache for the HTTP hot path.
/// </summary>
public sealed class WebhookRegistryActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IWebhookRouteCache _routeCache;
    private readonly Dictionary<string, RegisterWebhookListener> _routes = new();

    public static Props Props(IWebhookRouteCache routeCache) =>
        Akka.Actor.Props.Create(() => new WebhookRegistryActor(routeCache));

    public WebhookRegistryActor(IWebhookRouteCache routeCache)
    {
        _routeCache = routeCache;

        Receive<RegisterWebhookListener>(msg =>
        {
            if (_routes.TryGetValue(msg.RouteKey, out var existing) && !existing.Listener.Equals(msg.Listener))
            {
                _log.Warning("Webhook route '{RouteKey}' taken over by {New} (was {Old})",
                    msg.RouteKey, msg.Listener, existing.Listener);
                _routes[msg.RouteKey] = msg;
                UnwatchIfUnused(existing.Listener);
            }
            else
            {
                _routes[msg.RouteKey] = msg;
            }

            Context.Watch(msg.Listener);
            PushRouteCache();
        });

        Receive<UnregisterWebhookListener>(msg =>
        {
            if (!_routes.TryGetValue(msg.RouteKey, out var existing) || !existing.Listener.Equals(msg.Listener))
                return;
            _routes.Remove(msg.RouteKey);
            UnwatchIfUnused(msg.Listener);
            PushRouteCache();
            _log.Info("Webhook route '{RouteKey}' unregistered", msg.RouteKey);
        });

        Receive<WebhookReceived>(msg =>
        {
            if (_routes.TryGetValue(msg.RouteKey, out var route))
                route.Listener.Tell(msg);
            else
                _log.Info("Webhook for '{RouteKey}' dropped — no listener registered", msg.RouteKey);
        });

        Receive<Terminated>(t =>
        {
            var removed = _routes.Where(kv => kv.Value.Listener.Equals(t.ActorRef))
                .Select(kv => kv.Key).ToList();
            if (removed.Count == 0) return;
            foreach (var key in removed)
                _routes.Remove(key);
            _log.Info("Webhook listener {Listener} terminated, removed routes: {Routes}",
                t.ActorRef, string.Join(", ", removed));
            PushRouteCache();
        });
    }

    private void UnwatchIfUnused(IActorRef listener)
    {
        if (!_routes.Values.Any(r => r.Listener.Equals(listener)))
            Context.Unwatch(listener);
    }

    private void PushRouteCache() =>
        _routeCache.UpdateRoutes(_routes.ToDictionary(
            kv => kv.Key,
            kv => new WebhookRouteInfo(kv.Value.AuthMode, kv.Value.Secret, kv.Value.HeaderName)));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhookRegistryActorTests`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Vidar.Host/Actors/WebhookRegistryActor.cs tests/Vidar.Host.Tests/Actors/WebhookRegistryActorTests.cs
git commit -m "feat: webhook registry actor with DeathWatch and route cache sync"
```

---

### Task 4: GridFS payload repository

**Files:**
- Create: `src/Vidar.Host/Persistence/IWebhookPayloadRepository.cs`
- Create: `src/Vidar.Host/Persistence/MongoWebhookPayloadRepository.cs`

The other Mongo repositories in this codebase have no unit tests (they are mocked at their consumers); follow that convention. The interface is exercised via NSubstitute in Tasks 5 and 6, and the real implementation in the manual E2E (Task 9).

- [ ] **Step 1: Create the interface**

`src/Vidar.Host/Persistence/IWebhookPayloadRepository.cs`:
```csharp
namespace Vidar.Host.Persistence;

public sealed record WebhookPayload(Stream Content, string ContentType);

public interface IWebhookPayloadRepository
{
    /// <summary>Stores the body stream and returns the generated payload id.</summary>
    Task<Guid> StoreAsync(string routeKey, string contentType, Dictionary<string, string> headers, Stream body);

    /// <summary>Opens the stored payload for streaming, or null if missing/expired.</summary>
    Task<WebhookPayload?> OpenAsync(Guid payloadId);

    /// <summary>Deletes payloads uploaded before the cutoff. Returns the number deleted.</summary>
    Task<long> DeleteOlderThanAsync(DateTime cutoffUtc);
}
```

- [ ] **Step 2: Create the GridFS implementation**

`src/Vidar.Host/Persistence/MongoWebhookPayloadRepository.cs`:
```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Vidar.Host.Persistence;

public sealed class MongoWebhookPayloadRepository : IWebhookPayloadRepository
{
    private readonly GridFSBucket _bucket;

    public MongoWebhookPayloadRepository(IMongoDatabase database)
    {
        _bucket = new GridFSBucket(database, new GridFSBucketOptions { BucketName = "webhook_payloads" });
    }

    public async Task<Guid> StoreAsync(string routeKey, string contentType, Dictionary<string, string> headers, Stream body)
    {
        var payloadId = Guid.NewGuid();
        await _bucket.UploadFromStreamAsync(payloadId.ToString("N"), body, new GridFSUploadOptions
        {
            Metadata = new BsonDocument
            {
                ["routeKey"] = routeKey,
                ["contentType"] = contentType,
                ["headers"] = new BsonDocument(headers.Select(h => new BsonElement(SanitizeKey(h.Key), h.Value)))
            }
        });
        return payloadId;
    }

    public async Task<WebhookPayload?> OpenAsync(Guid payloadId)
    {
        var file = await FindByPayloadIdAsync(payloadId);
        if (file == null)
            return null;

        var stream = await _bucket.OpenDownloadStreamAsync(file.Id);
        var contentType = file.Metadata != null && file.Metadata.TryGetValue("contentType", out var ct)
            ? ct.AsString
            : "application/octet-stream";
        return new WebhookPayload(stream, contentType);
    }

    public async Task<long> DeleteOlderThanAsync(DateTime cutoffUtc)
    {
        // GridFS chunks must be deleted via the bucket API; a plain TTL index on the
        // files collection would orphan the chunks collection.
        var filter = Builders<GridFSFileInfo>.Filter.Lt(f => f.UploadDateTime, cutoffUtc);
        using var cursor = await _bucket.FindAsync(filter);
        var files = await cursor.ToListAsync();
        foreach (var file in files)
            await _bucket.DeleteAsync(file.Id);
        return files.Count;
    }

    private async Task<GridFSFileInfo?> FindByPayloadIdAsync(Guid payloadId)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(f => f.Filename, payloadId.ToString("N"));
        using var cursor = await _bucket.FindAsync(filter);
        return await cursor.FirstOrDefaultAsync();
    }

    // BSON element names must not contain '.' — HTTP header names can't either,
    // but defend against malicious input.
    private static string SanitizeKey(string key) => key.Replace('.', '_');
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Vidar.Host/Vidar.Host.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Vidar.Host/Persistence/IWebhookPayloadRepository.cs src/Vidar.Host/Persistence/MongoWebhookPayloadRepository.cs
git commit -m "feat: GridFS webhook payload repository"
```

---

### Task 5: WebhooksController (ingress + payload fetch)

**Files:**
- Create: `src/Vidar.Host/Api/WebhooksController.cs`
- Test: `tests/Vidar.Host.Tests/Api/WebhooksControllerTests.cs`

The test class extends `TestKit` solely to get a real `IActorRef` (`TestActor`) for asserting the `WebhookReceived` Tell; the controller itself is constructed directly like in `RoomsControllerTests`.

- [ ] **Step 1: Write the failing tests**

`tests/Vidar.Host.Tests/Api/WebhooksControllerTests.cs`:
```csharp
using System.Text;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Webhooks;
using Vidar.Host.Api;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Api;

public sealed class WebhooksControllerTests : TestKit
{
    private readonly IWebhookRouteCache _routes = Substitute.For<IWebhookRouteCache>();
    private readonly IWebhookPayloadRepository _payloads = Substitute.For<IWebhookPayloadRepository>();
    private readonly WebhooksController _sut;

    public WebhooksControllerTests()
    {
        var registry = Substitute.For<IRequiredActor<WebhookRegistry>>();
        registry.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TestActor));
        _sut = new WebhooksController(_routes, _payloads, registry)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void SetupRoute(string key, WebhookRouteInfo info) =>
        _routes.TryGetRoute(key, out Arg.Any<WebhookRouteInfo>()!)
            .Returns(x => { x[1] = info; return true; });

    private void SetBody(string content, string contentType = "application/json")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        _sut.Request.Body = new MemoryStream(bytes);
        _sut.Request.ContentLength = bytes.Length;
        _sut.Request.ContentType = contentType;
    }

    [Fact]
    public async Task Receive_UnknownRoute_Returns404_AndStoresNothing()
    {
        var result = await _sut.Receive("nope");

        Assert.IsType<NotFoundResult>(result);
        await _payloads.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Receive_NoAuth_StoresPayload_TellsRegistry_Returns200()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.None, null, null));
        SetBody("{\"alarm\":{}}");
        var payloadId = Guid.NewGuid();
        _payloads.StoreAsync("unifi-protect", "application/json", Arg.Any<Dictionary<string, string>>(), Arg.Any<Stream>())
            .Returns(payloadId);

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<OkResult>(result);
        ExpectMsg<WebhookReceived>(m =>
            m.RouteKey == "unifi-protect" &&
            m.PayloadId == payloadId &&
            m.ContentType == "application/json");
    }

    [Fact]
    public async Task Receive_UrlSecret_Wrong_Returns404()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.UrlSecret, "right", null));
        SetBody("{}");

        var result = await _sut.Receive("unifi-protect", "wrong");

        Assert.IsType<NotFoundResult>(result);
        await _payloads.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Receive_UrlSecret_Correct_Returns200()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.UrlSecret, "right", null));
        SetBody("{}");
        _payloads.StoreAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Guid.NewGuid());

        var result = await _sut.Receive("unifi-protect", "right");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Receive_HeaderToken_Correct_Returns200()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.HeaderToken, "tok", "X-Webhook-Token"));
        SetBody("{}");
        _sut.Request.Headers["X-Webhook-Token"] = "tok";
        _payloads.StoreAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Guid.NewGuid());

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Receive_HeaderToken_Missing_Returns404()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.HeaderToken, "tok", "X-Webhook-Token"));
        SetBody("{}");

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Receive_BodyTooLarge_Returns413()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.None, null, null));
        SetBody("{}");
        _sut.Request.ContentLength = 9L * 1024 * 1024; // over the 8 MB default

        var result = await _sut.Receive("unifi-protect");

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status.StatusCode);
    }

    [Fact]
    public async Task GetPayload_Missing_Returns404()
    {
        _payloads.OpenAsync(Arg.Any<Guid>()).Returns((WebhookPayload?)null);

        var result = await _sut.GetPayload(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPayload_Found_StreamsWithContentType()
    {
        var payloadId = Guid.NewGuid();
        var content = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));
        _payloads.OpenAsync(payloadId).Returns(new WebhookPayload(content, "application/json"));

        var result = await _sut.GetPayload(payloadId);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/json", file.ContentType);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhooksControllerTests`
Expected: FAIL — compile error, `WebhooksController` does not exist.

- [ ] **Step 3: Implement the controller**

`src/Vidar.Host/Api/WebhooksController.cs`:
```csharp
using Akka.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Webhooks;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Api;

[ApiController]
public sealed class WebhooksController : ControllerBase
{
    private static readonly long MaxBodyBytes =
        long.Parse(Environment.GetEnvironmentVariable("VIDAR_WEBHOOK_MAX_BODY_MB") ?? "8") * 1024 * 1024;

    private readonly IWebhookRouteCache _routes;
    private readonly IWebhookPayloadRepository _payloads;
    private readonly IRequiredActor<WebhookRegistry> _registry;

    public WebhooksController(
        IWebhookRouteCache routes,
        IWebhookPayloadRepository payloads,
        IRequiredActor<WebhookRegistry> registry)
    {
        _routes = routes;
        _payloads = payloads;
        _registry = registry;
    }

    [HttpPost("/webhooks/{key}")]
    [HttpPost("/webhooks/{key}/{secret}")]
    public async Task<IActionResult> Receive(string key, string? secret = null)
    {
        // Unknown route and failed auth both answer 404 — don't leak which routes exist.
        if (!_routes.TryGetRoute(key, out var route))
            return NotFound();
        if (!IsAuthorized(route, secret))
            return NotFound();
        if (Request.ContentLength > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Enforce the cap for chunked requests without a Content-Length, too.
        var sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
            sizeFeature.MaxRequestBodySize = MaxBodyBytes;

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        var contentType = Request.ContentType ?? "application/octet-stream";

        Guid payloadId;
        try
        {
            payloadId = await _payloads.StoreAsync(key, contentType, headers, Request.Body);
        }
        catch (BadHttpRequestException)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var registry = await _registry.GetAsync();
        registry.Tell(new WebhookReceived(
            key, payloadId, headers, contentType, Request.ContentLength ?? -1, DateTimeOffset.UtcNow));

        return Ok();
    }

    [HttpGet("/api/webhooks/payloads/{payloadId:guid}")]
    public async Task<IActionResult> GetPayload(Guid payloadId)
    {
        var payload = await _payloads.OpenAsync(payloadId);
        if (payload == null)
            return NotFound();
        return File(payload.Content, payload.ContentType);
    }

    private bool IsAuthorized(WebhookRouteInfo route, string? urlSecret) => route.AuthMode switch
    {
        WebhookAuthMode.None => true,
        WebhookAuthMode.UrlSecret => urlSecret != null && urlSecret == route.Secret,
        WebhookAuthMode.HeaderToken => route.HeaderName != null &&
            Request.Headers.TryGetValue(route.HeaderName, out var value) &&
            value.ToString() == route.Secret,
        _ => false
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhooksControllerTests`
Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Vidar.Host/Api/WebhooksController.cs tests/Vidar.Host.Tests/Api/WebhooksControllerTests.cs
git commit -m "feat: webhook ingress and payload fetch endpoints"
```

---

### Task 6: Payload cleanup actor

**Files:**
- Create: `src/Vidar.Host/Actors/WebhookPayloadCleanupActor.cs`
- Test: `tests/Vidar.Host.Tests/Actors/WebhookPayloadCleanupActorTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/Vidar.Host.Tests/Actors/WebhookPayloadCleanupActorTests.cs`:
```csharp
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Actors;

public sealed class WebhookPayloadCleanupActorTests : TestKit
{
    [Fact]
    public async Task PeriodicallyDeletesExpiredPayloads()
    {
        var repo = Substitute.For<IWebhookPayloadRepository>();
        repo.DeleteOlderThanAsync(Arg.Any<DateTime>()).Returns(0L);

        Sys.ActorOf(WebhookPayloadCleanupActor.Props(
            repo, retention: TimeSpan.FromHours(24), interval: TimeSpan.FromMilliseconds(100)));

        await AwaitAssertAsync(async () =>
            await repo.Received().DeleteOlderThanAsync(Arg.Is<DateTime>(d =>
                d <= DateTime.UtcNow.AddHours(-23))));
    }

    [Fact]
    public async Task RepositoryFailure_DoesNotStopTheTimer()
    {
        var repo = Substitute.For<IWebhookPayloadRepository>();
        repo.DeleteOlderThanAsync(Arg.Any<DateTime>())
            .Returns<long>(_ => throw new InvalidOperationException("mongo down"), _ => 0L);

        Sys.ActorOf(WebhookPayloadCleanupActor.Props(
            repo, retention: TimeSpan.FromHours(24), interval: TimeSpan.FromMilliseconds(100)));

        // At least two calls: the first throws, the second proves the actor survived
        await AwaitAssertAsync(async () =>
            await repo.Received(2).DeleteOlderThanAsync(Arg.Any<DateTime>()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhookPayloadCleanupActorTests`
Expected: FAIL — compile error.

- [ ] **Step 3: Implement the actor**

`src/Vidar.Host/Actors/WebhookPayloadCleanupActor.cs`:
```csharp
using Akka.Actor;
using Akka.Event;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

/// <summary>Deletes webhook payloads from GridFS once they exceed the retention window.</summary>
public sealed class WebhookPayloadCleanupActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IWebhookPayloadRepository _payloads;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _interval;

    private sealed class CleanupTick { public static readonly CleanupTick Instance = new(); }

    public static Props Props(IWebhookPayloadRepository payloads, TimeSpan retention, TimeSpan interval) =>
        Akka.Actor.Props.Create(() => new WebhookPayloadCleanupActor(payloads, retention, interval));

    public WebhookPayloadCleanupActor(IWebhookPayloadRepository payloads, TimeSpan retention, TimeSpan interval)
    {
        _payloads = payloads;
        _retention = retention;
        _interval = interval;

        ReceiveAsync<CleanupTick>(async _ =>
        {
            try
            {
                var deleted = await _payloads.DeleteOlderThanAsync(DateTime.UtcNow - _retention);
                if (deleted > 0)
                    _log.Info("Deleted {Count} expired webhook payloads", deleted);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Webhook payload cleanup failed");
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        Timers.StartPeriodicTimer("cleanup", CleanupTick.Instance, _interval, _interval);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vidar.Host.Tests --filter WebhookPayloadCleanupActorTests`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Vidar.Host/Actors/WebhookPayloadCleanupActor.cs tests/Vidar.Host.Tests/Actors/WebhookPayloadCleanupActorTests.cs
git commit -m "feat: webhook payload retention cleanup actor"
```

---

### Task 7: Host wiring (DI + cluster singleton + cleanup)

**Files:**
- Modify: `src/Vidar.Host/Program.cs`

- [ ] **Step 1: Add DI registrations**

In `src/Vidar.Host/Program.cs`, after line 30 (`builder.Services.AddSingleton<IApplicationConfigRepository>(...)`), add:

```csharp
builder.Services.AddSingleton<IWebhookRouteCache, WebhookRouteCache>();
builder.Services.AddSingleton<IWebhookPayloadRepository>(new MongoWebhookPayloadRepository(database));
```

Add to the usings at the top:
```csharp
using Akka.Actor;
using Vidar.Core.Webhooks;
using Vidar.Host.Webhooks;
```
(`Vidar.Host.Actors` and `Vidar.Host.Persistence` are already imported.)

- [ ] **Step 2: Add the singleton and cleanup actor to the Akka setup**

Inside `builder.Services.AddAkka(...)`, after the `var appRepo = ...` line, resolve the new services:

```csharp
    var webhookRouteCache = sp.GetRequiredService<IWebhookRouteCache>();
    var webhookPayloads = sp.GetRequiredService<IWebhookPayloadRepository>();
    var webhookRetention = TimeSpan.FromHours(
        int.Parse(Environment.GetEnvironmentVariable("VIDAR_WEBHOOK_RETENTION_HOURS") ?? "24"));
```

In the `configBuilder` chain, after `.WithShardRegion<DeviceTwinRegion>(...)`, add:

```csharp
        .WithSingleton<WebhookRegistry>(
            "webhook-registry",
            WebhookRegistryActor.Props(webhookRouteCache),
            new ClusterSingletonOptions { Role = "host" })
```

`WithSingleton<TKey>` (Akka.Cluster.Hosting 1.5.68) also creates a proxy and registers it in the `ActorRegistry` under `WebhookRegistry`, which is what `IRequiredActor<WebhookRegistry>` in the controller resolves. If the compiler rejects this overload, check the installed package's signature — there is also an overload taking `(ActorSystem, IActorRegistry, IDependencyResolver) => Props`.

Inside the existing `.WithActors((system, registry, resolver) => { ... })` block, add:

```csharp
            system.ActorOf(
                WebhookPayloadCleanupActor.Props(webhookPayloads, webhookRetention, TimeSpan.FromHours(1)),
                "webhook-payload-cleanup");
```

- [ ] **Step 3: Build and run all host tests**

Run: `dotnet build Vidar.slnx`
Expected: Build succeeded.

Run: `dotnet test tests/Vidar.Host.Tests`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/Vidar.Host/Program.cs
git commit -m "feat: wire webhook registry singleton, payload repo and cleanup into host"
```

---

### Task 8: UniFi webhook parsers + new test project

**Files:**
- Create: `tests/Vidar.Communication.UniFi.Tests/Vidar.Communication.UniFi.Tests.csproj`
- Create: `tests/Vidar.Communication.UniFi.Tests/TestData/unifi_protect.json` (copy of `docs/webhooks/unifi_protect.json`)
- Create: `tests/Vidar.Communication.UniFi.Tests/TestData/unifi_networks.json` (copy of `docs/webhooks/unifi_networks.json`)
- Create: `src/Vidar.Communication.UniFi/Webhooks/ProtectAlarmWebhookParser.cs`
- Create: `src/Vidar.Communication.UniFi/Webhooks/NetworkWebhookParser.cs`
- Test: `tests/Vidar.Communication.UniFi.Tests/Webhooks/WebhookParserTests.cs`

- [ ] **Step 1: Create the test project** (there is no UniFi test project yet; mirror `Vidar.Host.Tests`)

`tests/Vidar.Communication.UniFi.Tests/Vidar.Communication.UniFi.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Akka.TestKit.Xunit2" Version="1.5.68" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Vidar.Communication.UniFi\Vidar.Communication.UniFi.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="TestData\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

Copy the fixtures (PowerShell, from repo root):
```powershell
New-Item -ItemType Directory -Force tests/Vidar.Communication.UniFi.Tests/TestData
Copy-Item docs/webhooks/unifi_protect.json tests/Vidar.Communication.UniFi.Tests/TestData/
Copy-Item docs/webhooks/unifi_networks.json tests/Vidar.Communication.UniFi.Tests/TestData/
```

Add to solution:
```powershell
dotnet sln Vidar.slnx add tests/Vidar.Communication.UniFi.Tests/Vidar.Communication.UniFi.Tests.csproj
```

- [ ] **Step 2: Write the failing parser tests**

`tests/Vidar.Communication.UniFi.Tests/Webhooks/WebhookParserTests.cs`:
```csharp
using Vidar.Communication.UniFi.Webhooks;

namespace Vidar.Communication.UniFi.Tests.Webhooks;

public class WebhookParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    [Fact]
    public void ProtectParser_ParsesLicensePlateAlarm()
    {
        var evt = ProtectAlarmWebhookParser.Parse(Fixture("unifi_protect.json"));

        Assert.NotNull(evt);
        Assert.Equal("Kennzeichen", evt.AlarmName);
        Assert.Equal(2, evt.Triggers.Count);
        Assert.Equal("license_plate_known", evt.Triggers[0].Key);
        Assert.Equal("Mama", evt.Triggers[0].Value);
        Assert.Equal("942A6FD0A26B", evt.Triggers[0].Device);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1781077241154), evt.Timestamp);
    }

    [Fact]
    public void ProtectParser_NoAlarmProperty_ReturnsNull()
    {
        Assert.Null(ProtectAlarmWebhookParser.Parse("{}"));
    }

    [Fact]
    public void NetworkParser_ParsesClientDisconnectedEvent()
    {
        var evt = NetworkWebhookParser.Parse(Fixture("unifi_networks.json"));

        Assert.NotNull(evt);
        Assert.Equal("WiFi Client Disconnected", evt.Name);
        Assert.Contains("Samsung Android Phone", evt.Message);
        Assert.Equal("ba:5c:6e:84:23:77", evt.Parameters["UNIFIclientMac"]);
        Assert.Equal("Dirnhofer-AP", evt.Parameters["UNIFIwifiName"]);
    }

    [Fact]
    public void NetworkParser_MissingName_ReturnsNull()
    {
        Assert.Null(NetworkWebhookParser.Parse("{\"foo\":1}"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Vidar.Communication.UniFi.Tests`
Expected: FAIL — compile error, parsers do not exist.

- [ ] **Step 4: Implement the parsers**

`src/Vidar.Communication.UniFi/Webhooks/ProtectAlarmWebhookParser.cs`:
```csharp
using System.Text.Json;

namespace Vidar.Communication.UniFi.Webhooks;

public sealed record ProtectAlarmTrigger(string Device, string Key, string? Value);

public sealed record ProtectAlarmEvent(
    string AlarmName,
    DateTimeOffset Timestamp,
    List<ProtectAlarmTrigger> Triggers);

/// <summary>Parses UniFi Protect alarm-manager webhook payloads (see docs/webhooks/unifi_protect.json).</summary>
public static class ProtectAlarmWebhookParser
{
    public static ProtectAlarmEvent? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("alarm", out var alarm))
                return null;

            var name = alarm.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var timestamp = doc.RootElement.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var millis)
                ? DateTimeOffset.FromUnixTimeMilliseconds(millis)
                : DateTimeOffset.MinValue;

            var triggers = new List<ProtectAlarmTrigger>();
            if (alarm.TryGetProperty("triggers", out var trigs) && trigs.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in trigs.EnumerateArray())
                {
                    triggers.Add(new ProtectAlarmTrigger(
                        t.TryGetProperty("device", out var d) ? d.GetString() ?? "" : "",
                        t.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                        t.TryGetProperty("value", out var v) ? v.GetString() : null));
                }
            }

            return new ProtectAlarmEvent(name, timestamp, triggers);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

`src/Vidar.Communication.UniFi/Webhooks/NetworkWebhookParser.cs`:
```csharp
using System.Text.Json;

namespace Vidar.Communication.UniFi.Webhooks;

public sealed record NetworkWebhookEvent(
    string Name,
    string Message,
    Dictionary<string, string> Parameters);

/// <summary>Parses UniFi Network event webhook payloads (see docs/webhooks/unifi_networks.json).</summary>
public static class NetworkWebhookParser
{
    public static NetworkWebhookEvent? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                return null;

            var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

            var parameters = new Dictionary<string, string>();
            if (root.TryGetProperty("parameters", out var pars) && pars.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in pars.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String)
                        parameters[p.Name] = p.Value.GetString() ?? "";
            }

            return new NetworkWebhookEvent(name.GetString()!, message, parameters);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Vidar.Communication.UniFi.Tests`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add tests/Vidar.Communication.UniFi.Tests src/Vidar.Communication.UniFi/Webhooks Vidar.slnx
git commit -m "feat: UniFi webhook payload parsers with example-payload fixtures"
```

---

### Task 9: UniFi bridge registers webhooks and handles deliveries

**Files:**
- Modify: `src/Vidar.Communication.UniFi/UniFiBridgeActor.cs`
- Modify: `src/Vidar.Communication.UniFi/Program.cs`
- Modify: `docker-compose.yml` (vidar-comm-unifi service)
- Modify: `docker-compose.prod.yml` (vidar-comm-unifi service)
- Test: `tests/Vidar.Communication.UniFi.Tests/UniFiBridgeActorWebhookTests.cs`

- [ ] **Step 1: Write the failing tests**

The bridge subscribes to DistributedPubSub in `PreStart`, so the test system needs the cluster config (same trick as `DeviceTwinActorTests` in `tests/Vidar.Host.Tests/Actors/DeviceTwinActorTests.cs`).

`tests/Vidar.Communication.UniFi.Tests/UniFiBridgeActorWebhookTests.cs`:
```csharp
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using Vidar.Communication.UniFi;
using Vidar.Core.Messages;

namespace Vidar.Communication.UniFi.Tests;

public sealed class UniFiBridgeActorWebhookTests : TestKit
{
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://UniFiBridgeActorWebhookTests@127.0.0.1:2553""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    public UniFiBridgeActorWebhookTests() : base(TestConfig, "UniFiBridgeActorWebhookTests")
    {
    }

    [Fact]
    public void Bridge_RegistersBothWebhookRoutes_OnStart()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();

        Sys.ActorOf(UniFiBridgeActor.Props(shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1"), "unifi-bridge");

        var registrations = new[]
        {
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10)),
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10))
        };

        Assert.Contains(registrations, r => r.RouteKey == "unifi-protect");
        Assert.Contains(registrations, r => r.RouteKey == "unifi-network");
        Assert.All(registrations, r => Assert.Equal(WebhookAuthMode.None, r.AuthMode));
    }

    [Fact]
    public void Bridge_ReRegisters_Periodically()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();

        // Re-registration interval is parameterized so this test doesn't wait 60s
        Sys.ActorOf(UniFiBridgeActor.Props(
            shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1", TimeSpan.FromMilliseconds(200)), "unifi-bridge-2");

        // initial round + at least one re-registration round
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vidar.Communication.UniFi.Tests --filter UniFiBridgeActorWebhookTests`
Expected: FAIL — compile error, `Props` has no overload with registry/hostUrl.

- [ ] **Step 3: Extend UniFiBridgeActor**

In `src/Vidar.Communication.UniFi/UniFiBridgeActor.cs`:

Add using:
```csharp
using Vidar.Communication.UniFi.Webhooks;
```

Add fields (next to `_shardProxy`):
```csharp
    private readonly IActorRef _webhookRegistry;
    private readonly string _hostUrl;
    private readonly TimeSpan _webhookRegisterInterval;
    private static readonly HttpClient PayloadClient = new() { Timeout = TimeSpan.FromSeconds(15) };
```

Add an internal message (next to `PollTick`):
```csharp
    private sealed class RegisterWebhooks { public static readonly RegisterWebhooks Instance = new(); }
```

Replace the `Props` method and constructor signature:
```csharp
    public static Props Props(IActorRef shardProxy, IActorRef webhookRegistry, string hostUrl) =>
        Props(shardProxy, webhookRegistry, hostUrl, TimeSpan.FromSeconds(60));

    public static Props Props(IActorRef shardProxy, IActorRef webhookRegistry, string hostUrl, TimeSpan webhookRegisterInterval) =>
        Akka.Actor.Props.Create(() => new UniFiBridgeActor(shardProxy, webhookRegistry, hostUrl, webhookRegisterInterval));

    public UniFiBridgeActor(IActorRef shardProxy, IActorRef webhookRegistry, string hostUrl, TimeSpan webhookRegisterInterval)
    {
        _shardProxy = shardProxy;
        _webhookRegistry = webhookRegistry;
        _hostUrl = hostUrl.TrimEnd('/');
        _webhookRegisterInterval = webhookRegisterInterval;
```

Add receive handlers in the constructor (after the existing `Receive<DeviceCommand>` block):
```csharp
        Receive<RegisterWebhooks>(_ =>
        {
            _webhookRegistry.Tell(new RegisterWebhookListener("unifi-protect", Self));
            _webhookRegistry.Tell(new RegisterWebhookListener("unifi-network", Self));
        });

        ReceiveAsync<WebhookReceived>(HandleWebhookAsync);
```

In `PreStart()`, after the existing timer line, add:
```csharp
        // Idempotent re-registration: self-heals after host/singleton restarts
        Timers.StartPeriodicTimer("webhook-register", RegisterWebhooks.Instance,
            TimeSpan.Zero, _webhookRegisterInterval);
```

Add the handler method (e.g. after `HandleCommandAsync`):
```csharp
    private async Task HandleWebhookAsync(WebhookReceived msg)
    {
        string body;
        try
        {
            using var response = await PayloadClient.GetAsync($"{_hostUrl}/api/webhooks/payloads/{msg.PayloadId}");
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Webhook payload {PayloadId} for '{RouteKey}' not available (HTTP {Status})",
                    msg.PayloadId, msg.RouteKey, (int)response.StatusCode);
                return;
            }
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch webhook payload {PayloadId} from {HostUrl}", msg.PayloadId, _hostUrl);
            return;
        }

        switch (msg.RouteKey)
        {
            case "unifi-protect":
                var alarm = ProtectAlarmWebhookParser.Parse(body);
                if (alarm == null)
                {
                    _log.Warning("Unparseable unifi-protect webhook payload {PayloadId}", msg.PayloadId);
                    return;
                }
                _log.Info("Protect alarm '{Alarm}' at {Time}: {Triggers}",
                    alarm.AlarmName, alarm.Timestamp,
                    string.Join("; ", alarm.Triggers.Select(t => $"{t.Key}={t.Value} (device {t.Device})")));
                break;

            case "unifi-network":
                var evt = NetworkWebhookParser.Parse(body);
                if (evt == null)
                {
                    _log.Warning("Unparseable unifi-network webhook payload {PayloadId}", msg.PayloadId);
                    return;
                }
                _log.Info("Network event '{Name}': {Message}", evt.Name, evt.Message);
                break;

            default:
                _log.Warning("Webhook for unexpected route '{RouteKey}' ignored", msg.RouteKey);
                break;
        }
    }
```

- [ ] **Step 4: Wire the singleton proxy in the UniFi Program.cs**

In `src/Vidar.Communication.UniFi/Program.cs`:

Add using:
```csharp
using Vidar.Core.Webhooks;
```
(`ClusterSingletonOptions` lives in `Akka.Cluster.Hosting`, which is already imported.)

Add the env var (after the `port` line):
```csharp
var hostUrl = Environment.GetEnvironmentVariable("VIDAR_HOST_URL") ?? "http://vidar-host:8080";
```

In the `configBuilder` chain, after `.WithShardRegionProxy<DeviceTwinRegion>(...)`, add:
```csharp
        .WithSingletonProxy<WebhookRegistry>("webhook-registry", new ClusterSingletonOptions { Role = "host" })
```

Replace the `.WithActors(...)` body:
```csharp
        .WithActors((system, registry, resolver) =>
        {
            var shardProxy = registry.Get<DeviceTwinRegion>();
            var webhookRegistry = registry.Get<WebhookRegistry>();
            system.ActorOf(UniFiBridgeActor.Props(shardProxy, webhookRegistry, hostUrl), "unifi-bridge");
        });
```

- [ ] **Step 5: Add VIDAR_HOST_URL to both compose files**

In `docker-compose.yml` and `docker-compose.prod.yml`, in the `vidar-comm-unifi` service `environment:` list, add:
```yaml
      - VIDAR_HOST_URL=http://vidar-host:8080
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test`
Expected: all projects pass (the new bridge tests included).

- [ ] **Step 7: Commit**

```bash
git add src/Vidar.Communication.UniFi tests/Vidar.Communication.UniFi.Tests docker-compose.yml docker-compose.prod.yml
git commit -m "feat: UniFi bridge registers webhook routes and logs parsed events"
```

---

### Task 10: Manual end-to-end verification

**Files:** none (verification only)

- [ ] **Step 1: Start the stack**

```powershell
docker compose up -d --build mongodb lighthouse vidar-host vidar-comm-unifi
```

- [ ] **Step 2: Wait for webhook registration**

Watch the logs until the cluster has formed and at most 60 s have passed (registration timer):
```powershell
docker compose logs -f vidar-comm-unifi
```
Expected: no errors; host log (`docker compose logs vidar-host`) shows no webhook-related warnings.

- [ ] **Step 3: POST the example payloads** (host is mapped to 5280)

```powershell
curl.exe -s -o NUL -w "%{http_code}" -X POST -H "Content-Type: application/json" --data "@docs/webhooks/unifi_protect.json" http://localhost:5280/webhooks/unifi-protect
curl.exe -s -o NUL -w "%{http_code}" -X POST -H "Content-Type: application/json" --data "@docs/webhooks/unifi_networks.json" http://localhost:5280/webhooks/unifi-network
```
Expected: `200` for both.

- [ ] **Step 4: Verify plugin logs**

```powershell
docker compose logs vidar-comm-unifi --tail 20
```
Expected:
- `Protect alarm 'Kennzeichen' at ...: license_plate_known=Mama (device 942A6FD0A26B); license_plate_of_interest=Mama (device 942A6FD0A26B)`
- `Network event 'WiFi Client Disconnected': Samsung Android Phone 23:77 disconnected ...`

- [ ] **Step 5: Verify negative cases**

```powershell
curl.exe -s -o NUL -w "%{http_code}" -X POST --data "{}" http://localhost:5280/webhooks/does-not-exist
```
Expected: `404`.

- [ ] **Step 6: Mark the spec as implemented**

In `docs/superpowers/specs/2026-06-10-webhook-ingress-design.md`, change `**Status:** Draft, pending review` to `**Status:** Implemented (2026-06-10)`.

```bash
git add docs/superpowers/specs/2026-06-10-webhook-ingress-design.md
git commit -m "docs: mark webhook ingress spec as implemented"
```
