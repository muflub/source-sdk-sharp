//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A TELEPORT IS A DISCONTINUITY, AND THE CLIENT HAS TO BE TOLD
/// (plan_entity.md §3.9; <c>ContractBaseline.OpenDefects</c>, "An entity that
/// teleports is drawn sliding to where it went").
///
/// <para>
/// <b>What was wrong.</b> The client draws an entity at a point read out of its
/// own history of networked poses, slightly in the past — that is what
/// interpolation IS. A pose that arrives 500 units from the last one is
/// indistinguishable, to that history, from an entity that MOVED 500 units, so
/// the entity is drawn travelling the whole distance over the interpolation
/// window. Stock answers it by networking a counter that a teleport bumps
/// (<c>CBaseEntity::IncrementInterpolationFrame</c>, <c>baseentity.cpp:8301</c>)
/// and having the client throw its history away when the counter it receives
/// differs from the one it last acted on
/// (<c>C_BaseEntity::IsNoInterpolationFrame</c>, <c>c_baseentity.h:2416</c>).
/// This is that mechanism on this hierarchy, and it is C# on both sides.
/// </para>
///
/// <para>
/// <b>Both halves or neither.</b> The counter with no reader is dead state; the
/// reader with no counter never fires. The pair is
/// <see cref="Bump"/> — server, at the teleport — and
/// <see cref="HasTeleported"/> / <see cref="Acknowledge"/> — client, in
/// <c>SourceSharp.Client.Interpolation.SamplePass</c>.
/// </para>
///
/// <para>
/// <b>Why this is policy and not mechanism.</b> Nothing here is an engine call.
/// It is one word of the entity's own block, and the decision it records —
/// "this move is a jump and not a motion" — is exactly the kind of decision
/// CLAUDE.md says belongs in managed code. The native teleport path
/// (<c>SourceSharp_EntityTeleport</c>) is unchanged and still bumps nothing:
/// the bump happens on the managed side of the crossing, before it, for every
/// caller there is.
/// </para>
/// </summary>
public static class TeleportDiscontinuity
{
    /// <summary>
    /// <c>NOINTERP_PARITY_MAX</c>, <c>game/shared/shareddefs.h:963</c>. The
    /// counter wraps at four so it fits stock's two bits, and the wire prop is
    /// declared that wide in <c>standalone_nettable.h</c>.
    /// </summary>
    public const int ParityMax = 4;

    /// <summary>
    /// <c>NOINTERP_PARITY_MAX_BITS</c>, <c>game/shared/shareddefs.h:964</c> —
    /// how wide <c>m_ubInterpolationFrame</c> goes on the wire. Stated here as
    /// well as in the native table because this is the side that decides what
    /// values the field can hold, and two bits is a consequence of
    /// <see cref="ParityMax"/> rather than an independent choice.
    /// </summary>
    public const int ParityBits = 2;

    /// <summary>
    /// Where <see cref="EngineBlockHead.InterpolationFrame"/> starts, measured
    /// from the base of the block — the offset a write to it must be announced
    /// at, and the same arithmetic the SendProp is built from.
    /// </summary>
    public static int FrameOffset { get; }
        = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.InterpolationFrame));

    /// <summary>
    /// Where <see cref="EngineBlockHead.OldInterpolationFrame"/> starts, from
    /// the base of the block. Never announced: it is the CLIENT's own note of
    /// what it last acted on and crosses nothing.
    /// </summary>
    public static int SeenOffset { get; }
        = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.OldInterpolationFrame));

    /// <summary>
    /// The value after this one. Stock's
    /// <c>(m_ubInterpolationFrame + 1) % NOINTERP_PARITY_MAX</c>, over unsigned
    /// arithmetic so a word that somehow arrived negative still lands inside the
    /// two bits the wire has for it rather than staying negative for ever.
    /// </summary>
    public static int Next(int frame) => (int)(((uint)frame + 1u) % ParityMax);

    /// <summary>
    /// THE SERVER HALF: bump one entity's counter in its block.
    ///
    /// <para>
    /// Answers false — and writes nothing — for a span that is not a whole
    /// block, which is what <c>Engine.GetEntityBlock</c> gives back for an
    /// entity that is not on this hierarchy at all. An SDK entity does not need
    /// it: <c>CBaseEntity::Teleport</c> bumps its own
    /// <c>m_ubInterpolationFrame</c> the stock way.
    /// </para>
    /// </summary>
    /// <param name="block">The entity's engine block.</param>
    /// <param name="frame">The value written, or 0.</param>
    public static bool Bump(Span<byte> block, out int frame)
    {
        frame = 0;

        if (block.Length < EngineBlock.SizeBytes)
            return false;

        frame = Next(MemoryMarshal.Read<int>(block.Slice(FrameOffset, sizeof(int))));
        MemoryMarshal.Write(block.Slice(FrameOffset, sizeof(int)), in frame);
        return true;
    }

    /// <summary>What the block's counter currently reads, or 0.</summary>
    public static int FrameOf(ReadOnlySpan<byte> block)
        => block.Length < EngineBlock.SizeBytes
            ? 0
            : MemoryMarshal.Read<int>(block.Slice(FrameOffset, sizeof(int)));

    /// <summary>What the client last reset on, or 0.</summary>
    public static int SeenBy(ReadOnlySpan<byte> block)
        => block.Length < EngineBlock.SizeBytes
            ? 0
            : MemoryMarshal.Read<int>(block.Slice(SeenOffset, sizeof(int)));

    /// <summary>
    /// THE CLIENT HALF, ASKED: has this entity jumped since the last time the
    /// interpolation pass looked at it?
    ///
    /// <para>
    /// Stock's <c>IsNoInterpolationFrame()</c>, which is exactly the two words
    /// differing. False for a block that is not one, and false on a freshly
    /// zeroed block — a create is <c>fresh</c>'s business and resets the ring
    /// anyway.
    /// </para>
    /// </summary>
    public static bool HasTeleported(ReadOnlySpan<byte> block)
        => block.Length >= EngineBlock.SizeBytes && FrameOf(block) != SeenBy(block);

    /// <summary>
    /// THE CLIENT HALF, ANSWERED: this pass has acted on the counter, so the
    /// next one must not act on it again.
    ///
    /// <para>
    /// Called immediately after the history is thrown away and never before —
    /// acknowledging first and resetting second would leave an entity drawn
    /// sliding for ever if anything between the two returned early.
    /// </para>
    /// </summary>
    public static void Acknowledge(Span<byte> block)
    {
        if (block.Length < EngineBlock.SizeBytes)
            return;

        int frame = FrameOf(block);
        MemoryMarshal.Write(block.Slice(SeenOffset, sizeof(int)), in frame);
    }
}
