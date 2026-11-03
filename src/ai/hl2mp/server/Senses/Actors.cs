using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>A bot the senses serve this tick: its actor, its player, how it sees.</summary>
public readonly record struct SensedBot(ActorId Id, EntityHandle Player, VisionParams Vision);

/// <summary>A live player this tick, bot or human, as an actor.</summary>
public readonly record struct SensedActor(ActorId Id, EntityHandle Player);

/// <summary>
/// Who is watching, not playing (§4.1, ruling 2026-11-02: "bots should ignore
/// spectators as well"): team spectator, or in an observer mode while not dead. A
/// player dead and waiting to respawn is in an observer mode too and is still in the
/// game: the kill feed and the scoreboard go on having it.
/// </summary>
public static class Spectators
{
    public static bool Is(int team, ObserverMode mode, bool alive) => team == Observers.TeamSpectator || (mode != ObserverMode.None && alive);

    public static bool Is(IEnginePlayers players, EntityHandle player)
        => player != EntityHandle.None && Is(players.GetTeam(player), players.GetObserverMode(player), players.IsAlive(player));
}

/// <summary>
/// The players as actors (plan_hl2mp_ai §2.2): every connected, living player
/// in the game by slot, no spectator: what is not here has no sight lines, no
/// reads, no place in anyone's way (GiveWay's list), and its camera is never read. A bot is the actor the binding made it; a human's serial is its
/// player handle's, marked so it never equals a bot's. Reads no position:
/// that is the vision pass's.
/// </summary>
[SideService(Side.Server)]
public sealed class ActorDirectory(IEnginePlayers players, IEngineEnvironment environment)
{
    /// <summary>Marks a human's serial (the handle's serial bits are 19 wide).</summary>
    public const int HumanSerialBit = 0x4000_0000;

    /// <summary>The bits of a handle above its entry: its serial.</summary>
    public const int EntryBits = 13;

    /// <summary>A human's actor in <paramref name="slot"/> with <paramref name="player"/>.</summary>
    public static ActorId HumanActor(int slot, EntityHandle player) => new(slot, HumanSerialBit | (int)(player.Value >> EntryBits));

    /// <summary>
    /// Fills <paramref name="into"/> with every living player, slot order; a
    /// bot's actor from <paramref name="botOf"/> (by slot), else a human's.
    /// </summary>
    public void Living(List<SensedActor> into, Func<int, ActorId> botOf)
        => Living(players, environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0, into, botOf);

    /// <summary>The same over <paramref name="max"/> slots.</summary>
    public static void Living(IEnginePlayers players, int max, List<SensedActor> into, Func<int, ActorId> botOf)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(botOf);
        into.Clear();
        for (int slot = 1; slot <= max; slot++)
        {
            EntityHandle p = players.GetHandle(slot);
            if (p == EntityHandle.None || !players.IsAlive(p) || players.GetObserverMode(p) != ObserverMode.None || Spectators.Is(players, p))
                continue;
            ActorId bot = botOf(slot);
            into.Add(new SensedActor(bot.IsValid ? bot : HumanActor(slot, p), p));
        }
    }
}
