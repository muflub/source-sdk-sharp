using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

/// <summary>How a weapon family's attack reaches its target: a swing, bullets, a thrown projectile or a cone of flame.</summary>
public enum RobotAttackKind
{
    Melee,
    Hitscan,
    Projectile,
    Flame,

    /// <summary>A blast round the robot itself after its windup (the sentry buster's): every hero in its radius with a clear line, then the robot.</summary>
    Blast,
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
///
/// <para>
/// HO12's columns, each optional (0 or empty when a row does not use it): the damage type of a
/// row that is no weapon family (<see cref="DamageType"/>; a family's row takes its family's),
/// the minigun's spin before its first shot (<see cref="SpinUpSeconds"/>), how fast a brain may
/// turn the gun (<see cref="TurnDegreesPerSecond"/>, 0 for as fast as the body turns), the pull on
/// a lobbed projectile (<see cref="Gravity"/>, units a second squared), the knife's multiplier and
/// cone behind its victim (<see cref="BackstabMultiplier"/>, <see cref="BackstabConeDegrees"/>,
/// the half-angle about the victim's back), a blast's radius and how near its target a buster
/// starts it (<see cref="BlastRadius"/>, <see cref="TriggerUnits"/>), the seconds a flame sets a
/// hero alight for (<see cref="AfterburnSeconds"/>), and what a windup shows the heroes
/// (<see cref="Telegraph"/>: <c>laser</c>, the sniper's beam; <c>spin</c>, the buster's spin).
/// </para>
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
    double Speed,
    string DamageType = "",
    double SpinUpSeconds = 0,
    double TurnDegreesPerSecond = 0,
    double Gravity = 0,
    double BackstabMultiplier = 0,
    double BackstabConeDegrees = 0,
    double BlastRadius = 0,
    double TriggerUnits = 0,
    double AfterburnSeconds = 0,
    string Telegraph = "")
{
    /// <summary>The telegraphs a windup may show.</summary>
    public const string LaserTelegraph = "laser", SpinTelegraph = "spin";
}

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
                f.Errors.Add(f.Table, "attacks", ContentRule.Malformed, $"'kind' is '{kind}', not melee, hitscan, projectile, flame or blast");
            var r = new RobotAttack(a.Str("id"), parsed, a.Num("intervalSeconds"), a.Num("windupSeconds"), a.Num("damageShare"), a.Int("clip"),
                a.Num("reloadSeconds"), a.Int("pellets"), a.Num("spreadDegrees"), a.Num("rangeUnits"), a.Num("hullUnits"), a.Num("coneDegrees"),
                a.Str("projectile"), a.Num("speed"), a.StrOr("damageType", "") ?? "", a.NumOr("spinUpSeconds", 0), a.NumOr("turnDegreesPerSecond", 0),
                a.NumOr("gravity", 0), a.NumOr("backstabMultiplier", 0), a.NumOr("backstabConeDegrees", 0), a.NumOr("blastRadius", 0),
                a.NumOr("triggerUnits", 0), a.NumOr("afterburnSeconds", 0), a.StrOr("telegraph", "") ?? "");
            a.End();
            return r;
        }).ToList();
        return new RobotAttackSettings(f.Int("projectileCap"), heal, attacks);
    }

    /// <summary>
    /// The cap fits the edict budget; every attack a robot template holds (its family's, or the
    /// row its <c>attack</c> names) has exactly one row; each row names a family, or names its own
    /// damage type; its numbers are in range, and each kind has what it needs: a projectile names
    /// its class and a speed, a hitscan a reach and at least a pellet, a flame a reach and a cone,
    /// a melee swing a box, a blast a radius; a telegraph is one the hands know, on a windup.
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
            if (a.DamageType.Length > 0)
                ContentChecks.Reference<DamageTypeRow>(content, errors, t, a.Id, "damageType", a.DamageType, ContentTables.DamageTypes);
            else
                ContentChecks.Reference<FamilyRow>(content, errors, t, a.Id, "id", a.Id, ContentTables.Families);
            ContentChecks.Range(errors, t, a.Id, "spinUpSeconds", a.SpinUpSeconds, 0, 10);
            ContentChecks.Range(errors, t, a.Id, "turnDegreesPerSecond", a.TurnDegreesPerSecond, 0, 3600);
            ContentChecks.Range(errors, t, a.Id, "gravity", a.Gravity, 0, 10000);
            ContentChecks.Range(errors, t, a.Id, "backstabMultiplier", a.BackstabMultiplier, 0, 100);
            ContentChecks.Range(errors, t, a.Id, "backstabConeDegrees", a.BackstabConeDegrees, 0, 180);
            ContentChecks.Range(errors, t, a.Id, "triggerUnits", a.TriggerUnits, 0, 4096);
            ContentChecks.Range(errors, t, a.Id, "afterburnSeconds", a.AfterburnSeconds, 0, 60);
            if (a.BackstabMultiplier > 0 && a.Kind != RobotAttackKind.Melee)
                errors.Add(t, a.Id, ContentRule.Malformed, "only a melee attack backstabs");
            if (a.BackstabMultiplier > 0 && !(a.BackstabConeDegrees > 0))
                errors.Add(t, a.Id, ContentRule.OutOfRange, "a backstab names the cone behind its victim it lands in");
            if (a.Gravity > 0 && a.Kind != RobotAttackKind.Projectile)
                errors.Add(t, a.Id, ContentRule.Malformed, "only a projectile is lobbed under gravity");
            if (a.Telegraph is not ("" or RobotAttack.LaserTelegraph or RobotAttack.SpinTelegraph))
                errors.Add(t, a.Id, ContentRule.Malformed, $"'telegraph' is '{a.Telegraph}', not laser or spin");
            else if (a.Telegraph.Length > 0 && !(a.WindupSeconds > 0))
                errors.Add(t, a.Id, ContentRule.OutOfRange, "a telegraph is shown over a windup: the row has none");
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
                case RobotAttackKind.Blast:
                    ContentChecks.Range(errors, t, a.Id, "blastRadius", a.BlastRadius, 1, 4096);
                    break;
            }
        }

        // A template's family that is no family at all is robots.json's error, said once there.
        if (!content.TryGet<Table<RobotTemplate>>(ContentTables.Robots, out var robots)
            || !content.TryGet<Table<FamilyRow>>(ContentTables.Families, out var families))
            return;
        var told = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in robots.Rows)
        {
            if (template.AttackRow is { } named)
            {
                if (s.Of(named) is null && told.Add(named))
                    errors.Add(t, named, ContentRule.DanglingReference, $"robot '{template.Id}' names attack '{named}', which has no attack row");
            }
            else if (families.TryGet(template.Family, out _) && s.Of(template.Family) is null && told.Add(template.Family))
            {
                errors.Add(t, template.Family, ContentRule.DanglingReference, $"robots hold family '{template.Family}', which has no attack row");
            }
        }
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

    /// <summary>The damage type an attack is resisted as: its own, else its family's (families.json).</summary>
    public string DamageTypeOf(RobotAttack attack) => attack.DamageType.Length > 0 ? attack.DamageType : _content.Families[attack.Id].DamageType;

    /// <summary>The attack robots of <paramref name="template"/> make: the row it names, else its family's.</summary>
    public RobotAttack Of(RobotTemplate template) => Of(template.Attack);

    /// <summary>How far a robot's melee swing reaches: its own reach, which follows its scale.</summary>
    public static double MeleeReach(RobotStats robot) => robot.Reach;

    /// <summary>Life a second a medic of monster level <paramref name="level"/> heals: the heal's number by the level curve's damage rise, so a heal keeps pace with the damage it answers.</summary>
    public double HealPerSecond(int level) =>
        Settings.Heal.PerSecond * Math.Pow(1 + _content.RobotRules.Level.DamagePerLevelPct / 100, Math.Max(0, level - 1));

    /// <summary><paramref name="seconds"/> as whole server ticks at <paramref name="ticksPerSecond"/>, rounded, at least <paramref name="least"/>.</summary>
    public static long Ticks(double seconds, double ticksPerSecond, long least = 0) =>
        Math.Max(least, (long)Math.Round(seconds * ticksPerSecond, MidpointRounding.AwayFromZero));
}
