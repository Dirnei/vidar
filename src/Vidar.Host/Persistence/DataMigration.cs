using MongoDB.Bson;
using MongoDB.Driver;

namespace Vidar.Host.Persistence;

public static class DataMigration
{
    private static readonly Dictionary<string, (string Key, string Label, int Unit, bool Commandable)> EnumToDescriptor = new()
    {
        ["Switch"] = ("switch", "Switch", 9, true),
        ["0"] = ("switch", "Switch", 9, true),
        ["Dimmer"] = ("dimmer", "Dimmer", 6, true),
        ["1"] = ("dimmer", "Dimmer", 6, true),
        ["Cover"] = ("cover", "Cover", 8, true),
        ["2"] = ("cover", "Cover", 8, true),
        ["Temperature"] = ("temperature", "Temperature", 4, false),
        ["3"] = ("temperature", "Temperature", 4, false),
        ["Motion"] = ("motion", "Motion", 11, false),
        ["4"] = ("motion", "Motion", 11, false),
        ["Power"] = ("power", "Power", 0, false),
        ["5"] = ("power", "Power", 0, false),
        ["Energy"] = ("energy", "Energy", 2, false),
        ["6"] = ("energy", "Energy", 2, false),
        ["Humidity"] = ("humidity", "Humidity", 6, false),
        ["7"] = ("humidity", "Humidity", 6, false),
        ["Light"] = ("light", "Light", 9, true),
        ["8"] = ("light", "Light", 9, true),
        ["Contact"] = ("contact", "Contact", 10, false),
        ["9"] = ("contact", "Contact", 10, false),
        ["Action"] = ("action", "Action", 13, false),
        ["10"] = ("action", "Action", 13, false),
        ["Battery"] = ("battery", "Battery", 6, false),
        ["11"] = ("battery", "Battery", 6, false),
        ["Presence"] = ("presence", "Presence", 11, false),
        ["12"] = ("presence", "Presence", 11, false),
        ["Camera"] = ("camera", "Camera", 13, false),
        ["13"] = ("camera", "Camera", 13, false),
        ["Update"] = ("update", "Update", 13, false),
        ["14"] = ("update", "Update", 13, false),
        ["SolarProduction"] = ("solarProduction", "Solar Production", 0, false),
        ["15"] = ("solarProduction", "Solar Production", 0, false),
        ["GridPower"] = ("gridPower", "Grid Power", 0, false),
        ["16"] = ("gridPower", "Grid Power", 0, false),
        ["Consumption"] = ("consumption", "Consumption", 0, false),
        ["17"] = ("consumption", "Consumption", 0, false),
        ["Extras"] = ("extras", "Extras", 8, false),
        ["99"] = ("extras", "Extras", 8, false),
    };

    private static readonly Dictionary<string, string> EnumToKey = EnumToDescriptor
        .ToDictionary(kv => kv.Key, kv => kv.Value.Key);

    public static async Task MigrateIfNeededAsync(IMongoDatabase db)
    {
        await MigrateDeviceStatesAsync(db);
        await MigrateDeviceConfigurationsAsync(db);
        await MigrateDiscoveredDevicesAsync(db);
        await MigrateThresholdRulesAsync(db);
        await MigrateThresholdEventLogsAsync(db);
    }

    private static async Task MigrateDeviceStatesAsync(IMongoDatabase db)
    {
        var collection = db.GetCollection<BsonDocument>("deviceState");
        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        foreach (var doc in docs)
        {
            if (!doc.Contains("States")) continue;
            var states = doc["States"].AsBsonDocument;
            var newStates = new BsonDocument();
            var changed = false;
            foreach (var element in states)
            {
                if (EnumToKey.TryGetValue(element.Name, out var newKey))
                {
                    newStates[newKey] = element.Value;
                    changed = true;
                }
                else
                {
                    newStates[element.Name] = element.Value;
                }
            }
            if (changed)
            {
                doc["States"] = newStates;
                await collection.ReplaceOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), doc);
            }
        }
    }

    private static async Task MigrateDeviceConfigurationsAsync(IMongoDatabase db)
    {
        var collection = db.GetCollection<BsonDocument>("devices");
        await MigrateCapabilityListAsync(collection);
    }

    private static async Task MigrateDiscoveredDevicesAsync(IMongoDatabase db)
    {
        var collection = db.GetCollection<BsonDocument>("discoveredDevices");
        await MigrateCapabilityListAsync(collection);
    }

    private static async Task MigrateCapabilityListAsync(IMongoCollection<BsonDocument> collection)
    {
        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        foreach (var doc in docs)
        {
            if (!doc.Contains("Capabilities")) continue;
            var caps = doc["Capabilities"].AsBsonArray;
            if (caps.Count == 0) continue;
            if (caps[0].BsonType == BsonType.Document) continue;

            var newCaps = new BsonArray();
            foreach (var cap in caps)
            {
                var enumStr = cap.IsInt32 ? cap.AsInt32.ToString() : cap.AsString;
                if (EnumToDescriptor.TryGetValue(enumStr, out var desc))
                {
                    newCaps.Add(new BsonDocument
                    {
                        ["Key"] = desc.Key,
                        ["Label"] = desc.Label,
                        ["Unit"] = desc.Unit,
                        ["Commandable"] = desc.Commandable,
                    });
                }
                else
                {
                    newCaps.Add(new BsonDocument
                    {
                        ["Key"] = enumStr.ToLowerInvariant(),
                        ["Label"] = enumStr,
                        ["Unit"] = 8,
                        ["Commandable"] = false,
                    });
                }
            }
            doc["Capabilities"] = newCaps;
            await collection.ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), doc);
        }
    }

    private static async Task MigrateThresholdRulesAsync(IMongoDatabase db)
    {
        var collection = db.GetCollection<BsonDocument>("threshold_rules");
        await MigrateCapabilityFieldAsync(collection);
    }

    private static async Task MigrateThresholdEventLogsAsync(IMongoDatabase db)
    {
        var collection = db.GetCollection<BsonDocument>("threshold_event_log");
        await MigrateCapabilityFieldAsync(collection);
    }

    private static async Task MigrateCapabilityFieldAsync(IMongoCollection<BsonDocument> collection)
    {
        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        foreach (var doc in docs)
        {
            var changed = false;
            if (doc.Contains("Capability") && !doc.Contains("CapabilityKey"))
            {
                var capValue = doc["Capability"];
                var capStr = capValue.IsInt32 ? capValue.AsInt32.ToString() : capValue.AsString;
                doc["CapabilityKey"] = EnumToKey.GetValueOrDefault(capStr, capStr.ToLowerInvariant());
                doc.Remove("Capability");
                changed = true;
            }
            if (doc.Contains("MetricKey"))
            {
                doc.Remove("MetricKey");
                changed = true;
            }
            if (changed)
            {
                await collection.ReplaceOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), doc);
            }
        }
    }
}
