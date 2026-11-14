//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Player;

namespace TF2.Shared.Buildings;

/// <summary>
/// What a buildable tells the client about its build state: <c>m_bPlacing</c>
/// (<c>DT_BaseObject</c>, tf_shareddefs / baseobject's data table — the
/// client's <c>C_ObjectSentrygun::GetShadowRenderBounds</c>,
/// c_obj_sentrygun.cpp:588, keys its collision-hull shadow arm on it). The
/// other build states ride the model and the sound; the shadow arm is the one
/// reader, so this carries the flag alone. One declaration under the sentry's
/// name, the dispenser's, the teleporter's and the sapper's aliases: every
/// buildable carries the same field, as stock's shared <c>DT_BaseObject</c>
/// does.
/// </summary>
[StandaloneNetState(TFEntityNames.SentryGun)]
[StandaloneNetStateAlias(TFEntityNames.Dispenser)]
[StandaloneNetStateAlias(TFEntityNames.Teleporter)]
[StandaloneNetStateAlias(TFEntityNames.Sapper)]
public partial class TFObjectNetState
{
    /// <summary><c>m_bPlacing</c>: not paid for yet — the blueprint is worn and the shadow is the collision hull.</summary>
    public partial bool Placing { get; set; }

    /// <summary>
    /// <c>m_bMiniBuilding</c> (tf_obj.h:336, net in DT_BaseObject): the Gunslinger's
    /// small sentry. The client's arms that read it — the siren effect
    /// (<c>c_obj_sentrygun.cpp:216</c>), the target-ID's mini string and its
    /// no-upgrade-progress arm (<c>c_baseobject.cpp:912</c>, :1013) — are deferred rows
    /// of <c>docs/plans/tf2/deferred.tsv</c>; the flag itself crosses now so those arms
    /// have something to read when they land.
    /// </summary>
    public partial bool Mini { get; set; }

    /// <summary>
    /// <c>GetTeamNumber</c> (baseobject_shared.cpp's <c>StartPlacement</c>: the
    /// building wears its builder's team, and the placement's own arm reads it
    /// back). The client's friendly-building arm (<c>CTFWrench::Smack</c>'s
    /// <c>OnFriendlyBuildingHit</c> test, tf_weapon_wrench.cpp:180) is what
    /// needs it on the wire: the replay asks the struck building's team of
    /// this word, so the swing's predicted mode bit lands the same way the
    /// server's does.
    /// </summary>
    public partial byte Team { get; set; }
}
