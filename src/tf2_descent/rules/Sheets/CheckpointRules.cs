using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Sheets;

/// <summary>
/// What the host checks of every checkpoint (plan_tf2_rpg 3c, C.3; plan_descent_logic
/// RU13). A refusal names its rule:
/// <list type="bullet">
/// <item>the sheet after is one play could reach: <see cref="HeroRules.Audit"/>'s rules
/// (attribute_points, attribute_over_cap, skill_points, xp_level_mismatch, ...);</item>
/// <item>nothing that only grows went back: class_changed, hardcore_changed,
/// level_decreased, xp_decreased, depth_decreased, respec_count_decreased,
/// bestiary_decreased, boss_kill_lost; without a respec in between, no attribute base
/// or skill rank fell (attribute_decreased, skill_rank_decreased);</item>
/// <item>a fallen hero stays fallen (unfallen), and only a hardcore hero falls
/// (fallen_softcore);</item>
/// <item>what grew is what the evidence says was earned: experience by no more than the
/// evidence's (xp_unexplained), each template's kill count by no more than its kills
/// (bestiary_unexplained). Empty evidence earns nothing.</item>
/// </list>
/// Undecodable input is refused as sheet_malformed or evidence_malformed.
/// </summary>
public sealed class CheckpointRules
{
    private readonly HeroRules _heroes;
    private readonly SheetCodec _sheets;
    private readonly EvidenceCodec _evidence;

    public CheckpointRules(HeroRules heroes, SheetCodec sheets, EvidenceCodec evidence)
    {
        _heroes = heroes;
        _sheets = sheets;
        _evidence = evidence;
    }

    public ValidationResult Validate(CharacterSheet before, CharacterSheet after, CheckpointEvidence evidence)
    {
        HeroSheet a, b;
        CheckpointProof proof;
        try
        {
            a = _sheets.Read(before);
            b = _sheets.Read(after);
        }
        catch (InvalidDataException)
        {
            return ValidationResult.Fail("sheet_malformed");
        }
        try
        {
            proof = _evidence.Read(evidence);
        }
        catch (InvalidDataException)
        {
            return ValidationResult.Fail("evidence_malformed");
        }

        if (b.ClassId != a.ClassId) return ValidationResult.Fail("class_changed");
        if (b.Hardcore != a.Hardcore) return ValidationResult.Fail("hardcore_changed");
        if (a.Fallen && !b.Fallen) return ValidationResult.Fail("unfallen");
        if (b.Fallen && !b.Hardcore) return ValidationResult.Fail("fallen_softcore");

        var audit = _heroes.Audit(b);
        if (!audit.Ok) return ValidationResult.Fail(audit.Rule!);

        if (b.Level < a.Level) return ValidationResult.Fail("level_decreased");
        if (b.Xp < a.Xp) return ValidationResult.Fail("xp_decreased");
        if (b.Xp - a.Xp > proof.XpEarned) return ValidationResult.Fail("xp_unexplained");
        if (b.ReachedDepth < a.ReachedDepth) return ValidationResult.Fail("depth_decreased");
        if (b.RespecCount < a.RespecCount) return ValidationResult.Fail("respec_count_decreased");

        if (b.RespecCount == a.RespecCount)
        {
            if (a.Attributes.Any(kv => b.Base(kv.Key) < kv.Value)) return ValidationResult.Fail("attribute_decreased");
            if (a.SkillRanks.Any(kv => Rank(b.SkillRanks, kv.Key) < kv.Value)) return ValidationResult.Fail("skill_rank_decreased");
        }

        foreach (var (template, count) in b.Bestiary)
        {
            int was = Rank(a.Bestiary, template);
            if (count < was) return ValidationResult.Fail("bestiary_decreased");
            if (count - was > proof.KillsOf(template)) return ValidationResult.Fail("bestiary_unexplained");
        }
        if (a.Bestiary.Keys.Any(t => !b.Bestiary.ContainsKey(t))) return ValidationResult.Fail("bestiary_decreased");
        if (!a.BossKills.IsSubsetOf(b.BossKills)) return ValidationResult.Fail("boss_kill_lost");

        return ValidationResult.Pass;
    }

    private static int Rank(System.Collections.Immutable.ImmutableSortedDictionary<string, int> map, string key) =>
        map.TryGetValue(key, out var v) ? v : 0;
}
