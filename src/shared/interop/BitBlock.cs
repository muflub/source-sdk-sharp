using System;
using System.Buffers.Binary;

namespace SourceSharp.Abi;

/// <summary>
/// A length-prefixed bit buffer: how a bitstream crosses between managed and
/// native code in ONE call, whole (user ruling 2026-10-12: "prefix all buffers
/// with length bytes so a read or write can be atomic and send the whole
/// buffer in one go"). The block is a little-endian <c>int32</c> count of
/// valid BITS (a bitstream need not end on a byte), then the bits, LSB-first
/// within each byte -- bf_write's order, so native copies the payload into or
/// out of an engine buffer with one bit copy. The native half is
/// <c>ss_bitblock.h</c>. The prefix is only on this crossing; what reaches the
/// engine's buffer is the payload's bits and nothing else.
/// </summary>
public static class BitBlock
{
    /// <summary>The prefix: the payload's length in bits.</summary>
    public const int HeaderBytes = 4;

    /// <summary>The valid bits a block declares.</summary>
    public static int Bits(ReadOnlySpan<byte> block)
        => block.Length < HeaderBytes ? 0 : BinaryPrimitives.ReadInt32LittleEndian(block);

    /// <summary>The block's payload bytes (all of them; <see cref="Bits"/> says how many bits count).</summary>
    public static Span<byte> Payload(Span<byte> block) => block.Length < HeaderBytes ? default : block[HeaderBytes..];

    /// <inheritdoc cref="Payload(Span{byte})"/>
    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> block) => block.Length < HeaderBytes ? default : block[HeaderBytes..];

    /// <summary>Writes the prefix.</summary>
    public static void SetBits(Span<byte> block, int bits) => BinaryPrimitives.WriteInt32LittleEndian(block, bits);

    /// <summary>A reader over the block's declared bits.</summary>
    public static BitReader Reader(ReadOnlySpan<byte> block)
    {
        ReadOnlySpan<byte> payload = Payload(block);
        int bits = Math.Clamp(Bits(block), 0, payload.Length * 8);
        return new BitReader(payload, 0, bits);
    }

    /// <summary>A writer over the block's payload; <see cref="BitWriter.Finish"/> writes the prefix.</summary>
    public static BitWriter Writer(Span<byte> block) => new(block);
}

/// <summary>
/// Reads a bitstream LSB-first, as bf_read does. A read past the end reads
/// zeros, leaves the position at the end and sets <see cref="Overflowed"/>.
/// </summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _end;

    /// <summary>The next bit to read.</summary>
    public int Position;

    /// <summary>A read ran past the end.</summary>
    public bool Overflowed;

    public BitReader(ReadOnlySpan<byte> data, int start, int end)
    {
        _data = data;
        _end = end;
        Position = start;
        Overflowed = false;
    }

    public bool OneBit() => UBitLong(1) != 0;

    public uint UBitLong(int bits)
    {
        if (Position + bits > _end)
        {
            Overflowed = true;
            Position = _end;
            return 0;
        }
        uint v = 0;
        for (int i = 0; i < bits; i++, Position++)
            v |= (uint)((_data[Position >> 3] >> (Position & 7)) & 1) << i;
        return v;
    }

    /// <summary>bf_read::ReadFloat: the 32 raw bits.</summary>
    public float Float() => BitConverter.Int32BitsToSingle(unchecked((int)UBitLong(32)));

    /// <summary>bf_read::ReadShort: sixteen bits, sign-extended.</summary>
    public int Short() => (short)(ushort)UBitLong(16);
}

/// <summary>
/// Writes a bitstream LSB-first into a <see cref="BitBlock"/>'s payload, as
/// bf_write does. A write that does not fit writes nothing and sets
/// <see cref="Overflowed"/>.
/// </summary>
public ref struct BitWriter
{
    private readonly Span<byte> _block;
    private readonly int _capacity;

    /// <summary>Bits written.</summary>
    public int Position;

    /// <summary>A write did not fit.</summary>
    public bool Overflowed;

    public BitWriter(Span<byte> block)
    {
        _block = block;
        _capacity = Math.Max(0, block.Length - BitBlock.HeaderBytes) * 8;
        Position = 0;
        Overflowed = false;
    }

    public void OneBit(bool bit) => UBitLong(bit ? 1u : 0u, 1);

    public void UBitLong(uint value, int bits)
    {
        if (Position + bits > _capacity)
        {
            Overflowed = true;
            return;
        }
        Span<byte> payload = _block[BitBlock.HeaderBytes..];
        for (int i = 0; i < bits; i++, Position++)
        {
            int mask = 1 << (Position & 7);
            if (((value >> i) & 1) != 0)
                payload[Position >> 3] |= (byte)mask;
            else
                payload[Position >> 3] &= (byte)~mask;
        }
    }

    /// <summary>bf_write::WriteFloat: the 32 raw bits.</summary>
    public void Float(float value) => UBitLong(unchecked((uint)BitConverter.SingleToInt32Bits(value)), 32);

    /// <summary>
    /// bf_write::WriteShort (WriteSBitLong over 16 bits): the low fifteen bits
    /// of the value and its SIGN as the sixteenth, so a value out of a short's
    /// range still reads back with the right sign.
    /// </summary>
    public void Short(int value)
    {
        uint low = unchecked((uint)value) & 0x7FFF;
        UBitLong(value < 0 ? low | 0x8000 : low, 16);
    }

    /// <summary>Writes the prefix: the bits written, or -1 when a write overflowed.</summary>
    public int Finish()
    {
        int bits = Overflowed ? -1 : Position;
        BitBlock.SetBits(_block, bits);
        return bits;
    }
}
