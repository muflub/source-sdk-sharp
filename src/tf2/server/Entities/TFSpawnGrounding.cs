//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Shared;

namespace TF2.Server.Entities;

/// <summary>
/// The spawning player is dropped to the ground under the spot before the
/// core's one-unit lift: <c>CTFGameRules::GetPlayerSpawnSpot</c> calls
/// <c>DropToGround</c> on the spot's origin first
/// (shared/tf/tf_gamerules.cpp:9866-9875, :9960-9984) — a hull trace straight
/// down 500 units, MASK_SOLID, ignoring the player, and the trace's end
/// position is where they go. Without it a player spawning on a ctf_2fort
/// spawn (z 276) hangs in the air ~20 units above the floor (z 256.75).
///
/// <para>
/// Stock's <c>UTIL_TraceHull</c> is here through the <c>TraceBox</c> seam, the
/// same hull trace the <c>TraceHull</c> seam runs (same mask, same collision
/// group) — chosen because <see cref="HullResult"/> carries no end position
/// and the drop needs one. Stock's hulls are the player's scaled by
/// <c>GetModelScale()</c>; every TF2 player's scale is 1, so the selector's
/// unscaled hulls are the same numbers and no scale seam is needed.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ISpawnPlacement)])]
public sealed class TFSpawnGrounding(IEngineWorldQuery world) : ISpawnPlacement, IOrderedHook
{
    /// <summary>The stock trace length (<c>Vector(0, 0, -500)</c>).</summary>
    public const float DropRange = 500f;

    /// <summary>A stage of its own in TF2's thousand (see <see cref="TFSpawnSelector.Order"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 31;

    public int HookOrder => Order;

    /// <summary><c>GetPlayerSpawnSpot</c> before the lift: the position dropped to the ground.</summary>
    public Vector PlaceAt(EntityHandle player, EntityHandle spot, Vector placement)
        => DropToGround(player, placement);

    /// <summary>
    /// <c>CTFGameRules::DropToGround(pMainEnt, vPos, vMins, vMaxs)</c>: a hull
    /// trace from <paramref name="at"/> straight down <see cref="DropRange"/>,
    /// MASK_SOLID, the player ignored and COLLISION_GROUP_NONE (the seam's
    /// group already), answering the trace's end position — the start itself
    /// when the trace meets nothing.
    /// </summary>
    public Vector DropToGround(EntityHandle player, Vector at)
    {
        SurfaceTraceResult trace = world.TraceBox(at, at + new Vector(0f, 0f, -DropRange),
                                                  TFSpawnSelector.HullMin, TFSpawnSelector.HullMax,
                                                  TraceMask.Solid, ignore: player);
        return trace.End;
    }
}
