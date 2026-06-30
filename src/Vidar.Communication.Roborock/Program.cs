using Akka.Cluster.Hosting;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.Roborock;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4060");

var mqttHost = Environment.GetEnvironmentVariable("VIDAR_MQTT_HOST") ?? "emqx";
var mqttPort = int.Parse(Environment.GetEnvironmentVariable("VIDAR_MQTT_PORT") ?? "1883");
var roborockBaseTopic = Environment.GetEnvironmentVariable("VIDAR_ROBOROCK_BASE_TOPIC") ?? "roborock2mqtt";

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    configBuilder.AddHocon(Vidar.Core.ClusterDefaults.SplitBrainResolverHocon, HoconAddMode.Prepend);

    configBuilder
        .WithRemoting(hostname, port)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["communication-roborock"]
        })
        .WithDistributedPubSub("")
        .WithShardRegionProxy<DeviceTwinRegion>("device-twin", "host", new DeviceTwinMessageExtractor(100))
        .WithSingletonProxy<PluginRegistry>("plugin-registry", new ClusterSingletonOptions { Role = "host" })
        .WithActors((system, registry, resolver) =>
        {
            system.ActorOf(RoborockBridgeActor.Props(mqttHost, mqttPort, roborockBaseTopic), "roborock-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
