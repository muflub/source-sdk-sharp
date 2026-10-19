//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// THE FRAME'S OVERLAP REPORT, FETCHED — the one call, and the buffer it fills
/// (<c>plan_entity.md</c> step 2b).
///
/// <para>
/// <b>A CLASS RATHER THAN A LINE</b> because of what happens when the buffer is
/// too small. The ABI answers how many pairs there ARE, not how many it wrote,
/// precisely so that a truncation can be SEEN — and it has to be, because a
/// truncated report reads to <see cref="EntityDirectory.TouchFrame"/> as every
/// pair past the end having LEFT, and a departure is something the game acts on
/// (a trigger fires <c>OnEndTouch</c>, a volume empties its list). Taking the
/// short answer would be the silent wrong answer this port exists to remove.
/// </para>
///
/// <para>
/// So: ask, and if the answer does not fit, GROW AND ASK AGAIN. That is safe
/// because the native list is built once per FRAME and answered as many times
/// as it is asked for — the second ask sees the same pairs, including the ones
/// the eager reports contributed before it. If the second ask still does not
/// fit, the frame is REFUSED rather than diffed: the links stand, nothing is
/// reported as having left, and the refusal is printed. One frame of a stale
/// table is recoverable; a wave of departures that did not happen is not.
/// </para>
///
/// <para>
/// The buffer is kept and reused, so a steady state allocates nothing. It only
/// ever grows, because a map's busiest frame is the size it needs to be.
/// </para>
/// </summary>
public sealed class OverlapReader
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>
    /// Where it starts. dm_lockdown's report runs to a few dozen pairs, so this
    /// is comfortably past a busy frame on the shipped content and small enough
    /// that being wrong about it costs one regrow.
    /// </summary>
    public const int InitialPairs = 256;

    /// <summary>
    /// The most the buffer will ever grow to.
    ///
    /// <para>
    /// <b>A CEILING, AND IT IS NOT PRUDENCE.</b> The count comes across the ABI
    /// as an <c>int32</c> the caller does not control, and a doubling loop with
    /// no ceiling on such a number OVERFLOWS: 256 doubles to 2^30, doubles to
    /// <c>int.MinValue</c>, doubles to 0, and 0 doubles to 0 for ever. That is
    /// not a hypothetical — it hung the test suite the first time this class
    /// was run against a report claiming <c>int.MaxValue</c> pairs, and the
    /// symptom was a suite that stopped rather than a suite that failed.
    /// </para>
    ///
    /// <para>
    /// The value is past anything real by a wide margin: it is eight times the
    /// native side's own <c>kMaxOverlapPairs</c>, which is itself the number at
    /// which native decides nobody is reading the list at all. A report bigger
    /// than this is not a busy frame, it is a broken answer, and the frame is
    /// refused.
    /// </para>
    /// </summary>
    public const int MaxPairs = 65536;

    /// <summary>
    /// The one question this reader asks — <c>IEngineEntities.OverlapReport</c>,
    /// as a delegate.
    ///
    /// <para>
    /// A DELEGATE AND NOT THE AREA, because the area is ninety members and this
    /// depends on one of them. That is not only tidiness: a test double for the
    /// whole area is ninety forwarding lines, and ninety lines of forwarding is
    /// where a double comes to answer <c>default</c> for something the slot
    /// answers otherwise — the way eight of step 2f's facts came to measure the
    /// wrong path. Naming the single question makes a stand-in for it three
    /// lines long and impossible to get wrong.
    /// </para>
    /// </summary>
    public delegate int OverlapSource(Span<OverlapPair> into);

    private readonly OverlapSource _report;
    private OverlapPair[] _buffer = new OverlapPair[InitialPairs];

    public OverlapReader(ILog log, IEngineOverlaps overlaps) : this(log, overlaps.OverlapReport) { }

    public OverlapReader(ILog log, OverlapSource report) 

    {

        _log = log;

        _report = report;

    }

    /// <summary>How many pairs the last successful read returned.</summary>
    public int LastPairCount { get; private set; }

    /// <summary>How many pairs the buffer can hold.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>How many times the buffer has had to grow. Instrumentation.</summary>
    public int Regrows { get; private set; }

    /// <summary>How many frames were refused because the report would not fit twice running.</summary>
    public int Refused { get; private set; }

    /// <summary>
    /// This frame's pairs, or false if the report could not be read whole.
    ///
    /// <para>
    /// False is NOT "no pairs" and the two must not be conflated by the caller:
    /// no pairs means sweep everything, and could-not-read means change
    /// nothing.
    /// </para>
    /// </summary>
    public bool TryRead(out ReadOnlySpan<OverlapPair> pairs)
    {
        int found = _report(_buffer);

        if (found > _buffer.Length && found <= MaxPairs)
        {
            Grow(found);
            found = _report(_buffer);
        }

        // AND A NEGATIVE COUNT, which is not paranoia about the ABI so much as
        // about what the alternative does: `AsSpan(0, found)` with a negative
        // length THROWS, from inside the frame pass, once per frame, for as
        // long as the condition lasts. A refusal is a stale table and a printed
        // line; the exception is the server stopping.
        if (found > _buffer.Length || found < 0)
        {
            Refused++;
            _log.Warning($"[SourceSharp/Touch] the overlap report holds {found} pairs and " +
                        $"the buffer grew to {_buffer.Length}; this frame's touch links are " +
                        "left standing rather than swept.\n");
            pairs = default;
            return false;
        }

        LastPairCount = found;
        pairs = _buffer.AsSpan(0, found);
        return true;
    }

    private void Grow(int pairs)
    {
        if (pairs > MaxPairs || _buffer.Length >= MaxPairs)
            return;

        int size = _buffer.Length;

        while (size < pairs)
            size *= 2;

        _buffer = new OverlapPair[Math.Min(size, MaxPairs)];
        Regrows++;
    }
}
