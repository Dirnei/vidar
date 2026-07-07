using Akka.Cluster.Hosting;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.Loxone;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4064");

// Loxone Miniservers are reached over a local WebSocket, bridged to the local MQTT broker by
// the standalone `loxone2mqtt` sidecar. This worker consumes that broker; the WS/token protocol
// lives entirely in the sidecar.
var mqttHost = Environment.GetEnvironmentVariable("VIDAR_MQTT_HOST") ?? "emqx";
var mqttPort = int.Parse(Environment.GetEnvironmentVariable("VIDAR_MQTT_PORT") ?? "1883");
var baseTopic = Environment.GetEnvironmentVariable("VIDAR_LOXONE_BASE_TOPIC") ?? "loxone2mqtt";

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    configBuilder.AddHocon(Vidar.Core.ClusterDefaults.SplitBrainResolverHocon, HoconAddMode.Prepend);

    configBuilder
        .WithRemoting(hostname, port)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["communication-loxone"]
        })
        .WithDistributedPubSub("")
        .WithShardRegionProxy<DeviceTwinRegion>("device-twin", "host", new DeviceTwinMessageExtractor(100))
        .WithSingletonProxy<PluginRegistry>("plugin-registry", new ClusterSingletonOptions { Role = "host" })
        .WithActors((system, registry, resolver) =>
        {
            system.ActorOf(LoxoneBridgeActor.Props(mqttHost, mqttPort, baseTopic), "loxone-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
