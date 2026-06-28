namespace Vidar.Core;

public static class ClusterDefaults
{
    // Split Brain Resolver. Without a downing provider, members that become unreachable
    // (node restarts, rebuilds, GC pauses) are never removed, so the cluster cannot converge
    // and its singletons — e.g. the plugin registry — never (re-)form, silently breaking
    // device registration and commands. The SBR downs unreachable members automatically so the
    // cluster always heals. "keep-majority" keeps the larger side of any partition; "stable-after"
    // waits for transient blips to recover before acting. Must be configured on every node.
    public const string SplitBrainResolverHocon = @"
akka.cluster {
  downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
  split-brain-resolver {
    active-strategy = keep-majority
    stable-after = 20s
  }
}";
}
