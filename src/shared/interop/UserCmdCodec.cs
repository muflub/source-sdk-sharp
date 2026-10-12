//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SourceSharp.Abi;

/// <summary>
/// The server's half of the usercmd wire codec, in C# — plan_server_novalve.md
/// P2.2 (amended by the user: the decode moves, and native is called back once
/// with the work done). A port of <c>ReadUsercmd</c> (usercmd.cpp) and of
/// <c>MD5_PseudoRandom</c> (tier1), which seeds each command's shared random.
///
/// <para>
/// <b>THE FORMAT IS STOCK'S</b> (<see cref="Format"/>, the one place it lives):
/// the client writes commands with <see cref="EncodeToBlock"/>, a port of
/// usercmd.cpp's <c>WriteUsercmd</c>, into the engine's bitstream: each command
/// is a delta against the previous one (the first against an all-zero
/// command), every field behind a presence bit, bits LSB-first. A packet holds
/// <c>totalCmds</c> commands NEWEST FIRST in the array and is read oldest first,
/// so the delta chain runs from index totalCmds-1 down to 0.
/// </para>
///
/// <para>
/// <b>THE CLAMP IS PART OF THE CHAIN.</b> A command whose forward, side or up
/// move exceeds sv_max_usercmd_move_magnitude has all three zeroed right after
/// it is read — and the CLAMPED command is the base the next one is a delta
/// against, exactly as stock's loop (ProcessUsercmds) did it.
/// </para>
///
/// <para>
/// Held to the stock decoder on real traffic by UserCmdCodecTests' corpus
/// (captured from ReadUsercmd itself, in a live run, before it was deleted).
/// </para>
/// </summary>
public static class UserCmdCodec
{
    public const int MaxEdictBits = 11;
    public const int WeaponSubtypeBits = 6;

    /// <summary>
    /// tier1's <c>MD5_PseudoRandom</c>: the MD5 of the seed's four bytes, and
    /// the four bytes at offset 6 of the digest.
    /// </summary>
    public static uint Md5PseudoRandom(uint seed)
    {
        Span<byte> input = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(input, seed);
        Span<byte> digest = stackalloc byte[16];
        MD5.HashData(input, digest);
        return BinaryPrimitives.ReadUInt32LittleEndian(digest.Slice(6, 4));
    }

    /// <summary>The command's random seed, as ReadUsercmd sets it.</summary>
    public static int RandomSeedFor(int commandNumber)
        => (int)(Md5PseudoRandom(unchecked((uint)commandNumber)) & 0x7fffffffu);

    /// <summary>
    /// Decodes <paramref name="totalCmds"/> commands starting at bit
    /// <paramref name="startBit"/> of <paramref name="data"/>, which holds
    /// <paramref name="dataBits"/> valid bits, into <paramref name="cmds"/>
    /// (newest first, as the packet orders them). Answers the bit position
    /// after the last command, or -1 when the stream ran out: a truncated
    /// packet, which the engine treats as overflowed.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> data, int startBit, int dataBits, int totalCmds,
                             float maxMoveMagnitude, Span<DecodedUserCmd> cmds)
    {
        if (totalCmds < 0 || totalCmds > cmds.Length || startBit < 0 || dataBits > data.Length * 8)
            return -1;

        var r = new BitReader(data, startBit, dataBits);
        DecodedUserCmd from = default;

        for (int i = totalCmds - 1; i >= 0; i--)
        {
            DecodedUserCmd to = from;
            ReadOne(ref r, ref to, in from);
            if (r.Overflowed)
                return -1;

            if (MathF.Abs(to.Cmd.ForwardMove) > maxMoveMagnitude ||
                MathF.Abs(to.Cmd.SideMove) > maxMoveMagnitude ||
                MathF.Abs(to.Cmd.UpMove) > maxMoveMagnitude)
            {
                to.Cmd.ForwardMove = 0;
                to.Cmd.SideMove = 0;
                to.Cmd.UpMove = 0;
            }

            cmds[i] = to;
            from = to;
        }

        return r.Position;
    }

    //=========================================================================
    // THE CLIENT'S HALF (plan_client_novalve C9): WriteUsercmd, encoded here
    // and handed to native in one length-prefixed BitBlock, which native
    // copies into the engine's bf_write whole. And the client's ReadUsercmd
    // (CInput::DecodeUserCmdFromBuffer, demo playback), over a BitBlock native
    // copied out of the engine's bf_read.
    //=========================================================================

    /// <summary>
    /// <c>WriteUsercmd</c>: <paramref name="to"/> as a delta against
    /// <paramref name="from"/>, into <paramref name="block"/>. Answers the bits
    /// written (the block's prefix), or -1 when they do not fit.
    /// </summary>
    public static int EncodeToBlock(in DecodedUserCmd to, in DecodedUserCmd from, Span<byte> block)
    {
        BitWriter w = BitBlock.Writer(block);
        Format.Write(ref w, in to, in from);
        return w.Finish();
    }

    /// <summary>
    /// <c>ReadUsercmd</c> against the all-zero command, from a block's bits.
    /// Answers the bits read, or -1 when the block ran out.
    /// </summary>
    public static int DecodeFromBlock(ReadOnlySpan<byte> block, out DecodedUserCmd move)
    {
        BitReader r = BitBlock.Reader(block);
        DecodedUserCmd zero = default;
        move = zero;
        Format.Read(ref r, ref move, in zero);
        return r.Overflowed ? -1 : r.Position;
    }

    private static void ReadOne(ref BitReader r, ref DecodedUserCmd move, in DecodedUserCmd from)
        => Format.Read(ref r, ref move, in from);

    /// <summary>
    /// THE WIRE FORMAT, both directions side by side and nowhere else: the
    /// stock one (usercmd.cpp's WriteUsercmd and ReadUsercmd), every field
    /// behind a presence bit, LSB-first. Both ends are C# now, so a later
    /// commit may replace this one piece (docs/todo.md); until then
    /// UserCmdEncodeTests holds Write to pristine WriteUsercmd bit for bit and
    /// UserCmdCodecTests holds Read to stock's decode.
    /// </summary>
    private static class Format
    {
        public static void Write(ref BitWriter w, in DecodedUserCmd to, in DecodedUserCmd from)
        {
            ref readonly UserCmd t = ref to.Cmd;
            ref readonly UserCmd f = ref from.Cmd;

            if (Changed(ref w, t.CommandNumber != unchecked(f.CommandNumber + 1))) w.UBitLong(unchecked((uint)t.CommandNumber), 32);
            if (Changed(ref w, t.TickCount != unchecked(f.TickCount + 1))) w.UBitLong(unchecked((uint)t.TickCount), 32);

            if (Changed(ref w, t.Pitch != f.Pitch)) w.Float(t.Pitch);
            if (Changed(ref w, t.Yaw != f.Yaw)) w.Float(t.Yaw);
            if (Changed(ref w, t.Roll != f.Roll)) w.Float(t.Roll);

            if (Changed(ref w, t.ForwardMove != f.ForwardMove)) w.Float(t.ForwardMove);
            if (Changed(ref w, t.SideMove != f.SideMove)) w.Float(t.SideMove);
            if (Changed(ref w, t.UpMove != f.UpMove)) w.Float(t.UpMove);

            if (Changed(ref w, t.Buttons != f.Buttons)) w.UBitLong(unchecked((uint)t.Buttons), 32);
            if (Changed(ref w, t.Impulse != f.Impulse)) w.UBitLong(unchecked((uint)t.Impulse) & 0xFF, 8);

            if (Changed(ref w, t.WeaponSelect != f.WeaponSelect))
            {
                w.UBitLong(unchecked((uint)t.WeaponSelect) & ((1u << MaxEdictBits) - 1), MaxEdictBits);
                if (Changed(ref w, to.WeaponSubtype != from.WeaponSubtype))
                    w.UBitLong(unchecked((uint)to.WeaponSubtype) & ((1u << WeaponSubtypeBits) - 1), WeaponSubtypeBits);
            }

            if (Changed(ref w, t.MouseDx != f.MouseDx)) w.Short(t.MouseDx);
            if (Changed(ref w, t.MouseDy != f.MouseDy)) w.Short(t.MouseDy);
        }

        // The presence bit, and whether the value follows.
        private static bool Changed(ref BitWriter w, bool changed)
        {
            w.OneBit(changed);
            return changed;
        }

        public static void Read(ref BitReader r, ref DecodedUserCmd move, in DecodedUserCmd from)
        {
            move.Cmd.CommandNumber = r.OneBit() ? (int)r.UBitLong(32) : unchecked(from.Cmd.CommandNumber + 1);
            move.Cmd.TickCount = r.OneBit() ? (int)r.UBitLong(32) : unchecked(from.Cmd.TickCount + 1);

            if (r.OneBit()) move.Cmd.Pitch = r.Float();
            if (r.OneBit()) move.Cmd.Yaw = r.Float();
            if (r.OneBit()) move.Cmd.Roll = r.Float();

            if (r.OneBit()) move.Cmd.ForwardMove = r.Float();
            if (r.OneBit()) move.Cmd.SideMove = r.Float();
            if (r.OneBit()) move.Cmd.UpMove = r.Float();

            if (r.OneBit()) move.Cmd.Buttons = (int)r.UBitLong(32);
            if (r.OneBit()) move.Cmd.Impulse = (int)r.UBitLong(8);

            if (r.OneBit())
            {
                move.Cmd.WeaponSelect = (int)r.UBitLong(MaxEdictBits);
                if (r.OneBit())
                    move.WeaponSubtype = (int)r.UBitLong(WeaponSubtypeBits);
            }

            move.RandomSeed = RandomSeedFor(move.Cmd.CommandNumber);

            if (r.OneBit()) move.Cmd.MouseDx = r.Short();
            if (r.OneBit()) move.Cmd.MouseDy = r.Short();
        }
    }
}
