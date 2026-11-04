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
}
