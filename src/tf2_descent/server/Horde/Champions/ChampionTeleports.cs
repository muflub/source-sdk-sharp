using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Server.Horde.Champions;

/// <summary>
/// The teleporting champion (plan_tf2_rpg 2f's <i>Teleporting</i>, Diablo's; HO12), a member of
/// the robot hit chain: a hit from a hero farther than the row's <c>fromUnits</c>, once its
/// <c>cooldownSeconds</c> have passed, blinks the champion to a spot <c>toUnits</c> from that hero
/// on the line back towards where it stood, on the floor there, where its hull fits and the hero's
/// eye sees it. TF2's teleporter sparkles mark where it left and where it came, with the
/// teleporter's sounds. Its motion starts again from the new spot
/// (<see cref="RobotMotion.Restart"/>), as at a lend. A spot that fails any test is not used: it
/// stays, and tries at the next hit.
/// </summary>
[SideService(Side.Server)]
public sealed class ChampionTeleports(IHordeRules rules, IHordeCurrent horde, IRobotAttackWorld world, IEngineEntities entities, IHordeClock clock,
                                      RobotMotion motion) : IRobotHitSink, IOrderedHook
{
    public const int Order = ModStages.First + 545;

    /// <summary>TF2's teleporter look: BLU's sparkles out and in, and the send and receive sounds.</summary>
    public const string Out = "teleported_blue", In = "teleportedin_blue", SendSound = "Building_Teleporter.Send",
                        ReceiveSound = "Building_Teleporter.Receive";

    private const int MaxRows = 512;

    private readonly long[] _ready = new long[MaxRows];
    private readonly int[] _serial = new int[MaxRows];

    public int HookOrder => Order;

    private HordeController? _level;

    /// <summary>Blinks made, and spots refused.</summary>
    public long Blinks { get; private set; }

    public long Refused { get; private set; }

    public void OnRobotHit(in RobotHit hit)
    {
        if (hit.Killed || hit.Attacker == EntityHandle.None || !entities.IsValid(hit.Attacker) || !entities.IsPlayer(hit.Attacker)
            || horde.Current is not { Roster: { } roster, Bodies: { } bodies } c || (uint)hit.Robot.Index >= MaxRows || hit.Body < 1 || hit.Body > bodies.LastBody)
            return;
        if (!ReferenceEquals(c, _level))
        {
            // A new level's roster: its serials start again, so the last level's cooldowns are forgotten.
            _level = c;
            Array.Clear(_ready);
            Array.Clear(_serial);
        }

        int row = hit.Robot.Index;
        if (Teleporter(roster, row) is not { } affix)
            return;
        long now = clock.Tick;
        if (_serial[row] == hit.Robot.Serial && now < _ready[row])
            return;

        var body = bodies.Body(hit.Body);
        var hero = entities.GetOrigin(hit.Attacker);
        var from = HordeVectors.ToEngine(body.Position);
        var away = new Vector(from.X - hero.X, from.Y - hero.Y, 0f);
        float d = away.Length;
        if (d <= affix.Param("fromUnits"))
            return;

        _serial[row] = hit.Robot.Serial;
        _ready[row] = now + Math.Max(1, (long)Math.Round(affix.Param("cooldownSeconds") * clock.TicksPerSecond));
        if (Spot(hero, away * (1f / d), (float)affix.Param("toUnits"), body) is not { } to)
        {
            Refused++;
            return;
        }

        world.Particle(Out, from);
        world.Sound(body.Handle, SendSound);
        float yaw = MathF.Atan2(hero.Y - to.Y, hero.X - to.X) * (180f / MathF.PI);
        body.MoveTo(HordeVectors.ToAi(to), yaw);
        motion.Restart(body.Index);
        world.Particle(In, to);
        world.Sound(body.Handle, ReceiveSound);
        Blinks++;
    }

    /// <summary>A floor spot <paramref name="reach"/> from <paramref name="hero"/> along <paramref name="toward"/>, the hero's eye seeing it and the body's hull fitting; null for none.</summary>
    private Vector? Spot(Vector hero, Vector toward, float reach, RobotBody body)
    {
        var eye = hero + new Vector(0f, 0f, 64f);
        var aim = hero + toward * reach + new Vector(0f, 0f, 64f);
        var seen = world.Line(eye, aim, EntityHandle.None);
        if (seen.Hit && seen.Fraction < 0.98f)
            return null;
        var down = world.Line(aim, aim - new Vector(0f, 0f, 256f), EntityHandle.None);
        if (!down.Hit)
            return null;
        var floor = down.End + new Vector(0f, 0f, 1f);
        float half = RobotHull.Maxs.X * body.Scale;
        var fits = world.Box(floor + new Vector(0f, 0f, half + 2f), floor + new Vector(0f, 0f, half + 4f), half, body.Handle);
        return fits.Hit && fits.Fraction <= 0f ? null : floor;
    }

    private ChampionAffix? Teleporter(TF2.Descent.Ai.Roster.RobotRoster roster, int row)
    {
        var affixes = roster.Affixes(row);
        if (affixes.IsDefaultOrEmpty)
            return null;
        foreach (var id in affixes)
            if (rules.Rules.Content.Champions.TryGet(id, out var a) && a.Behaviour == ChampionBehaviours.Teleport)
                return a;
        return null;
    }
}
