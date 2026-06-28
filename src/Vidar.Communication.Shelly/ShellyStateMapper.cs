using System.Text.Json;

namespace Vidar.Communication.Shelly;

public record ShellyCapabilityValue(string CapabilityKey, object Value);

public static class ShellyStateMapper
{
    // ── Gen2 (RPC) ────────────────────────────────────────────────────────────

    public static List<ShellyCapabilityValue> MapSwitchStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();

        if (element.TryGetProperty("output", out var output) && (output.ValueKind == JsonValueKind.True || output.ValueKind == JsonValueKind.False))
            results.Add(new ShellyCapabilityValue("switch", output.GetBoolean()));

        if (element.TryGetProperty("apower", out var apower) && apower.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("power", apower.GetDouble()));

        if (element.TryGetProperty("aenergy", out var aenergy) && aenergy.ValueKind == JsonValueKind.Object)
        {
            if (aenergy.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
                results.Add(new ShellyCapabilityValue("energy", total.GetDouble() / 1000.0));
        }

        return results;
    }

    /// <summary>
    /// Maps a Gen2 light:0 component. Emits a composite "light" value ({ on, brightness })
    /// plus power/energy when the dimmer reports a power meter (PM models).
    /// </summary>
    public static List<ShellyCapabilityValue> MapLightStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();
        var light = new Dictionary<string, object>();

        if (element.TryGetProperty("output", out var output) && (output.ValueKind == JsonValueKind.True || output.ValueKind == JsonValueKind.False))
            light["on"] = output.GetBoolean();

        if (element.TryGetProperty("brightness", out var brightness) && brightness.ValueKind == JsonValueKind.Number)
            light["brightness"] = brightness.GetDouble();

        if (light.Count > 0)
            results.Add(new ShellyCapabilityValue("light", light));

        if (element.TryGetProperty("apower", out var apower) && apower.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("power", apower.GetDouble()));

        if (element.TryGetProperty("aenergy", out var aenergy) && aenergy.ValueKind == JsonValueKind.Object)
        {
            if (aenergy.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
                results.Add(new ShellyCapabilityValue("energy", total.GetDouble() / 1000.0));
        }

        return results;
    }

    public static List<ShellyCapabilityValue> MapCoverStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();

        // Uncalibrated channels (pos_control:false) omit current_pos.
        if (element.TryGetProperty("current_pos", out var pos) && pos.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("cover", pos.GetInt32()));

        if (element.TryGetProperty("apower", out var apower) && apower.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("power", apower.GetDouble()));

        if (element.TryGetProperty("aenergy", out var aenergy) && aenergy.ValueKind == JsonValueKind.Object)
        {
            if (aenergy.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
                results.Add(new ShellyCapabilityValue("energy", total.GetDouble() / 1000.0));
        }

        return results;
    }

    public static List<ShellyCapabilityValue> MapTemperatureStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();

        if (element.TryGetProperty("tC", out var tC) && tC.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("temperature", tC.GetDouble()));

        return results;
    }

    // ── Gen1 (REST) ───────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a single roller entry from the Gen1 /status response rollers array.
    /// </summary>
    public static List<ShellyCapabilityValue> MapGen1RollerStatus(JsonElement roller)
    {
        var results = new List<ShellyCapabilityValue>();

        if (roller.TryGetProperty("current_pos", out var pos) && pos.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("cover", pos.GetInt32()));

        if (roller.TryGetProperty("power", out var power) && power.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue("power", power.GetDouble()));

        return results;
    }

    /// <summary>
    /// Maps a single light entry from the Gen1 /status response lights array.
    /// Emits a composite "light" value ({ on, brightness }).
    /// </summary>
    public static List<ShellyCapabilityValue> MapGen1LightStatus(JsonElement light)
    {
        var results = new List<ShellyCapabilityValue>();
        var state = new Dictionary<string, object>();

        if (light.TryGetProperty("ison", out var ison) && (ison.ValueKind == JsonValueKind.True || ison.ValueKind == JsonValueKind.False))
            state["on"] = ison.GetBoolean();

        if (light.TryGetProperty("brightness", out var brightness) && brightness.ValueKind == JsonValueKind.Number)
            state["brightness"] = brightness.GetDouble();

        if (state.Count > 0)
            results.Add(new ShellyCapabilityValue("light", state));

        return results;
    }

    /// <summary>
    /// Extracts temperature from the Gen1 /status root element.
    /// Tries tmp.tC first, then temperature (float field present on some models).
    /// </summary>
    public static List<ShellyCapabilityValue> MapGen1Temperature(JsonElement root)
    {
        var results = new List<ShellyCapabilityValue>();

        if (root.TryGetProperty("tmp", out var tmp) &&
            tmp.TryGetProperty("tC", out var tC) &&
            tC.ValueKind == JsonValueKind.Number)
        {
            results.Add(new ShellyCapabilityValue("temperature", tC.GetDouble()));
        }
        else if (root.TryGetProperty("temperature", out var temp) &&
                 temp.ValueKind == JsonValueKind.Number)
        {
            results.Add(new ShellyCapabilityValue("temperature", temp.GetDouble()));
        }

        return results;
    }
}
