//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Effects;

/// <summary>
/// vstdlib's <c>CUniformRandomStream</c>, as server.so's own port of it is
/// (src/client/native/vstdlib_random_port.cpp, held draw for draw to the game's
/// libvstdlib.so by vstdlib_random_oracle_test): Park-Miller's minimal standard
/// (multiplier 16807, modulus 2^31 - 1, Schrage's method) behind a 32-entry
/// Bays-Durham shuffle. Seeding stores the seed negated, so the next draw
/// refills the table. A stream a sim owns and seeds (the wind's two), not the
/// process's. Every client generator of this kind is this type: the wind's
/// two and the beam system's noise stream (view_beams.cpp's <c>beamRandom</c>).
/// </summary>
public sealed class UniformStream : IParticleRandom
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

    /// <summary>The same state through a managed reload's writer (the beam system's stream).</summary>
    public void Save(ReloadWriter writer)
    {
        foreach (int v in _table)
            writer.Write(v);
        writer.Write(_state);
        writer.Write(_last);
    }

    public void Restore(ReloadReader reader)
    {
        for (int i = 0; i < TableSize; i++)
            _table[i] = reader.ReadInt();
        _state = reader.ReadInt();
        _last = reader.ReadInt();
    }

    public void Restore(BinaryReader r)
    {
        for (int i = 0; i < TableSize; i++)
            _table[i] = r.ReadInt32();
        _state = r.ReadInt32();
        _last = r.ReadInt32();
    }
}
