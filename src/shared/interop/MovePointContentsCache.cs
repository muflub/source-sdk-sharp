//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// Stock's <c>CGameMovement::GetPointContentsCached</c> (gamemovement.cpp):
/// three slots, one per height CheckWater asks about, each answering again
/// for a point within a unit of the one it last asked for. Stock's cache is
/// per player and every ProcessMovement resets all of them, so within one
/// command it is one player's three slots: this is that, and the movement
/// loop resets it at the top of each command
/// (<see cref="IMoveWorld.ResetPointContentsCache"/>). It was native's, in
/// CShellGameMovement, until 2026-10-19; the client's replay never reached
/// native's reset, which stock's client does on every command.
/// </summary>
public sealed class MovePointContentsCache
{
    /// <summary><c>MAX_PC_CACHE_SLOTS</c>.</summary>
    public const int Slots = 3;

    private const int Unset = -9999;

    private readonly int[] _contents = new int[Slots];
    private readonly Vector[] _points = new Vector[Slots];
    private bool _optimizations = true;

    // Whether the slots hold Unset or answers. A new cache's slots are zeros,
    // which would read as answers; the first Reset or Store fills them with
    // Unset first, so a constructor need not (it only stores, by the interop
    // layer's rule).
    private bool _primed;

    /// <summary>
    /// <c>ResetGetPointContentsCache</c>; <paramref name="optimizations"/> is
    /// <c>g_bMovementOptimizations</c>, without which nothing is cached.
    /// </summary>
    public void Reset(bool optimizations)
    {
        _optimizations = optimizations;
        Array.Fill(_contents, Unset);
        _primed = true;
    }

    /// <summary>The slot's answer, if it still holds for <paramref name="point"/>.</summary>
    public bool TryGet(in Vector point, int slot, out int contents)
    {
        contents = 0;
        if (!_optimizations || !_primed || (uint)slot >= Slots || _contents[slot] == Unset)
            return false;

        Vector last = _points[slot];
        float dx = point.X - last.X, dy = point.Y - last.Y, dz = point.Z - last.Z;
        if (dx * dx + dy * dy + dz * dz > 1)
            return false;

        contents = _contents[slot];
        return true;
    }

    /// <summary>What the engine answered for <paramref name="point"/>, kept in the slot.</summary>
    public void Store(in Vector point, int slot, int contents)
    {
        if (!_optimizations || (uint)slot >= Slots)
            return;

        if (!_primed)
            Reset(_optimizations);

        _contents[slot] = contents;
        _points[slot] = point;
    }
}
