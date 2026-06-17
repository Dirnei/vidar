using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.Shelly;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4054");

builder.Services.AddSingleton(new ShellyHttpClient(new HttpClient()));
builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    var httpClient = sp.GetRequiredService<ShellyHttpClient>();
    configBuilder
        .WithRemoting(hostname, port)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["communication-shelly"]
        })
        .WithDistributedPubSub("")
        .WithShardRegionProxy<DeviceTwinRegion>("device-twin", "host", new DeviceTwinMessageExtractor(100))
        .WithSingletonProxy<PluginRegistry>("plugin-registry", new ClusterSingletonOptions { Role = "host" })
        .WithActors((system, registry, resolver) =>
        {
            system.ActorOf(ShellyBridgeActor.Props(httpClient), "shelly-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
