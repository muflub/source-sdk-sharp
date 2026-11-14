using System.Text.Json;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Skills;

/// <summary>
/// skill_settings.json: the numbers every skill shares (plan_tf2_rpg 2e, D2, A.1, A.5, R27):
/// the hero level each tier unlocks at, each kind's max rank, the shared tab's class name and
/// its max rank, how far bonus ranks may pass max rank, the action damage formula, the stats
/// that change mana costs and Orders, and the spell rank cap and scroll stack.
/// </summary>
public sealed record SkillSettings(
    IReadOnlyList<int> TierLevels,
    IReadOnlyDictionary<SkillKind, int> MaxRank,
    string SharedClass,
    int SharedMaxRank,
    int BonusRankCap,
    double DamagePerHeroLevel,
    double DamageAttributeDivisor,
    string ManaCostStat,
    string OrderDurationStat,
    string OrderRadiusStat,
    string OrderCooldownStat,
    int SpellMaxRank,
    int ScrollStack,
    string SpellAttribute)
{
    /// <summary>The hero level tier <paramref name="tier"/> (1-based) unlocks at.</summary>
    public int TierLevel(int tier) => TierLevels[tier - 1];
}

public sealed class SkillSettingsTable : SingleTable<SkillSettings>
{
    public override string Name => ContentTables.SkillSettings;

    protected override SkillSettings Read(JsonFields f)
    {
        var tiers = new List<int>();
        foreach (var e in f.Array("tierLevels"))
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i)) tiers.Add(i);
            else f.Errors.Add(Name, null, ContentRule.Malformed, "'tierLevels' must hold only whole numbers");
        }
        var maxRank = new Dictionary<SkillKind, int>();
        var mr = f.Obj("maxRank");
        foreach (var kind in Enum.GetValues<SkillKind>())
            maxRank[kind] = mr.Int(SkillTable.KindName(kind));
        mr.End();
        var damage = f.Obj("damage");
        var orders = f.Obj("orders");
        var spells = f.Obj("spells");
        var s = new SkillSettings(tiers, maxRank, f.Str("sharedClass"), f.Int("sharedMaxRank"), f.Int("bonusRankCap"),
            damage.Num("perHeroLevel"), damage.Num("attributeDivisor"), f.Str("manaCostStat"),
            orders.Str("durationStat"), orders.Str("radiusStat"), orders.Str("cooldownStat"),
            spells.Int("maxRank"), spells.Int("scrollStack"), spells.Str("attribute"));
        damage.End();
        orders.End();
        spells.End();
        return s;
    }

    protected override void Validate(DescentContent content, SkillSettings s, ContentErrors errors)
    {
        int cap = content.TryGet<HeroSettings>(ContentTables.Hero, out var hero) ? hero.LevelCap : int.MaxValue;
        if (s.TierLevels.Count == 0)
            errors.Add(Name, null, ContentRule.Malformed, "'tierLevels' names no tier");
        for (int i = 0; i < s.TierLevels.Count; i++)
            ContentChecks.Range(errors, Name, null, $"tierLevels[{i}]", s.TierLevels[i], i == 0 ? 1 : s.TierLevels[i - 1] + 1, cap);
        foreach (var (kind, rank) in s.MaxRank)
            ContentChecks.Range(errors, Name, null, "maxRank." + SkillTable.KindName(kind), rank, 1, 100);
        ContentChecks.Range(errors, Name, null, "sharedMaxRank", s.SharedMaxRank, 1, 100);
        ContentChecks.Range(errors, Name, null, "bonusRankCap", s.BonusRankCap, 0, 100);
        ContentChecks.Range(errors, Name, null, "damage.perHeroLevel", s.DamagePerHeroLevel, 0, 10);
        ContentChecks.Range(errors, Name, null, "damage.attributeDivisor", s.DamageAttributeDivisor, 1e-9, 1e9);
        ContentChecks.Range(errors, Name, null, "spells.maxRank", s.SpellMaxRank, 1, 100);
        ContentChecks.Range(errors, Name, null, "spells.scrollStack", s.ScrollStack, 1, 1000);
        foreach (var stat in new[] { s.ManaCostStat, s.OrderDurationStat, s.OrderRadiusStat, s.OrderCooldownStat })
            ContentChecks.Stat(content, errors, Name, null, stat);
        ContentChecks.Reference<AttributeRow>(content, errors, Name, null, "spells.attribute", s.SpellAttribute, ContentTables.Attributes);
        if (content.TryGet<Table<ClassRow>>(ContentTables.Classes, out var classes) && classes.Contains(s.SharedClass))
            errors.Add(Name, null, ContentRule.DuplicateId, $"'sharedClass' '{s.SharedClass}' is a hero class's id");
    }
}

/// <summary>
/// skill_tabs.json: each class's three tabs and the shared Mercenary tab (D2, A.1). An
/// action's damage scales with its tab's <see cref="Attribute"/>, or with the attribute
/// <see cref="AttributeByDamageType"/> names for its damage type (Ordnance is Aim, its shock
/// actions Smarts). The shared tab has no actions and no attribute.
/// </summary>
public sealed record SkillTabRow(
    string Id,
    string Class,
    string Name,
    string? Attribute,
    IReadOnlyDictionary<string, string> AttributeByDamageType) : IContentRow
{
    /// <summary>The attribute an action of <paramref name="damageType"/> in this tab scales with, or null for none.</summary>
    public string? AttributeFor(string? damageType) =>
        damageType is not null && AttributeByDamageType.TryGetValue(damageType, out var a) ? a : Attribute;
}

public sealed class SkillTabTable : RowTable<SkillTabRow>
{
    public override string Name => ContentTables.SkillTabs;

    protected override SkillTabRow ReadRow(JsonFields f)
    {
        var by = new Dictionary<string, string>(StringComparer.Ordinal);
        if (f.Element("attributeByDamageType", required: false) is { ValueKind: JsonValueKind.Object } map)
            foreach (var p in map.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String) by[p.Name] = p.Value.GetString()!;
                else f.Errors.Add(Name, f.Row, ContentRule.Malformed, "'attributeByDamageType' must map to attribute ids");
            }
        return new SkillTabRow(f.Str("id"), f.Str("class"), f.Str("name"), f.StrOr("attribute", null), by);
    }

    protected override void ValidateRow(DescentContent content, SkillTabRow row, ContentErrors errors)
    {
        SkillChecks.ClassOrShared(content, errors, Name, row.Id, row.Class);
        if (row.Attribute is { } a)
            ContentChecks.Reference<AttributeRow>(content, errors, Name, row.Id, "attribute", a, ContentTables.Attributes);
        foreach (var (type, attr) in row.AttributeByDamageType)
        {
            ContentChecks.Reference<DamageTypeRow>(content, errors, Name, row.Id, "attributeByDamageType", type, ContentTables.DamageTypes);
            ContentChecks.Reference<AttributeRow>(content, errors, Name, row.Id, "attributeByDamageType." + type, attr, ContentTables.Attributes);
        }
    }
}

/// <summary>
/// skills.json: every skill (plan_tf2_rpg 2e, Appendix A). Each rule of R17 is a load error
/// here: a skill that names an item base, whose id or name is an item base's, that names a
/// family no families row has, an action that names a family at all, an action with no
/// damage type or an unknown one; and so is a dangling prerequisite or synergy, a
/// prerequisite cycle, a tier or level floor outside skill_settings' tiers, and a max rank
/// outside its kind's limit.
/// </summary>
public sealed class SkillTable : RowTable<SkillDef>
{
    public override string Name => ContentTables.Skills;

    public static string KindName(SkillKind kind) => kind switch
    {
        SkillKind.Action => "action",
        SkillKind.Upgrade => "upgrade",
        SkillKind.Toggle => "toggle",
        SkillKind.Capstone => "capstone",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static SkillKind? ParseKind(string name) =>
        Enum.GetValues<SkillKind>().Cast<SkillKind?>().FirstOrDefault(k => KindName(k!.Value) == name);

    protected override SkillDef ReadRow(JsonFields f)
    {
        string kindName = f.Str("kind");
        var kind = ParseKind(kindName);
        if (kind is null && kindName.Length > 0)
            f.Errors.Add(Name, f.Row, ContentRule.Malformed, $"'kind' is '{kindName}', not one of action, upgrade, toggle, capstone");
        int maxRank = f.Int("maxRank");
        int ranks = Math.Clamp(maxRank, 1, 100);
        return new SkillDef(
            f.Str("id"), f.Str("name"), f.Str("class"), f.Str("tab"), f.IntOr("tier", 0),
            kind ?? SkillKind.Upgrade, maxRank,
            f.StrOr("damageType", null), f.StrOr("scope", null),
            SkillChecks.Curve(f, "manaCost", ranks, 0), SkillChecks.Curve(f, "cooldown", ranks, 0),
            f.Element("levelFloor", required: false) is { } lf ? RankCurve.Read(lf, ranks, "levelFloor", Name, f.Row, f.Errors) : null,
            SkillChecks.ReadPrerequisites(f), SkillChecks.ReadSynergies(f), f.Strings("tags"), f.StrOr("requires", null),
            SkillChecks.ReadEffects(f, ranks));
    }

    protected override void ValidateRow(DescentContent content, SkillDef row, ContentErrors errors)
    {
        string t = Name;
        SkillChecks.Identity(content, errors, t, row.Id, row.Name);
        content.TryGet<SkillSettings>(ContentTables.SkillSettings, out var s);
        bool shared = s is not null && row.Class == s.SharedClass;

        // Class and tab.
        SkillChecks.ClassOrShared(content, errors, t, row.Id, row.Class);
        if (ContentChecks.Reference<SkillTabRow>(content, errors, t, row.Id, "tab", row.Tab, ContentTables.SkillTabs)
            && content.TryGet<Table<SkillTabRow>>(ContentTables.SkillTabs, out var tabs) && tabs.TryGet(row.Tab, out var tab)
            && tab.Class != row.Class)
            errors.Add(t, row.Id, ContentRule.DanglingReference, $"'tab' '{row.Tab}' belongs to '{tab.Class}', not '{row.Class}'");

        // Tier, level floors, max rank.
        if (s is not null)
        {
            if (shared)
            {
                if (row.Tier != 0)
                    errors.Add(t, row.Id, ContentRule.OutOfRange, $"'tier' is {row.Tier}: the shared tab has no tiers, its ranks have level floors");
                if (row.LevelFloor is null)
                    errors.Add(t, row.Id, ContentRule.Malformed, "'levelFloor' is required in the shared tab");
                else
                    SkillChecks.LevelFloors(content, errors, t, row.Id, row.LevelFloor, row.MaxRank);
                ContentChecks.Range(errors, t, row.Id, "maxRank", row.MaxRank, 1, s.SharedMaxRank);
                if (row.Kind != SkillKind.Upgrade)
                    errors.Add(t, row.Id, ContentRule.OutOfRange, $"'kind' is {KindName(row.Kind)}: the shared tab holds upgrades only");
            }
            else
            {
                ContentChecks.Range(errors, t, row.Id, "tier", row.Tier, 1, s.TierLevels.Count);
                if (row.LevelFloor is not null)
                    errors.Add(t, row.Id, ContentRule.OutOfRange,
                        $"'levelFloor' is given: a class skill unlocks at its tier's level ({string.Join(" / ", s.TierLevels)})");
                int limit = s.MaxRank[row.Kind];
                if (row.Kind == SkillKind.Capstone)
                    ContentChecks.Range(errors, t, row.Id, "maxRank", row.MaxRank, limit, limit);
                else
                    ContentChecks.Range(errors, t, row.Id, "maxRank", row.MaxRank, 1, limit);
            }
        }

        // R17: an action's damage type; an upgrade's scope.
        if (row.IsCast)
        {
            SkillChecks.DamageType(content, errors, t, row.Id, row.DamageType, required: row.Kind != SkillKind.Toggle || row.DealsDamage);
            if (row.Scope is { } scope)
                SkillChecks.FamilyOnAction(content, errors, t, row.Id, "scope", scope);
        }
        else
        {
            if (row.Scope is null)
                errors.Add(t, row.Id, ContentRule.Malformed, "'scope' is required for an upgrade: 'all' or a weapon family");
            else if (row.Scope != SkillDef.AllWeapons)
                SkillChecks.Family(content, errors, t, row.Id, "scope", row.Scope);
            if (row.DealsDamage || row.DamageType is not null)
                SkillChecks.DamageType(content, errors, t, row.Id, row.DamageType, required: row.DealsDamage);
        }
        SkillChecks.Costs(errors, t, row.Id, row.ManaCost, row.Cooldown, row.MaxRank, row.IsCast);

        // Prerequisites and synergies.
        foreach (var p in row.Prerequisites)
        {
            foreach (var id in p.Skills)
            {
                SkillChecks.Reference<SkillDef>(content, errors, t, row.Id, "prerequisites", id, ContentTables.Skills);
                if (id == row.Id)
                    errors.Add(t, row.Id, ContentRule.PrerequisiteCycle, "a skill is its own prerequisite");
            }
            ContentChecks.Range(errors, t, row.Id, "prerequisites.count", p.Count, 1, Math.Max(1, p.Skills.Count));
            ContentChecks.Range(errors, t, row.Id, "prerequisites.rank", p.MinRank, 1, 100);
        }
        foreach (var syn in row.Synergies)
        {
            SkillChecks.Reference<SkillDef>(content, errors, t, row.Id, "synergies", syn.Skill, ContentTables.Skills);
            if (syn.Skill == row.Id)
                errors.Add(t, row.Id, ContentRule.DanglingReference, "a skill is its own synergy");
            SkillChecks.Scales(errors, t, row.Id, syn, row.Effect);
        }
        foreach (var tag in row.Tags.Where(x => !Primitives.Tags.Contains(x)))
            errors.Add(t, row.Id, ContentRule.OutOfRange, $"'tags' names '{tag}', not one of {string.Join(", ", Primitives.Tags)}");
        if (row.Requires is { } req && !Primitives.Requirements.Contains(req))
            errors.Add(t, row.Id, ContentRule.OutOfRange, $"'requires' is '{req}', not one of {string.Join(", ", Primitives.Requirements)}");

        SkillChecks.Effects(content, errors, t, row.Id, row.Effect, row.MaxRank, cast: row.IsCast);
    }

    protected override void ValidateTable(DescentContent content, Table<SkillDef> table, ContentErrors errors)
    {
        // A prerequisite cycle: each skill on one is reported once, by a depth-first walk.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 visiting, 2 done
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        void Visit(string id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var next in table[id].Prerequisites.SelectMany(p => p.Skills).Where(table.Contains).Where(n => n != id))
            {
                state.TryGetValue(next, out var st);
                if (st == 1)
                {
                    var cycle = path.Skip(path.IndexOf(next)).ToList();
                    foreach (var c in cycle.Where(reported.Add))
                        errors.Add(Name, c, ContentRule.PrerequisiteCycle,
                            $"its prerequisites lead back to it: {string.Join(" -> ", cycle)} -> {next}");
                }
                else if (st == 0)
                    Visit(next);
            }
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }

        foreach (var row in table.Rows)
            if (!state.ContainsKey(row.Id))
                Visit(row.Id);
    }
}

/// <summary>The checks skills and spells share: R17 and the effect descriptors.</summary>
public static class SkillChecks
{
    internal static RankCurve Curve(JsonFields f, string field, int ranks, double fallback) =>
        f.Element(field, required: false) is { } e ? RankCurve.Read(e, ranks, field, f.Table, f.Row, f.Errors) : RankCurve.Constant(fallback, ranks);

    internal static IReadOnlyList<Prerequisite> ReadPrerequisites(JsonFields f)
    {
        var list = new List<Prerequisite>();
        foreach (var e in f.Array("prerequisites", required: false))
        {
            var p = new JsonFields(e, f.Table, f.Row, f.Errors);
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("anyOf", out _))
            {
                var skills = p.Strings("anyOf", required: true);
                list.Add(new Prerequisite(skills, p.Int("count"), p.IntOr("rank", 1)));
            }
            else
                list.Add(new Prerequisite([p.Str("skill")], 1, p.IntOr("rank", 1)));
            p.End();
        }
        return list;
    }

    internal static IReadOnlyList<Synergy> ReadSynergies(JsonFields f)
    {
        var list = new List<Synergy>();
        foreach (var e in f.Array("synergies", required: false))
        {
            var s = new JsonFields(e, f.Table, f.Row, f.Errors);
            list.Add(new Synergy(s.Str("skill"), s.Num("perRank"), s.Str("scales")));
            s.End();
        }
        return list;
    }

    internal static IReadOnlyList<EffectDef> ReadEffects(JsonFields f, int ranks)
    {
        var list = new List<EffectDef>();
        foreach (var e in f.Array("effect"))
        {
            var x = new JsonFields(e, f.Table, f.Row, f.Errors);
            string name = x.Str("primitive");
            var spec = Primitives.Find(name);
            if (spec is null)
            {
                if (name.Length > 0)
                    f.Errors.Add(f.Table, f.Row, ContentRule.Malformed,
                        $"'primitive' is '{name}', not one of {string.Join(", ", Primitives.All().Select(p => p.Name))}");
                continue;   // its parameters mean nothing without a primitive: the one error stands

            }
            var ps = new Dictionary<string, RankCurve>(StringComparer.Ordinal);
            foreach (var p in spec.Required)
                if (x.Element(p) is { } v)
                    ps[p] = RankCurve.Read(v, ranks, p, f.Table, f.Row, f.Errors);
            foreach (var p in spec.Optional.Keys)
                if (x.Element(p, required: false) is { } v)
                    ps[p] = RankCurve.Read(v, ranks, p, f.Table, f.Row, f.Errors);
            var stats = new List<StatCurve>();
            foreach (var s in x.Array("stats", required: false))
            {
                var sf = new JsonFields(s, f.Table, f.Row, f.Errors);
                var stat = sf.Str("stat");
                stats.Add(new StatCurve(stat, sf.Element("amount") is { } a ? RankCurve.Read(a, ranks, "amount", f.Table, f.Row, f.Errors) : RankCurve.Constant(0, ranks)));
                sf.End();
            }
            list.Add(new EffectDef(spec.Primitive, x.StrOr("trigger", null), x.StrOr("target", null), x.StrOr("family", null),
                ps, stats, x.Strings("conditions"), x.StrOr("capacity", null)));
            x.End();
        }
        return list;
    }

    /// <summary>A class id, or the shared tab's.</summary>
    public static void ClassOrShared(DescentContent content, ContentErrors errors, string table, string row, string cls)
    {
        if (content.TryGet<SkillSettings>(ContentTables.SkillSettings, out var s) && cls == s.SharedClass) return;
        ContentChecks.Reference<ClassRow>(content, errors, table, row, "class", cls, ContentTables.Classes);
    }

    /// <summary>R17, R12: an id or display name that is an item base's is a load error.</summary>
    public static void Identity(DescentContent content, ContentErrors errors, string table, string row, string name)
    {
        var bases = content.KnownItemBases();
        if (bases.HasId(row) || bases.HasName(row))
            errors.Add(table, row, ContentRule.NameCollision, $"id '{row}' is an item base's: a skill and an item never share a name (R17)");
        if (bases.HasName(name) || bases.HasId(name))
            errors.Add(table, row, ContentRule.NameCollision, $"name '{name}' is an item base's: a skill and an item never share a name (R17)");
        if (name.Trim().Length == 0)
            errors.Add(table, row, ContentRule.Malformed, "'name' is empty");
    }

    /// <summary>
    /// R17: a field that names an item base is a load error, whatever the field means. An
    /// id that is also a row of the table the field reads (a family "rocket_launcher" beside
    /// an item base "rocket_launcher") is that row, not the item.
    /// </summary>
    public static bool NotItemBase(DescentContent content, ContentErrors errors, string table, string row, string field, string id)
    {
        if (!content.KnownItemBases().HasId(id)) return true;
        errors.Add(table, row, ContentRule.ItemBaseReference, $"'{field}' names item base '{id}': a skill is never tied to an item the player finds (R17)");
        return false;
    }

    private static bool IsRow<TRow>(DescentContent content, string target, string id) where TRow : IContentRow =>
        content.TryGet<Table<TRow>>(target, out var t) && t.Contains(id);

    /// <summary>A reference to a row of <paramref name="target"/>: the row, else an item base (R17), else dangling.</summary>
    public static void Reference<TRow>(DescentContent content, ContentErrors errors, string table, string row, string field, string id, string target)
        where TRow : IContentRow
    {
        if (IsRow<TRow>(content, target, id)) return;
        if (NotItemBase(content, errors, table, row, field, id))
            ContentChecks.Reference<TRow>(content, errors, table, row, field, id, target);
    }

    /// <summary>A weapon family a row names: a row of families; an item base there is R17's error.</summary>
    public static void Family(DescentContent content, ContentErrors errors, string table, string row, string field, string family) =>
        Reference<FamilyRow>(content, errors, table, row, field, family, ContentTables.Families);

    /// <summary>R17: an action works with anything or nothing equipped, so naming a family (or an item) is a load error.</summary>
    public static void FamilyOnAction(DescentContent content, ContentErrors errors, string table, string row, string field, string family)
    {
        if (IsRow<FamilyRow>(content, ContentTables.Families, family) || NotItemBase(content, errors, table, row, field, family))
            errors.Add(table, row, ContentRule.FamilyOnAction, $"'{field}' names '{family}': an action never names a weapon family (R17)");
    }

    /// <summary>R17: exactly one damage type, a row of damage_types.</summary>
    public static void DamageType(DescentContent content, ContentErrors errors, string table, string row, string? type, bool required)
    {
        if (type is null)
        {
            if (required)
                errors.Add(table, row, ContentRule.Malformed, "'damageType' is required: an action declares exactly one damage type (R17)");
            return;
        }
        ContentChecks.Reference<DamageTypeRow>(content, errors, table, row, "damageType", type, ContentTables.DamageTypes);
    }

    /// <summary>A shared-tab skill's per-rank level floors: each a level the hero can reach, never falling.</summary>
    public static void LevelFloors(DescentContent content, ContentErrors errors, string table, string row, RankCurve floors, int maxRank)
    {
        int cap = content.TryGet<HeroSettings>(ContentTables.Hero, out var hero) ? hero.LevelCap : int.MaxValue;
        double previous = 1;
        for (int r = 1; r <= Math.Max(1, maxRank); r++)
        {
            double floor = floors.At(r);
            if (!ContentChecks.Range(errors, table, row, $"levelFloor[{r}]", floor, previous, cap)) return;
            previous = floor;
        }
    }

    /// <summary>Mana cost and cooldown are never negative; an upgrade costs neither.</summary>
    public static void Costs(ContentErrors errors, string table, string row, RankCurve mana, RankCurve cooldown, int maxRank, bool cast)
    {
        for (int r = 1; r <= Math.Clamp(maxRank, 1, 100); r++)
        {
            if (!ContentChecks.Range(errors, table, row, $"manaCost[{r}]", mana.At(r), 0, cast ? 1e6 : 0)) return;
            if (!ContentChecks.Range(errors, table, row, $"cooldown[{r}]", cooldown.At(r), 0, cast ? 1e6 : 0)) return;
        }
    }

    /// <summary>
    /// A synergy scales a parameter one of the skill's effects writes (it scales it only where
    /// written), or <c>potency_pct</c>: the stats of its effects that are not passives.
    /// </summary>
    public static void Scales(ContentErrors errors, string table, string row, Synergy syn, IReadOnlyList<EffectDef> effects)
    {
        bool ok = syn.Scales == Synergy.Potency
            ? effects.Any(e => e.Primitive != Primitive.Passive && e.Stats.Count > 0)
            : effects.Any(e => e.Params.ContainsKey(syn.Param));
        if (!ok)
            errors.Add(table, row, ContentRule.DanglingReference,
                $"synergy with '{syn.Skill}' scales '{syn.Scales}', which none of this skill's effects has");
    }

    /// <summary>Each effect: its vocabulary, its family (never on a cast), its stats, non-negative parameters.</summary>
    public static void Effects(DescentContent content, ContentErrors errors, string table, string row, IReadOnlyList<EffectDef> effects,
        int maxRank, bool cast)
    {
        if (effects.Count == 0)
            errors.Add(table, row, ContentRule.Malformed, "'effect' has no primitive");
        int ranks = Math.Clamp(maxRank, 1, 100);
        foreach (var e in effects)
        {
            string where = e.Spec.Name;
            if (e.Trigger is { } trig && !Primitives.Triggers.Contains(trig))
                errors.Add(table, row, ContentRule.OutOfRange, $"{where}: 'trigger' is '{trig}', not a trigger");
            if (e.Primitive == Primitive.Triggered && e.Trigger is null)
                errors.Add(table, row, ContentRule.Malformed, $"{where}: 'trigger' is required");
            if (e.Target is { } target && !Primitives.Targets.Contains(target))
                errors.Add(table, row, ContentRule.OutOfRange, $"{where}: 'target' is '{target}', not one of {string.Join(", ", Primitives.Targets)}");
            foreach (var c in e.Conditions.Where(c => !Primitives.Conditions.Contains(c)))
                errors.Add(table, row, ContentRule.OutOfRange, $"{where}: condition '{c}' is not a condition");
            if (e.Capacity is { } cap && (e.Primitive != Primitive.Passive || !Primitives.Capacities.Contains(cap)))
                errors.Add(table, row, ContentRule.OutOfRange, $"{where}: 'capacity' '{cap}' is not a store a passive sizes");
            if (e.Family is { } family)
            {
                if (cast) FamilyOnAction(content, errors, table, row, "effect.family", family);
                else Family(content, errors, table, row, "effect.family", family);
            }
            foreach (var s in e.Stats)
                for (int r = 1; r <= ranks; r++)
                    if (!ContentChecks.Stat(content, errors, table, row, s.Stat, s.Amount.At(r)))
                        break;
            foreach (var (name, curve) in e.Params)
                for (int r = 1; r <= ranks; r++)
                    if (!ContentChecks.Range(errors, table, row, $"{where}.{name}[{r}]", curve.At(r), 0, 1e6))
                        break;
        }
    }
}
