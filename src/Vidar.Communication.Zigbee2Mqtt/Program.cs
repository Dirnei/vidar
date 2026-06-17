using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Vidar.Communication.Zigbee2Mqtt;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

var builder = Host.CreateApplicationBuilder(args);

var clusterSeed = Environment.GetEnvironmentVariable("VIDAR_CLUSTER_SEED") ?? "localhost:4053";
var hostname = Environment.GetEnvironmentVariable("VIDAR_HOSTNAME") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("VIDAR_AKKA_PORT") ?? "4055");
var mqttHost = Environment.GetEnvironmentVariable("VIDAR_MQTT_HOST") ?? "localhost";
var mqttPort = int.Parse(Environment.GetEnvironmentVariable("VIDAR_MQTT_PORT") ?? "1883");
var mqttUser = Environment.GetEnvironmentVariable("VIDAR_MQTT_USER");
var mqttPassword = Environment.GetEnvironmentVariable("VIDAR_MQTT_PASSWORD");
var baseTopic = Environment.GetEnvironmentVariable("VIDAR_Z2M_BASE_TOPIC") ?? "zigbee2mqtt";

var mqttConfig = new Zigbee2MqttConfig(mqttHost, mqttPort, mqttUser, mqttPassword, baseTopic);

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
        .WithSingletonProxy<PluginRegistry>("plugin-registry", new ClusterSingletonOptions { Role = "host" })
        .WithActors((system, registry, resolver) =>
        {
            var shardProxy = registry.Get<DeviceTwinRegion>();
            var pluginRegistry = registry.Get<PluginRegistry>();
            var bridge = system.ActorOf(Zigbee2MqttBridgeActor.Props(mqttConfig, shardProxy, pluginRegistry), "zigbee2mqtt-bridge");
        });
});

var host = builder.Build();
await host.RunAsync();
