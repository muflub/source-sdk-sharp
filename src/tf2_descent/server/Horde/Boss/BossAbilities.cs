using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Bosses;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Attacks;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>Why a boss ability was not carried out.</summary>
public enum BossRefusal
{
    /// <summary>The robot is no boss, or its phase names no special.</summary>
    NoSpecial,

    /// <summary>A special asked while the last one still winds up.</summary>
    WindingUp,

    /// <summary>A phase's adds asked again (its brain started over).</summary>
    Summoned,

    /// <summary>The body went to another robot, or its robot died or was stunned, during the windup.</summary>
    Lost,
}

/// <summary>
/// THE BOSSES' VERBS (plan_descent_logic HO13), a member of the ability chain the horde's hands
/// hand an unknown ability to, and of the horde's tickers (a windup lands on the tick it is due).
///
/// <list type="bullet">
/// <item><b>Special</b> (<see cref="HordeAbilities.BossSpecial"/>, the phase in the ask): the
/// phase's special from bosses.json. Its TELEGRAPH starts at the ask: its sound on the boss and
/// its particle at its feet, for its windup; then it lands, if the body is still the boss's,
/// alive and not stunned. A <c>volley</c> fires its shots of its attack's projectile fanned round
/// the boss's aim (a crit when the row says), each counted against the robots' projectile cap; a
/// <c>slam</c> deals its share of the boss's damage to each hero within its radius with a clear
/// line, as its damage type.</item>
/// <item><b>Summon</b> (<see cref="HordeAbilities.BossSummon"/>): the phase's adds
/// (<see cref="IBossAdds"/>), once a phase until the boss is reset.</item>
/// <item><b>The Tank's station</b> (<see cref="HordeAbilities.TankStation"/>): its ping, its
/// drill and a notice to the heroes in its arena.</item>
/// <item><b>The roof</b> (<see cref="HordeAbilities.TankRoof"/>): the Tank has drilled its last
/// station and the fight is lost: every living hero in its arena takes the Tank's
/// <c>roofDamage</c> as a blast, MvM's bomb sound over it.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class BossAbilities(IHordeRules rules, IBossWorld world, IBossAdds adds, IHordeHeroes heroes, BossEncounter encounter, ILog log)
    : IRobotAbility, IHordeTicker, IOrderedHook
{
    /// <summary>In the boss band, after the encounter (its phase is current before a windup lands).</summary>
    public const int Order = ModStages.First + 1320;

    public int HookOrder => Order;

    private const int MaxBodies = 64, MaxHeroes = 33;

    private readonly long[] _landAt = new long[MaxBodies];
    private readonly int[] _serial = new int[MaxBodies];
    private readonly BossSpecial?[] _special = new BossSpecial?[MaxBodies];
    private readonly HeroPlace[] _places = new HeroPlace[MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[MaxHeroes];
    private readonly HashSet<(int Row, int Phase, int Reset)> _summoned = [];
    private readonly long[] _refused = new long[Enum.GetValues<BossRefusal>().Length];
    private HordeController? _level;
    private IRobotAbilityHands? _hands;

    /// <summary>Specials asked (each a telegraph shown), landed, volley shots fired and heroes a slam hit, this level.</summary>
    public long Telegraphs { get; private set; }

    public long Landed { get; private set; }

    public long Shots { get; private set; }

    public long SlamHits { get; private set; }

    /// <summary>Summons carried out, the Tank's stations told, roofs brought down, heroes the roof hit.</summary>
    public long Summons { get; private set; }

    public long Stations { get; private set; }

    public long Roofs { get; private set; }

    public long RoofHits { get; private set; }

    public long Refused(BossRefusal why) => _refused[(int)why];

    /// <summary>The tick body <paramref name="body"/>'s special lands, 0 for none winding up.</summary>
    public long LandsAt(int body) => (uint)body < MaxBodies ? _landAt[body] : 0;

    public bool Act(HordeController horde, RobotBody body, int row, in ActionIntent action, IRobotAbilityHands hands, long now)
    {
        ArgumentNullException.ThrowIfNull(horde);
        ArgumentNullException.ThrowIfNull(body);
        Begin(horde);
        _hands = hands;
        switch (action.WeaponSlot)
        {
            case HordeAbilities.BossSpecial:
                Special(horde, body, row, action.OneShotSeq, now);
                return true;
            case HordeAbilities.BossSummon:
                Summon(horde, row, action.OneShotSeq, now);
                return true;
            case HordeAbilities.TankStation:
                Station(horde, body, row, action.OneShotSeq, now);
                return true;
            case HordeAbilities.TankRoof:
                Roof(horde, body, row, now);
                return true;
            default:
                return false;
        }
    }

    public void Tick(HordeController horde, long now)
    {
        ArgumentNullException.ThrowIfNull(horde);
        Begin(horde);
        if (horde is not { Bodies: { } bodies, Roster: { } roster })
            return;
        for (int b = 1; b <= bodies.LastBody && b < MaxBodies; b++)
        {
            if (_landAt[b] == 0 || now < _landAt[b])
                continue;
            _landAt[b] = 0;
            var special = _special[b]!;
            _special[b] = null;
            var body = bodies.Body(b);
            if (body.Parked || body.Lease.Serial != _serial[b] || !roster.IsLive(body.Robot)
                || roster.State(body.Robot.Index) == RobotState.Dead || roster.Stunned(body.Robot.Index))
            {
                _refused[(int)BossRefusal.Lost]++;
                continue;
            }

            Land(horde, body, body.Robot.Index, special, now);
        }
    }

    //-------------------------------------------------------------------------

    private void Begin(HordeController horde)
    {
        if (ReferenceEquals(horde, _level))
            return;
        _level = horde;
        Array.Clear(_landAt);
        Array.Clear(_special);
        _summoned.Clear();
        Array.Clear(_refused);
        Telegraphs = Landed = Shots = SlamHits = Summons = Stations = Roofs = RoofHits = 0;
    }

    private void Special(HordeController horde, RobotBody body, int row, int phase, long now)
    {
        var roster = horde.Roster!;
        var bosses = rules.Rules.Bosses;
        if (bosses.Of(roster.Template(row)) is not { } script || bosses.SpecialOf(script, phase) is not { } special)
        {
            _refused[(int)BossRefusal.NoSpecial]++;
            return;
        }

        int b = body.Index;
        if ((uint)b >= MaxBodies)
            return;
        if (_landAt[b] != 0)
        {
            _refused[(int)BossRefusal.WindingUp]++;
            return;
        }

        _landAt[b] = now + BossRules.Ticks(special.WindupSeconds, roster.TicksPerSecond);
        _serial[b] = body.Lease.Serial;
        _special[b] = special;
        Telegraphs++;
        world.Sound(body.Handle, special.Sound);
        if (special.Particle.Length > 0)
            world.Particle(special.Particle, HordeVectors.ToEngine(body.Position) + new Vector(0f, 0f, 8f));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] telegraph: {roster.Template(row)} winds up {special.Id} (phase {phase}) for {special.WindupSeconds} s, tick {now}, lands {_landAt[b]}\n"));
    }

    private void Land(HordeController horde, RobotBody body, int row, BossSpecial special, long now)
    {
        var roster = horde.Roster!;
        var stats = roster.Stats(row);
        float damage = (float)(stats.Damage * special.DamageShare);
        Landed++;
        if (special.Kind == BossSpecial.Volley)
        {
            var attack = rules.Rules.Attacks.Of(special.Attack);
            Vector eye = RobotAim.Eye(body), fwd = RobotAim.Forward(body);
            float clear = RobotHull.Maxs.X * body.Scale + 16f;
            int fired = 0;
            for (int s = 0; s < special.Shots; s++)
            {
                float off = (float)special.FanDegrees * (s - (special.Shots - 1) * 0.5f);
                Vector dir = Turned(fwd, off);
                if (_hands?.Launch(body, attack.Projectile, eye + dir * clear, dir * (float)attack.Speed, damage, special.Crit) is { } made
                    && made != EntityHandle.None)
                    fired++;
            }

            Shots += fired;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Boss] {special.Id} lands: {roster.Template(row)} fires {fired} of {special.Shots} {attack.Projectile}{(special.Crit ? " (crit)" : "")} at {damage:0.#} each, tick {now}\n"));
            return;
        }

        // A slam: each hero inside the radius with a clear line.
        if (special.Particle.Length > 0)
            world.Particle(special.Particle, HordeVectors.ToEngine(body.Position) + new Vector(0f, 0f, 8f));
        int n = heroes.Gather(horde.Level!, _places, _eyes);
        Vector at = HordeVectors.ToEngine(body.Position) + new Vector(0f, 0f, 40f * body.Scale);
        int hit = 0;
        for (int h = 0; h < n; h++)
        {
            Vector to = _eyes[h].Eye;
            var d = to - at;
            if (d.Length > special.Radius || !world.Clear(at, to, body.Handle, _eyes[h].Player))
                continue;
            world.Hurt(_eyes[h].Player, body.Handle, damage, RobotDamageKinds.BitsOf(special.DamageType), d * (1f / Math.Max(1f, d.Length)));
            hit++;
        }

        SlamHits += hit;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] {special.Id} lands: {roster.Template(row)} hits {hit} hero(es) within {special.Radius} for {damage:0.#} {special.DamageType}, tick {now}\n"));
    }

    private void Summon(HordeController horde, int row, int phase, long now)
    {
        var roster = horde.Roster!;
        if (rules.Rules.Bosses.Of(roster.Template(row)) is not { } script)
        {
            _refused[(int)BossRefusal.NoSpecial]++;
            return;
        }

        if (!_summoned.Add((row, phase, encounter.Row == row ? encounter.Resets : 0)))
        {
            _refused[(int)BossRefusal.Summoned]++;
            return;
        }

        Summons++;
        ulong seed = roster.Seed(row) ^ ((ulong)(phase + 1) * 0xD1B54A32D192ED03UL);
        int k = 0;
        foreach (var add in BossRules.AddsOf(script, phase))
            adds.Summon(horde, row, add.Template, add.Count, seed + (ulong)k++ * 0x632BE59BD9B4E019UL, now);
    }

    private void Station(HordeController horde, RobotBody body, int row, int station, long now)
    {
        var s = rules.Rules.Bosses.Settings;
        Stations++;
        world.Sound(body.Handle, s.Sounds.TankStop);
        world.Sound(body.Handle, s.Sounds.TankDrill);
        int n = heroes.Gather(horde.Level!, _places, _eyes);
        int room = horde.Roster!.HomeRoom(row);
        for (int h = 0; h < n; h++)
            if (_places[h].Room == room)
                world.Notice(_places[h].Seat, DrillToken, "");
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] tank: station {station} reached at ({body.Position.X:0},{body.Position.Y:0}), drilling, tick {now}\n"));
    }

    private void Roof(HordeController horde, RobotBody body, int row, long now)
    {
        var s = rules.Rules.Bosses.Settings;
        Roofs++;
        world.Sound(body.Handle, s.Sounds.Roof);
        int n = heroes.Gather(horde.Level!, _places, _eyes);
        int room = horde.Roster!.HomeRoom(row);
        int hit = 0;
        for (int h = 0; h < n; h++)
        {
            if (_places[h].Room != room)
                continue;
            world.Notice(_places[h].Seat, RoofToken, "");
            world.Hurt(_eyes[h].Player, body.Handle, (float)s.Tank.RoofDamage, DamageType.Blast, new Vector(0f, 0f, -1f));
            hit++;
        }

        RoofHits += hit;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] tank: the last pillar falls: the roof comes down on {hit} hero(es) for {s.Tank.RoofDamage}, tick {now}\n"));
    }

    /// <summary><paramref name="d"/> turned by <paramref name="degrees"/> about the vertical.</summary>
    private static Vector Turned(Vector d, float degrees)
    {
        float r = degrees * (MathF.PI / 180f), c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector(d.X * c - d.Y * s, d.X * s + d.Y * c, d.Z);
    }

    /// <summary>The notices the Tank shows.</summary>
    public const string DrillToken = "#Descent_Boss_TankDrill", RoofToken = "#Descent_Boss_Roof";

    /// <summary>The verbs' counters as one line for <c>tf2_descent_boss</c>.</summary>
    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"specials: telegraphs {Telegraphs}, landed {Landed}, volley shots {Shots}, slam hits {SlamHits}; summons {Summons}; tank stations {Stations}, roofs {Roofs} (hitting {RoofHits}); refused: {string.Join(", ", Enum.GetValues<BossRefusal>().Select(r => $"{r} {_refused[(int)r]}"))}");
}
