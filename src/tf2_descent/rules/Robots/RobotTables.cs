using System.Text.Json;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

//-----------------------------------------------------------------------------
// robot_rules.json
//-----------------------------------------------------------------------------

/// <summary>A size class and its scale range (R16, B.11): runt, ordinary, giant, boss.</summary>
public sealed record SizeClass(string Id, double ScaleMin, double ScaleMax);

/// <summary>
/// The one curve that drives stats from scale (R16, 2f): life ∝ scale^<see cref="LifeExponent"/>,
/// melee reach ∝ scale^<see cref="ReachExponent"/>, move speed ∝ scale^<see cref="SpeedExponent"/>,
/// knockback resistance <see cref="KnockbackResistPerScale"/> percent a unit of scale (capped
/// at <see cref="KnockbackResistMax"/>), XP ∝ life × (budget / budget at scale 1)^<see cref="XpBudgetExponent"/>.
/// </summary>
public sealed record ScaleCurve(double LifeExponent, double ReachExponent, double SpeedExponent,
    double KnockbackResistPerScale, double KnockbackResistMax, double XpBudgetExponent);

/// <summary>What each monster level above 1 adds, compounding, in percent.</summary>
public sealed record LevelCurve(double LifePerLevelPct, double DamagePerLevelPct, double XpPerLevelPct);

/// <summary>Party scaling (D11): per member past the first, robot life and pack size rise by these percents.</summary>
public sealed record PartyScaling(double LifePerExtraMemberPct, double PackSizePerExtraMemberPct, int MaxMembers);

/// <summary>Resistance bounds (R25): a template or affix resist is clamped to [Min, Max]; immunity is <see cref="Immune"/>.</summary>
public sealed record ResistBounds(double Min, double Max, double Immune);

/// <summary>An AI skill (B.11) and the style word it gives a robot whose top skills include it.</summary>
public sealed record SkillRow(string Id, string Style);

/// <summary>
/// The temperament point-buy (B.11): budget = round(<see cref="BudgetPerScale"/> × scale), at
/// most <see cref="BudgetMax"/>; each skill 0 to <see cref="SkillMax"/>; two or three
/// signature skills loaded to <see cref="SignatureLevelMin"/>..<see cref="SignatureLevelMax"/>;
/// a pack of one style re-rolled up to <see cref="PackRerolls"/> times.
/// </summary>
public sealed record TemperamentSettings(
    double BudgetPerScale,
    int BudgetMax,
    int SkillMax,
    int SignatureCountMin,
    int SignatureCountMax,
    int SignatureLevelMin,
    int SignatureLevelMax,
    int PackRerolls,
    IReadOnlyList<SkillRow> Skills);

/// <summary>
/// The numbers of stock TF's push from damage (CTFPlayer::ApplyPushFromDamage and DamageForce,
/// tf_player.cpp): the standing hull, the cap on a push, and the force scales for another's
/// blast, one's own, and a Soldier's own rocket in the air and on the ground, and the share of
/// another's push a Heavy takes.
/// </summary>
public sealed record PushSettings(double HullWidth, double HullDepth, double HullHeight, double ForceCap,
    double OtherScale, double SelfScale, double SelfSoldierAirborneScale, double SelfSoldierGroundedScale, double HeavyOtherMult);

/// <summary>robot_rules.json (plan_tf2_rpg 2f, B.11, R16, R20, R25, D11; L12): the numbers every robot shares.</summary>
public sealed record RobotRulesSettings(
    IReadOnlyList<SizeClass> Sizes,
    ScaleCurve Scale,
    LevelCurve Level,
    PartyScaling Party,
    ResistBounds Resist,
    IReadOnlyList<string> Flags,
    TemperamentSettings Temperament,
    PushSettings Push)
{
    public SizeClass Size(string id) =>
        Sizes.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no size class '{id}'");
}

public sealed class RobotRulesTable : SingleTable<RobotRulesSettings>
{
    public override string Name => ContentTables.RobotRules;

    protected override RobotRulesSettings Read(JsonFields f)
    {
        var sizes = f.Array("sizes").Select(e =>
        {
            var s = new JsonFields(e, f.Table, "sizes", f.Errors);
            var r = new SizeClass(s.Str("id"), s.Num("scaleMin"), s.Num("scaleMax"));
            s.End();
            return r;
        }).ToList();
        var sc = f.Obj("scaleCurve");
        var scale = new ScaleCurve(sc.Num("lifeExponent"), sc.Num("reachExponent"), sc.Num("speedExponent"),
            sc.Num("knockbackResistPerScale"), sc.Num("knockbackResistMax"), sc.Num("xpBudgetExponent"));
        sc.End();
        var lc = f.Obj("levelCurve");
        var level = new LevelCurve(lc.Num("lifePerLevelPct"), lc.Num("damagePerLevelPct"), lc.Num("xpPerLevelPct"));
        lc.End();
        var pa = f.Obj("party");
        var party = new PartyScaling(pa.Num("lifePerExtraMemberPct"), pa.Num("packSizePerExtraMemberPct"), pa.Int("maxMembers"));
        pa.End();
        var re = f.Obj("resist");
        var resist = new ResistBounds(re.Num("min"), re.Num("max"), re.Num("immune"));
        re.End();
        var flags = RobotJson.Strings(f, "flags");
        var te = f.Obj("temperament");
        var skills = te.Array("skills").Select(e =>
        {
            var s = new JsonFields(e, f.Table, "temperament", f.Errors);
            var r = new SkillRow(s.Str("id"), s.Str("style"));
            s.End();
            return r;
        }).ToList();
        var temperament = new TemperamentSettings(te.Num("budgetPerScale"), te.Int("budgetMax"), te.Int("skillMax"),
            te.Int("signatureCountMin"), te.Int("signatureCountMax"), te.Int("signatureLevelMin"), te.Int("signatureLevelMax"),
            te.Int("packRerolls"), skills);
        te.End();
        var pu = f.Obj("push");
        var push = new PushSettings(pu.Num("hullWidth"), pu.Num("hullDepth"), pu.Num("hullHeight"), pu.Num("forceCap"),
            pu.Num("otherScale"), pu.Num("selfScale"), pu.Num("selfSoldierAirborneScale"), pu.Num("selfSoldierGroundedScale"), pu.Num("heavyOtherMult"));
        pu.End();
        return new RobotRulesSettings(sizes, scale, level, party, resist, flags, temperament, push);
    }

    protected override void Validate(DescentContent content, RobotRulesSettings s, ContentErrors errors)
    {
        string t = Name;
        RobotJson.Unique(errors, t, "sizes", s.Sizes.Select(x => x.Id));
        foreach (var z in s.Sizes)
            if (ContentChecks.Range(errors, t, z.Id, "scaleMin", z.ScaleMin, 0.1, 5))
                ContentChecks.Range(errors, t, z.Id, "scaleMax", z.ScaleMax, z.ScaleMin, 5);
        ContentChecks.Range(errors, t, "scaleCurve", "lifeExponent", s.Scale.LifeExponent, 0, 4);
        ContentChecks.Range(errors, t, "scaleCurve", "reachExponent", s.Scale.ReachExponent, 0, 4);
        ContentChecks.Range(errors, t, "scaleCurve", "speedExponent", s.Scale.SpeedExponent, -4, 0);
        ContentChecks.Range(errors, t, "scaleCurve", "knockbackResistPerScale", s.Scale.KnockbackResistPerScale, 0, 100);
        ContentChecks.Range(errors, t, "scaleCurve", "knockbackResistMax", s.Scale.KnockbackResistMax, 0, 100);
        ContentChecks.Range(errors, t, "scaleCurve", "xpBudgetExponent", s.Scale.XpBudgetExponent, 0, 4);
        ContentChecks.Range(errors, t, "levelCurve", "lifePerLevelPct", s.Level.LifePerLevelPct, 0, 100);
        ContentChecks.Range(errors, t, "levelCurve", "damagePerLevelPct", s.Level.DamagePerLevelPct, 0, 100);
        ContentChecks.Range(errors, t, "levelCurve", "xpPerLevelPct", s.Level.XpPerLevelPct, 0, 100);
        ContentChecks.Range(errors, t, "party", "lifePerExtraMemberPct", s.Party.LifePerExtraMemberPct, 0, 1000);
        ContentChecks.Range(errors, t, "party", "packSizePerExtraMemberPct", s.Party.PackSizePerExtraMemberPct, 0, 1000);
        ContentChecks.Range(errors, t, "party", "maxMembers", s.Party.MaxMembers, 1, 64);
        if (ContentChecks.Range(errors, t, "resist", "min", s.Resist.Min, -100, 0)
            && ContentChecks.Range(errors, t, "resist", "max", s.Resist.Max, 0, 99))
            ContentChecks.Range(errors, t, "resist", "immune", s.Resist.Immune, 100, 100);
        RobotJson.Unique(errors, t, "flags", s.Flags);

        var m = s.Temperament;
        ContentChecks.Range(errors, t, "temperament", "budgetPerScale", m.BudgetPerScale, 1, 1000);
        ContentChecks.Range(errors, t, "temperament", "skillMax", m.SkillMax, 1, 100);
        ContentChecks.Range(errors, t, "temperament", "budgetMax", m.BudgetMax, 1, m.SkillMax * Math.Max(1, m.Skills.Count));
        if (ContentChecks.Range(errors, t, "temperament", "signatureCountMin", m.SignatureCountMin, 1, m.Skills.Count))
            ContentChecks.Range(errors, t, "temperament", "signatureCountMax", m.SignatureCountMax, m.SignatureCountMin, m.Skills.Count);
        if (ContentChecks.Range(errors, t, "temperament", "signatureLevelMin", m.SignatureLevelMin, 0, m.SkillMax))
            ContentChecks.Range(errors, t, "temperament", "signatureLevelMax", m.SignatureLevelMax, m.SignatureLevelMin, m.SkillMax);
        ContentChecks.Range(errors, t, "temperament", "packRerolls", m.PackRerolls, 0, 1000);
        RobotJson.Unique(errors, t, "temperament.skills", m.Skills.Select(x => x.Id));
        RobotJson.Unique(errors, t, "temperament.styles", m.Skills.Select(x => x.Style));
        // Every size's largest scale must buy no more than the budget cap (B.11: never above 100).
        foreach (var z in s.Sizes)
            ContentChecks.Range(errors, t, z.Id, "budget at scaleMax", Math.Round(m.BudgetPerScale * z.ScaleMax, MidpointRounding.AwayFromZero), 0, m.BudgetMax);

        var p = s.Push;
        foreach (var (field, v) in new[] { ("hullWidth", p.HullWidth), ("hullDepth", p.HullDepth), ("hullHeight", p.HullHeight),
                     ("forceCap", p.ForceCap), ("otherScale", p.OtherScale), ("selfScale", p.SelfScale),
                     ("selfSoldierAirborneScale", p.SelfSoldierAirborneScale), ("selfSoldierGroundedScale", p.SelfSoldierGroundedScale),
                     ("heavyOtherMult", p.HeavyOtherMult) })
            ContentChecks.Range(errors, t, "push", field, v, 1e-6, 1e6);
    }
}

//-----------------------------------------------------------------------------
// robots.json
//-----------------------------------------------------------------------------

/// <summary>
/// A robot template (plan_tf2_rpg 2f; MvM's spawner vocabulary: class, weapon, scale, health,
/// attributes): its TF class, weapon family (a row of families.json), size class and scale
/// range, base life, damage, move speed, melee reach and XP at scale 1 and monster level 1,
/// resistance per damage type (−50 to +75 %), temperament weights and per-skill caps (B.11;
/// unnamed skills weigh 1 and cap at the skill maximum), an authored temperament for a boss,
/// MvM attribute flags, and the shallowest depth it appears at.
/// </summary>
public sealed record RobotTemplate(
    string Id,
    string Name,
    string Class,
    string Family,
    string Size,
    double ScaleMin,
    double ScaleMax,
    double Life,
    double Damage,
    double Speed,
    double Reach,
    double Xp,
    IReadOnlyDictionary<string, double> Resist,
    IReadOnlyDictionary<string, double> Weights,
    IReadOnlyDictionary<string, int> Caps,
    IReadOnlyDictionary<string, int>? FixedTemperament,
    IReadOnlyList<string> Flags) : IContentRow
{
    public bool Boss => FixedTemperament is not null;

    public double ResistTo(string damageType) => Resist.TryGetValue(damageType, out var r) ? r : 0;

    public double Weight(string skill) => Weights.TryGetValue(skill, out var w) ? w : 1;

    public int Cap(string skill, int skillMax) => Caps.TryGetValue(skill, out var c) ? Math.Min(c, skillMax) : skillMax;
}

public sealed class RobotTemplateTable : RowTable<RobotTemplate>
{
    public override string Name => ContentTables.Robots;

    protected override RobotTemplate ReadRow(JsonFields f)
    {
        var fixedElements = f.Array("fixedTemperament", required: false);
        bool hasFixed = fixedElements.Count > 0;
        return new RobotTemplate(
            f.Str("id"), f.Str("name"), f.Str("class"), f.Str("family"), f.Str("size"),
            f.Num("scaleMin"), f.Num("scaleMax"), f.Num("life"), f.Num("damage"), f.Num("speed"), f.Num("reach"), f.Num("xp"),
            RobotJson.Numbers(f, "resist"), RobotJson.Numbers(f, "weights"), RobotJson.Ints(f, "caps"),
            hasFixed ? RobotJson.Levels(f, fixedElements) : null,
            RobotJson.Strings(f, "flags"));
    }

    protected override void ValidateRow(DescentContent content, RobotTemplate r, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Reference<FamilyRow>(content, errors, t, r.Id, "family", r.Family, ContentTables.Families);
        if (ContentChecks.Range(errors, t, r.Id, "scaleMin", r.ScaleMin, 0.1, 5))
            ContentChecks.Range(errors, t, r.Id, "scaleMax", r.ScaleMax, r.ScaleMin, 5);
        ContentChecks.Range(errors, t, r.Id, "life", r.Life, 1, 1e7);
        ContentChecks.Range(errors, t, r.Id, "damage", r.Damage, 0, 1e5);
        ContentChecks.Range(errors, t, r.Id, "speed", r.Speed, 1, 2000);
        ContentChecks.Range(errors, t, r.Id, "reach", r.Reach, 1, 1000);
        ContentChecks.Range(errors, t, r.Id, "xp", r.Xp, 0, 1e7);
        foreach (var (type, _) in r.Resist)
            ContentChecks.Reference<DamageTypeRow>(content, errors, t, r.Id, "resist", type, ContentTables.DamageTypes);

        if (!content.TryGet<RobotRulesSettings>(ContentTables.RobotRules, out var rules)) return;
        foreach (var (type, v) in r.Resist)
            ContentChecks.Range(errors, t, r.Id, "resist." + type, v, rules.Resist.Min, rules.Resist.Max);
        var size = rules.Sizes.FirstOrDefault(z => z.Id == r.Size);
        if (size is null)
            errors.Add(t, r.Id, ContentRule.DanglingReference, $"'size' names '{r.Size}', which is not a size class of '{ContentTables.RobotRules}'");
        else if (r.ScaleMin < size.ScaleMin - 1e-9 || r.ScaleMax > size.ScaleMax + 1e-9)
            errors.Add(t, r.Id, ContentRule.OutOfRange, $"scale {r.ScaleMin}..{r.ScaleMax} is outside the {size.Id} range {size.ScaleMin}..{size.ScaleMax}");
        foreach (var flag in r.Flags.Where(x => !rules.Flags.Contains(x)))
            errors.Add(t, r.Id, ContentRule.DanglingReference, $"flag '{flag}' is not one of '{ContentTables.RobotRules}' flags");

        var m = rules.Temperament;
        var skills = m.Skills.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (skill, w) in r.Weights)
        {
            if (!skills.Contains(skill)) errors.Add(t, r.Id, ContentRule.DanglingReference, $"weight for '{skill}', which is not an AI skill");
            ContentChecks.Range(errors, t, r.Id, "weights." + skill, w, 0, 100);
        }
        foreach (var (skill, c) in r.Caps)
        {
            if (!skills.Contains(skill)) errors.Add(t, r.Id, ContentRule.DanglingReference, $"cap for '{skill}', which is not an AI skill");
            ContentChecks.Range(errors, t, r.Id, "caps." + skill, c, 0, m.SkillMax);
        }
        int budgetAtMax = (int)Math.Round(m.BudgetPerScale * r.ScaleMax, MidpointRounding.AwayFromZero);
        int room = m.Skills.Sum(s => r.Cap(s.Id, m.SkillMax));
        if (room < budgetAtMax)
            errors.Add(t, r.Id, ContentRule.OutOfRange, $"its caps hold {room} points, fewer than the {budgetAtMax} its largest scale buys");
        if (r.FixedTemperament is { } fixedT)
        {
            if (r.ScaleMin != r.ScaleMax)
                errors.Add(t, r.Id, ContentRule.OutOfRange, "a boss (an authored temperament) has a fixed scale");
            foreach (var (skill, v) in fixedT)
            {
                if (!skills.Contains(skill)) errors.Add(t, r.Id, ContentRule.DanglingReference, $"temperament names '{skill}', which is not an AI skill");
                ContentChecks.Range(errors, t, r.Id, "fixedTemperament." + skill, v, 0, r.Cap(skill, m.SkillMax));
            }
            if (fixedT.Values.Sum() != budgetAtMax)
                errors.Add(t, r.Id, ContentRule.OutOfRange, $"its authored temperament spends {fixedT.Values.Sum()} points, not the {budgetAtMax} its scale buys");
        }
    }
}

//-----------------------------------------------------------------------------
// champions.json
//-----------------------------------------------------------------------------

/// <summary>
/// A champion affix (2f, R25) and its stat effects, in percent: life, damage, move speed, XP,
/// knockback resistance, resistance per damage type added to the template's (then clamped),
/// and the one damage type it makes the robot immune to, if any. What it does in a fight is
/// <see cref="Behaviour"/>, the horde's to carry out.
/// </summary>
public sealed record ChampionAffix(
    string Id,
    string Name,
    double LifePct,
    double DamagePct,
    double SpeedPct,
    double XpPct,
    double KnockbackResistPct,
    IReadOnlyDictionary<string, double> Resist,
    string? Immunity,
    string Behaviour) : IContentRow;

public sealed class ChampionAffixTable : RowTable<ChampionAffix>
{
    public override string Name => ContentTables.Champions;

    protected override ChampionAffix ReadRow(JsonFields f) =>
        new(f.Str("id"), f.Str("name"), f.Num("lifePct"), f.Num("damagePct"), f.Num("speedPct"), f.Num("xpPct"),
            f.Num("knockbackResistPct"), RobotJson.Numbers(f, "resist"), f.StrOr("immunity", null), f.Str("behaviour"));

    protected override void ValidateRow(DescentContent content, ChampionAffix a, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, a.Id, "lifePct", a.LifePct, -90, 1000);
        ContentChecks.Range(errors, t, a.Id, "damagePct", a.DamagePct, -90, 1000);
        ContentChecks.Range(errors, t, a.Id, "speedPct", a.SpeedPct, -90, 300);
        ContentChecks.Range(errors, t, a.Id, "xpPct", a.XpPct, 0, 1000);
        ContentChecks.Range(errors, t, a.Id, "knockbackResistPct", a.KnockbackResistPct, 0, 100);
        foreach (var (type, v) in a.Resist)
        {
            ContentChecks.Reference<DamageTypeRow>(content, errors, t, a.Id, "resist", type, ContentTables.DamageTypes);
            ContentChecks.Range(errors, t, a.Id, "resist." + type, v, -100, 100);
        }
        if (a.Immunity is { } immune)
            ContentChecks.Reference<DamageTypeRow>(content, errors, t, a.Id, "immunity", immune, ContentTables.DamageTypes);
    }
}

//-----------------------------------------------------------------------------
// levels.json
//-----------------------------------------------------------------------------

/// <summary>A template's weight in a depth's packs.</summary>
public sealed record TemplateWeight(string Template, double Weight);

/// <summary>
/// One depth of the descent (2f, D11, D13): its act, monster level, item level and loot table
/// (the host's LevelRow), the pack size range, the chance a robot is a champion and the most
/// affixes a champion carries, and the templates its packs draw from by weight.
/// </summary>
public sealed record DepthRow(
    string Id,
    int Depth,
    int Act,
    int MonsterLevel,
    int ItemLevel,
    string LootTable,
    int PackMin,
    int PackMax,
    double ChampionPct,
    int MaxAffixes,
    IReadOnlyList<TemplateWeight> Templates) : IContentRow;

public sealed class DepthTable : RowTable<DepthRow>
{
    public override string Name => ContentTables.Levels;

    protected override DepthRow ReadRow(JsonFields f)
    {
        var templates = new List<TemplateWeight>();
        foreach (var (key, value) in f.Map("templates"))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var w))
                templates.Add(new TemplateWeight(key, w));
            else
                f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'templates.{key}' must be a number");
        }
        return new DepthRow(f.Str("id"), f.Int("depth"), f.Int("act"), f.Int("monsterLevel"), f.Int("itemLevel"), f.Str("lootTable"),
            f.Int("packMin"), f.Int("packMax"), f.Num("championPct"), f.Int("maxAffixes"), templates);
    }

    protected override void ValidateRow(DescentContent content, DepthRow d, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, d.Id, "act", d.Act, 1, 100);
        ContentChecks.Range(errors, t, d.Id, "monsterLevel", d.MonsterLevel, 1, 1000);
        ContentChecks.Range(errors, t, d.Id, "itemLevel", d.ItemLevel, 1, 1000);
        if (ContentChecks.Range(errors, t, d.Id, "packMin", d.PackMin, 1, 64))
            ContentChecks.Range(errors, t, d.Id, "packMax", d.PackMax, d.PackMin, 64);
        ContentChecks.Range(errors, t, d.Id, "championPct", d.ChampionPct, 0, 100);
        ContentChecks.Range(errors, t, d.Id, "maxAffixes", d.MaxAffixes, 0, 8);
        if (string.IsNullOrEmpty(d.LootTable)) errors.Add(t, d.Id, ContentRule.Malformed, "a depth names its loot table");
        foreach (var w in d.Templates)
        {
            ContentChecks.Reference<RobotTemplate>(content, errors, t, d.Id, "templates", w.Template, ContentTables.Robots);
            ContentChecks.Range(errors, t, d.Id, "templates." + w.Template, w.Weight, 0, 1e6);
            if (content.TryGet<Table<RobotTemplate>>(ContentTables.Robots, out var robots) && robots.TryGet(w.Template, out var r) && r.Boss)
                errors.Add(t, d.Id, ContentRule.OutOfRange, $"'{w.Template}' is a boss: bosses have arenas, not packs");
        }
        if (!(d.Templates.Sum(w => w.Weight) > 0))
            errors.Add(t, d.Id, ContentRule.OutOfRange, "no template has weight: a pack has nothing to draw");
    }

    /// <summary>Depths run 1, 2, 3 … in file order, and monster level never falls with depth.</summary>
    protected override void ValidateTable(DescentContent content, Table<DepthRow> table, ContentErrors errors)
    {
        for (int i = 0; i < table.Count; i++)
        {
            var d = table.Rows[i];
            if (d.Depth != i + 1)
                errors.Add(Name, d.Id, ContentRule.OutOfRange, $"row {i + 1} is depth {d.Depth}: depths run 1, 2, 3 … in order");
            if (i > 0 && d.MonsterLevel <= table.Rows[i - 1].MonsterLevel)
                errors.Add(Name, d.Id, ContentRule.OutOfRange, $"monster level {d.MonsterLevel} does not rise from depth {i}'s {table.Rows[i - 1].MonsterLevel}");
        }
        if (table.Count == 0) errors.Add(Name, null, ContentRule.OutOfRange, "no depths");
    }
}

//-----------------------------------------------------------------------------
// status.json
//-----------------------------------------------------------------------------

/// <summary>How a second application of a status meets the first.</summary>
public enum StatusStacking
{
    /// <summary>The end moves to now + duration; one stack.</summary>
    Refresh,
    /// <summary>One more stack, to the maximum; the end moves to now + duration.</summary>
    Stack,
    /// <summary>Whichever ends later stands.</summary>
    Longest,
}

/// <summary>
/// A robot status effect (default E9): burn, bleed, stun, slow, marked. A damaging one names
/// its damage type (a robot immune to it cannot take it), deals <see cref="DamagePerSecond"/>
/// a stack in ticks of <see cref="TickSeconds"/>; the others slow, raise damage taken, or stun.
/// </summary>
public sealed record StatusRow(
    string Id,
    string? DamageType,
    double DamagePerSecond,
    double TickSeconds,
    double DurationSeconds,
    StatusStacking Stacking,
    int MaxStacks,
    double SlowPct,
    double DamageTakenPct,
    bool Stuns) : IContentRow;

public sealed class StatusTable : RowTable<StatusRow>
{
    public override string Name => ContentTables.Status;

    protected override StatusRow ReadRow(JsonFields f)
    {
        string stacking = f.Str("stacking");
        var mode = Enum.GetValues<StatusStacking>().FirstOrDefault(v => string.Equals(v.ToString(), stacking, StringComparison.OrdinalIgnoreCase), (StatusStacking)(-1));
        if ((int)mode < 0)
            f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'stacking' is '{stacking}', not refresh, stack or longest");
        return new StatusRow(f.Str("id"), f.StrOr("damageType", null), f.Num("damagePerSecond"), f.Num("tickSeconds"), f.Num("durationSeconds"),
            mode, f.Int("maxStacks"), f.Num("slowPct"), f.Num("damageTakenPct"), f.Bool("stuns"));
    }

    protected override void ValidateRow(DescentContent content, StatusRow s, ContentErrors errors)
    {
        string t = Name;
        if (s.DamageType is { } type)
            ContentChecks.Reference<DamageTypeRow>(content, errors, t, s.Id, "damageType", type, ContentTables.DamageTypes);
        ContentChecks.Range(errors, t, s.Id, "damagePerSecond", s.DamagePerSecond, 0, 1e5);
        ContentChecks.Range(errors, t, s.Id, "durationSeconds", s.DurationSeconds, 0.01, 600);
        ContentChecks.Range(errors, t, s.Id, "maxStacks", s.MaxStacks, 1, 100);
        ContentChecks.Range(errors, t, s.Id, "slowPct", s.SlowPct, 0, 100);
        ContentChecks.Range(errors, t, s.Id, "damageTakenPct", s.DamageTakenPct, 0, 1000);
        if (s.DamagePerSecond > 0)
        {
            ContentChecks.Range(errors, t, s.Id, "tickSeconds", s.TickSeconds, 0.01, s.DurationSeconds);
            if (s.DamageType is null) errors.Add(t, s.Id, ContentRule.Malformed, "a damaging status names its damage type");
        }
    }
}

//-----------------------------------------------------------------------------

/// <summary>Readers the robot tables share.</summary>
internal static class RobotJson
{
    public static IReadOnlyList<string> Strings(JsonFields f, string field)
    {
        var list = new List<string>();
        foreach (var e in f.Array(field))
            if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString()!);
            else f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'{field}' must hold only strings");
        return list;
    }

    public static IReadOnlyDictionary<string, double> Numbers(JsonFields f, string field)
    {
        var map = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in f.Map(field))
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)) map[key] = d;
            else f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'{field}.{key}' must be a number");
        return map;
    }

    public static IReadOnlyDictionary<string, int> Ints(JsonFields f, string field)
    {
        var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in f.Map(field))
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) map[key] = i;
            else f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'{field}.{key}' must be a whole number");
        return map;
    }

    /// <summary>An authored temperament: an array of {"skill": id, "level": n}.</summary>
    public static IReadOnlyDictionary<string, int> Levels(JsonFields f, IReadOnlyList<JsonElement> elements)
    {
        var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in elements)
        {
            var l = new JsonFields(e, f.Table, f.Row, f.Errors);
            string skill = l.Str("skill");
            int level = l.Int("level");
            l.End();
            if (!map.TryAdd(skill, level))
                f.Errors.Add(f.Table, f.Row, ContentRule.DuplicateId, $"'fixedTemperament' names '{skill}' more than once");
        }
        return map;
    }

    public static void Unique(ContentErrors errors, string table, string field, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id))
                errors.Add(table, id, ContentRule.DuplicateId, $"'{field}' names '{id}' more than once");
    }
}
