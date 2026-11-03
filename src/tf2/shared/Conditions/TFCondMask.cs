//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Numerics;

namespace TF2.Shared.Conditions;

/// <summary>
/// A set of conditions as stock keeps it: five 32-bit words, condition
/// <c>n</c> at bit <c>n % 32</c> of word <c>n / 32</c>
/// (<c>CConditionVars</c>, <c>tf_player_shared.cpp:1036-1096</c>).
/// <see cref="Word0"/> is <c>m_nPlayerCond</c>, <see cref="Word1"/>
/// <c>m_nPlayerCondEx</c>, up to <see cref="Word4"/> <c>m_nPlayerCondEx4</c>
/// (<c>tf_player_shared.h:941-945</c>), each a networked, predicted int in
/// stock, so a mask copies field for field into five int netstate fields.
///
/// <para>
/// A plain value: five ints, no references, so a copy is a snapshot. A
/// condition outside <c>Aiming..Last-1</c> throws: stock asserts there
/// (<c>AddCond</c>/<c>RemoveCond</c>/<c>InCond</c>, :1107, :1168, :1211).
/// </para>
/// </summary>
public struct TFCondMask : IEquatable<TFCondMask>
{
    /// <summary>How many 32-bit words stock networks (<c>TF_COND_LAST &lt; 160</c>, :780).</summary>
    public const int WordCount = 5;

    /// <summary><c>m_nPlayerCond</c>: conditions 0-31.</summary>
    public int Word0;

    /// <summary><c>m_nPlayerCondEx</c>: conditions 32-63.</summary>
    public int Word1;

    /// <summary><c>m_nPlayerCondEx2</c>: conditions 64-95.</summary>
    public int Word2;

    /// <summary><c>m_nPlayerCondEx3</c>: conditions 96-127.</summary>
    public int Word3;

    /// <summary><c>m_nPlayerCondEx4</c>: conditions 128-130.</summary>
    public int Word4;

    /// <summary>A mask from its five words, as a netstate holds them.</summary>
    public TFCondMask(int word0, int word1, int word2, int word3, int word4)
    {
        Word0 = word0;
        Word1 = word1;
        Word2 = word2;
        Word3 = word3;
        Word4 = word4;
    }

    /// <summary>The set holding only <paramref name="cond"/>.</summary>
    public static TFCondMask Of(TFCond cond)
    {
        var mask = default(TFCondMask);
        mask.Set(cond);
        return mask;
    }

    /// <summary>The set holding each of <paramref name="conds"/>.</summary>
    public static TFCondMask Of(ReadOnlySpan<TFCond> conds)
    {
        var mask = default(TFCondMask);
        foreach (TFCond cond in conds)
        {
            mask.Set(cond);
        }

        return mask;
    }

    /// <summary>True when no condition is in the set.</summary>
    public readonly bool IsEmpty => (Word0 | Word1 | Word2 | Word3 | Word4) == 0;

    /// <summary>How many conditions are in the set.</summary>
    public readonly int Count =>
        BitOperations.PopCount(unchecked((uint)Word0)) + BitOperations.PopCount(unchecked((uint)Word1))
        + BitOperations.PopCount(unchecked((uint)Word2)) + BitOperations.PopCount(unchecked((uint)Word3))
        + BitOperations.PopCount(unchecked((uint)Word4));

    /// <summary>Whether <paramref name="cond"/> is in the set (<c>InCond</c>'s bit test, :1218-1219).</summary>
    public readonly bool Has(TFCond cond)
    {
        int index = Check(cond);
        return (WordAt(index >> 5) & (1 << (index & 31))) != 0;
    }

    /// <summary>Puts <paramref name="cond"/> in the set.</summary>
    public void Set(TFCond cond)
    {
        int index = Check(cond);
        SetWord(index >> 5, WordAt(index >> 5) | (1 << (index & 31)));
    }

    /// <summary>Takes <paramref name="cond"/> out of the set.</summary>
    public void Clear(TFCond cond)
    {
        int index = Check(cond);
        SetWord(index >> 5, WordAt(index >> 5) & ~(1 << (index & 31)));
    }

    /// <summary>Word <paramref name="index"/> (0-4).</summary>
    public readonly int WordAt(int index) => index switch
    {
        0 => Word0,
        1 => Word1,
        2 => Word2,
        3 => Word3,
        4 => Word4,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "a condition mask has five words"),
    };

    /// <summary>Replaces word <paramref name="index"/> (0-4).</summary>
    public void SetWord(int index, int value)
    {
        switch (index)
        {
            case 0: Word0 = value; break;
            case 1: Word1 = value; break;
            case 2: Word2 = value; break;
            case 3: Word3 = value; break;
            case 4: Word4 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index), index, "a condition mask has five words");
        }
    }

    /// <summary>
    /// The conditions in <paramref name="now"/> and not in <paramref name="before"/>:
    /// <c>SyncConditions</c>' <c>nCondAdded</c> (:1510), what a client
    /// runs <c>OnConditionAdded</c> for when new bits arrive.
    /// </summary>
    public static TFCondMask Added(TFCondMask before, TFCondMask now) => now & ~before;

    /// <summary>
    /// The conditions in <paramref name="before"/> and not in <paramref name="now"/>:
    /// <c>SyncConditions</c>' <c>nCondRemoved</c> (:1511).
    /// </summary>
    public static TFCondMask Removed(TFCondMask before, TFCondMask now) => before & ~now;

    /// <summary>
    /// Writes the set's conditions in ascending order into <paramref name="destination"/>
    /// and returns how many. Throws when the span is shorter than <see cref="Count"/>.
    /// </summary>
    public readonly int CopyTo(Span<TFCond> destination)
    {
        int count = Count;
        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"the span holds {destination.Length} conditions and the set has {count}", nameof(destination));
        }

        int written = 0;
        foreach (TFCond cond in this)
        {
            destination[written++] = cond;
        }

        return written;
    }

    /// <summary>Enumerates the set in ascending order, without allocating.</summary>
    public readonly Enumerator GetEnumerator() => new(this);

    public static TFCondMask operator |(TFCondMask a, TFCondMask b) =>
        new(a.Word0 | b.Word0, a.Word1 | b.Word1, a.Word2 | b.Word2, a.Word3 | b.Word3, a.Word4 | b.Word4);

    public static TFCondMask operator &(TFCondMask a, TFCondMask b) =>
        new(a.Word0 & b.Word0, a.Word1 & b.Word1, a.Word2 & b.Word2, a.Word3 & b.Word3, a.Word4 & b.Word4);

    /// <summary>The complement within the valid conditions: bits at or past <c>Last</c> stay clear.</summary>
    public static TFCondMask operator ~(TFCondMask a) =>
        new(~a.Word0, ~a.Word1, ~a.Word2, ~a.Word3, ~a.Word4 & ((1 << ((int)TFCond.Last - 128)) - 1));

    public static bool operator ==(TFCondMask a, TFCondMask b) => a.Equals(b);

    public static bool operator !=(TFCondMask a, TFCondMask b) => !a.Equals(b);

    public readonly bool Equals(TFCondMask other) =>
        Word0 == other.Word0 && Word1 == other.Word1 && Word2 == other.Word2
        && Word3 == other.Word3 && Word4 == other.Word4;

    public override readonly bool Equals(object? obj) => obj is TFCondMask other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Word0, Word1, Word2, Word3, Word4);

    public override readonly string ToString()
    {
        if (IsEmpty)
        {
            return "{}";
        }

        var text = new System.Text.StringBuilder("{");
        foreach (TFCond cond in this)
        {
            if (text.Length > 1)
            {
                text.Append(", ");
            }

            text.Append(cond);
        }

        return text.Append('}').ToString();
    }

    /// <summary>The bit index of a valid condition; throws on <c>Invalid</c>, <c>Last</c> and beyond.</summary>
    internal static int Check(TFCond cond)
    {
        int index = (int)cond;
        if ((uint)index >= (uint)TFCond.Last)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cond), cond, "a condition is TF_COND_AIMING..TF_COND_LAST-1 (stock asserts the range)");
        }

        return index;
    }

    /// <summary>Ascending walk over a mask's set bits.</summary>
    public struct Enumerator
    {
        private readonly TFCondMask _mask;
        private int _word;
        private int _remaining;
        private TFCond _current;

        internal Enumerator(TFCondMask mask)
        {
            _mask = mask;
            _word = 0;
            _remaining = mask.Word0;
            _current = TFCond.Invalid;
        }

        public readonly TFCond Current => _current;

        public bool MoveNext()
        {
            while (_remaining == 0)
            {
                if (++_word >= WordCount)
                {
                    return false;
                }

                _remaining = _mask.WordAt(_word);
            }

            int bit = BitOperations.TrailingZeroCount(_remaining);
            _remaining &= _remaining - 1;
            _current = (TFCond)((_word << 5) + bit);
            return true;
        }
    }
}
