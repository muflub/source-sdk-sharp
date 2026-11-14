using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Rules.Bosses;

/// <summary>A boss's adds for one phase: <see cref="Count"/> robots of <see cref="Template"/>, put down in its arena when the phase begins.</summary>
public sealed record BossAdd(string Template, int Count);

/// <summary>
/// One phase of a boss's fight (plan_tf2_rpg D13, plan_descent_logic HO13): it begins when the
/// boss's life falls to <see cref="At"/> percent of its most or below (the first phase at 100),
/// asks its <see cref="Special"/> (a row of <see cref="BossSettings.Specials"/>; "" for none)
/// every <see cref="SpecialSeconds"/> while a hero is in sight, and summons its
/// <see cref="Adds"/> once, as it begins.
/// </summary>
public sealed record BossPhase(double At, string Special, double SpecialSeconds, IReadOnlyList<BossAdd> Adds);

/// <summary>
/// A boss's script (D13): its template, the depth whose arena it holds, the companion its fight
/// brings (the level 10 Tank; "" for none) and its phases, the first at 100 % and each later one
/// at a lower share of its life.
/// </summary>
public sealed record BossScript(string Id, int Depth, string Companion, IReadOnlyList<BossPhase> Phases) : IContentRow;

/// <summary>
/// A boss's big attack, telegraphed (HO13): a windup of <see cref="WindupSeconds"/> during which
/// the heroes hear <see cref="Sound"/> and see <see cref="Particle"/> on the boss, then either a
/// <c>volley</c> (<see cref="Shots"/> of <see cref="Attack"/>'s projectile, fanned
/// <see cref="FanDegrees"/> apart round the boss's aim, each dealing <see cref="DamageShare"/> of
/// the boss's damage, crit when <see cref="Crit"/>) or a <c>slam</c> (every hero within
/// <see cref="Radius"/> with a clear line takes <see cref="DamageShare"/> of the boss's damage as
/// <see cref="DamageType"/>).
/// </summary>
public sealed record BossSpecial(string Id, string Kind, double WindupSeconds, string Sound, string Particle, string Attack, int Shots,
                                 double FanDegrees, double DamageShare, bool Crit, double Radius, string DamageType) : IContentRow
{
    public const string Volley = "volley", Slam = "slam";
}

/// <summary>
/// The level 10 Tank (mine plan D11, M14): its template; how long it stops at each station of its
/// track and at the last; how near a station counts as there; how far short of the arena's centre
/// its last station stands (the roof pillar's room); what each hero in the arena takes when it has
/// drilled the last (the roof comes down: the fight is lost); and the condition words it shrugs
/// (a machine on its track is not stunned, slowed or thrown).
/// </summary>
public sealed record TankSettings(string Template, double StopSeconds, double FinalSeconds, double ArriveUnits, double CentreClearUnits,
                                  double RoofDamage, IReadOnlyList<string> Immune);

/// <summary>The fight's sounds and effects, TF2's MvM ones.</summary>
public sealed record BossSounds(string Intro, string Phase, string Death, string ExitOpens, string TankStart, string TankStop, string TankDrill,
                                string TankDeath, string TankDeathParticle, string Roof);

/// <summary>
/// bosses.json (D13, mine plan D4, D6, D11; plan_descent_logic HO13): how long the arena must be
/// empty of living heroes before its boss resets (<see cref="ResetSeconds"/>), how far inside its
/// arena's walls a boss keeps (<see cref="ArenaMargin"/>), how far apart its adds are put down
/// (<see cref="AddSpacing"/>), the three bosses' scripts, their specials, the Tank, the sounds.
/// </summary>
public sealed record BossSettings(double ResetSeconds, double ArenaMargin, double AddSpacing, IReadOnlyList<BossScript> Bosses,
                                  IReadOnlyList<BossSpecial> Specials, TankSettings Tank, BossSounds Sounds)
{
    /// <summary>The script of boss template <paramref name="template"/>; null for a robot that is no boss.</summary>
    public BossScript? Of(string template)
    {
        foreach (var b in Bosses)
            if (b.Id == template)
                return b;
        return null;
    }

    /// <summary>The boss whose arena is at <paramref name="depth"/>; null for a depth with none.</summary>
    public BossScript? AtDepth(int depth)
    {
        foreach (var b in Bosses)
            if (b.Depth == depth)
                return b;
        return null;
    }

    public BossSpecial? Special(string id)
    {
        foreach (var s in Specials)
            if (s.Id == id)
                return s;
        return null;
    }
}

/// <summary>
/// The boss table. Load errors: a boss template (robots.json's authored temperament) with no
/// script, or a script for a robot that is no boss; a depth that is not a boss depth (5, 10, 15)
/// or two scripts at one; phases that do not start at 100 or do not fall; a special or an add
/// that names nothing, an add that is a boss, more adds than the pool can hold; a special of no
/// known kind, a volley that names no projectile attack, a slam with no radius, a telegraph's
/// windup too short to see; a companion that is no robot or is a boss; the Tank's numbers out of
/// range and a condition it shrugs that robot_conditions.json does not know.
/// </summary>
public sealed class BossTable : SingleTable<BossSettings>
{
    public const string TableName = "bosses";

    /// <summary>The boss depths (mine plan M5): the one arena on each.</summary>
    public static IReadOnlyList<int> Depths => [TownDescents.FirstBoss, TownDescents.SecondBoss, TownDescents.LastBoss];

    /// <summary>Adds one boss may summon over its whole fight: a third of the 48 bodies.</summary>
    public const int MaxAdds = 16;

    /// <summary>The most robots one phase may add, and the most shots one volley fires.</summary>
    public const int MaxPhaseAdds = 8, MaxShots = 8;

    /// <summary>A telegraph shorter than this cannot be seen and answered.</summary>
    public const double LeastWindup = 0.2;

    public override string Name => TableName;

    protected override BossSettings Read(JsonFields f)
    {
        var bosses = f.Array("bosses").Select(e =>
        {
            var b = new JsonFields(e, f.Table, "bosses", f.Errors);
            var phases = b.Array("phases").Select(p =>
            {
                var ph = new JsonFields(p, f.Table, "phases", f.Errors);
                var adds = ph.Array("adds").Select(a =>
                {
                    var ad = new JsonFields(a, f.Table, "adds", f.Errors);
                    var add = new BossAdd(ad.Str("template"), ad.Int("count"));
                    ad.End();
                    return add;
                }).ToList();
                var phase = new BossPhase(ph.Num("at"), ph.Str("special"), ph.Num("specialSeconds"), adds);
                ph.End();
                return phase;
            }).ToList();
            var script = new BossScript(b.Str("id"), b.Int("depth"), b.Str("companion"), phases);
            b.End();
            return script;
        }).ToList();

        var specials = f.Array("specials").Select(e =>
        {
            var s = new JsonFields(e, f.Table, "specials", f.Errors);
            var special = new BossSpecial(s.Str("id"), s.Str("kind"), s.Num("windupSeconds"), s.Str("sound"), s.Str("particle"), s.Str("attack"),
                s.Int("shots"), s.Num("fanDegrees"), s.Num("damageShare"), s.Bool("crit"), s.Num("radius"), s.Str("damageType"));
            s.End();
            return special;
        }).ToList();

        var t = f.Obj("tank");
        var tank = new TankSettings(t.Str("template"), t.Num("stopSeconds"), t.Num("finalSeconds"), t.Num("arriveUnits"), t.Num("centreClearUnits"),
            t.Num("roofDamage"), t.Strings("immune", required: true));
        t.End();

        var o = f.Obj("sounds");
        var sounds = new BossSounds(o.Str("intro"), o.Str("phase"), o.Str("death"), o.Str("exitOpens"), o.Str("tankStart"), o.Str("tankStop"),
            o.Str("tankDrill"), o.Str("tankDeath"), o.Str("tankDeathParticle"), o.Str("roof"));
        o.End();

        return new BossSettings(f.Num("resetSeconds"), f.Num("arenaMargin"), f.Num("addSpacing"), bosses, specials, tank, sounds);
    }

    protected override void Validate(DescentContent content, BossSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "resetSeconds", s.ResetSeconds, 1, 600);
        ContentChecks.Range(errors, t, null, "arenaMargin", s.ArenaMargin, 0, 512);
        ContentChecks.Range(errors, t, null, "addSpacing", s.AddSpacing, 16, 512);
        RobotJson.Unique(errors, t, "bosses", s.Bosses.Select(b => b.Id));
        RobotJson.Unique(errors, t, "specials", s.Specials.Select(b => b.Id));
        content.TryGet<Table<RobotTemplate>>(ContentTables.Robots, out var robots);

        var depths = new HashSet<int>();
        foreach (var b in s.Bosses)
        {
            if (robots is not null)
            {
                if (!robots.TryGet(b.Id, out var template))
                    errors.Add(t, b.Id, ContentRule.DanglingReference, $"'{b.Id}' is not a robot template");
                else if (!template.Boss)
                    errors.Add(t, b.Id, ContentRule.OutOfRange, $"'{b.Id}' is not a boss (robots.json gives it no authored temperament)");
            }

            if (!Depths.Contains(b.Depth))
                errors.Add(t, b.Id, ContentRule.OutOfRange, $"'depth' is {b.Depth}, not a boss depth ({string.Join(", ", Depths)})");
            else if (!depths.Add(b.Depth))
                errors.Add(t, b.Id, ContentRule.DuplicateId, $"depth {b.Depth} has more than one boss");

            if (b.Companion.Length > 0)
                CheckMinion(robots, errors, b.Id, "companion", b.Companion);

            if (b.Phases.Count == 0 || b.Phases[0].At != 100)
                errors.Add(t, b.Id, ContentRule.OutOfRange, "the first phase begins at 100 (the boss's full life)");
            int adds = b.Companion.Length > 0 ? 1 : 0;
            for (int i = 0; i < b.Phases.Count; i++)
            {
                var p = b.Phases[i];
                ContentChecks.Range(errors, t, b.Id, $"phases[{i}].at", p.At, 1, 100);
                if (i > 0 && p.At >= b.Phases[i - 1].At)
                    errors.Add(t, b.Id, ContentRule.OutOfRange, $"phase {i} begins at {p.At}, not below phase {i - 1}'s {b.Phases[i - 1].At}");
                if (p.Special.Length > 0)
                {
                    if (s.Special(p.Special) is null)
                        errors.Add(t, b.Id, ContentRule.DanglingReference, $"phase {i} names special '{p.Special}', which is not a row of 'specials'");
                    ContentChecks.Range(errors, t, b.Id, $"phases[{i}].specialSeconds", p.SpecialSeconds, 1, 120);
                }

                int phaseAdds = 0;
                foreach (var a in p.Adds)
                {
                    CheckMinion(robots, errors, b.Id, $"phases[{i}].adds", a.Template);
                    ContentChecks.Range(errors, t, b.Id, $"phases[{i}].adds.{a.Template}", a.Count, 1, MaxPhaseAdds);
                    phaseAdds += a.Count;
                }

                if (phaseAdds > MaxPhaseAdds)
                    errors.Add(t, b.Id, ContentRule.OutOfRange, $"phase {i} adds {phaseAdds} robots, more than {MaxPhaseAdds}");
                adds += phaseAdds;
            }

            if (adds > MaxAdds)
                errors.Add(t, b.Id, ContentRule.OutOfRange, $"its fight adds {adds} robots, more than the {MaxAdds} a boss may take of the pool");
        }

        if (robots is not null)
            foreach (var r in robots.Rows.Where(r => r.Boss && s.Of(r.Id) is null))
                errors.Add(t, r.Id, ContentRule.DanglingReference, $"boss '{r.Id}' has no script");

        content.TryGet<RobotAttackSettings>(RobotAttackTable.TableName, out var attacks);
        foreach (var sp in s.Specials)
        {
            ContentChecks.Range(errors, t, sp.Id, "windupSeconds", sp.WindupSeconds, LeastWindup, 10);
            ContentChecks.Range(errors, t, sp.Id, "damageShare", sp.DamageShare, 0, 100);
            if (sp.Sound.Length == 0)
                errors.Add(t, sp.Id, ContentRule.Malformed, "a telegraph names the sound the heroes hear over its windup");
            switch (sp.Kind)
            {
                case BossSpecial.Volley:
                    ContentChecks.Range(errors, t, sp.Id, "shots", sp.Shots, 1, MaxShots);
                    ContentChecks.Range(errors, t, sp.Id, "fanDegrees", sp.FanDegrees, 0, 45);
                    if (attacks is not null && attacks.Of(sp.Attack) is not { Kind: RobotAttackKind.Projectile })
                        errors.Add(t, sp.Id, ContentRule.DanglingReference, $"a volley names a projectile attack: '{sp.Attack}' is none");
                    break;
                case BossSpecial.Slam:
                    ContentChecks.Range(errors, t, sp.Id, "radius", sp.Radius, 1, 2048);
                    ContentChecks.Reference<DamageTypeRow>(content, errors, t, sp.Id, "damageType", sp.DamageType, ContentTables.DamageTypes);
                    break;
                default:
                    errors.Add(t, sp.Id, ContentRule.Malformed, $"'kind' is '{sp.Kind}', not {BossSpecial.Volley} or {BossSpecial.Slam}");
                    break;
            }
        }

        var tank = s.Tank;
        CheckMinion(robots, errors, "tank", "template", tank.Template);
        ContentChecks.Range(errors, t, "tank", "stopSeconds", tank.StopSeconds, 0, 600);
        ContentChecks.Range(errors, t, "tank", "finalSeconds", tank.FinalSeconds, 1, 1200);
        ContentChecks.Range(errors, t, "tank", "arriveUnits", tank.ArriveUnits, 8, 512);
        ContentChecks.Range(errors, t, "tank", "centreClearUnits", tank.CentreClearUnits, 0, 2048);
        ContentChecks.Range(errors, t, "tank", "roofDamage", tank.RoofDamage, 1, 1e6);
        foreach (string word in tank.Immune)
            ContentChecks.Reference<RobotConditionRow>(content, errors, t, "tank", "immune", word, ContentTables.RobotConditions);
    }

    /// <summary>A robot a boss brings (an add, its companion): a template, and not a boss.</summary>
    private void CheckMinion(Table<RobotTemplate>? robots, ContentErrors errors, string row, string field, string template)
    {
        if (robots is null)
            return;
        if (!robots.TryGet(template, out var r))
            errors.Add(Name, row, ContentRule.DanglingReference, $"'{field}' names '{template}', which is not a robot template");
        else if (r.Boss)
            errors.Add(Name, row, ContentRule.OutOfRange, $"'{field}' names '{template}', a boss: an arena holds one");
    }
}
