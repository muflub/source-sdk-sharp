//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// vstdlib's <c>CUniformRandomStream</c>, for BOTH sides: Park-Miller's
/// minimal standard (multiplier 16807, modulus 2^31 - 1, Schrage's method)
/// behind a 32-entry Bays-Durham shuffle. Seeding stores the seed negated, so
/// the next draw refills the table.
///
/// <para>
/// SHARED because a predicted decision draws from it: stock's FireBullets
/// re-seeds the process's stream from the command's random seed before every
/// pellet (<c>RandomSeed( iSeed )</c>, baseentity_shared.cpp), so the server
/// and the predicting client spread the same pellets the same way. A stream
/// owned by the caller and seeded by it, never the process's.
/// </para>
/// </summary>
public sealed class SeededUniformStream
{
    private const int Multiplier = 16807;
    private const int Modulus = 2147483647;
    private const int SchrageQ = 127773;
    private const int SchrageR = 2836;

    /// <summary><c>NTAB</c>: the shuffle table's length.</summary>
    public const int TableSize = 32;

    private const int Divisor = 1 + (Modulus - 1) / TableSize;
    private const uint MaxRandomRange = 0x7FFFFFFFu;
    private const double Scale = 1.0 / Modulus;
    private const double MaxFraction = 1.0 - 1.2e-7;

    private readonly int[] _table = new int[TableSize];
    private int _state;
    private int _last;

    public SeededUniformStream(int seed = 0) => SetSeed(seed);

    /// <summary><c>SetSeed</c>: <c>m_idum = iSeed &lt; 0 ? iSeed : -iSeed; m_iy = 0</c>.</summary>
    public void SetSeed(int seed)
    {
        _state = seed < 0 ? seed : -seed;
        _last = 0;
    }

    private static int Step(int n)
    {
        int k = n / SchrageQ;
        n = Multiplier * (n - k * SchrageQ) - SchrageR * k;
        return n < 0 ? n + Modulus : n;
    }

    private int Draw()
    {
        if (_state <= 0 || _last == 0)
        {
            _state = -_state < 1 ? 1 : -_state;
            for (int j = TableSize + 7; j >= 0; --j)
            {
                _state = Step(_state);
                if (j < TableSize)
                    _table[j] = _state;
            }

            _last = _table[0];
        }

        _state = Step(_state);
        int slot = _last / Divisor;
        if (slot < 0 || slot >= TableSize)
            slot = (slot % TableSize) & 0x7fffffff;
        _last = _table[slot];
        _table[slot] = _state;
        return _last;
    }

    /// <summary><c>RandomFloat</c>: the draw scaled in double, held in a float, then <c>fl * (high - low) + low</c>.</summary>
    public float RandomFloat(float min, float max)
    {
        float fl = (float)(Scale * Draw());
        if (fl > MaxFraction)
            fl = (float)MaxFraction;
        return (fl * (max - min)) + min;
    }

    /// <summary><c>RandomInt</c>: inclusive at both ends, by rejection of the top partial range.</summary>
    public int RandomInt(int min, int max)
    {
        uint x = unchecked((uint)max - (uint)min + 1u);
        if (x <= 1 || MaxRandomRange < x - 1)
            return min;
        uint maxAcceptable = MaxRandomRange - ((MaxRandomRange + 1u) % x);
        uint n;
        do
        {
            n = (uint)Draw();
        }
        while (n > maxAcceptable);
        return min + (int)(n % x);
    }

    /// <summary>The whole state, for a save: the table, <c>m_idum</c> and <c>m_iy</c>.</summary>
    public void CopyState(Span<int> table, out int state, out int last)
    {
        _table.CopyTo(table);
        state = _state;
        last = _last;
    }

    /// <summary>The state <see cref="CopyState"/> took, put back.</summary>
    public void RestoreState(ReadOnlySpan<int> table, int state, int last)
    {
        table[..TableSize].CopyTo(_table);
        _state = state;
        _last = last;
    }
}
