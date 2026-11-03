//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// The ICE block cipher (Matthew Kwan's public-domain algorithm, 1996) as
/// Valve's <c>src/mathlib/IceKey.cpp</c> carries it: what stock decrypts the
/// <c>.ctx</c> game scripts with (<see cref="EncryptedKeyValues"/>).
///
/// <para>
/// Level 0 is "thin ICE" (an 8-byte key, 8 rounds), level n &gt;= 1 an
/// 8n-byte key over 16n rounds. The block is 8 bytes. Stock keeps the S-boxes
/// in a process-wide table filled on first use; here each key builds its own
/// (4 x 1024 words, computed once per instance), so no static state exists.
/// </para>
/// </summary>
public sealed class IceKey
{
    private const int BlockBytes = 8;

    private readonly int _size;
    private readonly int _rounds;

    // ice_sbox[4][1024], flattened.
    private readonly uint[] _sbox = new uint[4 * 1024];

    // The key schedule: three 20-bit words per round (IceSubkey::val).
    private readonly uint[] _schedule;

    public IceKey(int level)
    {
        if (level < 1)
        {
            _size = 1;
            _rounds = 8;
        }
        else
        {
            _size = level;
            _rounds = level * 16;
        }

        _schedule = new uint[_rounds * 3];
        BuildSBoxes();
    }

    /// <summary>The key length in bytes (<c>IceKey::keySize</c>).</summary>
    public int KeySize => _size * 8;

    /// <summary>The block length in bytes (<c>IceKey::blockSize</c>).</summary>
    public int BlockSize => BlockBytes;

    // ice_smod / ice_sxor / ice_pbox / ice_keyrot: constant data, no fields.
    private static ReadOnlySpan<short> SMod =>
    [
        333, 313, 505, 369,
        379, 375, 319, 391,
        361, 445, 451, 397,
        397, 425, 395, 505,
    ];

    private static ReadOnlySpan<byte> SXor =>
    [
        0x83, 0x85, 0x9b, 0xcd,
        0xcc, 0xa7, 0xad, 0x41,
        0x4b, 0x2e, 0xd4, 0x33,
        0xea, 0xcb, 0x2e, 0x04,
    ];

    private static ReadOnlySpan<uint> PBox =>
    [
        0x00000001, 0x00000080, 0x00000400, 0x00002000,
        0x00080000, 0x00200000, 0x01000000, 0x40000000,
        0x00000008, 0x00000020, 0x00000100, 0x00004000,
        0x00010000, 0x00800000, 0x04000000, 0x20000000,
        0x00000004, 0x00000010, 0x00000200, 0x00008000,
        0x00020000, 0x00400000, 0x08000000, 0x10000000,
        0x00000002, 0x00000040, 0x00000800, 0x00001000,
        0x00040000, 0x00100000, 0x02000000, 0x80000000,
    ];

    private static ReadOnlySpan<byte> KeyRot =>
    [
        0, 1, 2, 3, 2, 1, 3, 0,
        1, 3, 2, 0, 3, 1, 0, 2,
    ];

    /// <summary>
    /// <c>IceKey::set</c>: builds the schedule from a key of exactly
    /// <see cref="KeySize"/> bytes (stock reads that many and trusts the caller;
    /// a short key is refused here rather than read past).
    /// </summary>
    public void Set(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"an ICE key of this level is {KeySize} bytes, not {key.Length}", nameof(key));

        Span<ushort> kb = stackalloc ushort[4];

        if (_rounds == 8)
        {
            for (int i = 0; i < 4; i++)
                kb[3 - i] = (ushort)((key[i * 2] << 8) | key[i * 2 + 1]);

            ScheduleBuild(kb, 0, KeyRot[..8]);
            return;
        }

        for (int i = 0; i < _size; i++)
        {
            for (int j = 0; j < 4; j++)
                kb[3 - j] = (ushort)((key[i * 8 + j * 2] << 8) | key[i * 8 + j * 2 + 1]);

            ScheduleBuild(kb, i * 8, KeyRot[..8]);
            ScheduleBuild(kb, _rounds - 8 - i * 8, KeyRot[8..]);
        }
    }

    /// <summary><c>IceKey::encrypt</c>: one 8-byte block.</summary>
    public void Encrypt(ReadOnlySpan<byte> plain, Span<byte> cipher)
    {
        uint l = ReadWord(plain, 0);
        uint r = ReadWord(plain, 4);

        for (int i = 0; i < _rounds; i += 2)
        {
            l ^= F(r, i);
            r ^= F(l, i + 1);
        }

        WriteWord(cipher, 0, r);
        WriteWord(cipher, 4, l);
    }

    /// <summary><c>IceKey::decrypt</c>: one 8-byte block.</summary>
    public void Decrypt(ReadOnlySpan<byte> cipher, Span<byte> plain)
    {
        uint l = ReadWord(cipher, 0);
        uint r = ReadWord(cipher, 4);

        for (int i = _rounds - 1; i > 0; i -= 2)
        {
            l ^= F(r, i);
            r ^= F(l, i - 1);
        }

        WriteWord(plain, 0, r);
        WriteWord(plain, 4, l);
    }

    private static uint ReadWord(ReadOnlySpan<byte> b, int at)
    {
        if (b.Length < BlockBytes)
            throw new ArgumentException($"an ICE block is {BlockBytes} bytes, not {b.Length}");
        return ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];
    }

    private static void WriteWord(Span<byte> b, int at, uint word)
    {
        if (b.Length < BlockBytes)
            throw new ArgumentException($"an ICE block is {BlockBytes} bytes, not {b.Length}");
        b[at] = (byte)(word >> 24);
        b[at + 1] = (byte)(word >> 16);
        b[at + 2] = (byte)(word >> 8);
        b[at + 3] = (byte)word;
    }

    // ice_f: the single-round function.
    private uint F(uint p, int round)
    {
        uint tl = ((p >> 16) & 0x3ff) | (((p >> 14) | (p << 18)) & 0xffc00);
        uint tr = (p & 0x3ff) | ((p << 2) & 0xffc00);

        int k = round * 3;
        uint al = _schedule[k + 2] & (tl ^ tr);
        uint ar = al ^ tr;
        al ^= tl;

        al ^= _schedule[k];
        ar ^= _schedule[k + 1];

        return _sbox[(int)(al >> 10)] | _sbox[1024 + (int)(al & 0x3ff)]
             | _sbox[2048 + (int)(ar >> 10)] | _sbox[3072 + (int)(ar & 0x3ff)];
    }

    // IceKey::scheduleBuild: eight rounds [n, n+7].
    private void ScheduleBuild(Span<ushort> kb, int n, ReadOnlySpan<byte> keyrot)
    {
        for (int i = 0; i < 8; i++)
        {
            int kr = keyrot[i];
            int sk = (n + i) * 3;

            _schedule[sk] = _schedule[sk + 1] = _schedule[sk + 2] = 0;

            for (int j = 0; j < 15; j++)
            {
                int word = sk + j % 3;

                for (int k = 0; k < 4; k++)
                {
                    int index = (kr + k) & 3;
                    int bit = kb[index] & 1;

                    _schedule[word] = (_schedule[word] << 1) | (uint)bit;
                    kb[index] = (ushort)((kb[index] >> 1) | ((bit ^ 1) << 15));
                }
            }
        }
    }

    // ice_sboxes_init.
    private void BuildSBoxes()
    {
        for (int i = 0; i < 1024; i++)
        {
            int col = (i >> 1) & 0xff;
            int row = (i & 0x1) | ((i & 0x200) >> 8);

            _sbox[i] = Perm32(GfExp7(col ^ SXor[row], SMod[row]) << 24);
            _sbox[1024 + i] = Perm32(GfExp7(col ^ SXor[4 + row], SMod[4 + row]) << 16);
            _sbox[2048 + i] = Perm32(GfExp7(col ^ SXor[8 + row], SMod[8 + row]) << 8);
            _sbox[3072 + i] = Perm32(GfExp7(col ^ SXor[12 + row], SMod[12 + row]));
        }
    }

    // gf_mult: 8-bit Galois field multiplication of a by b, modulo m.
    private static uint GfMult(uint a, uint b, uint m)
    {
        uint res = 0;

        while (b != 0)
        {
            if ((b & 1) != 0)
                res ^= a;

            a <<= 1;
            b >>= 1;

            if (a >= 256)
                a ^= m;
        }

        return res;
    }

    // gf_exp7: b to the power of 7, modulo m.
    private static uint GfExp7(int b, int m)
    {
        if (b == 0)
            return 0;

        uint ub = (uint)b, um = (uint)m;
        uint x = GfMult(ub, ub, um);
        x = GfMult(ub, x, um);
        x = GfMult(x, x, um);
        return GfMult(ub, x, um);
    }

    // ice_perm32: the 32-bit P-box permutation.
    private static uint Perm32(uint x)
    {
        uint res = 0;
        int i = 0;

        while (x != 0)
        {
            if ((x & 1) != 0)
                res |= PBox[i];
            i++;
            x >>= 1;
        }

        return res;
    }
}
