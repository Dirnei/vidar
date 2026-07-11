using Akka.Cluster.Hosting;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.Spotify;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;
using Vidar.Core.Webhooks;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4065");
var tokenFilePath = Environment.GetEnvironmentVariable("VIDAR_SPOTIFY_TOKEN_PATH") ?? "/data/spotify-token.json";
var volumePath = Environment.GetEnvironmentVariable("VIDAR_SPOTIFY_VOLUME_PATH") ?? "/data/spotify-volumes.json";

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    configBuilder.AddHocon(Vidar.Core.ClusterDefaults.SplitBrainResolverHocon, HoconAddMode.Prepend);

    configBuilder
        .WithRemoting(hostname, port)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["communication-spotify"]
        })
        .WithDistributedPubSub("")
        .WithShardRegionProxy<DeviceTwinRegion>("device-twin", "host", new DeviceTwinMessageExtractor(100))
        .WithSingletonProxy<WebhookRegistry>("webhook-registry", new ClusterSingletonOptions { Role = "host" })
        .WithSingletonProxy<PluginRegistry>("plugin-registry", new ClusterSingletonOptions { Role = "host" })
        .WithActors((system, registry, resolver) =>
        {
            system.ActorOf(SpotifyBridgeActor.Props(tokenFilePath, volumePath), "spotify-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
