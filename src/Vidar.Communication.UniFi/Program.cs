using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.UniFi;
using Vidar.Core.Sharding;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4056");

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    configBuilder
        .WithRemoting(hostname, port)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["communication-unifi"]
        })
        .WithDistributedPubSub("")
        .WithShardRegionProxy<DeviceTwinRegion>("device-twin", "host", new DeviceTwinMessageExtractor(100))
        .WithActors((system, registry, resolver) =>
        {
            var shardProxy = registry.Get<DeviceTwinRegion>();
            system.ActorOf(UniFiBridgeActor.Props(shardProxy), "unifi-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
