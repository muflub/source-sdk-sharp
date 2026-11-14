using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Horde.Session;
using TF2.Descent.Shared;
using TF2.Shared;

namespace TF2.Descent.Server.Horde.Attacks;

/// <summary>Why an attack or a heal a brain asked for was not carried out.</summary>
public enum AttackRefusal
{
    /// <summary>The body is parked or its robot gone.</summary>
    NoBody,
    Dead,
    Stunned,

    /// <summary>The body went to another robot between the ask and the hit (a windup's).</summary>
    Relent,

    /// <summary>The attack's interval has not passed since the last.</summary>
    Cooling,

    /// <summary>A swing is still winding up.</summary>
    WindingUp,
    Reloading,

    /// <summary>The robot projectiles in flight are at the cap: the shot is not fired.</summary>
    Cap,

    /// <summary>The engine refused the projectile.</summary>
    Refused,

    /// <summary>A heal whose look names no living pack-mate.</summary>
    NoPatient,

    /// <summary>A heal on a pack-mate out of the beam's reach or behind the world.</summary>
    OutOfReach,

    /// <summary>An action the horde has no verb for.</summary>
    Unknown,
}

/// <summary>
/// THE HORDE'S HANDS (plan_descent_logic 3.8, HO7): every action a robot's brain asks for,
/// carried out on the game thread as its weapon family's attack, at the cadence the rules give
/// (robot_attacks.json, <see cref="RobotAttackRules"/>), in the body's name, along where the
/// body LOOKS (<see cref="RobotAim"/>: its facing and its torso dials, so a brain's aim error
/// and its turn rate are in every shot).
///
/// <list type="bullet">
/// <item><b>Cadence.</b> A brain may ask every decide: an ask inside the interval, during a
/// windup or a reload, by a stunned or dead robot, or for a body that has gone to another robot
/// is refused and counted by kind (<see cref="AttackRefusal"/>). A clip runs down a round a
/// shot and reloads whole when empty.</item>
/// <item><b>Melee</b>: the swing starts at the ask and lands after the windup: a line from the
/// eye along the look over the robot's reach, else the swing's box; the first thing hit that is
/// not a robot body takes the attack's damage as a club.</item>
/// <item><b>Hitscan</b>: each pellet a line inside the spread's cone; each victim takes its
/// pellets' damage as bullets, once.</item>
/// <item><b>Projectiles</b>: a TF rocket or pipe (CR6) armed with the attack's damage, on the
/// robots' team, from just outside the hull along the look. TF's projectiles are created per
/// shot and remove themselves when they explode, so they are not pooled: the cap is a count.
/// No more than <see cref="RobotAttackRules.ProjectileCap"/> are alive; the next is not fired
/// and is counted. The blast hurts heroes, and robots (the shooter too: CR10) through the
/// bodies' own damage path, which pushes them (L12).</item>
/// <item><b>Flame</b>: each hero inside the cone's reach and angle with a clear line takes a
/// tick of fire; no entity is made.</item>
/// <item><b>Heal</b> (<see cref="HordeAbilities.Heal"/>): a beam on the pack-mate the medic's
/// look names, inside the beam's reach with a clear line, for the rules' hold after each ask;
/// the patient's life rises by the rules' rate each tick and its health bar with it.</item>
/// </list>
///
/// The damage a hero takes is the attack's, through TF's damage layer and the hero's own
/// (armour, resistance): this class hands the rules' number and nothing else.
/// </summary>
[SideService(Side.Server, As = [typeof(IRobotActionSink)])]
public sealed class RobotAttacks(IHordeRules rules, IRobotAttackWorld world, IHordeHeroes heroes, IRobotMotionReports motion,
                                 IHordeClock clock)
    : IRobotActionSink, IPlayerHurtHook, IOrderedHook
{
    /// <summary>After the hero's own hurt members: it only counts.</summary>
    public const int Order = ModStages.First + 530;

    /// <summary>Families by <see cref="HordeWeapons"/> number, and a slot for none.</summary>
    public const int Families = 10;

    private const int MaxHeroes = 33;
    private const int MaxBodies = 64;

    private readonly long[] _asked = new long[Families];
    private readonly long[] _fired = new long[Families];
    private readonly long[,] _refused = new long[Families, Enum.GetValues<AttackRefusal>().Length];
    private readonly long[] _heroHits = new long[Families];
    private readonly double[] _heroDamage = new double[Families];
    private readonly HeroPlace[] _places = new HeroPlace[MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[MaxHeroes];
    private readonly (EntityHandle Victim, float Damage)[] _victims = new (EntityHandle, float)[64];
    private readonly int[] _serial = new int[MaxBodies], _clip = new int[MaxBodies], _patient = new int[MaxBodies], _patientSerial = new int[MaxBodies];
    private readonly long[] _ready = new long[MaxBodies], _reloadUntil = new long[MaxBodies], _windupAt = new long[MaxBodies], _beamUntil = new long[MaxBodies];
    private readonly int[] _windupSerial = new int[MaxBodies], _devAsks = new int[MaxBodies];
    private readonly long[] _gestureUntil = new long[MaxBodies];
    private readonly Dictionary<(string Model, string Activity), (int Sequence, float Seconds)> _gestures = new();
    private EntityHandle[] _flying = [];
    private HordeController? _horde;
    private long _now;
    private ulong _rng = 0x9E3779B97F4A7C15UL;

    public int HookOrder => Order;

    /// <summary>Robot projectiles alive at the last count, and the most since the level began.</summary>
    public int Flying { get; private set; }

    public int MostFlying { get; private set; }

    /// <summary>Life the medics' beams gave, all levels.</summary>
    public double Healed { get; private set; }

    /// <summary>Ticks a beam healed on.</summary>
    public long HealTicks { get; private set; }

    /// <summary>Attack gestures laid over a body's gait.</summary>
    public long Gestures { get; private set; }

    /// <summary>Attacks asked, carried out, and refused by kind, for <paramref name="family"/> (a <see cref="HordeWeapons"/> number).</summary>
    public long Asked(int family) => _asked[Family(family)];

    public long Fired(int family) => _fired[Family(family)];

    public long Refused(int family, AttackRefusal why) => _refused[Family(family), (int)why];

    public long RefusedAll(AttackRefusal why)
    {
        long n = 0;
        for (int f = 0; f < Families; f++)
            n += _refused[f, (int)why];
        return n;
    }

    /// <summary>Hits a hero took from robots of <paramref name="family"/>, and the damage they did after every layer (the hurt event's number).</summary>
    public long HeroHits(int family) => _heroHits[Family(family)];

    public double HeroDamage(int family) => _heroDamage[Family(family)];

    public long TotalAsked => _asked.Sum();

    public long TotalFired => _fired.Sum();

    public void Begin(HordeController horde, long now)
    {
        ArgumentNullException.ThrowIfNull(horde);
        _horde = horde;
        _now = now;
        _flying = new EntityHandle[rules.Rules.Attacks.ProjectileCap];
        Flying = 0;
        MostFlying = 0;
        Array.Clear(_serial);
        Array.Clear(_devAsks);
        Array.Clear(_beamUntil);
        Array.Clear(_windupAt);
        _rng = horde.Level is { } level ? level.Seed * 0x9E3779B97F4A7C15UL + 1 : 1;
    }

    public void End()
    {
        _horde = null;
        Array.Clear(_gestureUntil);
        Array.Clear(_flying);
        Flying = 0;
        Array.Clear(_serial);
        Array.Clear(_devAsks);
        Array.Clear(_beamUntil);
        Array.Clear(_windupAt);
    }

    /// <summary>A dev ask: <paramref name="times"/> attacks by <paramref name="body"/>, each asked through <see cref="Act"/> the first tick it is ready.</summary>
    public void AskByHand(int body, int times)
    {
        if ((uint)body < MaxBodies)
            _devAsks[body] = Math.Max(0, times);
    }

    public int AsksLeft(int body) => (uint)body < MaxBodies ? _devAsks[body] : 0;

    /// <summary>The hands' main-thread cost, while switched on (<c>tf2_descent_horde_attacks_time</c>).</summary>
    public AttackCosts Costs { get; } = new();

    public void Tick(long now)
    {
        Costs.NextTick();
        Costs.Enter();
        TickCore(now);
        Costs.Leave();
    }

    public void Act(int body, in ActionIntent action)
    {
        Costs.Enter();
        ActCore(body, action);
        Costs.Leave();
    }

    private void TickCore(long now)
    {
        _now = now;
        if (_horde is not { Bodies: { } bodies, Roster: { } roster })
            return;

        CountFlying();
        for (int b = 1; b <= bodies.LastBody && b < MaxBodies; b++)
        {
            if (_windupAt[b] != 0 && now >= _windupAt[b])
                Land(b, bodies, roster);
            if (_gestureUntil[b] != 0 && now >= _gestureUntil[b])
            {
                _gestureUntil[b] = 0;
                if (!bodies.Body(b).Parked)
                    bodies.Body(b).Gesture(-1);
            }
            if (_beamUntil[b] != 0)
                Beam(b, bodies, roster);
            if (_devAsks[b] > 0 && Ready(b, bodies, roster))
            {
                _devAsks[b]--;
                ActCore(b, new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0));
            }
        }
    }

    private void ActCore(int body, in ActionIntent action)
    {
        if (_horde is not { Bodies: { } bodies, Roster: { } roster } || body < 1 || body > bodies.LastBody || body >= MaxBodies)
            return;

        var b = bodies.Body(body);
        if (b.Parked || !roster.IsLive(b.Robot))
        {
            Refuse(0, AttackRefusal.NoBody);
            return;
        }

        int i = b.Robot.Index;
        var template = rules.Rules.Robots.Template(roster.Template(i));
        int family = HordeWeapons.Of(template.Family);
        _asked[family]++;
        Adopt(body, b);
        if (roster.State(i) == RobotState.Dead || ((RobotFlags)b.State.Flags & RobotFlags.Dead) != 0)
        {
            Refuse(family, AttackRefusal.Dead);
            return;
        }

        if (roster.Stunned(i))
        {
            Refuse(family, AttackRefusal.Stunned);
            return;
        }

        if (action.Kind == ActionKind.Ability && action.WeaponSlot == HordeAbilities.Heal)
        {
            StartBeam(body, b, family, bodies, roster);
            return;
        }

        if (action.Kind != ActionKind.Fire)
        {
            Refuse(family, AttackRefusal.Unknown);
            return;
        }

        var attack = rules.Rules.Attacks.Of(template.Family);
        double tps = clock.TicksPerSecond;
        if (_windupAt[body] != 0)
        {
            Refuse(family, AttackRefusal.WindingUp);
            return;
        }

        if (_reloadUntil[body] != 0)
        {
            if (_now < _reloadUntil[body])
            {
                Refuse(family, AttackRefusal.Reloading);
                return;
            }

            _reloadUntil[body] = 0;
            _clip[body] = attack.Clip;
        }

        if (_now < _ready[body])
        {
            Refuse(family, AttackRefusal.Cooling);
            return;
        }

        if (attack.Kind == RobotAttackKind.Projectile && CountFlying() >= _flying.Length)
        {
            Refuse(family, AttackRefusal.Cap);
            return;
        }

        _ready[body] = _now + RobotAttackRules.Ticks(attack.IntervalSeconds, tps, least: 1);
        if (attack.Clip > 0 && --_clip[body] <= 0)
            _reloadUntil[body] = _now + RobotAttackRules.Ticks(attack.ReloadSeconds, tps, least: 1);

        if (RobotAttackLooks.FireSound(template) is { } sound)
            world.Sound(b.Handle, sound);
        Gesture(body, b, template);

        long windup = RobotAttackRules.Ticks(attack.WindupSeconds, tps);
        if (windup > 0)
        {
            _windupAt[body] = _now + windup;
            _windupSerial[body] = b.Lease.Serial;
            return;
        }

        Fire(b, i, family, template, attack);
    }

    /// <summary>The hurt hook: a hit on a hero from a lent body counts as its family's.</summary>
    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        if (_horde is not { Bodies: { } bodies, Roster: { } roster } || hurt.Attacker == EntityHandle.None || hurt.Taken <= 0)
            return;
        for (int b = 1; b <= bodies.LastBody; b++)
        {
            var body = bodies.Body(b);
            if (body.Parked || body.Handle != hurt.Attacker || !roster.IsLive(body.Robot))
                continue;
            int family = HordeWeapons.Of(rules.Rules.Robots.Template(roster.Template(body.Robot.Index)).Family);
            _heroHits[family]++;
            _heroDamage[family] += hurt.Taken;
            return;
        }
    }

    /// <summary>The counters as lines for <c>tf2_descent_horde_attacks</c>: one a family that asked, and the projectiles and beams.</summary>
    public IEnumerable<string> Lines()
    {
        var kinds = Enum.GetValues<AttackRefusal>();
        for (int f = 0; f < Families; f++)
        {
            if (_asked[f] == 0 && _heroHits[f] == 0 && kinds.All(k => _refused[f, (int)k] == 0))
                continue;
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"{FamilyName(f)}: asked {_asked[f]}, fired {_fired[f]}, refused");
            bool any = false;
            foreach (var k in kinds)
            {
                if (_refused[f, (int)k] == 0)
                    continue;
                sb.Append(CultureInfo.InvariantCulture, $" {k} {_refused[f, (int)k]}");
                any = true;
            }

            if (!any)
                sb.Append(" none");
            sb.Append(CultureInfo.InvariantCulture, $"; hero hits {_heroHits[f]}, damage {_heroDamage[f]:0.#}");
            yield return sb.ToString();
        }

        yield return string.Create(CultureInfo.InvariantCulture,
            $"projectiles alive {Flying} of {_flying.Length} (most {MostFlying}); beams healed {Healed:0.#} over {HealTicks} ticks; attack gestures {Gestures}");
    }

    //-------------------------------------------------------------------------

    /// <summary>A body lent to a new robot starts with a full clip, no windup and no beam.</summary>
    private void Adopt(int body, RobotBody b)
    {
        if (_serial[body] == b.Lease.Serial)
            return;
        _serial[body] = b.Lease.Serial;
        var template = rules.Rules.Robots.Template(_horde!.Roster!.Template(b.Robot.Index));
        _clip[body] = rules.Rules.Attacks.Settings.Of(template.Family)?.Clip ?? 0;
        _ready[body] = 0;
        _reloadUntil[body] = 0;
        _windupAt[body] = 0;
        _beamUntil[body] = 0;
    }

    private bool Ready(int body, RobotBodies bodies, RobotRoster roster)
    {
        var b = bodies.Body(body);
        if (b.Parked || !roster.IsLive(b.Robot))
            return false;
        if (_serial[body] != b.Lease.Serial)
            return true;
        return _windupAt[body] == 0 && _now >= _ready[body] && (_reloadUntil[body] == 0 || _now >= _reloadUntil[body]);
    }

    /// <summary>A windup's hit, if the body is still the robot's, alive and not stunned.</summary>
    private void Land(int body, RobotBodies bodies, RobotRoster roster)
    {
        _windupAt[body] = 0;
        var b = bodies.Body(body);
        if (b.Parked || b.Lease.Serial != _windupSerial[body] || !roster.IsLive(b.Robot))
        {
            Refuse(0, AttackRefusal.Relent);
            return;
        }

        int i = b.Robot.Index;
        var template = rules.Rules.Robots.Template(roster.Template(i));
        int family = HordeWeapons.Of(template.Family);
        if (roster.State(i) == RobotState.Dead || ((RobotFlags)b.State.Flags & RobotFlags.Dead) != 0)
        {
            Refuse(family, AttackRefusal.Dead);
            return;
        }

        if (roster.Stunned(i))
        {
            Refuse(family, AttackRefusal.Stunned);
            return;
        }

        Fire(b, i, family, template, rules.Rules.Attacks.Of(template.Family));
    }

    private void Fire(RobotBody b, int i, int family, RobotTemplate template, RobotAttack attack)
    {
        // A weakened robot (a debuff's damage_all_pct on its row, HO8) deals that much less with every kind.
        // Read from the row, not through the status service: that service is given the session, which is given these hands.
        var roster = _horde!.Roster!;
        var stats = roster.Stats(i);
        double weak = RobotConditionRules.OutgoingMultiplier(roster.WeakenPct(i, _now), roster.WeakenUntil(i), _now);
        if (weak != 1)
            stats = stats with { Damage = stats.Damage * weak };
        Vector eye = RobotAim.Eye(b), fwd = RobotAim.Forward(b);
        switch (attack.Kind)
        {
            case RobotAttackKind.Melee:
                Swing(b, template, attack, stats, eye, fwd);
                break;
            case RobotAttackKind.Hitscan:
                Shoot(b, attack, stats, eye, fwd);
                break;
            case RobotAttackKind.Projectile:
                if (!Launch(b, attack, stats, eye, fwd))
                {
                    Refuse(family, AttackRefusal.Refused);
                    return;
                }

                break;
            case RobotAttackKind.Flame:
                Burn(b, attack, stats, eye, fwd);
                break;
        }

        _fired[family]++;
    }

    private void Swing(RobotBody b, RobotTemplate template, RobotAttack attack, RobotStats stats, Vector eye, Vector fwd)
    {
        Vector end = eye + fwd * (float)RobotAttackRules.MeleeReach(stats);
        var hit = world.Line(eye, end, b.Handle);
        if (!hit.Hit || hit.Entity == EntityHandle.None)
            hit = world.Box(eye, end, (float)attack.HullUnits, b.Handle);
        if (!hit.Hit || hit.Entity == EntityHandle.None || IsBody(hit.Entity))
            return;
        world.Hurt(hit.Entity, b.Handle, (float)RobotAttackRules.AttackDamage(stats, attack), DamageType.Club, fwd);
        if (RobotAttackLooks.HitSound(template) is { } sound)
            world.Sound(b.Handle, sound);
    }

    private void Shoot(RobotBody b, RobotAttack attack, RobotStats stats, Vector eye, Vector fwd)
    {
        float pellet = (float)RobotAttackRules.PelletDamage(stats, attack);
        int victims = 0;
        float range = (float)attack.RangeUnits;
        for (int p = 0; p < attack.Pellets; p++)
        {
            Vector dir = Spread(fwd, (float)attack.SpreadDegrees);
            var hit = world.Line(eye, eye + dir * range, b.Handle);
            if (!hit.Hit || hit.Entity == EntityHandle.None || IsBody(hit.Entity))
                continue;
            int k = 0;
            while (k < victims && _victims[k].Victim != hit.Entity)
                k++;
            if (k == victims)
            {
                if (victims == _victims.Length)
                    continue;
                _victims[victims++] = (hit.Entity, 0f);
            }

            _victims[k].Damage += pellet;
        }

        var bits = attack.Pellets > 1 ? DamageType.Bullet | DamageType.Buckshot : DamageType.Bullet;
        for (int k = 0; k < victims; k++)
            world.Hurt(_victims[k].Victim, b.Handle, _victims[k].Damage, bits, fwd);
    }

    private bool Launch(RobotBody b, RobotAttack attack, RobotStats stats, Vector eye, Vector fwd)
    {
        float clear = RobotHull.Maxs.X * b.Scale + 16f;
        Vector muzzle = eye + fwd * clear;
        var made = world.Launch(attack.Projectile, b.Handle, muzzle, fwd * (float)attack.Speed, (float)RobotAttackRules.AttackDamage(stats, attack),
                                (int)TFTeam.Blue);
        if (made == EntityHandle.None)
            return false;
        for (int k = 0; k < _flying.Length; k++)
        {
            if (!world.Exists(_flying[k]))
            {
                _flying[k] = made;
                break;
            }
        }

        CountFlying();
        return true;
    }

    private void Burn(RobotBody b, RobotAttack attack, RobotStats stats, Vector eye, Vector fwd)
    {
        var level = _horde!.Level!;
        int n = heroes.Gather(level, _places, _eyes);
        float range = (float)attack.RangeUnits;
        float cos = MathF.Cos((float)attack.ConeDegrees * (MathF.PI / 180f));
        float damage = (float)RobotAttackRules.AttackDamage(stats, attack);
        for (int h = 0; h < n; h++)
        {
            var feet = HordeVectors.ToEngine(_places[h].Position);
            Vector chest = feet + (_eyes[h].Eye - feet) * 0.6f;
            Vector to = chest - eye;
            float d = to.Length;
            if (d > range || d < 1e-3f || Dot(to * (1f / d), fwd) < cos)
                continue;
            var line = world.Line(eye, chest, b.Handle);
            if (line.Hit && line.Entity != _eyes[h].Player)
                continue;
            world.Hurt(_eyes[h].Player, b.Handle, damage, DamageType.Burn, fwd);
        }
    }

    private void StartBeam(int body, RobotBody b, int family, RobotBodies bodies, RobotRoster roster)
    {
        if (!Patient(body, b, bodies, roster, out int patient, out var why))
        {
            Refuse(family, why);
            return;
        }

        _patient[body] = patient;
        _patientSerial[body] = bodies.Body(patient).Lease.Serial;
        _beamUntil[body] = _now + RobotAttackRules.Ticks(rules.Rules.Attacks.Settings.Heal.HoldSeconds, clock.TicksPerSecond, least: 1);
        _fired[family]++;
    }

    /// <summary>The pack-mate the medic's look names, when it is lent, alive, in the beam's reach and in clear sight.</summary>
    private bool Patient(int body, RobotBody b, RobotBodies bodies, RobotRoster roster, out int patient, out AttackRefusal why)
    {
        patient = 0;
        why = AttackRefusal.NoPatient;
        if (!motion.TryView(body, out var view) || view.Look.Kind != LookKind.Actor)
            return false;
        int p = view.Look.Actor.Slot;
        if (p < 1 || p > bodies.LastBody || p == body)
            return false;
        var mate = bodies.Body(p);
        if (mate.Parked || mate.Lease.Serial != view.Look.Actor.Serial || !roster.IsLive(mate.Robot) || roster.State(mate.Robot.Index) != RobotState.Embodied
            || roster.Pack(mate.Robot.Index) != roster.Pack(b.Robot.Index))
            return false;
        why = AttackRefusal.OutOfReach;
        if (!InReach(b, mate))
            return false;
        patient = p;
        return true;
    }

    private bool InReach(RobotBody medic, RobotBody mate)
    {
        Vector eye = RobotAim.Eye(medic), chest = RobotAim.Eye(mate) - new Vector(0f, 0f, HordeBrains.EyeHeight * mate.Scale * 0.4f);
        if ((chest - eye).Length > rules.Rules.Attacks.Settings.Heal.Range)
            return false;
        var line = world.Line(eye, chest, medic.Handle);
        return !line.Hit || line.Entity == mate.Handle;
    }

    /// <summary>One tick of a beam: the patient's life rises by the rules' rate while both are still there and in reach.</summary>
    private void Beam(int body, RobotBodies bodies, RobotRoster roster)
    {
        var b = bodies.Body(body);
        int p = _patient[body];
        bool on = _now <= _beamUntil[body] && !b.Parked && b.Lease.Serial == _serial[body] && roster.IsLive(b.Robot)
                  && roster.State(b.Robot.Index) == RobotState.Embodied && !roster.Stunned(b.Robot.Index)
                  && p >= 1 && p <= bodies.LastBody;
        var mate = on ? bodies.Body(p) : null;
        on = on && mate is { Parked: false } && mate.Lease.Serial == _patientSerial[body] && roster.IsLive(mate.Robot)
             && roster.State(mate.Robot.Index) == RobotState.Embodied && InReach(b, mate);
        if (!on)
        {
            _beamUntil[body] = 0;
            return;
        }

        int i = mate!.Robot.Index;
        double gained = roster.Heal(i, rules.Rules.Attacks.HealPerSecond(roster.MonsterLevel(b.Robot.Index)) / clock.TicksPerSecond);
        if (gained > 0)
        {
            mate.Healed(roster.Life(i), roster.Stats(i).Life);
            Healed += gained;
        }

        HealTicks++;
    }

    /// <summary>The model's attack gesture laid over the gait for its length (a model without one plays nothing), from the attack's start.</summary>
    private void Gesture(int body, RobotBody b, RobotTemplate template)
    {
        string activity = RobotAttackLooks.AttackActivity(template);
        if (!_gestures.TryGetValue((b.Model, activity), out var g))
        {
            g = world.Gesture(b.Handle, activity);
            if (g.Sequence < 0 && RobotAttackLooks.AttackActivityElse(template) is { } other)
                g = world.Gesture(b.Handle, other);
            _gestures[(b.Model, activity)] = g;
        }

        if (g.Sequence < 0)
            return;
        b.Gesture(g.Sequence);
        _gestureUntil[body] = _now + RobotAttackRules.Ticks(g.Seconds, clock.TicksPerSecond, least: 1);
        Gestures++;
    }

    private int CountFlying()
    {
        int n = 0;
        for (int k = 0; k < _flying.Length; k++)
        {
            if (_flying[k] != EntityHandle.None && world.Exists(_flying[k]))
                n++;
            else
                _flying[k] = EntityHandle.None;
        }

        Flying = n;
        if (n > MostFlying)
            MostFlying = n;
        return n;
    }

    private bool IsBody(EntityHandle e)
    {
        var bodies = _horde!.Bodies!;
        for (int b = 1; b <= bodies.LastBody; b++)
            if (bodies.Body(b).Handle == e)
                return true;
        return false;
    }

    private void Refuse(int family, AttackRefusal why) => _refused[family, (int)why]++;

    /// <summary>A direction inside a cone of half-angle <paramref name="degrees"/> about <paramref name="fwd"/>, from the level's own stream.</summary>
    private Vector Spread(Vector fwd, float degrees)
    {
        if (degrees <= 0f)
            return fwd;
        float t = MathF.Tan(degrees * (MathF.PI / 180f));
        Vector up = MathF.Abs(fwd.Z) < 0.99f ? new Vector(0f, 0f, 1f) : new Vector(1f, 0f, 0f);
        Vector right = Cross(fwd, up).Normalized;
        Vector top = Cross(right, fwd);
        // A point in the unit disc, uniform: rejection on the square.
        float x, y;
        do
        {
            x = Next() * 2f - 1f;
            y = Next() * 2f - 1f;
        }
        while (x * x + y * y > 1f);
        return (fwd + right * (x * t) + top * (y * t)).Normalized;
    }

    private float Next()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / (float)(1UL << 24);
    }

    private static float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vector Cross(Vector a, Vector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static int Family(int family) => (uint)family < Families ? family : 0;

    private static string FamilyName(int f) => f switch
    {
        HordeWeapons.Melee => "melee",
        HordeWeapons.Scattergun => "scattergun",
        HordeWeapons.RocketLauncher => "rocket_launcher",
        HordeWeapons.GrenadeLauncher => "grenade_launcher",
        HordeWeapons.Flamethrower => "flamethrower",
        HordeWeapons.Minigun => "minigun",
        HordeWeapons.Shotgun => "shotgun",
        HordeWeapons.SyringeGun => "syringe_gun",
        HordeWeapons.SniperRifle => "sniper_rifle",
        _ => "no family",
    };
}
