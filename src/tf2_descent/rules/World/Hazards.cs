using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.World;

/// <summary>
/// One kind of hazard (hazards.json; plan_descent_mine_levels D8): its id (a level names a
/// volume of it <c>tf2_descent_hazard_&lt;id&gt;</c>), its display name (the kill feed's), the
/// damage type a hero resists it as (a damage_types.json row, or <see cref="HazardRules.Unresisted"/>),
/// its damage per second at depth 1 and what each depth below adds, and whether it hurts robots.
/// </summary>
public sealed record HazardRow(string Id, string Name, string DamageType, double PerSecond, double PerDepth, bool Robots) : IContentRow;

/// <summary>hazards.json: how often a hazard bites (<see cref="Cadence"/> seconds), and its kinds.</summary>
public sealed record HazardSettings(double Cadence, IReadOnlyList<HazardRow> Kinds)
{
    /// <summary>The kind named <paramref name="id"/>, or null.</summary>
    public HazardRow? Kind(string id) => Kinds.FirstOrDefault(k => k.Id == id);
}

/// <summary>
/// The hazards table. Load errors: a cadence or a damage out of range, a kind named twice, an
/// id that is not one lower-case word, a damage type that is no damage type, and a kind that
/// hurts robots with no damage type (a robot resists every hit as one of the four).
/// </summary>
public sealed class HazardTable : SingleTable<HazardSettings>
{
    public const string TableName = "hazards";

    public override string Name => TableName;

    protected override HazardSettings Read(JsonFields f)
    {
        var kinds = new List<HazardRow>();
        foreach (var e in f.Array("kinds"))
        {
            var k = new JsonFields(e, f.Table, f.Row, f.Errors);
            kinds.Add(new HazardRow(k.Str("id"), k.Str("name"), k.Str("damageType"), k.Num("perSecond"), k.Num("perDepth"), k.Bool("robots")));
            k.End();
        }
        return new HazardSettings(f.Num("cadence"), kinds);
    }

    protected override void Validate(DescentContent content, HazardSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "cadence", s.Cadence, 0.05, 10);
        // A hero's networked state carries the kind he stands in, plus one, in 4 bits.
        ContentChecks.Range(errors, t, null, "kinds (count)", s.Kinds.Count, 1, 15);
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in s.Kinds)
        {
            if (!named.Add(k.Id))
                errors.Add(t, k.Id, ContentRule.DuplicateId, $"hazard '{k.Id}' is named twice");
            if (k.Id.Length == 0 || !k.Id.All(c => c is >= 'a' and <= 'z'))
                errors.Add(t, k.Id, ContentRule.OutOfRange, $"'id' '{k.Id}' is not one lower-case word: a level names its volumes tf2_descent_hazard_<id>");
            ContentChecks.Range(errors, t, k.Id, "perSecond", k.PerSecond, 0, 1000);
            ContentChecks.Range(errors, t, k.Id, "perDepth", k.PerDepth, 0, 1000);
            bool typed = k.DamageType != HazardRules.Unresisted;
            if (typed)
                ContentChecks.Reference<DamageTypeRow>(content, errors, t, k.Id, "damageType", k.DamageType, ContentTables.DamageTypes);
            if (k.Robots && !typed)
                errors.Add(t, k.Id, ContentRule.OutOfRange, $"hazard '{k.Id}' hurts robots but has no damage type: a robot resists every hit as one of the damage types");
        }
    }
}

/// <summary>
/// The hazards' rules (pure; plan_descent_mine_levels D8). A hazard bites a hero or robot standing
/// in it every <see cref="HazardSettings.Cadence"/> seconds: at once on stepping in, then each
/// cadence while he stays. A bite is the kind's damage per second at the depth times the cadence,
/// before the victim's resistance to its type. Stepping out and back in within a cadence of the
/// last bite waits for the cadence: stepping in and out earns no extra bites.
/// </summary>
public static class HazardRules
{
    /// <summary>The damage type of a hazard no resistance reduces (slime until D8 rules it).</summary>
    public const string Unresisted = "none";

    /// <summary>The prefix of a hazard volume's name in a level: this plus the kind's id.</summary>
    public const string VolumePrefix = "tf2_descent_hazard_";

    /// <summary>The language token of a kind's name: <c>#Descent_Hazard_&lt;id&gt;</c>.</summary>
    public static string Token(string id) => "#Descent_Hazard_" + id;

    /// <summary>The kind a volume's name names (<c>tf2_descent_hazard_lava</c> is lava), or null for a name that is no hazard volume.</summary>
    public static string? KindOfVolume(string name) =>
        name.StartsWith(VolumePrefix, StringComparison.Ordinal) && name.Length > VolumePrefix.Length ? name[VolumePrefix.Length..] : null;

    /// <summary>Damage per second of <paramref name="kind"/> at <paramref name="depth"/>: the first depth's, plus each depth below it. The town (0) and depth 1 are the same.</summary>
    public static double PerSecond(HazardRow kind, int depth) => kind.PerSecond + kind.PerDepth * Math.Max(0, depth - 1);

    /// <summary>One bite of <paramref name="kind"/> at <paramref name="depth"/>, before resistance.</summary>
    public static double Bite(HazardSettings s, HazardRow kind, int depth) => PerSecond(kind, depth) * s.Cadence;

    /// <summary>
    /// Whether a victim in a hazard is bitten at <paramref name="now"/>, his last bite at
    /// <paramref name="last"/> (null for none yet): when he has never been bitten, or a cadence has passed.
    /// </summary>
    public static bool Due(HazardSettings s, double? last, double now) => last is not { } l || now - l >= s.Cadence - 1e-9;

    /// <summary>A bite after the victim's resistance (0 to 1) to its type: an unresisted kind takes it whole.</summary>
    public static double AfterResist(double bite, double resist) => bite * (1 - resist);
}
