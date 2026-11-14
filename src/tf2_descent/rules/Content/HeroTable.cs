namespace TF2.Descent.Rules.Content;

/// <summary>A pool stat (life, mana, speed): the flat stat summed, then raised by the percent stat.</summary>
public sealed record PoolStats(string Stat, string PercentStat);

/// <summary>A stat summed onto a base and clamped: crit chance, knockback resistance, spread.</summary>
public sealed record ClampedStat(string Stat, double Base, double Min, double Max);

/// <summary>A damage kind or a resistance, and the stat that adds to it.</summary>
public sealed record KindStat(string Id, string Stat);

/// <summary>
/// One sustain total (plan_tf2_rpg 2a, A.5): (base + the stat) raised by the bonus stat
/// in percent, and never above the cap. Units: percent of max life or mana per second
/// for regeneration, percent of damage dealt for leech.
/// </summary>
public sealed record SustainRule(string Stat, string? BonusStat, double Base, double Cap)
{
    /// <summary>
    /// A stat of flat amounts per second (life_regen_hp: Forward March's "+1 → 3 HP/s") added
    /// to the percent total before the cap, so every source stacks under one cap (A.5). Null:
    /// none.
    /// </summary>
    public string? FlatStat { get; init; }
}

/// <summary>
/// hero.json: the sheet's numbers that are the same for every class (plan_tf2_rpg 2a,
/// Appendix C, D1, D2; ruling L3): the level cap, points a level, the XP curve, armour,
/// resistances, damage kinds, sustain caps and the respec price.
/// </summary>
public sealed record HeroSettings(
    int LevelCap,
    int AttributePointsPerLevel,
    int SkillPointsPerLevel,
    bool LevelUpRefill,
    double XpScale,
    double XpExponent,
    string ArmourStat,
    double ArmourConstant,
    double ArmourPerAttackerLevel,
    PoolStats Life,
    PoolStats Mana,
    PoolStats Speed,
    ClampedStat Crit,
    ClampedStat KnockbackResist,
    ClampedStat Spread,
    string DamageAllStat,
    IReadOnlyList<KindStat> DamageKinds,
    string ResistAllStat,
    double ResistCap,
    double ResistFloor,
    IReadOnlyList<KindStat> Resistances,
    SustainRule LifeRegen,
    SustainRule LifeLeech,
    SustainRule ManaRegen,
    SustainRule ManaLeech,
    long RespecPrice,
    double RespecGrowth);

public sealed class HeroTable : SingleTable<HeroSettings>
{
    public override string Name => ContentTables.Hero;

    protected override HeroSettings Read(JsonFields f)
    {
        var xp = f.Obj("xp");
        var armour = f.Obj("armour");
        var respec = f.Obj("respec");
        var sustain = f.Obj("sustain");
        var settings = new HeroSettings(
            f.Int("levelCap"),
            f.Int("attributePointsPerLevel"),
            f.Int("skillPointsPerLevel"),
            f.Bool("levelUpRefill"),
            xp.Num("scale"),
            xp.Num("exponent"),
            armour.Str("stat"),
            armour.Num("constant"),
            armour.Num("perAttackerLevel"),
            Pool(f.Obj("life")),
            Pool(f.Obj("mana")),
            Pool(f.Obj("speed")),
            Clamped(f.Obj("crit")),
            Clamped(f.Obj("knockbackResist")),
            Clamped(f.Obj("spread")),
            f.Str("damageAll"),
            Kinds(f, "damageKinds"),
            f.Str("resistAll"),
            f.Num("resistCap"),
            f.Num("resistFloor"),
            Kinds(f, "resistances"),
            Sustain(sustain.Obj("lifeRegen")),
            Sustain(sustain.Obj("lifeLeech")),
            Sustain(sustain.Obj("manaRegen")),
            Sustain(sustain.Obj("manaLeech")),
            (long)respec.Num("price"),
            respec.Num("growth"));
        xp.End();
        armour.End();
        respec.End();
        sustain.End();
        return settings;
    }

    private static PoolStats Pool(JsonFields f)
    {
        var p = new PoolStats(f.Str("stat"), f.Str("percentStat"));
        f.End();
        return p;
    }

    private static ClampedStat Clamped(JsonFields f)
    {
        var c = new ClampedStat(f.Str("stat"), f.Num("base"), f.Num("min"), f.Num("max"));
        f.End();
        return c;
    }

    private static SustainRule Sustain(JsonFields f)
    {
        var s = new SustainRule(f.Str("stat"), f.StrOr("bonusStat", null), f.Num("base"), f.Num("cap")) { FlatStat = f.StrOr("flatStat", null) };
        f.End();
        return s;
    }

    private static IReadOnlyList<KindStat> Kinds(JsonFields f, string field) =>
        [.. f.Array(field).Select(e =>
        {
            var k = new JsonFields(e, f.Table, f.Row, f.Errors);
            var kind = new KindStat(k.Str("id"), k.Str("stat"));
            k.End();
            return kind;
        })];

    protected override void Validate(DescentContent content, HeroSettings h, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "levelCap", h.LevelCap, 1, 1000);
        ContentChecks.Range(errors, t, null, "attributePointsPerLevel", h.AttributePointsPerLevel, 0, 100);
        ContentChecks.Range(errors, t, null, "skillPointsPerLevel", h.SkillPointsPerLevel, 0, 100);
        ContentChecks.Range(errors, t, null, "xp.scale", h.XpScale, 1e-9, 1e9);
        ContentChecks.Range(errors, t, null, "xp.exponent", h.XpExponent, 1e-9, 10);
        ContentChecks.Range(errors, t, null, "armour.constant", h.ArmourConstant, 1e-9, 1e9);
        ContentChecks.Range(errors, t, null, "armour.perAttackerLevel", h.ArmourPerAttackerLevel, 0, 1e9);
        ContentChecks.Range(errors, t, null, "resistCap", h.ResistCap, 0, 100);
        ContentChecks.Range(errors, t, null, "resistFloor", h.ResistFloor, -1000, 0);
        ContentChecks.Range(errors, t, null, "respec.price", h.RespecPrice, 0, 1e12);
        ContentChecks.Range(errors, t, null, "respec.growth", h.RespecGrowth, 1, 100);

        foreach (var stat in new[] { h.ArmourStat, h.Life.Stat, h.Life.PercentStat, h.Mana.Stat, h.Mana.PercentStat,
                     h.Speed.Stat, h.Speed.PercentStat, h.DamageAllStat, h.ResistAllStat })
            ContentChecks.Stat(content, errors, t, null, stat);

        foreach (var (field, c) in new[] { ("crit", h.Crit), ("knockbackResist", h.KnockbackResist), ("spread", h.Spread) })
        {
            ContentChecks.Stat(content, errors, t, null, c.Stat);
            if (ContentChecks.Roll(errors, t, null, field + ".min..max", c.Min, c.Max))
                ContentChecks.Range(errors, t, null, field + ".base", c.Base, c.Min, c.Max);
        }

        foreach (var (field, kinds) in new[] { ("damageKinds", h.DamageKinds), ("resistances", h.Resistances) })
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in kinds)
            {
                if (!seen.Add(k.Id))
                    errors.Add(t, k.Id, ContentRule.DuplicateId, $"'{field}' names '{k.Id}' more than once");
                ContentChecks.Stat(content, errors, t, k.Id, k.Stat);
            }
        }

        foreach (var (field, s) in new[] { ("lifeRegen", h.LifeRegen), ("lifeLeech", h.LifeLeech), ("manaRegen", h.ManaRegen), ("manaLeech", h.ManaLeech) })
        {
            ContentChecks.Stat(content, errors, t, field, s.Stat);
            if (s.BonusStat is { } bonus)
                ContentChecks.Stat(content, errors, t, field, bonus);
            if (s.FlatStat is { } flat)
                ContentChecks.Stat(content, errors, t, field, flat);
            if (ContentChecks.Range(errors, t, field, "base", s.Base, 0, 1e6))
                ContentChecks.Range(errors, t, field, "cap", s.Cap, s.Base, 1e6);
        }
    }
}
