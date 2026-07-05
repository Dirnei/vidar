using System.Globalization;

namespace Vidar.Communication.Bambu;

/// <summary>
/// Static lookup tables ported from the community bambu integrations: HMS error codes and
/// filament ids. Unknown values fall back to the raw code/id so nothing is ever dropped.
/// </summary>
public static class BambuHmsCatalog
{
    // Seed with a few well-known codes; extend from a captured device over time.
    private static readonly Dictionary<string, string> Hms = new()
    {
        ["0300_0100_0001_0004"] = "Nozzle temperature is abnormal; heating may be too slow.",
        ["0300_0200_0002_0001"] = "Heatbed temperature is abnormal.",
        ["0700_2000_0002_0001"] = "The AMS filament has run out.",
    };

    private static readonly Dictionary<string, string> Filament = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GFL99"] = "PLA",
        ["GFL95"] = "PLA",
        ["GFB99"] = "ABS",
        ["GFG99"] = "PETG",
        ["GFT98"] = "TPU",
    };

    public static string FormatCode(long attr, long code) =>
        string.Create(CultureInfo.InvariantCulture,
            $"HMS_{(attr >> 16) & 0xFFFF:X4}_{attr & 0xFFFF:X4}_{(code >> 16) & 0xFFFF:X4}_{code & 0xFFFF:X4}");

    public static string Describe(long attr, long code)
    {
        var full = FormatCode(attr, code);
        var key = full["HMS_".Length..];
        return Hms.TryGetValue(key, out var text) ? text : full;
    }

    public static string FilamentName(string id) =>
        Filament.TryGetValue(id, out var name) ? name : id;
}
