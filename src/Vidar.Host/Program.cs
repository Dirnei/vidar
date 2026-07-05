using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using MongoDB.Driver;
using Vidar.Core.Model;
using Vidar.Core.Sharding;
using Vidar.Core.Plugins;
using Vidar.Core.Webhooks;
using Vidar.Host.Actors;
using Vidar.Host.Dreo;
using Vidar.Host.Dyson;
using Vidar.Host.Persistence;
using Vidar.Host.Roborock;
using Vidar.Host.Webhooks;

var builder = WebApplication.CreateBuilder(args);

var mongoConnection = Environment.GetEnvironmentVariable("VIDAR_MONGO_CONNECTION") ?? "mongodb://localhost:27017";
var mongoDatabase = Environment.GetEnvironmentVariable("VIDAR_MONGO_DATABASE") ?? "vidar";
var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";

BsonClassMapRegistration.Register();

var mongoClient = new MongoClient(mongoConnection);
var database = mongoClient.GetDatabase(mongoDatabase);

builder.Services.AddSingleton<IMongoDatabase>(database);
builder.Services.AddSingleton<IRoomRepository>(new MongoRoomRepository(database));
builder.Services.AddSingleton<IDeviceRepository>(new MongoDeviceRepository(database));
builder.Services.AddSingleton<IDiscoveredDeviceRepository>(new MongoDiscoveredDeviceRepository(database));
builder.Services.AddSingleton<IDeviceStateRepository>(new MongoDeviceStateRepository(database));
builder.Services.AddSingleton<IGroupRepository>(new MongoGroupRepository(database));
builder.Services.AddSingleton<IHistoryRepository>(new MongoHistoryRepository(database));
builder.Services.AddSingleton<IApplicationConfigRepository>(new MongoApplicationConfigRepository(database));
builder.Services.AddSingleton<IWebhookRouteCache, WebhookRouteCache>();
builder.Services.AddSingleton<IWebhookPayloadRepository>(new MongoWebhookPayloadRepository(database));
builder.Services.AddSingleton<IWebhookEventRepository>(new MongoWebhookEventRepository(database));
builder.Services.AddSingleton<IThresholdRuleRepository>(new MongoThresholdRuleRepository(database));
builder.Services.AddSingleton<IThresholdEventLogRepository>(new MongoThresholdEventLogRepository(database));
builder.Services.AddHttpClient<DysonCloudClient>(c =>
    c.BaseAddress = new Uri("https://appapi.cp.dyson.com"));
var roborock2mqttUrl = Environment.GetEnvironmentVariable("ROBOROCK2MQTT_URL") ?? "http://roborock2mqtt:8895";
builder.Services.AddHttpClient<IRoborockAuth, SidecarRoborockAuth>(c =>
    c.BaseAddress = new Uri(roborock2mqttUrl));
var dreo2mqttUrl = Environment.GetEnvironmentVariable("DREO2MQTT_URL") ?? "http://dreo2mqtt:8896";
builder.Services.AddHttpClient<IDreoAuth, SidecarDreoAuth>(c =>
    c.BaseAddress = new Uri(dreo2mqttUrl));
builder.Services.AddHttpClient("shelly", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient("protect", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
});
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    var stateRepo = sp.GetRequiredService<IDeviceStateRepository>();
    var deviceRepo = sp.GetRequiredService<IDeviceRepository>();
    var discoveredRepo = sp.GetRequiredService<IDiscoveredDeviceRepository>();
    var historyRepo = sp.GetRequiredService<IHistoryRepository>();

    var appRepo = sp.GetRequiredService<IApplicationConfigRepository>();

    var webhookRouteCache = sp.GetRequiredService<IWebhookRouteCache>();
    var webhookPayloads = sp.GetRequiredService<IWebhookPayloadRepository>();
    var webhookRetention = TimeSpan.FromHours(
        int.TryParse(Environment.GetEnvironmentVariable("VIDAR_WEBHOOK_RETENTION_HOURS"), out var h) ? h : 24);

    configBuilder.AddHocon(Vidar.Core.ClusterDefaults.SplitBrainResolverHocon, HoconAddMode.Prepend);

    configBuilder
        .WithRemoting(hostname, 4053)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["host"]
        })
        .WithDistributedPubSub("")
        .WithSingleton<PluginRegistry>(
            "plugin-registry",
            PluginRegistryActor.Props(deviceRepo, appRepo),
            new ClusterSingletonOptions { Role = "host" })
        .WithShardRegion<DeviceTwinRegion>(
            "device-twin",
            (system, registry, resolver) =>
            {
                return entityId =>
                    DeviceTwinActor.Props(entityId, stateRepo, deviceRepo, historyRepo);
            },
            new DeviceTwinMessageExtractor(100),
            new ShardOptions
            {
                Role = "host",
                StateStoreMode = StateStoreMode.DData
            })
        .WithSingleton<WebhookRegistry>(
            "webhook-registry",
            WebhookRegistryActor.Props(webhookRouteCache),
            new ClusterSingletonOptions { Role = "host" })
        .WithActors((system, registry, resolver) =>
        {
            var discoveryManager = system.ActorOf(DiscoveryManagerActor.Props(discoveredRepo, deviceRepo), "discovery-manager");
            registry.Register<DiscoveryManagerActor>(discoveryManager);
            var sseManager = system.ActorOf(SseManagerActor.Props(), "sse-manager");
            registry.Register<SseManagerActor>(sseManager);
            var webhookSseActor = system.ActorOf(WebhookEventSseActor.Props(), "webhook-event-sse");
            registry.Register<WebhookEventSseActor>(webhookSseActor);
            var webhookEventRepo = sp.GetRequiredService<IWebhookEventRepository>();
            var webhookRegistryProxy = registry.Get<WebhookRegistry>();
            webhookRegistryProxy.Tell(new SetWebhookDependencies(webhookSseActor, webhookEventRepo), ActorRefs.Nobody);
            var appStatusActor = system.ActorOf(ApplicationStatusActor.Props(), "application-status");
            registry.Register<ApplicationStatusActor>(appStatusActor);
            system.ActorOf(
                WebhookPayloadCleanupActor.Props(webhookPayloads, webhookRetention, TimeSpan.FromHours(1)),
                "webhook-payload-cleanup");
            var thresholdRuleRepo = sp.GetRequiredService<IThresholdRuleRepository>();
            var thresholdEventLogRepo = sp.GetRequiredService<IThresholdEventLogRepository>();
            var thresholdEvaluator = system.ActorOf(ThresholdEvaluatorActor.Props(thresholdRuleRepo, thresholdEventLogRepo), "threshold-evaluator");
            registry.Register<ThresholdEvaluatorActor>(thresholdEvaluator);
        });
});

var app = builder.Build();

// Ensure the "Home" room exists
{
    var roomRepo = app.Services.GetRequiredService<IRoomRepository>();
    if (await roomRepo.GetByIdAsync(RoomConfiguration.HomeId) == null)
        await roomRepo.CreateAsync(new RoomConfiguration { Id = RoomConfiguration.HomeId, Name = "Home", IsHome = true });
}

{
    var db = app.Services.GetRequiredService<IMongoDatabase>();
    await DataMigration.MigrateIfNeededAsync(db);
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();
