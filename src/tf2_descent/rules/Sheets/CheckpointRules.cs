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
/// An operator's edit (<see cref="ValidateEdit"/>, contract 1.3, plan X4) is held to every rule
/// but the evidence's: it may raise experience and level (and the bestiary) with none.
/// </summary>
public sealed class CheckpointRules
{
    private readonly HeroRules _heroes;
    private readonly SheetCodec _sheets;
    private readonly EvidenceCodec _evidence;
    private readonly IKillXpBound? _bound;

    public CheckpointRules(HeroRules heroes, SheetCodec sheets, EvidenceCodec evidence)
        : this(heroes, sheets, evidence, null)
    {
    }

    /// <summary>
    /// With <paramref name="bound"/>, the evidence's experience is also held to what its kills
    /// could have paid (xp_beyond_kills, RU4): at most the sum over its templates of kills ×
    /// <see cref="IKillXpBound.MostXpPerKill"/>.
    /// </summary>
    public CheckpointRules(HeroRules heroes, SheetCodec sheets, EvidenceCodec evidence, IKillXpBound? bound)
    {
        _heroes = heroes;
        _sheets = sheets;
        _evidence = evidence;
        _bound = bound;
    }

    /// <summary>The most the evidence's kills could have paid; long.MaxValue with no bound.</summary>
    public long MostXpFor(CheckpointProof proof)
    {
        if (_bound is null) return long.MaxValue;
        long most = 0;
        foreach (var (template, kills) in proof.Kills)
        {
            long each = _bound.MostXpPerKill(template);
            most = each > 0 && kills > (long.MaxValue - most) / each ? long.MaxValue : most + kills * each;
        }
        return most;
    }

    public ValidationResult Validate(CharacterSheet before, CharacterSheet after, CheckpointEvidence evidence)
    {
        if (!Read(before, after, out var a, out var b))
            return ValidationResult.Fail("sheet_malformed");
        CheckpointProof proof;
        try
        {
            proof = _evidence.Read(evidence);
        }
        catch (InvalidDataException)
        {
            return ValidationResult.Fail("evidence_malformed");
        }
        return Check(a, b, proof);
    }

    /// <summary>
    /// An operator's edit of a sheet (contract 1.3's <c>IGameRules.ValidateEdit</c>, plan X4): every
    /// rule a checkpoint holds but the evidence's, so the admin UI's character edit can raise
    /// experience and level; the sheet after must still be one play could reach, and nothing that
    /// only grows may go back.
    /// </summary>
    public ValidationResult ValidateEdit(CharacterSheet before, CharacterSheet after) =>
        Read(before, after, out var a, out var b) ? Check(a, b, null) : ValidationResult.Fail("sheet_malformed");

    private bool Read(CharacterSheet before, CharacterSheet after, out HeroSheet a, out HeroSheet b)
    {
        try
        {
            a = _sheets.Read(before);
            b = _sheets.Read(after);
            return true;
        }
        catch (InvalidDataException)
        {
            a = b = null!;
            return false;
        }
    }

    /// <summary>The rules over two read sheets; <paramref name="proof"/> null is an edit, whose growth needs no evidence.</summary>
    private ValidationResult Check(HeroSheet a, HeroSheet b, CheckpointProof? proof)
    {

        if (b.ClassId != a.ClassId) return ValidationResult.Fail("class_changed");
        if (b.Hardcore != a.Hardcore) return ValidationResult.Fail("hardcore_changed");
        if (a.Fallen && !b.Fallen) return ValidationResult.Fail("unfallen");
        if (b.Fallen && !b.Hardcore) return ValidationResult.Fail("fallen_softcore");

        var audit = _heroes.Audit(b);
        if (!audit.Ok) return ValidationResult.Fail(audit.Rule!);

        if (b.Level < a.Level) return ValidationResult.Fail("level_decreased");
        if (b.Xp < a.Xp) return ValidationResult.Fail("xp_decreased");
        if (proof is not null && b.Xp - a.Xp > proof.XpEarned) return ValidationResult.Fail("xp_unexplained");
        if (proof is not null && proof.XpEarned > MostXpFor(proof)) return ValidationResult.Fail("xp_beyond_kills");
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
            if (proof is not null && count - was > proof.KillsOf(template)) return ValidationResult.Fail("bestiary_unexplained");
        }
        if (a.Bestiary.Keys.Any(t => !b.Bestiary.ContainsKey(t))) return ValidationResult.Fail("bestiary_decreased");
        if (!a.BossKills.IsSubsetOf(b.BossKills)) return ValidationResult.Fail("boss_kill_lost");

        return ValidationResult.Pass;
    }

    private static int Rank(System.Collections.Immutable.ImmutableSortedDictionary<string, int> map, string key) =>
        map.TryGetValue(key, out var v) ? v : 0;
}
