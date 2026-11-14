using SourceSharp.Abi;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde.Champions;

/// <summary>
/// The champions that turn hits aside (plan_tf2_rpg 2f; HO12), members of the guard chain
/// (<see cref="IRobotDamageGuard"/>) from the roster's affixes and champions.json's numbers:
///
/// <list type="bullet">
/// <item><b>Über at low health</b> (<c>uber_at_low_health</c>): the first hit that would take the
/// champion under the row's share of its most life leaves it at that share and turns on its über
/// for the row's seconds, once a life; while it lasts it takes nothing. The body's
/// <see cref="RobotFlags.Uber"/> flag draws TF2's invulnerable sheen, with TF2's sounds on and off
/// (this service's tick ends it).</item>
/// <item><b>Medic-shielded</b> (<c>medic_shield</c>): a hit from inside the row's cone about the
/// champion's facing (the attacker's place, else where the hit landed) takes the row's share less.
/// Nothing draws the shield: the port has no shield prop for a robot (see the hand-back).</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class ChampionGuards(IHordeRules rules, IHordeCurrent horde, IHordeClock clock, IRobotAttackWorld world, IEngineEntities entities)
    : IRobotDamageGuard, IHordeTicker, IOrderedHook
{
    /// <summary>After the hands' own guard (the buster's spin, <see cref="RobotAttacks.Order"/>).</summary>
    public const int Order = ModStages.First + 535;

    /// <summary>TF2's über sounds.</summary>
    public const string UberOn = "TFPlayer.InvulnerableOn", UberOff = "TFPlayer.InvulnerableOff";

    private const int MaxRows = 512;

    private readonly long[] _uberUntil = new long[MaxRows];
    private readonly int[] _uberSerial = new int[MaxRows];
    private readonly int[] _uberBody = new int[MaxRows];

    public int HookOrder => Order;

    private HordeController? _level;

    /// <summary>A new level's horde (a new roster, whose serials start again): every über of the last is forgotten.</summary>
    private void Level(HordeController c)
    {
        if (ReferenceEquals(c, _level))
            return;
        _level = c;
        Array.Clear(_uberUntil);
        Array.Clear(_uberSerial);
        Array.Clear(_uberBody);
    }

    /// <summary>Übers turned on, hits an über took whole, hits a shield cut and the life it saved.</summary>
    public long Ubers { get; private set; }

    public long UberBlocked { get; private set; }

    public long Shielded { get; private set; }

    public double ShieldSaved { get; private set; }

    /// <summary>Whether roster row <paramref name="row"/>'s über is on now.</summary>
    public bool UberOnNow(RobotRoster roster, int row) =>
        (uint)row < MaxRows && _uberSerial[row] == roster.Id(row).Serial && _uberUntil[row] > clock.Tick;

    public double Guard(in RobotGuardHit hit)
    {
        if (horde.Current is not { Roster: { } roster } c || (uint)hit.Row >= MaxRows)
            return hit.Taken;
        Level(c);
        int row = hit.Row;
        double taken = hit.Taken;
        var affixes = roster.Affixes(row);
        if (affixes.IsDefaultOrEmpty)
            return taken;

        foreach (var id in affixes)
        {
            if (!rules.Rules.Content.Champions.TryGet(id, out var a))
                continue;
            if (a.Behaviour == ChampionBehaviours.UberAtLowHealth)
                taken = Uber(hit, roster, a, taken);
            else if (a.Behaviour == ChampionBehaviours.MedicShield)
                taken = Shield(hit, a, taken);
        }

        return taken;
    }

    private double Uber(in RobotGuardHit hit, RobotRoster roster, ChampionAffix a, double taken)
    {
        int row = hit.Row, serial = roster.Id(row).Serial;
        long now = clock.Tick;
        if (_uberSerial[row] == serial)
        {
            if (_uberUntil[row] > now)
            {
                UberBlocked++;
                return 0;
            }

            return taken;   // spent this life
        }

        double floor = hit.Most * a.Param("belowLifePct") / 100;
        if (hit.Life - taken >= floor)
            return taken;

        _uberSerial[row] = serial;
        _uberUntil[row] = now + Math.Max(1, (long)Math.Round(a.Param("seconds") * clock.TicksPerSecond));
        _uberBody[row] = hit.Body.Index;
        if (hit.Body.SetFlag(RobotFlags.Uber, true))
            world.Sound(hit.Body.Handle, UberOn);
        Ubers++;
        return Math.Max(0, hit.Life - Math.Max(1, floor));
    }

    private double Shield(in RobotGuardHit hit, ChampionAffix a, double taken)
    {
        var at = hit.Attacker != EntityHandle.None && hit.Attacker != hit.Body.Handle && entities.IsValid(hit.Attacker)
            ? entities.GetOrigin(hit.Attacker) : hit.Position;
        float dx = at.X - hit.Body.Position.X, dy = at.Y - hit.Body.Position.Y, l = MathF.Sqrt(dx * dx + dy * dy);
        if (l < 1e-3f)
            return taken;
        float yaw = hit.Body.Yaw * (MathF.PI / 180f);
        float cos = (dx * MathF.Cos(yaw) + dy * MathF.Sin(yaw)) / l;
        if (cos < MathF.Cos((float)a.Param("frontDegrees") * (MathF.PI / 180f)))
            return taken;
        double kept = taken * (1 - a.Param("absorbPct") / 100);
        Shielded++;
        ShieldSaved += taken - kept;
        return kept;
    }

    /// <summary>An über that has run out goes off: the flag cleared, TF2's sound.</summary>
    public void Tick(HordeController controller, long now)
    {
        if (controller is not { Roster: { } roster, Bodies: { } bodies })
            return;
        Level(controller);
        int n = Math.Min(roster.Count, MaxRows);
        for (int row = 0; row < n; row++)
        {
            if (_uberUntil[row] == 0 || now < _uberUntil[row])
                continue;
            _uberUntil[row] = 0;
            int b = _uberBody[row];
            if (b < 1 || b > bodies.LastBody)
                continue;
            var body = bodies.Body(b);
            if (body.Robot == roster.Id(row) && body.SetFlag(RobotFlags.Uber, false))
                world.Sound(body.Handle, UberOff);
        }
    }
}
