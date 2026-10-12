//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the viewmodel networks — the arms and gun the local player sees.
///
/// <para>
/// THIS IS WHERE STOCK'S ELEVEN WENT. `baseviewmodel_shared.cpp` declared 11
/// DEFINE_PRED_FIELDs and the plan expected them here verbatim. Four survive;
/// the rest dissolved, each named rather than silently dropped:
/// </para>
///
/// <para>
/// — <c>m_nModelIndex</c>: which v_ model. A pure function of the weapon's
///   <see cref="WeaponNetState.WeaponTypeId"/> through the shared table (and
///   the entity's own typed m_nModelIndex prop still carries it for the
///   renderer, as it does for everything on the hierarchy).
/// — <c>m_flCycle</c>, <c>m_flAnimTime</c>: the animation CLOCK. Stock marks
///   the cycle FTYPEDESC_NOERRORCHECK because the two sides legitimately
///   disagree about it every frame — a field whose compare must always be
///   ignored is not prediction state, it is a client-local clock, and here the
///   client keeps it locally (the native side advances the cycle from the
///   sequence's own frame rate).
/// — <c>m_nSkin</c>, <c>m_nBody</c>: no v_ model of the nine uses either.
/// — <c>m_fEffects</c>: stock toggles EF_NODRAW here; visibility of a
///   viewmodel is "is this my player's", which the client derives.
/// — <c>m_nViewModelIndex</c>: which of several viewmodels — this mod has one.
/// — <c>m_hWeapon</c> survives as <see cref="OwnerIndex"/>: the weapon and the
///   viewmodel share an owner, and the pairing goes through the player.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsViewModel)]
public partial class ViewModelNetState
{
    /// <summary>Entindex of the player whose eyes this belongs to.</summary>
    [Predicted]
    public partial int OwnerIndex { get; set; }

    /// <summary>The sequence being played, in the CURRENT v_ model's numbering.</summary>
    [Predicted]
    public partial int Sequence { get; set; }

    /// <summary>
    /// Bumped when the same sequence is sent again, so a second shot restarts
    /// the fire animation rather than being invisible. Stock's field, kept for
    /// stock's reason.
    /// </summary>
    [Predicted]
    public partial int AnimationParity { get; set; }

    /// <summary>Playback speed. Stock predicts it with a 0.125 tolerance.</summary>
    [Predicted(0.125f)]
    public partial float PlaybackRate { get; set; }
}
