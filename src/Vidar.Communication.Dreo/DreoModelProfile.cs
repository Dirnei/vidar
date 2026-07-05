namespace Vidar.Communication.Dreo;

public sealed record DreoCapability(
    string Key, string Label, string Unit, bool Commandable, double? Min = null, double? Max = null);

public sealed record DreoModelProfile(string Model, IReadOnlyList<DreoCapability> Capabilities);
