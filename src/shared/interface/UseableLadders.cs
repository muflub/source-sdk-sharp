//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// One <c>func_useableladder</c> as the movement loop sees it: stock's
/// <c>CFuncLadder</c> read through <c>GetTopPosition</c>,
/// <c>GetBottomPosition</c>, <c>IsEnabled</c> and <c>DontGetOnLadder</c>.
/// </summary>
/// <param name="Index">The ladder's entity index, which both sides share: the
/// player's networked ladder word names a ladder by it.</param>
/// <param name="Top">The upper mount point, in world space.</param>
/// <param name="Bottom">The lower mount point, in world space.</param>
/// <param name="Enabled">Not disabled by its Disable input (<c>StartDisabled</c> at spawn).</param>
/// <param name="Fake">Spawnflag 1, <c>SF_LADDER_DONTGETON</c>: an auto-mount point
/// that is not really a ladder.</param>
public readonly record struct UseableLadder(int Index, Vector Top, Vector Bottom, bool Enabled, bool Fake);

/// <summary>
/// The three replicated ConVars <c>hl_gamemovement.cpp</c> declares, as one
/// command reads them.
/// </summary>
/// <param name="AutoDismount"><c>sv_autoladderdismount</c> (stock 1).</param>
/// <param name="AutoMountDot"><c>sv_ladderautomountdot</c> (stock 0.4).</param>
/// <param name="UseOnly"><c>sv_ladder_useonly</c> (stock 0).</param>
public readonly record struct LadderConVars(bool AutoDismount, float AutoMountDot, bool UseOnly);

/// <summary>
/// Everything <c>CHL2GameMovement</c>'s ladder code asks of the useable ladders
/// in the world and tells them: stock's <c>CFuncLadder::s_Ladders</c>, each
/// ladder's dismount list, and the two notifications a ladder fires.
///
/// <para>
/// A seam of its own rather than more of <see cref="IMoveWorld"/>, because a
/// loop with no ladders is base <c>CGameMovement</c>: the parity fixture and
/// the blob path run that one, and a loop handed one of these is HL2MP's,
/// whose <c>OnLadder</c> answers from the player's ladder and never from the
/// trace (so brush ladders, <c>CONTENTS_LADDER</c>, do nothing in HL2MP).
/// </para>
/// </summary>
public interface ILadderWorld
{
    /// <summary>How many ladders exist: <c>CFuncLadder::GetLadderCount</c>.</summary>
    int Count { get; }

    /// <summary>Ladder <paramref name="index"/> in creation order: <c>CFuncLadder::GetLadder</c>.</summary>
    UseableLadder At(int index);

    /// <summary>The ladder at entity index <paramref name="entityIndex"/>, if one is there.</summary>
    bool TryGet(int entityIndex, out UseableLadder ladder);

    /// <summary>
    /// Where the ladder's dismount points are, in its own list's order
    /// (<c>CFuncLadder::GetDismount</c>). Empty for an unknown ladder.
    /// </summary>
    IReadOnlyList<Vector> Dismounts(int ladderIndex);

    /// <summary>The ladder ConVars for this command.</summary>
    LadderConVars ConVars { get; }

    /// <summary>
    /// <c>CFuncLadder::PlayerGotOn</c>: the server fires OnPlayerGotOnLadder and
    /// plays the step sound on the player; the client does nothing.
    /// </summary>
    void PlayerGotOn(int ladderIndex, int playerIndex);

    /// <summary><c>CFuncLadder::PlayerGotOff</c>: the server fires OnPlayerGotOffLadder.</summary>
    void PlayerGotOff(int ladderIndex, int playerIndex);
}
