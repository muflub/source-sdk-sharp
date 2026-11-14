using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using SourceSharp.Host.Contracts;
using Xunit;

namespace TF2.Descent.Tests.Sheets;

/// <summary>
/// Checkpoint validation through the host's own surface (IGameRules, ISheetCodec's JSON),
/// as the host calls it (plan_descent_logic RU13).
/// </summary>
public sealed class CheckpointHostSurfaceTests
{
    /// <summary><paramref name="sheet"/> with its experience field (whatever its schema spells it) set to <paramref name="xp"/>.</summary>
    private static CharacterSheet WithXp(IGameRules rules, CharacterSheet sheet, long xp)
    {
        var node = JsonNode.Parse(rules.Sheets.ToJson(sheet))!.AsObject();
        var key = node.Select(p => p.Key).Single(k => string.Equals(k, "xp", StringComparison.OrdinalIgnoreCase));
        node[key] = xp;
        return rules.Sheets.FromJson(node.ToJsonString());
    }

    [Fact]
    public void Experience_the_evidence_does_not_explain_is_refused_through_the_host_surface()
    {
        IGameRules rules = new TF2DescentRules();
        var before = rules.NewCharacter("soldier", "a", hardcore: false);
        var after = WithXp(rules, before, 50);   // still level 1: only the evidence can refuse it

        var verdict = rules.ValidateCheckpoint(before, after, new CheckpointEvidence([], 0));

        Assert.Equal(50, rules.Sheets.Summarize(after).Xp);   // the stimulus: the forged sheet carries it
        Assert.False(verdict.Ok);
        Assert.Equal("xp_unexplained", verdict.FailedRule);
    }
}
