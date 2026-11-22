using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Mode;

/// <summary>
/// The message of the day on a hero's first level of the session only (user
/// 2026-11-15): the town he joins on may greet him, and every later load in
/// the same session (a cage descent, a hop, a portal home, an admin
/// <c>changelevel</c>) lets him simply arrive. Under another mode every
/// activation shows it, as the core's does.
///
/// <para>
/// "The session" is the engine's user id: a level change keeps it, and every
/// new connection gets a new one. Not the slot and its disconnect: the engine
/// takes every client out of the game at a level change too
/// (<c>ClientDisconnect</c> through <c>RemoveClientFromGame</c>), so the core's
/// <c>OnClientDisconnected</c> fires before each new level (seen live
/// 2026-11-15: "player 1 left" ahead of the level shutdown), and a slot keyed
/// greeting showed the message on every load.
/// </para>
///
/// <para>The core's <see cref="MotdPanel"/> asks once per activation.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroWelcome(IDescentModeState mode, IEnginePlayers players, ILog log) : IMotdGate, IOrderedHook
{
    /// <summary>The one Descent member of the chain, past TF2's band.</summary>
    public const int Order = ModStages.First + 110;

    /// <summary>The user ids already greeted.</summary>
    private readonly HashSet<int> _greeted = [];

    public int HookOrder => Order;

    public bool ShowsMotd(int entIndex)
    {
        // Counted under any mode, so a session that turns Descent mid-way
        // does not greet a player twice. No user id (no player in the slot):
        // nothing to remember him by, so he is greeted.
        int user = players.GetUserId(players.GetHandle(entIndex));
        bool first = user <= 0 || _greeted.Add(user);
        bool shows = first || !mode.Active;
        log.Msg(shows
            ? $"[TF2Descent/Mode] player {entIndex} (user {user}): the message of the day shows (first level of the session)\n"
            : $"[TF2Descent/Mode] player {entIndex} (user {user}): no message of the day (a later level of the session)\n");
        return shows;
    }
}
