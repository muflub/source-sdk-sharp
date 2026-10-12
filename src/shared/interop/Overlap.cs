//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// ONE OVERLAPPING PAIR — an element of the per-frame report
/// (<c>plan_entity.md</c> step 2b, the <c>EntityOverlapReport</c> slot).
///
/// <para>
/// <b>NEITHER END IS "THE TOUCHER".</b> An overlap is symmetric and the engine
/// reports it as one: <c>MarkEntitiesAsTouching</c> takes two edicts and gives
/// each of them a link naming the other. Naming one field <c>Toucher</c> would
/// be inventing a direction the partition does not have, and the directory's
/// table is keyed by the pair for the same reason.
/// </para>
///
/// <para>
/// <b>TWO <see cref="EntityHandle"/>s AND NOTHING ELSE, laid out sequentially,
/// because the native side writes this array directly.</b> Element 2n and 2n+1
/// of the <c>uint32</c> buffer are the two ends of pair n; the fixed width is
/// the ABI's rule (<c>uint32_t</c>, never <c>unsigned</c>) and
/// <c>TouchLinkTests.APairIsTwoHandlesWide</c> is what holds this declaration
/// to it.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct OverlapPair : IEquatable<OverlapPair>
{
    // RAW uint32s, NOT EntityHandles: native writes these words, and an
    // EntityHandle's storage is the raw handle inverted (Entity.cs), so the
    // conversion happens in the two accessors and never over the buffer.
    private readonly uint _a;
    private readonly uint _b;

    public OverlapPair(EntityHandle a, EntityHandle b)
    {
        _a = a.Value;
        _b = b.Value;
    }

    /// <summary>One end.</summary>
    public EntityHandle A => new(_a);

    /// <summary>The other.</summary>
    public EntityHandle B => new(_b);

    /// <summary>
    /// Equal irrespective of which way round the two ends are, because the pair
    /// is what this names and the report may name it either way.
    /// </summary>
    public bool Equals(OverlapPair other)
        => (A == other.A && B == other.B) || (A == other.B && B == other.A);

    public override bool Equals(object? obj) => obj is OverlapPair other && Equals(other);

    public override int GetHashCode()
        => A.Value <= B.Value
            ? HashCode.Combine(A.Value, B.Value)
            : HashCode.Combine(B.Value, A.Value);

    public static bool operator ==(OverlapPair left, OverlapPair right) => left.Equals(right);
    public static bool operator !=(OverlapPair left, OverlapPair right) => !left.Equals(right);

    public override string ToString() => $"({A.Value:X8}, {B.Value:X8})";
}
