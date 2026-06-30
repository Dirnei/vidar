using Vidar.Core.Capabilities;

namespace Vidar.Communication.Roborock;

public sealed record RoborockModelProfile(
    string Model, IReadOnlyList<CapabilityDescriptor> Capabilities);
