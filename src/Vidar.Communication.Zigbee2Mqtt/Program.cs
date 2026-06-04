using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.Zigbee2Mqtt;
using Vidar.Core.Sharding;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4055");
var mqttHost = Environment.GetEnvironmentVariable("VIDAR_MQTT_HOST") ?? "localhost";
var mqttPort = int.Parse(Environment.GetEnvironmentVariable("VIDAR_MQTT_PORT") ?? "1883");

builder.Services.AddAkka("vidar", (configBuilder, sp) =>
{
    configBuilder
        .WithRemoting(hostname, port)
        .WithClustering(new ClusterOptions
        {
            SeedNodes = [$"akka.tcp://vidar@{clusterSeed}"],
            Roles = ["communication-zigbee2mqtt"]
        })
        .WithDistributedPubSub("")
        .WithShardRegionProxy<DeviceTwinRegion>("device-twin", "host", new DeviceTwinMessageExtractor(100))
        .WithActors((system, registry, resolver) =>
        {
            var shardProxy = registry.Get<DeviceTwinRegion>();
            var bridge = system.ActorOf(Zigbee2MqttBridgeActor.Props(mqttHost, mqttPort, shardProxy), "zigbee2mqtt-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
