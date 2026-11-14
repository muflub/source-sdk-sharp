namespace SourceSharp.Shared;

/// <summary>
/// The one answer both sides give to "do these two players block each
/// other's movement" under a mode whose teammates walk through each other
/// (plan A.7, ruled 2026-11-02: "team members can walk through each other").
/// The server's <c>TeammatesPassRule</c> and the client's prediction rule ask
/// it with the same inputs, so the predicted player moves as the server moves
/// it.
/// </summary>
public static class TeammateCollision
{
    /// <summary><c>COLLISION_GROUP_PLAYER_MOVEMENT</c>: the group every movement hull trace carries.</summary>
    public const int PlayerMovementGroup = 8;

    /// <summary><c>FIRST_GAME_TEAM</c>: unassigned (0) and spectators (1) are nobody's teammates.</summary>
    public const int FirstGameTeam = 2;

    /// <summary>
    /// Whether a movement trace of a player on <paramref name="moverTeam"/>
    /// passes through a player on <paramref name="entityTeam"/>: only the
    /// player-movement group, only while teammates pass, and only two players
    /// on the same game team. A team of -1 (not a player) is nobody's teammate.
    /// </summary>
    public static bool Passes(bool teammatesPass, int collisionGroup, int moverTeam, int entityTeam)
        => teammatesPass
           && collisionGroup == PlayerMovementGroup
           && moverTeam >= FirstGameTeam
           && moverTeam == entityTeam;
}
