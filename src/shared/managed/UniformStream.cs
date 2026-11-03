//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// vstdlib's <c>CUniformRandomStream</c>, held draw for draw to the game's libvstdlib.so
/// by <c>UniformStreamOracleTests</c>: Park-Miller's minimal standard
/// (multiplier 16807, modulus 2^31 - 1, Schrage's method) behind a 32-entry
/// Bays-Durham shuffle. Seeding stores the seed negated, so the next draw
/// refills the table. A stream its owner seeds, not the process's. Every client
/// generator of this kind is this type: the wind's two and the beam system's
/// noise stream (view_beams.cpp's <c>beamRandom</c>).
///
/// <para>
/// <b>SHARED, FOR PREDICTION.</b> Stock's predicted weapon code reseeds the
/// global stream from the command's seed and draws
/// (<c>RandomSeed(iSeed); RandomInt(0, WEAPON_RANDOM_RANGE - 1)</c>, TF's
/// <c>CalcIsAttackCriticalHelper</c>), so the server's run and the client's
/// replay of a command draw the same numbers. Managed code may not seed the
/// engine's stream (<see cref="IUniformRandom"/> has no SetSeed, by design),
/// so a behaviour seeds one of these from
/// <see cref="WeaponAttackContext.Command"/> instead: the same algorithm, so
/// the same draws, on both sides. <c>UniformStreamOracleTests</c> holds it to
/// the game's libvstdlib.so.
/// </para>
///
/// <para>
/// The client's half of the class (<c>UniformStream.Reload.cs</c>) adds the
/// particle sims' <c>IParticleRandom</c> and the managed-reload save.
/// </para>
/// </summary>
public sealed partial class UniformStream : IUniformRandom
{
    private const int Multiplier = 16807;
    private const int Modulus = 2147483647;
    private const int SchrageQ = 127773;
    private const int SchrageR = 2836;
    private const int TableSize = 32;
    private const int Divisor = 1 + (Modulus - 1) / TableSize;
    private const uint MaxRandomRange = 0x7FFFFFFFu;
    private const double Scale = 1.0 / Modulus;
    private const double MaxFraction = 1.0 - 1.2e-7;

    private readonly int[] _table = new int[TableSize];
    private int _state;
    private int _last;

    public UniformStream(int seed = 0) => SetSeed(seed);

    /// <summary><c>SetSeed</c> (stock's <c>RandomSeed</c>): the next draw starts the sequence this seed names.</summary>
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

    /// <summary><c>RandomInt</c>: a uniform pick of the range by rejecting the draws above its largest multiple.</summary>
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

    public void Save(BinaryWriter w)
    {
        foreach (int v in _table)
            w.Write(v);
        w.Write(_state);
        w.Write(_last);
    }

    public void Restore(BinaryReader r)
    {
        for (int i = 0; i < TableSize; i++)
            _table[i] = r.ReadInt32();
        _state = r.ReadInt32();
        _last = r.ReadInt32();
    }
}
