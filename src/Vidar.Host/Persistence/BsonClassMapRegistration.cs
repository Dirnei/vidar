using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
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

            // Use standard string-based GUIDs for MongoDB
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

            BsonClassMap.RegisterClassMap<DeviceState>(cm =>
            {
                cm.AutoMap();
                cm.MapIdProperty(s => s.DeviceId);
            });

            _registered = true;
        }
    }
}
