using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Furniture;

/// <summary>What a furniture marker can hold.</summary>
public enum FurnitureKind
{
    Shrine,
    Container,
    Dispenser,
    Explosive,
    Trap,
}

/// <summary>
/// A marker kind a room declares (its <c>poi_type</c>, src/mapgen/Descent/Markers.cs), what
/// it holds, how many of that kind one room may furnish, and the chance each chosen marker
/// is furnished at all.
/// </summary>
public sealed record MarkerRule(string Poi, FurnitureKind Kind, int PerRoom, double FillPct);

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

/// <summary>A Dispenser's finite charge and its rates; health and ammo each cost charge by the point.</summary>
public sealed record DispenserSettings(double Charge, double HealPerSecond, double AmmoPerSecond, double ChargePerHealth, double ChargePerAmmo);

public sealed record ContainerKind(string Id, double Weight);

/// <summary>Containers: the loot table they roll from (the loot package's id), the share locked and trapped, and their kinds.</summary>
public sealed record ContainerSettings(string LootTable, double LockedPct, double TrappedPct, IReadOnlyList<ContainerKind> Kinds);

/// <summary>
/// A barrel or a pumpkin bomb: full damage at its centre falling linearly to
/// <see cref="EdgePct"/> of it at <see cref="Radius"/>, nothing beyond; any other explosive
/// within <see cref="ChainRadius"/> goes off too.
/// </summary>
public sealed record ExplosiveRow(string Id, double Weight, string DamageType, double Damage, double Radius, double EdgePct, double ChainRadius) : IContentRow;

/// <summary>A trap's tell: something a careful player sees or hears before it fires (2l).</summary>
public sealed record TrapTell(string Kind, string Token);

/// <summary>A trap: its damage type and damage, its radius (0: only what trips it), and its tell.</summary>
public sealed record TrapRow(string Id, double Weight, string DamageType, double Damage, double Radius, TrapTell Tell) : IContentRow;

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
    IReadOnlyList<TrapRow> Traps)
{
    public ShrineEffect Shrine(string id) =>
        Shrines.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no shrine effect '{id}'");

    public ExplosiveRow Explosive(string id) =>
        Explosives.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no explosive '{id}'");

    public TrapRow Trap(string id) =>
        Traps.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no trap '{id}'");
}

public sealed class FurnitureTable : SingleTable<FurnitureSettings>
{
    public override string Name => ContentTables.Furniture;

    protected override FurnitureSettings Read(JsonFields f)
    {
        var markers = f.Array("markers").Select(e =>
        {
            var m = new JsonFields(e, f.Table, "markers", f.Errors);
            var r = new MarkerRule(m.Str("poi"), ParseEnum<FurnitureKind>(m, "kind"), m.Int("perRoom"), m.Num("fillPct"));
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
        var dispenser = new DispenserSettings(d.Num("charge"), d.Num("healPerSecond"), d.Num("ammoPerSecond"), d.Num("chargePerHealth"), d.Num("chargePerAmmo"));
        d.End();

        var c = f.Obj("containers");
        var containers = new ContainerSettings(c.Str("lootTable"), c.Num("lockedPct"), c.Num("trappedPct"),
            [.. c.Array("kinds").Select(e =>
            {
                var k = new JsonFields(e, f.Table, "containers", f.Errors);
                var r = new ContainerKind(k.Str("id"), k.Num("weight"));
                k.End();
                return r;
            })]);
        c.End();

        var explosives = f.Array("explosives").Select(e =>
        {
            var x = new JsonFields(e, f.Table, "explosives", f.Errors);
            var r = new ExplosiveRow(x.Str("id"), x.Num("weight"), x.Str("damageType"), x.Num("damage"), x.Num("radius"), x.Num("edgePct"), x.Num("chainRadius"));
            x.End();
            return r;
        }).ToList();

        var traps = f.Array("traps").Select(e =>
        {
            var t = new JsonFields(e, f.Table, "traps", f.Errors);
            var tell = t.Obj("tell");
            var r = new TrapRow(t.Str("id"), t.Num("weight"), t.Str("damageType"), t.Num("damage"), t.Num("radius"),
                new TrapTell(tell.Str("kind"), tell.Str("token")));
            tell.End();
            t.End();
            return r;
        }).ToList();

        return new FurnitureSettings(markers, badCap, effects, dispenser, containers, explosives, traps);
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
        }
        Weighted(errors, "explosives", s.Explosives.Select(x => x.Weight));

        Unique(errors, "traps", s.Traps.Select(x => x.Id));
        foreach (var x in s.Traps)
        {
            ContentChecks.Range(errors, t, x.Id, "weight", x.Weight, 0, 1e6);
            DamageType(content, errors, x.Id, x.DamageType);
            ContentChecks.Range(errors, t, x.Id, "damage", x.Damage, 0, 1e5);
            ContentChecks.Range(errors, t, x.Id, "radius", x.Radius, 0, 4096);
            if (x.Tell.Kind is not ("sound" or "visual"))
                errors.Add(t, x.Id, ContentRule.OutOfRange, $"a tell is a sound or a visual, not '{x.Tell.Kind}'");
            if (string.IsNullOrEmpty(x.Tell.Token))
                errors.Add(t, x.Id, ContentRule.Malformed, "a trap has a tell");
        }
        Weighted(errors, "traps", s.Traps.Select(x => x.Weight));
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
