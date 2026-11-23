//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

namespace TF2.Client.TempEnts;

/// <summary>
/// TF's arm of the client's <c>BreakModelCreateSingle</c> (physpropclientside.cpp:752-754, under
/// <c>TF_CLIENT_DLL</c>): every broken piece is COLLISION_GROUP_DEBRIS, so pieces pass each other,
/// the ragdolls and the players, and lie where they fall.
/// </summary>
[SideService(Side.Client)]
public sealed class TFBreakPieceGroup : IBreakPieceGroupRule, IOrderedHook
{
    /// <summary>The rule's order: TF2's layer, below the mods' range.</summary>
    public const int Order = 1_000_000 - 1_000 + 24;

    public int HookOrder => Order;

    public EngineCollisionGroup Group(EngineCollisionGroup group) => EngineCollisionGroup.Debris;
}
