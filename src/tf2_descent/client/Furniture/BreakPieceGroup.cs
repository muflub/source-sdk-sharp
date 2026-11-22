//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

namespace TF2.Descent.Client.Furniture;

/// <summary>
/// TF's arm of the client's <c>BreakModelCreateSingle</c> (physpropclientside.cpp, under
/// <c>TF_CLIENT_DLL</c>): every broken piece is COLLISION_GROUP_DEBRIS, so a crate's or a barrel's
/// pieces pass each other, the ragdolls and the players, and lie where they fall. Registered by the
/// mod, whose breaks they are; the TF2 port registers no rule of its own.
/// </summary>
[SideService(Side.Client)]
public sealed class TFBreakPieceGroup : IBreakPieceGroupRule, IOrderedHook
{
    /// <summary>In the client furniture's block, after the breakables' drawing (34, 35).</summary>
    public const int Order = 36;

    public int HookOrder => Order;

    public EngineCollisionGroup Group(EngineCollisionGroup group) => EngineCollisionGroup.Debris;
}
