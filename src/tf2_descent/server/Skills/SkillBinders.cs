using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Hero;
using TF2.Server.Projectiles;

namespace TF2.Descent.Server.Skills;

/// <summary>
/// One effect primitive carried out in the world (plan_descent_logic HE6): a
/// binder is handed the cast and the effect with every number the rules
/// resolved at the hero's rank, and does the engine work. It decides
/// nothing the rules decide.
/// </summary>
public interface ISkillBinder
{
    Primitive Primitive { get; }

    /// <summary>The effect goes off now (or schedules its parts on the caster).</summary>
    void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect);

    /// <summary>A part this binder scheduled is due.</summary>
    void Due(SkillCaster caster, SkillPending pending);
}

/// <summary>
/// Every binder by primitive, and what the binders share: the party around
/// a hero, an effect on a hit target, ticks from seconds. <c>passive</c>,
/// <c>charge</c>, <c>toggle</c> and <c>triggered</c> have no binder: the
/// runtime carries them (contributions, the held key, the drain, the
/// triggers). <c>summon</c> has none: no shipped skill or spell summons.
/// </summary>
[SideService(Side.Server)]
public sealed class SkillBinders
{
    private readonly ISkillWorld _world;
    private readonly Heroes _heroes;
    private readonly SkillBuffs _buffs;
    private readonly IRobotStatusSink _robots;
    private readonly ILog _log;
    private readonly Dictionary<Primitive, ISkillBinder> _byPrimitive = new();

    public SkillBinders(ISkillWorld world, Heroes heroes, SkillBuffs buffs, IRobotStatusSink robots, ILog log)
    {
        _world = world;
        _heroes = heroes;
        _buffs = buffs;
        _robots = robots;
        _log = log;
    }

    public ISkillWorld World => _world;

    public ILog Log => _log;

    public IRobotStatusSink Robots => _robots;

    /// <summary>The binder of <paramref name="primitive"/>, or null when it has none.</summary>
    public ISkillBinder? Of(Primitive primitive)
    {
        if (_byPrimitive.Count == 0)
            foreach (var binder in Make())
                _byPrimitive[binder.Primitive] = binder;
        return _byPrimitive.GetValueOrDefault(primitive);
    }

    private ISkillBinder[] Make() =>
    [
        new ProjectileBinder(this), new HitscanBinder(this), new AreaBurstBinder(this),
        new BuffBinder(this, Primitive.Aura), new BuffBinder(this, Primitive.Buff), new DebuffBinder(this),
        new DashBinder(this), new HealBinder(this), new ThreatBinder(this),
    ];

    /// <summary>Whole server ticks in <paramref name="seconds"/>, rounded.</summary>
    public int Ticks(double seconds) =>
        seconds <= 0 ? 0 : (int)Math.Round(seconds / _world.TickInterval, MidpointRounding.AwayFromZero);

    /// <summary>The heroes alive within <paramref name="radius"/> of <paramref name="hero"/>, the hero first; just the hero for a radius of 0.</summary>
    public IReadOnlyList<HeroComponent> PartyAround(EntityHandle hero, double radius)
    {
        var found = new List<HeroComponent>();
        var self = _heroes.All.FirstOrDefault(h => h.Player == hero);
        if (self is not null)
            found.Add(self);
        if (radius <= 0)
            return found;

        Vector at = _world.OriginOf(hero);
        foreach (var h in _heroes.All.OrderBy(h => h.PlayerIndex))
            if (!ReferenceEquals(h, self) && h.Player != EntityHandle.None && _world.IsAlive(h.Player)
                && (_world.OriginOf(h.Player) - at).Length <= radius)
                found.Add(h);
        return found;
    }

    /// <summary>The hurtable things within <paramref name="radius"/> that are not of the hero's team.</summary>
    public IReadOnlyList<EntityHandle> EnemiesAround(EntityHandle hero, Vector at, double radius)
    {
        int team = _world.TeamOf(hero);
        return [.. _world.Near(at, (float)radius).Where(e => e != hero && (!_world.IsPlayer(e) || _world.TeamOf(e) != team))];
    }

    /// <summary>
    /// A debuff's conditions on one target: a player takes the TF condition
    /// of each word (<see cref="SkillConditions.OnPlayer"/>), anything else
    /// the horde's status sink. Each word's answer is logged.
    /// </summary>
    public void Afflict(SkillCast cast, ResolvedEffect effect, EntityHandle target)
    {
        double seconds = effect.Params.TryGetValue("duration", out var d) ? d : 0;
        foreach (string condition in effect.Conditions)
        {
            string answer;
            if (_world.IsPlayer(target))
                answer = SkillConditions.OnPlayer(condition) is { } cond
                    ? (_world.AddCondition(target, cond, (float)seconds, cast.Hero) ? $"TF {cond}" : $"TF {cond} refused")
                    : "not bound on a player";
            else
                answer = _robots.Apply(target, condition, seconds, cast.Hero, cast.SkillId, effect.Target ?? "enemies").ToString();
            _log.Msg(Line(cast, $"{condition} on #{target.EntryIndex} for {seconds:0.##}s: {answer}"));
        }
    }

    /// <summary>A timed buff (its stats and conditions) on each hero of <paramref name="party"/>.</summary>
    public void Buff(SkillCast cast, ResolvedEffect effect, IReadOnlyList<HeroComponent> party, string sourceId)
    {
        double seconds = effect["duration"];
        int end = cast.Tick + Ticks(seconds);
        var stats = SkillRules.AsBuff(sourceId, effect);
        foreach (var hero in party)
        {
            _buffs.Add(hero.PlayerIndex, new HeroBuff(sourceId, cast.PlayerIndex, stats, end));
            hero.Recompute();
            foreach (string condition in effect.Conditions)
                if (SkillConditions.OnPlayer(condition) is { } cond)
                    _world.AddCondition(hero.Player, cond, (float)seconds, cast.Hero);
            _log.Msg(Line(cast, $"{effect.Primitive.ToString().ToLowerInvariant()} on {hero.PlayerIndex} until tick {end}: "
                                + string.Join(", ", stats.Select(s => $"{s.Stat} {s.Amount:+0.##;-0.##}"))));
        }
    }

    public static string Line(SkillCast cast, string what) =>
        string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Skills] {cast.PlayerIndex} {cast.SkillId}: {what}\n");

    /// <summary>
    /// <paramref name="count"/> directions fanned across <paramref name="spreadDeg"/>
    /// each about the aim's up: the <paramref name="i"/>th, the middle one the aim.
    /// </summary>
    public static Vector Fan(SkillAim aim, int i, int count, double spreadDeg)
    {
        if (count <= 1 || spreadDeg == 0)
            return aim.Forward;
        double yaw = (i - (count - 1) / 2.0) * spreadDeg * Math.PI / 180;
        return (aim.Forward * (float)Math.Cos(yaw) + aim.Right * (float)Math.Sin(yaw)).Normalized;
    }
}

/// <summary>
/// <c>projectile</c>: TF projectiles through TF2's spawner (CR6), one for
/// each <c>count</c> and each charge stage, fanned across <c>spreadDeg</c>
/// and spread over the charge's <c>releaseTime</c> (or the effect's
/// <c>duration</c>). The class is the skill's damage type's (blast a rocket,
/// shock an energy ball, fire a flare, bullet an arrow); the damage is the
/// rules' (<see cref="SkillRules.Damage"/>, never the weapon in the hands);
/// the team is the hero's, named.
/// </summary>
public sealed class ProjectileBinder(SkillBinders shared) : ISkillBinder
{
    public Primitive Primitive => Primitive.Projectile;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        int count = Math.Max(1, (int)effect["count"]) * Math.Max(1, cast.Stages);
        double window = cast.Skill.Effects.FirstOrDefault(e => e.Primitive == Primitive.Charge) is { } charge
            ? charge["releaseTime"]
            : effect["duration"];
        for (int i = 0; i < count; i++)
        {
            int after = count > 1 ? shared.Ticks(window * i / count) : 0;
            if (after == 0)
                Shoot(cast, effect, i, count);
            else
                caster.Pending.Add(new SkillPending(cast.Tick + after, cast, effect, i, count, default));
        }
    }

    public void Due(SkillCaster caster, SkillPending pending) => Shoot(pending.Cast, pending.Effect, pending.Index, pending.Of);

    /// <summary>The TF projectile a skill of <paramref name="damageType"/> fires.</summary>
    public static string ClassFor(string? damageType) => damageType switch
    {
        "shock" => TFProjectileEntityNames.EnergyBall,
        "fire" => TFProjectileEntityNames.Flare,
        "bullet" => TFProjectileEntityNames.Arrow,
        _ => TFProjectileEntityNames.Rocket,
    };

    /// <summary>The class's stock speed, when the effect names none.</summary>
    public static float StockSpeed(string className) => className switch
    {
        TFProjectileEntityNames.EnergyBall => TFProjectileDefs.EnergyBallSpeed,
        TFProjectileEntityNames.Flare => TFProjectileDefs.FlareSpeed,
        TFProjectileEntityNames.Arrow => TFProjectileDefs.ArrowMaxChargeSpeed,
        _ => TFProjectileDefs.RocketSpeed,
    };

    private void Shoot(SkillCast cast, ResolvedEffect effect, int i, int count)
    {
        var world = shared.World;
        SkillAim aim = world.AimOf(cast.Hero);
        Vector forward = SkillBinders.Fan(aim, i, count, effect["spreadDeg"]);
        string className = ClassFor(cast.Def.DamageType);
        float speed = effect["speed"] > 0 ? (float)effect["speed"] : StockSpeed(className);

        // The stock launcher's muzzle (FireRocket's offset), so a skill's
        // rocket leaves where a launcher's would, whatever is in the hands.
        Vector origin = aim.Eyes + aim.Forward * TFProjectileDefs.RocketMuzzleForward
                        + aim.Right * TFProjectileDefs.RocketMuzzleRight + aim.Up * TFProjectileDefs.RocketMuzzleUp;
        float damage = (float)effect["damage"];
        int team = world.TeamOf(cast.Hero);
        var shot = new SkillShot(className, cast.Hero, origin, forward * speed, damage, team, cast.WeaponName);
        EntityHandle fired = world.Spawn(in shot);
        shared.Log.Msg(SkillBinders.Line(cast, fired == EntityHandle.None
            ? $"{className} refused by the spawner"
            : string.Create(CultureInfo.InvariantCulture,
                $"{className} #{fired.EntryIndex} owner {cast.PlayerIndex} team {team} damage {damage:0.##} ({i + 1}/{count})")));
    }
}

/// <summary>
/// <c>hitscan</c>: a line from the eyes along the aim to <c>range</c> (one a
/// <c>count</c>, fanned), and the rules' damage on what it hits, through the
/// damage rules as a bullet under the skill's weapon name.
/// </summary>
public sealed class HitscanBinder(SkillBinders shared) : ISkillBinder
{
    public Primitive Primitive => Primitive.Hitscan;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        var world = shared.World;
        SkillAim aim = world.AimOf(cast.Hero);
        int count = Math.Max(1, (int)effect["count"]) * Math.Max(1, cast.Stages);
        float damage = (float)effect["damage"];
        for (int i = 0; i < count; i++)
        {
            Vector direction = SkillBinders.Fan(aim, i, count, effect["spreadDeg"]);
            TraceResult trace = world.Trace(aim.Eyes, aim.Eyes + direction * (float)effect["range"], cast.Hero);
            if (!trace.Hit || trace.Entity == EntityHandle.None || trace.Entity.EntryIndex == 0)
            {
                shared.Log.Msg(SkillBinders.Line(cast, "hitscan hit nothing"));
                continue;
            }

            world.Hurt(trace.Entity, cast.Hero, damage, DamageType.Bullet, direction * damage, trace.End, cast.WeaponName);
            shared.Log.Msg(SkillBinders.Line(cast, string.Create(CultureInfo.InvariantCulture,
                $"hitscan hit #{trace.Entity.EntryIndex} for {damage:0.##}")));
        }
    }

    public void Due(SkillCaster caster, SkillPending pending) { }
}

/// <summary>
/// <c>area_burst</c>: blasts of the rules' damage and <c>radius</c> through
/// TF2's blast applier, the caster spared: at the hero (target
/// <c>enemies</c>), or at the point the aim marks (target <c>point</c>), in
/// <c>count</c> shells spread round <c>area</c>, each after <c>delay</c>.
/// </summary>
public sealed class AreaBurstBinder(SkillBinders shared) : ISkillBinder
{
    /// <summary>How far the aim marks a point.</summary>
    public const float MarkRange = 4096f;

    public Primitive Primitive => Primitive.AreaBurst;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        var world = shared.World;
        Vector center = world.OriginOf(cast.Hero);
        if (effect.Target == "point")
        {
            SkillAim aim = world.AimOf(cast.Hero);
            center = world.Trace(aim.Eyes, aim.Eyes + aim.Forward * MarkRange, cast.Hero).End;
        }

        int count = Math.Max(1, (int)effect["count"]);
        int delay = shared.Ticks(effect["delay"]);
        for (int i = 0; i < count; i++)
        {
            Vector at = count == 1 ? center : center + Ring(i, count, (float)effect["area"]);
            if (delay == 0)
                Burst(cast, effect, at);
            else
                caster.Pending.Add(new SkillPending(cast.Tick + delay, cast, effect, i, count, at));
        }

        if (delay > 0)
            shared.Log.Msg(SkillBinders.Line(cast, $"{count} shell(s) land at tick {cast.Tick + delay}"));
    }

    public void Due(SkillCaster caster, SkillPending pending) => Burst(pending.Cast, pending.Effect, pending.At);

    /// <summary>The <paramref name="i"/>th of <paramref name="count"/> points on a ring of half <paramref name="area"/>.</summary>
    public static Vector Ring(int i, int count, float area)
    {
        double angle = 2 * Math.PI * i / count;
        float r = area / 2;
        return new Vector((float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r, 0f);
    }

    private void Burst(SkillCast cast, ResolvedEffect effect, Vector at)
    {
        float damage = (float)effect["damage"], radius = (float)effect["radius"];
        shared.World.Blast(at, damage, radius, cast.Hero, cast.WeaponName);
        shared.Log.Msg(SkillBinders.Line(cast, string.Create(CultureInfo.InvariantCulture,
            $"burst at ({at.X:0},{at.Y:0},{at.Z:0}) damage {damage:0.##} radius {radius:0.#}")));
    }
}

/// <summary>
/// <c>aura</c> and <c>buff</c>: the effect's stats as a timed buff
/// (<see cref="SkillRules.AsBuff"/>) on the heroes of its target (the caster;
/// the party within <c>radius</c>), and its conditions as TF conditions for
/// the same time. The buffed heroes' stats are recomputed at once and again
/// when the buff ends.
/// </summary>
public sealed class BuffBinder(SkillBinders shared, Primitive primitive) : ISkillBinder
{
    public Primitive Primitive => primitive;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        double radius = effect.Target == "party" && effect.Params.TryGetValue("radius", out var r) ? r : 0;
        shared.Buff(cast, effect, shared.PartyAround(cast.Hero, radius), cast.SkillId);
    }

    public void Due(SkillCaster caster, SkillPending pending) { }
}

/// <summary>
/// <c>debuff</c> with a radius: its conditions on the enemies within it
/// (<see cref="SkillBinders.Afflict"/>: TF conditions on players, the horde's
/// sink for anything else). A debuff with no radius rides the cast's hits
/// instead (<see cref="SkillCast.OnHit"/>), applied by the hit hooks.
/// </summary>
public sealed class DebuffBinder(SkillBinders shared) : ISkillBinder
{
    public Primitive Primitive => Primitive.Debuff;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        double radius = effect["radius"];
        if (radius <= 0)
            return;
        foreach (var target in shared.EnemiesAround(cast.Hero, shared.World.OriginOf(cast.Hero), radius))
            shared.Afflict(cast, effect, target);
    }

    public void Due(SkillCaster caster, SkillPending pending) { }
}

/// <summary>
/// <c>dash</c>: an <c>impulse</c> on the hero along the aim (or towards the
/// point the aim marks within <c>range</c>), the movement seam TF2's blast
/// push uses; <c>blast_jumping</c> makes it a rocket jump as TF2 counts one,
/// and the effect's other conditions go on the hero for the flight.
/// </summary>
public sealed class DashBinder(SkillBinders shared) : ISkillBinder
{
    /// <summary>How long a dash's conditions (Human Rocket's invulnerability) hold: the flight, at most.</summary>
    public const float FlightSeconds = 3f;

    public Primitive Primitive => Primitive.Dash;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        var world = shared.World;
        SkillAim aim = world.AimOf(cast.Hero);
        Vector direction = aim.Forward;
        if (effect.Target == "point" && effect["range"] > 0)
        {
            Vector mark = world.Trace(aim.Eyes, aim.Eyes + aim.Forward * (float)effect["range"], cast.Hero).End;
            Vector towards = mark - world.OriginOf(cast.Hero);
            if (towards.Length > 0)
                direction = towards.Normalized;
        }

        Vector impulse = direction * (float)effect["impulse"];
        world.Push(cast.Hero, impulse);
        foreach (string condition in effect.Conditions)
        {
            if (condition == "blast_jumping")
                world.BeginBlastJump(cast.Hero);
            else if (SkillConditions.OnPlayer(condition) is { } cond)
                world.AddCondition(cast.Hero, cond, FlightSeconds, cast.Hero);
        }

        caster.Dashing = cast;
        caster.DashLeftGround = false;
        shared.Log.Msg(SkillBinders.Line(cast, string.Create(CultureInfo.InvariantCulture,
            $"dash impulse ({impulse.X:0},{impulse.Y:0},{impulse.Z:0})")));
    }

    public void Due(SkillCaster caster, SkillPending pending) { }
}

/// <summary>
/// <c>heal</c>: <c>amount</c> plus <c>pctMaxLife</c> % of each target's
/// maximum life, at once or in a share each second over <c>duration</c>, on
/// the caster or the party within <c>radius</c>; its cleanse words take the
/// TF condition off first.
/// </summary>
public sealed class HealBinder(SkillBinders shared) : ISkillBinder
{
    public Primitive Primitive => Primitive.Heal;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        var world = shared.World;
        double radius = effect.Target == "party" ? effect["radius"] : 0;
        int seconds = Math.Max(1, (int)Math.Ceiling(effect["duration"]));
        foreach (var hero in shared.PartyAround(cast.Hero, radius))
        {
            foreach (string condition in effect.Conditions)
                if (SkillConditions.Cleanses(condition) is { } cond)
                    world.RemoveCondition(hero.Player, cond);

            float total = (float)(effect["amount"] + effect["pctMaxLife"] / 100 * world.MaxHealth(hero.Player));
            float share = total / seconds;
            Heal(cast, hero.Player, share);
            for (int s = 1; s < seconds; s++)
                caster.Pending.Add(new SkillPending(cast.Tick + shared.Ticks(s), cast, effect, s, seconds, default, hero.Player, share));
        }
    }

    public void Due(SkillCaster caster, SkillPending pending) => Heal(pending.Cast, pending.Target, pending.Amount);

    private void Heal(SkillCast cast, EntityHandle target, float amount)
    {
        int healed = shared.World.Heal(target, amount);
        shared.Log.Msg(SkillBinders.Line(cast, string.Create(CultureInfo.InvariantCulture,
            $"heal #{target.EntryIndex} {amount:0.##}: {healed} healed")));
    }
}

/// <summary>
/// <c>threat</c>: every robot within <c>radius</c> targets the caster for
/// <c>duration</c>, through the horde's sink (players are not taunted).
/// </summary>
public sealed class ThreatBinder(SkillBinders shared) : ISkillBinder
{
    public Primitive Primitive => Primitive.Threat;

    public void Fire(SkillCaster caster, SkillCast cast, ResolvedEffect effect)
    {
        foreach (var target in shared.EnemiesAround(cast.Hero, shared.World.OriginOf(cast.Hero), effect["radius"]))
        {
            if (shared.World.IsPlayer(target))
                continue;
            var answer = shared.Robots.Threaten(target, cast.Hero, effect["duration"], cast.SkillId);
            shared.Log.Msg(SkillBinders.Line(cast, $"threat on #{target.EntryIndex}: {answer}"));
        }
    }

    public void Due(SkillCaster caster, SkillPending pending) { }
}
