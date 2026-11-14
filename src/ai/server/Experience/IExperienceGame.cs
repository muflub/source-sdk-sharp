using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// What the experience feed asks of the game its bots play (plan_bots_teams_tf2
/// Part G, G.3), beside <see cref="IBotGame"/>: whether the game keeps
/// experience at all, and the small values a row is keyed by, which only the
/// game can name: the mode's id, the stage, a player's role and class, a
/// weapon's class, and the round's state. The binding of one game registers
/// the one implementation. Read on the game's thread.
/// </summary>
public interface IExperienceGame
{
    /// <summary>Whether this game's bots keep experience. A game that answers false is never asked anything else, and no file is opened for it.</summary>
    bool Keeps { get; }

    /// <summary>The mode being played, by the id the game's mode catalog names it; "" with none.</summary>
    string Mode { get; }

    /// <summary>The stage of the round: the active objective's index, 0 in a mode with one stage.</summary>
    int Stage { get; }

    /// <summary>A player's team role and class as the game numbers them; a role of 0 is a player on no side, who is not sampled.</summary>
    (int Role, int Class) Of(EntityHandle player);

    /// <summary>The class of the weapon <paramref name="killer"/> holds, as the game numbers it; 0 when it cannot say.</summary>
    int WeaponClass(EntityHandle killer);

    /// <summary>Whether the round has ended and the next has not begun.</summary>
    bool RoundOver { get; }

    /// <summary>The role that won the round that is over; 0 for nobody.</summary>
    int WinningRole { get; }
}
