using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

/// <summary>How a weapon family's attack reaches its target: a swing, bullets, a thrown projectile or a cone of flame.</summary>
public enum RobotAttackKind
{
    Melee,
    Hitscan,
    Projectile,
    Flame,
}

/// <summary>
/// One weapon family's attack as a robot makes it (plan_descent_logic 3.8, HO7): what kind it
/// is, the time between two attacks, the windup from the ask to the hit (a melee swing's
/// smack), the share of the robot's damage one attack deals (<see cref="RobotAttackRules.AttackDamage"/>),
/// the clip and the time a whole reload takes (a clip of 0 never reloads), the pellets the
/// attack's damage is split over and their cone's half-angle, the reach of a bullet or a flame,
/// the half-width of a swing's box, the flame cone's half-angle, and the projectile it fires and
/// its speed. A melee attack's reach is the robot's own (<see cref="RobotStats.Reach"/>, which
/// follows scale).
/// </summary>
public sealed record RobotAttack(
    string Id,
    RobotAttackKind Kind,
    double IntervalSeconds,
    double WindupSeconds,
    double DamageShare,
    int Clip,
    double ReloadSeconds,
    int Pellets,
    double SpreadDegrees,
    double RangeUnits,
    double HullUnits,
    double ConeDegrees,
    string Projectile,
    double Speed);

/// <summary>A medic robot's heal (HO6): life a second on its patient, how far its beam reaches, and how long one ask keeps the beam on.</summary>
public sealed record RobotHealSettings(double PerSecond, double Range, double HoldSeconds);

/// <summary>
/// robot_attacks.json (plan_descent_logic 3.8, section 2; HO6, HO7): every robot attack's
/// numbers. <see cref="ProjectileCap"/> robot projectiles may be in flight at once (the edict
/// budget's 32); one attack row a weapon family a robot template holds.
/// </summary>
public sealed record RobotAttackSettings(int ProjectileCap, RobotHealSettings Heal, IReadOnlyList<RobotAttack> Attacks)
{
    public RobotAttack? Of(string family)
    {
        foreach (var a in Attacks)
            if (a.Id == family)
                return a;
        return null;
    }
}

public sealed class RobotAttackTable : SingleTable<RobotAttackSettings>
{
    public const string TableName = "robot_attacks";

    /// <summary>The edict budget's robot projectiles (plan_descent_logic section 2).</summary>
    public const int BudgetProjectiles = 32;

    public override string Name => TableName;

    protected override RobotAttackSettings Read(JsonFields f)
    {
        var h = f.Obj("heal");
        var heal = new RobotHealSettings(h.Num("perSecond"), h.Num("range"), h.Num("holdSeconds"));
        h.End();
        var attacks = f.Array("attacks").Select(e =>
        {
            var a = new JsonFields(e, f.Table, "attacks", f.Errors);
            string kind = a.Str("kind");
            var parsed = Enum.GetValues<RobotAttackKind>().FirstOrDefault(k => string.Equals(k.ToString(), kind, StringComparison.OrdinalIgnoreCase), (RobotAttackKind)(-1));
            if ((int)parsed < 0)
                f.Errors.Add(f.Table, "attacks", ContentRule.Malformed, $"'kind' is '{kind}', not melee, hitscan, projectile or flame");
            var r = new RobotAttack(a.Str("id"), parsed, a.Num("intervalSeconds"), a.Num("windupSeconds"), a.Num("damageShare"), a.Int("clip"),
                a.Num("reloadSeconds"), a.Int("pellets"), a.Num("spreadDegrees"), a.Num("rangeUnits"), a.Num("hullUnits"), a.Num("coneDegrees"),
                a.Str("projectile"), a.Num("speed"));
            a.End();
            return r;
        }).ToList();
        return new RobotAttackSettings(f.Int("projectileCap"), heal, attacks);
    }

    /// <summary>
    /// The cap fits the edict budget; every family a robot template holds has exactly one row;
    /// each row names a family; its numbers are in range, and each kind has what it needs: a
    /// projectile names its class and a speed, a hitscan a reach and at least a pellet, a flame
    /// a reach and a cone, a melee swing a box.
    /// </summary>
    protected override void Validate(DescentContent content, RobotAttackSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "projectileCap", s.ProjectileCap, 1, BudgetProjectiles);
        ContentChecks.Range(errors, t, "heal", "perSecond", s.Heal.PerSecond, 0, 10000);
        ContentChecks.Range(errors, t, "heal", "range", s.Heal.Range, 1, 4096);
        ContentChecks.Range(errors, t, "heal", "holdSeconds", s.Heal.HoldSeconds, 0.01, 10);
        RobotJson.Unique(errors, t, "attacks", s.Attacks.Select(a => a.Id));

        foreach (var a in s.Attacks)
        {
            ContentChecks.Reference<FamilyRow>(content, errors, t, a.Id, "id", a.Id, ContentTables.Families);
            ContentChecks.Range(errors, t, a.Id, "intervalSeconds", a.IntervalSeconds, 0.01, 60);
            ContentChecks.Range(errors, t, a.Id, "windupSeconds", a.WindupSeconds, 0, 10);
            ContentChecks.Range(errors, t, a.Id, "damageShare", a.DamageShare, 0, 100);
            ContentChecks.Range(errors, t, a.Id, "clip", a.Clip, 0, 1000);
            ContentChecks.Range(errors, t, a.Id, "reloadSeconds", a.ReloadSeconds, a.Clip > 0 ? 0.01 : 0, 60);
            ContentChecks.Range(errors, t, a.Id, "pellets", a.Pellets, 1, 64);
            ContentChecks.Range(errors, t, a.Id, "spreadDegrees", a.SpreadDegrees, 0, 45);
            ContentChecks.Range(errors, t, a.Id, "hullUnits", a.HullUnits, 0, 256);
            switch (a.Kind)
            {
                case RobotAttackKind.Projectile:
                    if (string.IsNullOrEmpty(a.Projectile))
                        errors.Add(t, a.Id, ContentRule.Malformed, "a projectile attack names its projectile");
                    ContentChecks.Range(errors, t, a.Id, "speed", a.Speed, 1, 10000);
                    break;
                case RobotAttackKind.Hitscan:
                    ContentChecks.Range(errors, t, a.Id, "rangeUnits", a.RangeUnits, 1, 16384);
                    break;
                case RobotAttackKind.Flame:
                    ContentChecks.Range(errors, t, a.Id, "rangeUnits", a.RangeUnits, 1, 4096);
                    ContentChecks.Range(errors, t, a.Id, "coneDegrees", a.ConeDegrees, 1, 90);
                    break;
                case RobotAttackKind.Melee:
                    ContentChecks.Range(errors, t, a.Id, "hullUnits", a.HullUnits, 1, 256);
                    break;
            }
        }

        // A template's family that is no family at all is robots.json's error, said once there.
        if (!content.TryGet<Table<RobotTemplate>>(ContentTables.Robots, out var robots)
            || !content.TryGet<Table<FamilyRow>>(ContentTables.Families, out var families))
            return;
        foreach (var family in robots.Rows.Select(r => r.Family).Distinct(StringComparer.Ordinal))
            if (families.TryGet(family, out _) && s.Of(family) is null)
                errors.Add(t, family, ContentRule.DanglingReference, $"robots hold family '{family}', which has no attack row");
    }
}

/// <summary>
/// A robot's attack in numbers (plan_descent_logic 3.8; HO6, HO7), pure over robot_attacks.json
/// and robot_rules.json: the server carries these out and nothing else decides them.
/// </summary>
public sealed class RobotAttackRules
{
    private readonly DescentContent _content;

    public RobotAttackRules(DescentContent content) => _content = content;

    public RobotAttackSettings Settings => _content.RobotAttacks;

    /// <summary>Robot projectiles in flight at once, at most.</summary>
    public int ProjectileCap => Settings.ProjectileCap;

    /// <summary>The attack of weapon family <paramref name="family"/>; throws for a family with no row (the loader refuses a template's).</summary>
    public RobotAttack Of(string family) =>
        Settings.Of(family) ?? throw new KeyNotFoundException($"no robot attack for family '{family}'");

    /// <summary>What one attack deals: the robot's damage (its template's, by level and affixes) times the attack's share.</summary>
    public static double AttackDamage(RobotStats robot, RobotAttack attack) => robot.Damage * attack.DamageShare;

    /// <summary>What one pellet of an attack deals: the attack's damage split evenly over its pellets.</summary>
    public static double PelletDamage(RobotStats robot, RobotAttack attack) => AttackDamage(robot, attack) / Math.Max(1, attack.Pellets);

    /// <summary>The damage type a family's attack is resisted as (families.json).</summary>
    public string DamageTypeOf(RobotAttack attack) => _content.Families[attack.Id].DamageType;

    /// <summary>How far a robot's melee swing reaches: its own reach, which follows its scale.</summary>
    public static double MeleeReach(RobotStats robot) => robot.Reach;

    /// <summary>Life a second a medic of monster level <paramref name="level"/> heals: the heal's number by the level curve's damage rise, so a heal keeps pace with the damage it answers.</summary>
    public double HealPerSecond(int level) =>
        Settings.Heal.PerSecond * Math.Pow(1 + _content.RobotRules.Level.DamagePerLevelPct / 100, Math.Max(0, level - 1));

    /// <summary><paramref name="seconds"/> as whole server ticks at <paramref name="ticksPerSecond"/>, rounded, at least <paramref name="least"/>.</summary>
    public static long Ticks(double seconds, double ticksPerSecond, long least = 0) =>
        Math.Max(least, (long)Math.Round(seconds * ticksPerSecond, MidpointRounding.AwayFromZero));
}
