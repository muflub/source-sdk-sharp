namespace SourceSharp.Abi;

/// <summary>
/// Fixed numbers the engine is built around.
///
/// <para>
/// Constants, not calls — which is why they are here rather than on an area
/// interface. There is nothing to fake about the number of player slots: it is
/// the same number in a test as in a game, and an interface offering it would
/// invite somebody to answer differently and produce a test that proves nothing
/// about the engine it is standing in for.
/// </para>
/// </summary>
public static class EngineLimits
{
    /// <summary>
    /// How many player slots a walk over the players covers — Source's own
    /// MAX_PLAYERS for a build that is neither Counter-Strike nor TF, from
    /// shareddefs.h.
    ///
    /// An over-count costs nothing but iterations: the native side resolves a
    /// slot through UTIL_PlayerByIndex, which bounds-checks against the
    /// server's actual maxClients and answers null beyond it.
    /// </summary>
    public const int MaxPlayerSlots = 33;

    /// <summary>
    /// <c>MAX_EDICTS</c> from const.h: how many entities the engine can hold at
    /// once, on both hierarchies together. A hard stop rather than a tuning
    /// number — running past it ends the level.
    ///
    /// <para>
    /// Checked against the header by <c>CollisionGroupParityTests</c>: it is a
    /// COPY of a number the engine owns, and a copy of a hard limit that nobody
    /// checks is worse than no limit at all.
    /// </para>
    /// </summary>
    public const int MaxEdicts = 2048;

    /// <summary>
    /// <c>MAX_TEAMS</c> from <c>game/shared/shareddefs.h</c>: how many team
    /// indices exist, the bound on the player resource's team-score run
    /// (tf2 stage 0.7 part C). <c>MAX_TEAMS_ARRAY_SAFE</c> equals it — unlike
    /// the player arrays, there is no +1 row to spare here.
    /// </summary>
    public const int MaxTeams = 32;
}
