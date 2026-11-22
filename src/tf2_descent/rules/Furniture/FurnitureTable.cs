using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Loot;

namespace TF2.Descent.Rules.Furniture;

/// <summary>What a furniture marker can hold.</summary>
public enum FurnitureKind
{
    Shrine,
    Container,
    Dispenser,
    Explosive,
    Trap,

    /// <summary>A crate or a barrel any hit breaks, for a roll (<see cref="BreakRules"/>).</summary>
    Breakable,
}

/// <summary>
/// A marker kind a room declares (its <c>poi_type</c>, src/mapgen/Descent/Markers.cs), what
/// it holds, how many of that kind one room may furnish, and the chance each chosen marker
/// is furnished at all. A breakable marker names its <see cref="Variant"/> (a crate, a barrel).
/// <see cref="Otherwise"/>, when set, is the breakable a marker of the kind holds when the
/// furnishing passed it over (past the room's limit, or its fill roll failed). The shipped table
/// sets none: the levels carry their own breakable markers, and a stand-in put crates where a
/// theme has none.
/// </summary>
public sealed record MarkerRule(string Poi, FurnitureKind Kind, int PerRoom, double FillPct, string? Variant = null, string? Otherwise = null);

/// <summary>Whether a shrine helps, trades one stat for another, or hurts (2l).</summary>
public enum ShrineNature
{
    Good,
    Trade,
    Bad,
}

/// <summary>How long a shrine's effect lasts: done on use, or until its taker leaves the level.</summary>
public enum ShrineLasts
{
    Instant,
    Level,
}

/// <summary>
/// One shrine effect (2l): its name and its cryptic message (localisation tokens), the
/// stats it grants, whether it refills life and mana, reveals the automap, or raises a
/// random known spell by some ranks, whether the whole party takes it, and how long it lasts.
/// </summary>
public sealed record ShrineEffect(
    string Id,
    ShrineNature Nature,
    double Weight,
    string NameToken,
    string MessageToken,
    bool PartyWide,
    ShrineLasts Lasts,
    IReadOnlyList<StatAmount> Stats,
    bool Refill,
    bool RevealMap,
    int SpellRanks) : IContentRow;

/// <summary>
/// A Dispenser's finite charge and its rates; health and ammo each cost charge by the point.
/// <see cref="Radius"/> is how near a hero stands to draw from it (stock's dispenser radius, 64).
/// </summary>
public sealed record DispenserSettings(double Charge, double HealPerSecond, double AmmoPerSecond, double ChargePerHealth, double ChargePerAmmo,
    double Radius);

/// <summary>A container kind: its weight, and whether a hit breaks it open (a wooden supply crate) as well as +USE.</summary>
public sealed record ContainerKind(string Id, double Weight, bool Breaks);

/// <summary>Containers: the loot table they roll from (the loot package's id), the share locked and trapped, and their kinds.</summary>
public sealed record ContainerSettings(string LootTable, double LockedPct, double TrappedPct, IReadOnlyList<ContainerKind> Kinds);

/// <summary>
/// A barrel or a pumpkin bomb: full damage at its centre falling linearly to
/// <see cref="EdgePct"/> of it at <see cref="Radius"/>, nothing beyond; any other explosive
/// within <see cref="ChainRadius"/> goes off too, <see cref="ChainDelay"/> seconds after it.
/// It goes off when the damage it has taken reaches <see cref="Health"/>.
/// </summary>
public sealed record ExplosiveRow(string Id, double Weight, string DamageType, double Damage, double Radius, double EdgePct, double ChainRadius,
    double Health, double ChainDelay) : IContentRow;

/// <summary>A trap's tell: something a careful player sees or hears before it fires (2l).</summary>
public sealed record TrapTell(string Kind, string Token);

/// <summary>
/// A trap: its damage type and damage, its radius (0: only what stands in its trigger area), and
/// its tell. A body that walks within <see cref="Trigger"/> units of it trips it (a robot only when
/// <see cref="Robots"/>); the tell is given then, and it fires <see cref="Fuse"/> seconds later.
/// It is armed again <see cref="Rearm"/> seconds after it fires; 0 never.
/// </summary>
public sealed record TrapRow(string Id, double Weight, string DamageType, double Damage, double Radius, TrapTell Tell,
    double Trigger, double Fuse, double Rearm, bool Robots) : IContentRow;

/// <summary>
/// furniture.json (plan_tf2_rpg 2l, R24; plan_descent_logic RU11): the markers and their
/// per-room limits, the shrine table and its cap on bad shrines, the Dispenser, containers,
/// explosives and traps.
/// </summary>
public sealed record FurnitureSettings(
    IReadOnlyList<MarkerRule> Markers,
    double BadShrineCapPct,
    IReadOnlyList<ShrineEffect> Shrines,
    DispenserSettings Dispenser,
    ContainerSettings Containers,
    IReadOnlyList<ExplosiveRow> Explosives,
    IReadOnlyList<TrapRow> Traps,
    BreakableSettings Breakables)
{
    public ShrineEffect Shrine(string id) =>
        Shrines.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no shrine effect '{id}'");

    public ExplosiveRow Explosive(string id) =>
        Explosives.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no explosive '{id}'");

    public TrapRow Trap(string id) =>
        Traps.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no trap '{id}'");

    /// <summary>Whether a hit breaks a container of <paramref name="kind"/> open.</summary>
    public bool Breaks(string kind) => Containers.Kinds.FirstOrDefault(k => k.Id == kind)?.Breaks ?? false;
}

public sealed class FurnitureTable : SingleTable<FurnitureSettings>
{
    public override string Name => ContentTables.Furniture;

    protected override FurnitureSettings Read(JsonFields f)
    {
        var markers = f.Array("markers").Select(e =>
        {
            var m = new JsonFields(e, f.Table, "markers", f.Errors);
            var r = new MarkerRule(m.Str("poi"), ParseEnum<FurnitureKind>(m, "kind"), m.Int("perRoom"), m.Num("fillPct"),
                m.StrOr("variant", null), m.StrOr("otherwise", null));
            m.End();
            return r;
        }).ToList();

        var shrines = f.Obj("shrines");
        double badCap = shrines.Num("badCapPct");
        var effects = shrines.Array("effects").Select(e =>
        {
            var s = new JsonFields(e, f.Table, "shrines", f.Errors);
            var r = new ShrineEffect(s.Str("id"), ParseEnum<ShrineNature>(s, "nature"), s.Num("weight"), s.Str("name"), s.Str("message"),
                s.Bool("partyWide"), ParseEnum<ShrineLasts>(s, "lasts"), StatAmount.List(s, "stats"), s.Bool("refill"), s.Bool("revealMap"),
                s.Int("spellRanks"));
            s.End();
            return r;
        }).ToList();
        shrines.End();

        var d = f.Obj("dispenser");
        var dispenser = new DispenserSettings(d.Num("charge"), d.Num("healPerSecond"), d.Num("ammoPerSecond"), d.Num("chargePerHealth"), d.Num("chargePerAmmo"),
            d.Num("radius"));
        d.End();

        var c = f.Obj("containers");
        var containers = new ContainerSettings(c.Str("lootTable"), c.Num("lockedPct"), c.Num("trappedPct"),
            [.. c.Array("kinds").Select(e =>
            {
                var k = new JsonFields(e, f.Table, "containers", f.Errors);
                var r = new ContainerKind(k.Str("id"), k.Num("weight"), k.Bool("breaks"));
                k.End();
                return r;
            })]);
        c.End();

        var explosives = f.Array("explosives").Select(e =>
        {
            var x = new JsonFields(e, f.Table, "explosives", f.Errors);
            var r = new ExplosiveRow(x.Str("id"), x.Num("weight"), x.Str("damageType"), x.Num("damage"), x.Num("radius"), x.Num("edgePct"), x.Num("chainRadius"),
                x.Num("health"), x.Num("chainDelay"));
            x.End();
            return r;
        }).ToList();

        var traps = f.Array("traps").Select(e =>
        {
            var t = new JsonFields(e, f.Table, "traps", f.Errors);
            var tell = t.Obj("tell");
            var r = new TrapRow(t.Str("id"), t.Num("weight"), t.Str("damageType"), t.Num("damage"), t.Num("radius"),
                new TrapTell(tell.Str("kind"), tell.Str("token")), t.Num("trigger"), t.Num("fuse"), t.Num("rearm"), t.Bool("robots"));
            tell.End();
            t.End();
            return r;
        }).ToList();

        return new FurnitureSettings(markers, badCap, effects, dispenser, containers, explosives, traps, ReadBreakables(f.Obj("breakables")));
    }

    private static BreakableSettings ReadBreakables(JsonFields b)
    {
        var o = b.Obj("outcomes");
        var outcomes = ImmutableDictionary.CreateBuilder<BreakOutcome, double>();
        foreach (var outcome in System.Enum.GetValues<BreakOutcome>())
            outcomes[outcome] = o.Num(outcome.ToString().ToLowerInvariant());
        o.End();
        var c = b.Obj("cloud");
        var cloud = new BreakCloud(c.Str("condition"), c.Num("seconds"), c.Num("radius"));
        c.End();
        var a = b.Obj("ambush");
        var ambush = new BreakAmbush(a.Int("min"), a.Int("max"), a.Num("huntSeconds"));
        a.End();
        var settings = new BreakableSettings(b.Strings("kinds", required: true), outcomes.ToImmutable(), b.Num("badCapPct"), b.Str("lootTable"),
            b.Num("australiumScale"), b.Str("blast"), cloud, ambush);
        b.End();
        return settings;
    }

    /// <summary>A field naming one of an enum's values, in lower snake case ("party_wide" style names are not used: one word each).</summary>
    private static T ParseEnum<T>(JsonFields f, string field) where T : struct, System.Enum
    {
        string s = f.Str(field);
        foreach (var v in System.Enum.GetValues<T>())
            if (string.Equals(v.ToString(), s, StringComparison.OrdinalIgnoreCase))
                return v;
        f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'{field}' is '{s}', not one of {string.Join(", ", System.Enum.GetNames<T>()).ToLowerInvariant()}");
        return default;
    }

    protected override void Validate(DescentContent content, FurnitureSettings s, ContentErrors errors)
    {
        string t = Name;
        var pois = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in s.Markers)
        {
            if (!pois.Add(m.Poi)) errors.Add(t, m.Poi, ContentRule.DuplicateId, $"marker '{m.Poi}' appears more than once");
            ContentChecks.Range(errors, t, m.Poi, "perRoom", m.PerRoom, 0, 64);
            ContentChecks.Range(errors, t, m.Poi, "fillPct", m.FillPct, 0, 100);
        }

        Unique(errors, "shrines", s.Shrines.Select(x => x.Id));
        foreach (var e in s.Shrines)
        {
            ContentChecks.Range(errors, t, e.Id, "weight", e.Weight, 0, 1e6);
            ContentChecks.Range(errors, t, e.Id, "spellRanks", e.SpellRanks, 0, 10);
            foreach (var a in e.Stats)
                ContentChecks.Stat(content, errors, t, e.Id, a.Stat, a.Amount);
            if (e.Lasts == ShrineLasts.Instant && (e.Stats.Count > 0 || e.SpellRanks > 0))
                errors.Add(t, e.Id, ContentRule.OutOfRange, "an instant shrine grants no stats and no spell ranks: they would never expire");
            if (e.Nature == ShrineNature.Trade && !(e.Stats.Any(a => a.Amount > 0) && e.Stats.Any(a => a.Amount < 0)))
                errors.Add(t, e.Id, ContentRule.OutOfRange, "a trade shrine gives one stat and takes another");
            if (e.Nature == ShrineNature.Bad && e.Stats.Any(a => a.Amount > 0))
                errors.Add(t, e.Id, ContentRule.OutOfRange, "a bad shrine gives nothing");
        }
        if (ContentChecks.Range(errors, t, "shrines", "badCapPct", s.BadShrineCapPct, 0, 100))
        {
            double total = s.Shrines.Sum(x => x.Weight);
            double bad = s.Shrines.Where(x => x.Nature == ShrineNature.Bad).Sum(x => x.Weight);
            if (!(total > 0))
                errors.Add(t, "shrines", ContentRule.OutOfRange, "the shrine effects have no weight");
            else
                ContentChecks.Range(errors, t, "shrines", "bad shrines' share of the weight (percent)", 100 * bad / total, 0, s.BadShrineCapPct);
        }

        var d = s.Dispenser;
        ContentChecks.Range(errors, t, "dispenser", "charge", d.Charge, 0, 1e6);
        ContentChecks.Range(errors, t, "dispenser", "healPerSecond", d.HealPerSecond, 0, 1e4);
        ContentChecks.Range(errors, t, "dispenser", "ammoPerSecond", d.AmmoPerSecond, 0, 1e4);
        ContentChecks.Range(errors, t, "dispenser", "chargePerHealth", d.ChargePerHealth, 1e-6, 1e4);
        ContentChecks.Range(errors, t, "dispenser", "chargePerAmmo", d.ChargePerAmmo, 1e-6, 1e4);
        ContentChecks.Range(errors, t, "dispenser", "radius", d.Radius, 1, 1024);

        ContentChecks.Range(errors, t, "containers", "lockedPct", s.Containers.LockedPct, 0, 100);
        ContentChecks.Range(errors, t, "containers", "trappedPct", s.Containers.TrappedPct, 0, 100);
        Unique(errors, "containers", s.Containers.Kinds.Select(k => k.Id));
        foreach (var k in s.Containers.Kinds) ContentChecks.Range(errors, t, k.Id, "weight", k.Weight, 0, 1e6);
        Weighted(errors, "containers", s.Containers.Kinds.Select(k => k.Weight));

        Unique(errors, "explosives", s.Explosives.Select(x => x.Id));
        foreach (var x in s.Explosives)
        {
            ContentChecks.Range(errors, t, x.Id, "weight", x.Weight, 0, 1e6);
            DamageType(content, errors, x.Id, x.DamageType);
            ContentChecks.Range(errors, t, x.Id, "damage", x.Damage, 0, 1e5);
            ContentChecks.Range(errors, t, x.Id, "radius", x.Radius, 1, 4096);
            ContentChecks.Range(errors, t, x.Id, "edgePct", x.EdgePct, 0, 100);
            ContentChecks.Range(errors, t, x.Id, "chainRadius", x.ChainRadius, 0, 4096);
            ContentChecks.Range(errors, t, x.Id, "health", x.Health, 1, 1e5);
            ContentChecks.Range(errors, t, x.Id, "chainDelay", x.ChainDelay, 0, 10);
            // The chain is the only way one explosive sets off another: one its blast reaches
            // must be one its chain reaches, or the blast's damage would set it off by another rule.
            if (x.ChainRadius < x.Radius)
                errors.Add(t, x.Id, ContentRule.OutOfRange, $"chainRadius {x.ChainRadius} is less than radius {x.Radius}: a blast would reach an explosive its chain does not");
        }
        Weighted(errors, "explosives", s.Explosives.Select(x => x.Weight));

        Unique(errors, "traps", s.Traps.Select(x => x.Id));
        foreach (var x in s.Traps)
        {
            ContentChecks.Range(errors, t, x.Id, "weight", x.Weight, 0, 1e6);
            DamageType(content, errors, x.Id, x.DamageType);
            ContentChecks.Range(errors, t, x.Id, "damage", x.Damage, 0, 1e5);
            ContentChecks.Range(errors, t, x.Id, "radius", x.Radius, 0, 4096);
            ContentChecks.Range(errors, t, x.Id, "trigger", x.Trigger, 1, 1024);
            ContentChecks.Range(errors, t, x.Id, "fuse", x.Fuse, 0, 10);
            ContentChecks.Range(errors, t, x.Id, "rearm", x.Rearm, 0, 3600);
            if (x.Tell.Kind is not ("sound" or "visual"))
                errors.Add(t, x.Id, ContentRule.OutOfRange, $"a tell is a sound or a visual, not '{x.Tell.Kind}'");
            if (string.IsNullOrEmpty(x.Tell.Token))
                errors.Add(t, x.Id, ContentRule.Malformed, "a trap has a tell");
        }
        Weighted(errors, "traps", s.Traps.Select(x => x.Weight));

        ValidateMarkers(errors, s);
        ValidateBreakables(content, errors, s);
    }

    /// <summary>A breakable marker names a breakable kind; another marker's otherwise names one too; a variant belongs only to a breakable marker.</summary>
    private void ValidateMarkers(ContentErrors errors, FurnitureSettings s)
    {
        string t = Name;
        var kinds = s.Breakables.Kinds.ToHashSet(StringComparer.Ordinal);
        foreach (var m in s.Markers)
        {
            if (m.Kind == FurnitureKind.Breakable)
            {
                if (m.Variant is not { } v || !kinds.Contains(v))
                    errors.Add(t, m.Poi, ContentRule.DanglingReference, $"a breakable marker names one of the breakables' kinds ({string.Join(", ", kinds)}), not '{m.Variant}'");
                if (m.Otherwise is not null)
                    errors.Add(t, m.Poi, ContentRule.OutOfRange, "a breakable marker has no otherwise: it is one");
            }
            else if (m.Variant is not null)
            {
                errors.Add(t, m.Poi, ContentRule.OutOfRange, $"only a breakable marker names a variant, not a {m.Kind.ToString().ToLowerInvariant()} marker");
            }

            if (m.Otherwise is { } o && !kinds.Contains(o))
                errors.Add(t, m.Poi, ContentRule.DanglingReference, $"otherwise '{o}' is not one of the breakables' kinds ({string.Join(", ", kinds)})");
        }
    }

    private void ValidateBreakables(DescentContent content, ContentErrors errors, FurnitureSettings s)
    {
        string t = Name;
        var b = s.Breakables;
        if (b.Kinds.Count == 0)
            errors.Add(t, "breakables", ContentRule.OutOfRange, "the breakables name at least one kind");
        Unique(errors, "breakables.kinds", b.Kinds);
        foreach (var (outcome, weight) in b.Outcomes)
            ContentChecks.Range(errors, t, "breakables", "outcomes." + outcome.ToString().ToLowerInvariant(), weight, 0, 1e6);
        if (ContentChecks.Range(errors, t, "breakables", "badCapPct", b.BadCapPct, 0, 100))
        {
            double total = b.Outcomes.Values.Sum();
            double bad = b.Outcomes.Where(p => BreakableSettings.Bad(p.Key)).Sum(p => p.Value);
            if (!(total > 0))
                errors.Add(t, "breakables", ContentRule.OutOfRange, "the breakables' outcomes have no weight");
            else
                ContentChecks.Range(errors, t, "breakables", "bad outcomes' share of the weight (percent)", 100 * bad / total, 0, b.BadCapPct);
        }

        if (content.TryGet<LootSettings>(LootTable.TableName, out var loot) && loot.Tables.All(x => x.Id != b.LootTable))
            errors.Add(t, "breakables", ContentRule.DanglingReference, $"lootTable '{b.LootTable}' is not a loot table");
        ContentChecks.Range(errors, t, "breakables", "australiumScale", b.AustraliumScale, 0, 100);
        if (s.Explosives.All(x => x.Id != b.Blast))
            errors.Add(t, "breakables", ContentRule.DanglingReference, $"blast '{b.Blast}' is not an explosive");
        ContentChecks.Reference<Robots.RobotConditionRow>(content, errors, t, "breakables", "cloud.condition", b.Cloud.Condition, ContentTables.RobotConditions);
        ContentChecks.Range(errors, t, "breakables", "cloud.seconds", b.Cloud.Seconds, 0, 60);
        ContentChecks.Range(errors, t, "breakables", "cloud.radius", b.Cloud.Radius, 1, 1024);
        ContentChecks.Range(errors, t, "breakables", "ambush.min", b.Ambush.Min, 1, 8);
        ContentChecks.Range(errors, t, "breakables", "ambush.max", b.Ambush.Max, b.Ambush.Min, 8);
        ContentChecks.Range(errors, t, "breakables", "ambush.huntSeconds", b.Ambush.HuntSeconds, 0, 600);
    }

    private void DamageType(DescentContent content, ContentErrors errors, string row, string type) =>
        ContentChecks.Reference<DamageTypeRow>(content, errors, Name, row, "damageType", type, ContentTables.DamageTypes);

    private void Unique(ContentErrors errors, string field, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id))
                errors.Add(Name, id, ContentRule.DuplicateId, $"'{field}' names '{id}' more than once");
    }

    private void Weighted(ContentErrors errors, string field, IEnumerable<double> weights)
    {
        if (!(weights.Sum() > 0))
            errors.Add(Name, field, ContentRule.OutOfRange, $"'{field}' has nothing to pick: no weight above 0");
    }
}
