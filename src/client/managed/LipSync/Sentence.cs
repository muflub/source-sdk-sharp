//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Beams;

namespace SourceSharp.Client.LipSync;

/// <summary>One runtime phoneme of a sentence: its code and its span in seconds.</summary>
public readonly record struct RuntimePhoneme(int Code, float Start, float End);

/// <summary>One point of a sentence's emphasis track (value 0..1, 0.5 neutral).</summary>
public readonly record struct EmphasisSample(float Time, float Value);

/// <summary>
/// The part of a sound's sentence (public/sentence.cpp's CSentence) the viseme
/// pass reads: the runtime phonemes in order and the emphasis track, with
/// <see cref="Intensity"/>, CSentence::GetIntensity's spline over that track.
/// <c>LipSyncOracleTests</c> holds it to pristine sentence.cpp bit for bit.
/// </summary>
public sealed class Sentence(IReadOnlyList<RuntimePhoneme> phonemes, IReadOnlyList<EmphasisSample> samples)
{
    private const float Neutral = 0.5f;

    public IReadOnlyList<RuntimePhoneme> Phonemes { get; } = phonemes;

    public IReadOnlyList<EmphasisSample> Samples { get; } = samples;

    // Before the first sample the track starts at (0, neutral); past the last
    // it ends at (endTime, neutral).
    private EmphasisSample Bounded(int number, float endTime)
    {
        if (number < 0)
            return new EmphasisSample(0.0f, Neutral);
        if (number >= Samples.Count)
            return new EmphasisSample(endTime, Neutral);
        return Samples[number];
    }

    /// <summary>
    /// The emphasis at <paramref name="time"/>: neutral with no samples, else a
    /// Catmull-Rom spline through the span holding the time (the first span
    /// that does, the end span when none does) and its two neighbours, the
    /// span's length held to 0.01..1 s and the result to 0..1.
    /// </summary>
    public float Intensity(float time, float endTime)
    {
        int count = Samples.Count;
        if (count <= 0)
            return Neutral;

        int span = -1;
        while (span < count)
        {
            EmphasisSample from = Bounded(span, endTime);
            EmphasisSample to = Bounded(span + 1, endTime);
            if (time >= from.Time && time <= to.Time)
                break;
            span++;
        }

        EmphasisSample before = Bounded(Math.Max(-1, span - 1), endTime);
        EmphasisSample start = Bounded(Math.Max(-1, span), endTime);
        EmphasisSample end = Bounded(Math.Min(span + 1, count), endTime);
        EmphasisSample after = Bounded(Math.Min(span + 2, count), endTime);

        float length = ClampSse(end.Time - start.Time, 0.01f, 1.0f);
        float fraction = ClampSse((time - start.Time) / length, 0.0f, 1.0f);

        Vector curve = BeamDraw.CatmullRomSpline(
            new Vector(before.Time, before.Value, 0.0f),
            new Vector(start.Time, start.Value, 0.0f),
            new Vector(end.Time, end.Value, 0.0f),
            new Vector(after.Time, after.Value, 0.0f),
            fraction);
        return ClampSse(curve.Y, 0.0f, 1.0f);
    }

    // mathlib.h's release clamp: max_ss then min_ss (the second operand wins a
    // tie or a NaN, as the instructions do).
    internal static float ClampSse(float value, float min, float max)
    {
        value = value > min ? value : min;
        return value < max ? value : max;
    }
}
