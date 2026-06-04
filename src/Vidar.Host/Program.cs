using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Hosting;
using Akka.Remote.Hosting;
using MongoDB.Driver;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;

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
builder.Services.AddHttpClient("shelly", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    var stateRepo = sp.GetRequiredService<IDeviceStateRepository>();
    var deviceRepo = sp.GetRequiredService<IDeviceRepository>();
    var discoveredRepo = sp.GetRequiredService<IDiscoveredDeviceRepository>();

    configBuilder
        .WithRemoting(hostname, 4053)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["host"]
        })
        .WithDistributedPubSub("")
        .WithShardRegion<DeviceTwinRegion>(
            "device-twin",
            (system, registry, resolver) => entityId =>
                DeviceTwinActor.Props(entityId, stateRepo, deviceRepo),
            new DeviceTwinMessageExtractor(100),
            new ShardOptions
            {
                Role = "host",
                StateStoreMode = StateStoreMode.DData
            })
        .WithActors((system, registry, resolver) =>
        {
            var discoveryManager = system.ActorOf(DiscoveryManagerActor.Props(discoveredRepo), "discovery-manager");
            registry.Register<DiscoveryManagerActor>(discoveryManager);
            var sseManager = system.ActorOf(SseManagerActor.Props(), "sse-manager");
            registry.Register<SseManagerActor>(sseManager);
        })
        .AddStartup((system, registry) =>
        {
            // Re-broadcast device registrations every 30s so comm nodes that
            // join late (or rejoin after a split) pick up their devices.
            // The bridge actor is idempotent — duplicate registrations are harmless.
            async Task BroadcastRegistrations()
            {
                var mediator = DistributedPubSub.Get(system).Mediator;
                var devices = await deviceRepo.GetAllAsync();
                foreach (var d in devices)
                {
                    if (d.CommunicationType != "shelly") continue;
                    if (!d.Settings.TryGetValue("host", out var host)) continue;
                    int.TryParse(d.Settings.GetValueOrDefault("generation", "2"), out var generation);
                    mediator.Tell(new Publish("register.shelly", new RegisterDeviceForPolling(
                        d.Id, d.CommunicationType, d.NativeId, host, generation, d.Capabilities)));
                }
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                while (true)
                {
                    try { await BroadcastRegistrations(); }
                    catch { /* cluster not ready yet */ }
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            });
        });
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();
