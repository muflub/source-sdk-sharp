using SourceSharp.Abi;

using TF2.Descent.Server.Bestiary;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// IMMUNE, told to the hero who hit (plan_tf2_rpg R25: "a hit on an immune robot says IMMUNE
/// rather than a silent zero"; plan_descent_logic HO8). A member of the robot hit chain: an
/// immune hit (<see cref="RobotHit.Immune"/>) by a player is told to that player's client
/// alone, with the body's entity index and the damage type
/// (<see cref="ClientMessages.RobotImmune"/>), so his robot bar shows IMMUNE over
/// that robot (UI6).
///
/// <para>
/// THE THROTTLE: at most one notice a hero a second (the server's ticks a second), whichever
/// robot he hits, so a flamethrower's stream tells him once a second and not every tick. The
/// notice holds on screen longer than that, so a steady stream reads as one line.
/// </para>
///
/// <para>
/// WHY NOT TF2'S COMBAT TEXT: it draws numbers, read from <c>player_hurt</c>, which a robot
/// never raises, and TF2 has no IMMUNE art (its <c>miss_text</c> particle says MISS). Until
/// UI6 the word went on the mod's notice line (<see cref="ImmuneToken"/>); UI6 moved it over
/// the robot.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class RobotImmuneNotices(ClientMessages messages, IEngineEntities entities, IHordeClock clock, IHordeRules rules,
                                       IRobotFactsSource bodies)
    : IRobotHitSink, IOrderedHook
{
    /// <summary>After the brain hears the hit (<see cref="Senses.HordeHitSense"/>, First + 515), before the blast's push.</summary>
    public const int Order = ModStages.First + 517;

    /// <summary>The notice line's token before UI6; its <c>%s1</c> is the damage type's name.</summary>
    public const string ImmuneToken = "#Descent_Robot_Immune";

    /// <summary>Player slots the throttle keeps a clock for.</summary>
    public const int Slots = 65;

    private readonly long[] _lastTold = new long[Slots];

    public int HookOrder => Order;

    /// <summary>Notices sent, and immune hits not told for the throttle.</summary>
    public long Told { get; private set; }

    public long Held { get; private set; }

    public void OnRobotHit(in RobotHit hit)
    {
        if (!hit.Immune || hit.Attacker == EntityHandle.None || !entities.IsValid(hit.Attacker) || !entities.IsPlayer(hit.Attacker))
            return;

        int player = entities.GetIndex(hit.Attacker);
        if ((uint)player >= Slots)
            return;

        long now = clock.Tick;
        long gap = Math.Max(1, (long)Math.Round(clock.TicksPerSecond));
        long last = _lastTold[player];
        if (last != 0 && now >= last && now - last < gap)
        {
            Held++;
            return;
        }

        _lastTold[player] = Math.Max(1, now);
        var types = rules.Rules.Content.DamageTypes.Rows;
        int type = -1;
        for (int t = 0; t < types.Count && type < 0; t++)
            if (types[t].Id == hit.DamageType)
                type = t;
        messages.RobotImmune(player, bodies.EntityOfBody(hit.Body), type);
        Told++;
    }
}
