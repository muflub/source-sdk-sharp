using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugBuild"/> over the server's build stamps and the crossing
/// counters (group G8). Both are process-wide statics in the Abi today (Q4);
/// this instance is the server composition's reading of them.
/// </summary>
public sealed class DebugBuild(BuildInfo build, Abi.Crossings crossings) : IDebugBuild
{
    public DebugBuildInfo Build()
        => new(Half(build.Native, build.NativeFreshness),
               Half(build.Managed, build.ManagedFreshness));

    public DebugCrossings Crossings()
        => new(crossings.Total, crossings.Resets, Abi.Crossings.OpCount,
               crossings.ServerFrames, crossings.ClientFrames,
               Counts(crossings.Active()), Counts(crossings.ActiveEntityOps()));

    /// <summary>One half, as the dev API has always rendered it.</summary>
    internal static DebugBuildHalf Half(BuildHalf half, BuildFreshness freshness)
        => new(half.Stamp.IsKnown, half.Stamp.Hash, half.Stamp.BuiltAtUnixMs, half.Stamp.Parts,
               half.Source.IsKnown ? half.Source.BranchName : "", half.Source.Commit, half.Source.Dirty,
               half.Source.Describe(), freshness.ToString().ToLowerInvariant(), half.Path);

    private static DebugCount[] Counts(List<(string Name, long Count)> counts)
        => counts.Select(c => new DebugCount(c.Name, c.Count)).ToArray();
}
