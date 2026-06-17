using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;
using Vidar.Core.Capabilities;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public static class BsonClassMapRegistration
{
    private static bool _registered;
    private static readonly object _lock = new();

    public static void Register()
    {
        if (_registered) return;
        lock (_lock)
        {
            if (_registered) return;

            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            BsonClassMap.RegisterClassMap<RoomConfiguration>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(r => r.Id);
            });

            BsonClassMap.RegisterClassMap<DeviceConfiguration>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(d => d.Id);
            });

            BsonClassMap.RegisterClassMap<DiscoveredDevice>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(d => d.Id);
            });

            BsonClassMap.RegisterClassMap<GroupConfiguration>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(g => g.Id);
            });

            var statesDictSerializer = new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>(
                DictionaryRepresentation.Document,
                new StringSerializer(),
                new ObjectSerializer(ObjectSerializer.AllAllowedTypes));

            BsonClassMap.RegisterClassMap<DeviceState>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(s => s.DeviceId);
                cm.MapProperty(s => s.States).SetSerializer(statesDictSerializer);
            });

            BsonClassMap.RegisterClassMap<StateHistoryEntry>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapProperty(e => e.Value).SetSerializer(new ObjectSerializer(ObjectSerializer.AllAllowedTypes));
            });

            BsonClassMap.RegisterClassMap<CommandHistoryEntry>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapProperty(e => e.Value).SetSerializer(new ObjectSerializer(ObjectSerializer.AllAllowedTypes));
            });

            BsonClassMap.RegisterClassMap<ApplicationConfig>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(c => c.Id);
            });

            BsonClassMap.RegisterClassMap<CapabilityDescriptor>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<ThresholdRule>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(r => r.Id);
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<ThresholdEventLog>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(e => e.Id);
                cm.SetIgnoreExtraElements(true);
            });

            _registered = true;
        }
    }
}
