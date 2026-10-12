//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// The client's <c>random-&gt;RandomInt</c> / <c>RandomFloat</c>, in C#.
///
/// <para>
/// <b>NOT AN ABI SLOT, AND THAT IS A DECISION.</b> The ABI's
/// <c>RandomFloat</c> is server-only and answers a silent zero on this side —
/// <c>ClientPlasma</c> records the same finding. But a temp entity's randomness
/// is not the server's stream and never was: stock's spawn helpers draw from
/// <c>g_pUniformStream</c>, the CLIENT's own generator, and nothing on the wire
/// or in prediction depends on the numbers agreeing with anything. A gib's
/// tumble, a bubble's wander and a blood sprite's roll are decoration, drawn on
/// one machine, seen by one player. <c>ClientBreakableSurf</c> uses
/// <see cref="Random"/> for the same reason and is the precedent.
/// </para>
///
/// <para>
/// <b>THE RANGES ARE SOURCE'S AND NOT .NET'S,</b> which is the whole reason
/// this type exists rather than a bare <see cref="Random"/> at each call site.
/// <c>random-&gt;RandomInt(0, 255)</c> can answer 255; <c>Random.Next(0, 255)</c>
/// cannot. Off by one on a bound that gates <c>FTENT_ROTATE</c> is a gib that
/// tumbles a fraction less often than stock's, which nothing would ever notice
/// and which is exactly the kind of drift this port is trying not to accumulate.
/// </para>
///
/// <para>
/// <b>ONE STREAM PER OWNER, SEEDED EXPLICITLY.</b> It was one static
/// <see cref="Random"/> for the process. <see cref="TempEntSpawns"/> now owns
/// one, seeded from the client module's <c>IRandomSeeds</c>, so the stream is
/// the module's and a test with a fixed-seed source replays the same effects.
/// </para>
/// </summary>
internal sealed class TempEntRandom
{
    private readonly Random _stream;

    /// <summary>A stream whose draws are fixed by <paramref name="seed"/>.</summary>
    public TempEntRandom(int seed)
    {
        _stream = new Random(seed);
    }

    /// <summary>
    /// <c>IUniformRandomStream::RandomInt</c>: INCLUSIVE at both ends.
    ///
    /// <para>
    /// Source's own implementation clamps a reversed range to
    /// <paramref name="low"/>, which is what <see cref="Random.Next(int,int)"/>
    /// would throw on, so it is handled here rather than left to crash a frame.
    /// </para>
    /// </summary>
    public int Int(int low, int high)
        => high <= low ? low : _stream.Next(low, high + 1);

    /// <summary>
    /// <c>IUniformRandomStream::RandomFloat</c>, written as stock writes it:
    /// <c>low + (high - low) * fraction</c>.
    ///
    /// <para>
    /// <b>A REVERSED RANGE IS NOT GUARDED, ON PURPOSE.</b>
    /// <c>CTempEnts::BloodSprite</c> asks for
    /// <c>RandomFloat( size / 25, size / 35 )</c> — a LOW that is larger than
    /// its HIGH — and stock's arithmetic answers a value between the two
    /// anyway, because the interpolation does not care which end it starts
    /// from. Clamping it, or swapping the arguments, would change a live
    /// effect's scale to correct something stock does not treat as an error.
    /// </para>
    /// </summary>
    public float Float(float low, float high)
        => low + (high - low) * _stream.NextSingle();
}
