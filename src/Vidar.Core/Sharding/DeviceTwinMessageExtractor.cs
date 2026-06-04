using Akka.Cluster.Sharding;
using Vidar.Core.Messages;
namespace Vidar.Core.Sharding;
public sealed class DeviceTwinMessageExtractor : HashCodeMessageExtractor
{
    public DeviceTwinMessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards) { }
    public override string EntityId(object message) => message switch
    {
        DeviceStateUpdate m => m.DeviceId.ToString(),
        DeviceCommand m => m.DeviceId.ToString(),
        _ => throw new ArgumentException($"Unknown message type: {message.GetType()}")
    };
}
