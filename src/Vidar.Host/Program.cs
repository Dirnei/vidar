using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Hosting;
using Akka.Remote.Hosting;
using MongoDB.Driver;
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
builder.Services.AddControllers();

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
        });
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();
