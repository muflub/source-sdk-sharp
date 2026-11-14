using TF2.Shared;

namespace TF2.Shared.Rules;

/// <summary>
/// A player's team, whoever holds it: the server's roster (<c>ITeamRoundRoster
/// </c>'s <c>TeamOf</c>, stock's <c>CBaseEntity::GetTeamNumber</c>) or the
/// client's player-resource row (<c>g_TF_PR-&gt;GetTeamNumber</c>). The
/// movement's mask arm asks it on both sides — stock's
/// <c>CTFGameMovement::PlayerSolidMask</c> switches on the mover's team
/// (tf_gamemovement.cpp:271-281) — so the replay's mask is the server's, the
/// same shape as <see cref="ITFRoundStateView"/>'s two halves. The REAL team
/// throughout: a disguised spy's <c>DisguiseTeam</c> is the HUD's word, never
/// this one (tf_player.cpp:10164).
/// </summary>
public interface ITFPlayerTeam
{
    /// <summary>The player's team by entity index; <see cref="TFTeam.Unassigned"/> for nobody.</summary>
    TFTeam Of(int playerIndex);
}
