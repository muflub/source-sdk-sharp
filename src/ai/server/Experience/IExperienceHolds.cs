namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// Where the game lets the experience feed count a hold (plan_bots_teams_tf2_part_g G.3's <c>spot</c> rows of the kind
/// "hold", lane G4b): the kind's number as the game numbers its spots, and whether a place a player of a side held is near
/// that side's own objective (a flag at home, a point it owns). A game that registers none gathers no hold. Read on the
/// game's thread.
/// </summary>
public interface IExperienceHolds
{
    /// <summary>The spot kind a hold is written as.</summary>
    int HoldKind { get; }

    /// <summary>Whether a hold at <paramref name="at"/> by a player of <paramref name="role"/> is near its side's own objective.</summary>
    bool NearOwn(int role, AiVector at);
}
