namespace Vidar.Communication.Bambu;

public sealed record BambuDiscovered(string Serial, string Model, string Host);

public static class BambuDiscovery
{
    public static BambuDiscovered? ParseSsdpNotify(string ssdp)
    {
        string? host = null, serial = null, model = null, name = null;
        foreach (var raw in ssdp.Split('\n'))
        {
            var line = raw.Trim();
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var header = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            switch (header.ToLowerInvariant())
            {
                case "location": host = val; break;
                case "usn": serial = val; break;
                case "devmodel.bambu.com": model = val; break;
                case "devname.bambu.com": name = val; break;
            }
        }
        // A Bambu advertisement always carries the model header; use it as the discriminator.
        if (host is null || serial is null || model is null) return null;
        _ = name;
        return new BambuDiscovered(serial, model, host);
    }
}
