using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Loot;

/// <summary>
/// experience.json (D10): each party member in <see cref="PartyRangeUnits"/> of a kill gets its
/// full XP, scaled by the hero-versus-monster level difference: a piecewise-linear curve of the
/// percent paid against (monster level − hero level), flat past its ends.
/// </summary>
public sealed record ExperienceSettings(double PartyRangeUnits, IReadOnlyList<(int Diff, double Pct)> LevelDifference);

public sealed class ExperienceTable : SingleTable<ExperienceSettings>
{
    public const string TableName = "experience";

    public override string Name => TableName;

    protected override ExperienceSettings Read(JsonFields f)
    {
        var points = new List<(int, double)>();
        foreach (var e in f.Array("levelDifference"))
        {
            var p = new JsonFields(e, f.Table, "levelDifference", f.Errors);
            points.Add((p.Int("diff"), p.Num("pct")));
            p.End();
        }
        return new ExperienceSettings(f.Num("partyRangeUnits"), points);
    }

    /// <summary>The curve has points, its differences rise strictly, and what it pays never falls as the monster gets deeper.</summary>
    protected override void Validate(DescentContent content, ExperienceSettings s, ContentErrors errors)
    {
        ContentChecks.Range(errors, Name, null, "partyRangeUnits", s.PartyRangeUnits, 0, 1e6);
        if (s.LevelDifference.Count == 0) errors.Add(Name, null, ContentRule.Malformed, "'levelDifference' has no points");
        for (int i = 0; i < s.LevelDifference.Count; i++)
        {
            var (diff, pct) = s.LevelDifference[i];
            ContentChecks.Range(errors, Name, "levelDifference", "pct", pct, 0, 1000);
            if (i == 0) continue;
            var (lastDiff, lastPct) = s.LevelDifference[i - 1];
            if (diff <= lastDiff)
                errors.Add(Name, "levelDifference", ContentRule.OutOfRange, $"difference {diff} does not rise from {lastDiff}");
            if (pct < lastPct)
                errors.Add(Name, "levelDifference", ContentRule.OutOfRange, $"a deeper monster pays less: {pct} % at {diff} after {lastPct} % at {lastDiff}");
        }
    }
}
