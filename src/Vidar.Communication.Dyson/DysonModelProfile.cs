namespace Vidar.Communication.Dyson;

public enum DysonFeature { Base, HotCool, HumidifyCool }

public sealed record DysonCapability(
    string Key, string Label, string Unit, bool Commandable, double? Min = null, double? Max = null);

public sealed record DysonModelProfile(
    string ProductType, DysonFeature Feature, IReadOnlyList<DysonCapability> Capabilities);
