//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Client;

//=============================================================================
// CLIENT INTERPOLATION, IN MANAGED CODE (plan_entity.md §3.9, step 6a).
//
// WHAT THIS REPLACES. game/client/interpolatedvar.h -- CInterpolatedVar and its
// CInterpolatedVarArrayBase base, 1591 lines of template -- and the two
// instantiations the standalone client shell carried per entity
// (m_iv_vecOrigin, m_iv_angRotation). The algorithm is the same one, ported
// arm for arm and checked against a recording of the C++ it replaces
// (Fixtures/interpolation-stock.tsv). It is not a reimplementation from the
// description: a smoother curve that is not the engine's curve is a client that
// disagrees with every other client about where things are.
//
// WHY IT IS POLICY AND NOT MECHANISM. Nothing here is an engine call. It is
// arithmetic over values the entity already holds, deciding WHERE A THING IS
// DRAWN -- which is the definition the plan gives for what belongs in C#.
//=============================================================================

/// <summary>
/// One field of the engine block that the client keeps a history of, found by
/// reflection from <see cref="InterpolatedAttribute"/>.
/// </summary>
public sealed class InterpolatedField
{
    internal InterpolatedField(string name, InterpolationKind kind, int sourceOffset, int renderOffset)
    {
        Name = name;
        Kind = kind;
        SourceOffset = sourceOffset;
        RenderOffset = renderOffset;
    }

    /// <summary>The block-head field's name, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>How two samples of it are blended.</summary>
    public InterpolationKind Kind { get; }

    /// <summary>
    /// Byte offset, from the base of <see cref="EngineBlock"/>, of the NETWORKED
    /// value — what the engine's decoder wrote this update.
    /// </summary>
    public int SourceOffset { get; }

    /// <summary>
    /// Byte offset, from the base of <see cref="EngineBlock"/>, of the render-tail
    /// field the DRAWN value is written into.
    /// </summary>
    public int RenderOffset { get; }

    public override string ToString() => $"{Name} ({Kind}) {SourceOffset}->{RenderOffset}";
}

/// <summary>
/// The <see cref="InterpolatedAttribute"/> fields of the engine block, read
/// once.
///
/// <para>
/// THE ONLY LIST. Sizing the ring table, walking the per-frame pass and naming
/// a ring in a diagnostic all come from here, so a field that stops carrying
/// the attribute stops being interpolated everywhere at once rather than in
/// three places out of four.
/// </para>
/// </summary>
public static class InterpolatedFields
{
    /// <summary>Every interpolated block field, in declaration order.</summary>
    public static IReadOnlyList<InterpolatedField> All { get; } = Discover();

    /// <summary>How many rings one entity needs.</summary>
    public static int Count => All.Count;

    private static InterpolatedField[] Discover()
    {
        var found = new List<InterpolatedField>();

        // The head only. The client tail is where answers GO; a field there
        // carrying the attribute would be a ring fed by its own output.
        int headBase = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head));
        int tailBase = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client));

        foreach (FieldInfo field in EngineBlock.FieldsOf(typeof(EngineBlockHead)))
        {
            var attribute = field.GetCustomAttribute<InterpolatedAttribute>();
            if (attribute is null)
                continue;

            FieldInfo? render = typeof(EngineBlockClientTail).GetField(attribute.RenderField);

            // NOT A SILENT SKIP. A misnamed destination is a field that would
            // never be drawn from, and the block's own generator would not
            // notice: nothing else reads this name.
            if (render is null)
                throw new InvalidOperationException(
                    $"[Interpolated] on EngineBlockHead.{field.Name} names render field " +
                    $"'{attribute.RenderField}', which EngineBlockClientTail does not have.");

            if (render.FieldType != field.FieldType)
                throw new InvalidOperationException(
                    $"[Interpolated] on EngineBlockHead.{field.Name} ({field.FieldType.Name}) names " +
                    $"EngineBlockClientTail.{render.Name}, which is a {render.FieldType.Name}.");

            found.Add(new InterpolatedField(
                field.Name,
                attribute.Kind,
                headBase + (int)Marshal.OffsetOf<EngineBlockHead>(field.Name),
                tailBase + (int)Marshal.OffsetOf<EngineBlockClientTail>(render.Name)));
        }

        return found.ToArray();
    }
}

/// <summary>Three floats and the server time they are from.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct InterpolationSample
{
    public float ChangeTime;
    public float A, B, C;

    public InterpolationSample(float changeTime, float a, float b, float c)
    {
        ChangeTime = changeTime;
        A = a; B = b; C = c;
    }

    /// <summary>The three values, ignoring the time — <c>COMPARE_HISTORY</c>.</summary>
    public bool SameValueAs(in InterpolationSample other)
        => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C);
}

/// <summary>
/// Which arm of the algorithm ran, counted.
///
/// <para>
/// NOT DEBUG SPEW. Two things read these. The fixture replay counts what its
/// recording actually reaches, so an arm nothing exercises is NAMED rather than
/// assumed covered; and <c>cs_cl_interp</c> reads them off a live client, so
/// "interpolation costs no crossings" cannot be true of a build that quietly
/// stopped interpolating. A readout that only ever goes up is a readout that
/// cannot fail.
/// </para>
/// </summary>
public sealed class InterpolationArms
{
    public long Hermite;
    public long Linear;
    public long SingleSample;
    public long PastEveryEntry;
    public long AllNewer;
    public long Extrapolated;
    public long Empty;
    public long FractionOverOne;
    public long HermiteTimeFixup;
    public long Teleported;
    public long SampleIdentical;
    public long FlushedNewer;
    public long Truncated;
    public long RingOverflowed;
    public long AngleSlerpEqual;
    public long AngleSlerpFlipped;

    public void Clear()
    {
        Hermite = Linear = SingleSample = PastEveryEntry = AllNewer = Extrapolated = Empty = 0;
        FractionOverOne = HermiteTimeFixup = Teleported = SampleIdentical = 0;
        FlushedNewer = Truncated = RingOverflowed = 0;
        AngleSlerpEqual = AngleSlerpFlipped = 0;
    }

    /// <summary>Name and count, in declaration order, for a readout or a table.</summary>
    public IEnumerable<(string Name, long Count)> Rows()
    {
        yield return (nameof(Hermite), Hermite);
        yield return (nameof(Linear), Linear);
        yield return (nameof(SingleSample), SingleSample);
        yield return (nameof(PastEveryEntry), PastEveryEntry);
        yield return (nameof(AllNewer), AllNewer);
        yield return (nameof(Extrapolated), Extrapolated);
        yield return (nameof(Empty), Empty);
        yield return (nameof(FractionOverOne), FractionOverOne);
        yield return (nameof(HermiteTimeFixup), HermiteTimeFixup);
        yield return (nameof(Teleported), Teleported);
        yield return (nameof(SampleIdentical), SampleIdentical);
        yield return (nameof(FlushedNewer), FlushedNewer);
        yield return (nameof(Truncated), Truncated);
        yield return (nameof(RingOverflowed), RingOverflowed);
        yield return (nameof(AngleSlerpEqual), AngleSlerpEqual);
        yield return (nameof(AngleSlerpFlipped), AngleSlerpFlipped);
    }
}

/// <summary>
/// The history of ONE interpolated field of ONE entity — Source's
/// <c>CInterpolatedVarArrayBase</c> with <c>m_nMaxCount == 1</c>, which is what
/// every var the standalone client shell held was.
///
/// <para>
/// A FIXED RING RATHER THAN A GROWING BUFFER. §3.9 says preallocated, and the
/// reason is that this is per entity per field and the size is bounded by
/// arithmetic rather than by hope: entries older than
/// <c>curtime − interp − 0.05</c> are dropped every time a sample lands and
/// every time the value is read, so the history holds about
/// <c>(interp + 0.05) × updaterate + 3</c> entries — thirteen at a 0.1s interp
/// and 66Hz. <see cref="Capacity"/> is well over that, and an overflow drops
/// the OLDEST entry and is COUNTED (<see cref="InterpolationArms.RingOverflowed"/>)
/// rather than being allowed to be invisible.
/// </para>
/// </summary>
public sealed class InterpolationRing
{
    /// <summary>
    /// How many samples one field of one entity keeps. See the class remarks
    /// for where the number comes from.
    /// </summary>
    public const int Capacity = 24;

    /// <summary>
    /// <c>EXTRA_INTERPOLATION_HISTORY_STORED</c>, interpolatedvar.h:41 — how
    /// much history past the target time is kept so that a read slightly in the
    /// past still has a neighbourhood.
    /// </summary>
    public const float ExtraHistoryStored = 0.05f;

    // Newest first, exactly as CSimpleRingBuffer is used here: index 0 is the
    // head and Count-1 the oldest. A flat array walked from an offset, so
    // AddToHead is O(1) and does not shuffle.
    private readonly InterpolationSample[] _entries = new InterpolationSample[Capacity];
    private int _head;
    private int _count;

    private readonly InterpolationKind _kind;
    private readonly InterpolationArms _arms;

    public InterpolationRing(InterpolationKind kind, InterpolationArms arms)
    {
        _kind = kind;
        _arms = arms;
    }

    public InterpolationKind Kind => _kind;

    /// <summary>How many samples the ring holds.</summary>
    public int Count => _count;

    /// <summary>Sample <paramref name="i"/>, newest first.</summary>
    public InterpolationSample this[int i]
    {
        get
        {
            if ((uint)i >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(i));
            return _entries[(_head + i) % Capacity];
        }
    }

    /// <summary><c>ClearHistory</c>.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// <c>AddToHead</c> with <c>bFlushNewer</c>, which is the only form
    /// <c>NoteChanged</c> uses.
    ///
    /// <para>
    /// The flush is not tidiness. The server can correct the client's clock
    /// backwards, so a sample can arrive stamped EARLIER than one already
    /// filed; blending across that pair draws the entity moving backwards
    /// through time. Stock drops everything at or after the new timestamp
    /// (with a 0.0001 slack) and so does this.
    /// </para>
    /// </summary>
    public void AddToHead(float changeTime, float a, float b, float c, bool flushNewer)
    {
        if (flushNewer)
        {
            while (_count > 0 && _entries[_head].ChangeTime + 0.0001f > changeTime)
            {
                _arms.FlushedNewer++;
                RemoveAtHead();
            }
        }

        if (_count == Capacity)
        {
            _arms.RingOverflowed++;
            _count--;                       // drop the oldest to make room
        }

        _head = (_head + Capacity - 1) % Capacity;
        _count++;
        _entries[_head] = new InterpolationSample(changeTime, a, b, c);
    }

    private void RemoveAtHead()
    {
        _head = (_head + 1) % Capacity;
        _count--;
    }

    /// <summary><c>Truncate</c> — keep the newest <paramref name="newCount"/>.</summary>
    private void Truncate(int newCount)
    {
        if (newCount < _count)
        {
            _arms.Truncated++;
            _count = newCount;
        }
    }

    /// <summary>
    /// <c>RemoveEntriesPreviousTo</c>: keep the sample right before
    /// <paramref name="time"/>, the one before THAT (hermite needs three), and
    /// drop the rest.
    /// </summary>
    public void RemoveEntriesPreviousTo(float time)
    {
        for (int i = 0; i < _count; i++)
        {
            if (this[i].ChangeTime < time)
            {
                Truncate(i + 3);
                break;
            }
        }
    }

    /// <summary>
    /// <c>Reset</c>: throw the history away and seed three entries at
    /// <paramref name="curTime"/> holding the value that just arrived.
    ///
    /// <para>
    /// Three and not one, because that is what stock does and the count is
    /// load-bearing: <c>GetInterpolationInfo</c> needs a newer, an older and an
    /// oldest to take the hermite arm, and one entry at the current time would
    /// make a newly created entity take the single-sample arm for its first two
    /// updates. All three at the SAME time, so every <c>dt</c> between them is
    /// zero and the arm chosen is the flat one until real samples arrive.
    /// </para>
    /// </summary>
    public void Reset(float curTime, float a, float b, float c)
    {
        Clear();
        _arms.Teleported++;

        for (int i = 0; i < 3; i++)
            AddToHead(curTime, a, b, c, flushNewer: false);
    }

    /// <summary>
    /// <c>NoteChanged</c>: file the value that just arrived.
    /// </summary>
    /// <returns>
    /// Whether the value actually differs from the newest one already filed —
    /// stock's return, which is its "can I skip interpolating this" hint.
    /// </returns>
    public bool NoteChanged(float changeTime, float interpolationAmount, float curTime,
                            float a, float b, float c)
    {
        bool changed = true;

        if (_count > 0)
        {
            InterpolationSample newest = this[0];
            if (newest.A.Equals(a) && newest.B.Equals(b) && newest.C.Equals(c))
            {
                changed = false;
                _arms.SampleIdentical++;
            }
        }

        AddToHead(changeTime, a, b, c, flushNewer: true);

        // JAY's comment in interpolatedvar.h:667 -- the #else arm, which is the
        // one that is compiled. Trimming here as well as in Interpolate is what
        // keeps a var nobody reads (a model not drawn in first person) from
        // accumulating forty entries.
        RemoveEntriesPreviousTo(curTime - interpolationAmount - ExtraHistoryStored);

        return changed;
    }

    //-------------------------------------------------------------------------
    // Reading it.
    //-------------------------------------------------------------------------

    /// <summary>Which way <see cref="GetInterpolationInfo"/> left.</summary>
    public enum Exit
    {
        /// <summary>No usable entry at all — the ring is empty.</summary>
        Empty,
        /// <summary>The target time falls between two entries. The ordinary case.</summary>
        Straddled,
        /// <summary>The target time is PAST every entry: the newest value holds.</summary>
        PastEveryEntry,
        /// <summary>Every entry is NEWER than the target: the oldest one holds.</summary>
        AllNewer,
        /// <summary>One entry, and it is the answer.</summary>
        SingleSample,
    }

    /// <summary>Which three samples <see cref="Interpolate"/> would use.</summary>
    public struct Info
    {
        public int Oldest, Older, Newer;
        public float Fraction;
        public bool Hermite;
        public bool NoMoreChanges;
        public Exit Left;
    }

    private const int InvalidIndex = -1;

    /// <summary>
    /// <c>GetInterpolationInfo</c>: find the pair of samples that straddle
    /// <c>currentTime − interpolationAmount</c>, and the one before them if a
    /// cubic is possible.
    /// </summary>
    public bool GetInterpolationInfo(float currentTime, float interpolationAmount, out Info info)
    {
        info = default;
        info.Oldest = info.Older = info.Newer = InvalidIndex;

        float targetTime = currentTime - interpolationAmount;

        for (int i = 0; i < _count; i++)
        {
            info.Older = i;

            float olderChangeTime = this[i].ChangeTime;
            if (olderChangeTime == 0.0f)
                break;

            if (targetTime < olderChangeTime)
            {
                info.Newer = info.Older;
                continue;
            }

            if (info.Newer == InvalidIndex)
            {
                // The target time is PAST every entry: hold the newest.
                info.Newer = info.Older;
                info.NoMoreChanges = true;
                info.Left = Exit.PastEveryEntry;
                return true;
            }

            float newerChangeTime = this[info.Newer].ChangeTime;
            float dt = newerChangeTime - olderChangeTime;
            if (dt > 0.0001f)
            {
                info.Fraction = (targetTime - olderChangeTime) / (newerChangeTime - olderChangeTime);
                info.Fraction = MathF.Min(info.Fraction, 2.0f);

                int oldestIndex = i + 1;

                // INTERPOLATE_LINEAR_ONLY is not set on either of the shell's
                // vars -- both are LATCH_SIMULATION_VAR and nothing else -- so
                // the cubic is always allowed where there is a third sample.
                if (oldestIndex < _count)
                {
                    info.Oldest = oldestIndex;
                    float oldestChangeTime = this[oldestIndex].ChangeTime;
                    if (olderChangeTime - oldestChangeTime > 0.0001f)
                        info.Hermite = true;
                }

                if (info.Newer == 0)
                {
                    InterpolationSample newer = this[info.Newer];
                    if (newer.SameValueAs(this[info.Older]))
                    {
                        if (!info.Hermite || newer.SameValueAs(this[info.Oldest]))
                            info.NoMoreChanges = true;
                    }
                }
            }

            info.Left = Exit.Straddled;
            return true;
        }

        if (info.Newer != InvalidIndex)
        {
            info.Older = info.Newer;
            info.Left = Exit.AllNewer;
            return true;
        }

        // The single-element case, and the empty one.
        info.Newer = info.Older;
        info.Left = info.Older != InvalidIndex ? Exit.SingleSample : Exit.Empty;
        return info.Older != InvalidIndex;
    }

    /// <summary>
    /// <c>Interpolate</c>: the drawn value at <paramref name="currentTime"/>.
    ///
    /// <para>
    /// EXTRAPOLATION IS NOT REACHED FROM HERE, and that is a fact about the
    /// caller rather than a decision taken in this method. Stock's arm is
    /// guarded by <c>CInterpolationContext::IsExtrapolationAllowed()</c>, which
    /// is false unless something on the stack made a context AND called
    /// <c>EnableExtrapolation(true)</c>. The standalone client shell never made
    /// one, so the flag was false for all 10,380 Interpolate calls in the
    /// recording this port was checked against. The arm is ported anyway,
    /// counted by <see cref="InterpolationArms.Extrapolated"/>, and reached by
    /// hermetic facts only.
    /// </para>
    /// </summary>
    /// <returns>False when the ring is empty and nothing was written.</returns>
    public bool Interpolate(float currentTime, float interpolationAmount,
                            bool allowExtrapolation, float maxExtrapolationAmount,
                            out float a, out float b, out float c)
    {
        a = b = c = 0f;

        if (!GetInterpolationInfo(currentTime, interpolationAmount, out Info info))
        {
            _arms.Empty++;
            return false;
        }

        if (info.Fraction > 1.0f)
            _arms.FractionOverOne++;

        if (info.Hermite)
        {
            _arms.Hermite++;
            InterpolateHermite(info.Fraction, this[info.Oldest], this[info.Older], this[info.Newer],
                               out a, out b, out c);
        }
        else if (info.Newer == info.Older)
        {
            int realOlder = info.Newer + 1;

            if (allowExtrapolation && realOlder < _count && this[realOlder].ChangeTime != 0.0
                && interpolationAmount > 0.000001f)
            {
                _arms.Extrapolated++;
                Extrapolate(this[realOlder], this[info.Newer], currentTime - interpolationAmount,
                            maxExtrapolationAmount, out a, out b, out c);
            }
            else
            {
                switch (info.Left)
                {
                    case Exit.PastEveryEntry: _arms.PastEveryEntry++; break;
                    case Exit.AllNewer:       _arms.AllNewer++;       break;
                    default:                  _arms.SingleSample++;   break;
                }

                InterpolateLinear(info.Fraction, this[info.Older], this[info.Newer],
                                  sameEntry: true, out a, out b, out c);
            }
        }
        else
        {
            _arms.Linear++;
            InterpolateLinear(info.Fraction, this[info.Older], this[info.Newer],
                              sameEntry: info.Older == info.Newer, out a, out b, out c);
        }

        // "Clear out all entries before the oldest since we should never access
        // them again" -- and the one caller stock names for why it is not safe
        // to assume time only moves forward is BecomeRagdollOnClient.
        RemoveEntriesPreviousTo(currentTime - interpolationAmount - ExtraHistoryStored);
        return true;
    }

    //-------------------------------------------------------------------------
    // The blends. One per kind, and the kind is the field's declaration.
    //-------------------------------------------------------------------------

    private void InterpolateLinear(float fraction, in InterpolationSample start, in InterpolationSample end,
                                   bool sameEntry, out float a, out float b, out float c)
    {
        // _Interpolate's quick exit is `start == end`, POINTER equality: the
        // same entry, not two entries that happen to hold the same numbers. The
        // caller passes whether the two indices were the same, because two
        // distinct entries with equal values still go through the blend in
        // stock -- and for an angle that means through the quaternions.
        if (sameEntry)
        {
            a = end.A; b = end.B; c = end.C;
            return;
        }

        if (_kind == InterpolationKind.Angle)
        {
            InterpolationMath.SlerpAngles(fraction, start, end, _arms, out a, out b, out c);
            return;
        }

        a = InterpolationMath.Lerp(fraction, start.A, end.A);
        b = InterpolationMath.Lerp(fraction, start.B, end.B);
        c = InterpolationMath.Lerp(fraction, start.C, end.C);
    }

    private void InterpolateHermite(float fraction, InterpolationSample previous,
                                    in InterpolationSample start, in InterpolationSample end,
                                    out float a, out float b, out float c)
    {
        // TimeFixup_Hermite: the cubic assumes the two intervals either side of
        // `start` are equal, so an earlier sample at the wrong spacing is
        // renormalised onto the same interval as the later one before it is
        // used. Skipping this is a curve that overshoots wherever the update
        // rate wobbled, which is everywhere.
        float dt1 = end.ChangeTime - start.ChangeTime;
        float dt2 = start.ChangeTime - previous.ChangeTime;

        if (MathF.Abs(dt1 - dt2) > 0.0001f && dt2 > 0.0001f)
        {
            _arms.HermiteTimeFixup++;

            float fixupFraction = dt1 / dt2;
            var fixup = new InterpolationSample(start.ChangeTime - dt1, 0f, 0f, 0f);

            if (_kind == InterpolationKind.Angle)
            {
                InterpolationMath.SlerpAngles(1f - fixupFraction, previous, start, _arms,
                                              out fixup.A, out fixup.B, out fixup.C);
            }
            else
            {
                fixup.A = InterpolationMath.Lerp(1f - fixupFraction, previous.A, start.A);
                fixup.B = InterpolationMath.Lerp(1f - fixupFraction, previous.B, start.B);
                fixup.C = InterpolationMath.Lerp(1f - fixupFraction, previous.C, start.C);
            }

            previous = fixup;
        }

        if (_kind == InterpolationKind.Angle)
        {
            // Lerp_Hermite<QAngle> is SPECIALISED to a plain two-sample lerp --
            // "can't do hermite with QAngles, get discontinuities" -- so the
            // oldest sample is deliberately unused here.
            InterpolationMath.SlerpAngles(fraction, start, end, _arms, out a, out b, out c);
            return;
        }

        InterpolationMath.LerpHermite(fraction, previous.A, start.A, end.A, out a);
        InterpolationMath.LerpHermite(fraction, previous.B, start.B, end.B, out b);
        InterpolationMath.LerpHermite(fraction, previous.C, start.C, end.C, out c);
    }

    private void Extrapolate(in InterpolationSample old, in InterpolationSample recent,
                             float destinationTime, float maxExtrapolationAmount,
                             out float a, out float b, out float c)
    {
        if (MathF.Abs(old.ChangeTime - recent.ChangeTime) < 0.001f || destinationTime <= recent.ChangeTime)
        {
            a = recent.A; b = recent.B; c = recent.C;
            return;
        }

        float amount = MathF.Min(destinationTime - recent.ChangeTime, maxExtrapolationAmount);
        float divisor = 1.0f / (recent.ChangeTime - old.ChangeTime);
        float t = 1.0f + amount * divisor;

        if (_kind == InterpolationKind.Angle)
        {
            InterpolationMath.SlerpAngles(t, old, recent, _arms, out a, out b, out c);
            return;
        }

        a = InterpolationMath.Lerp(t, old.A, recent.A);
        b = InterpolationMath.Lerp(t, old.B, recent.B);
        c = InterpolationMath.Lerp(t, old.C, recent.C);
    }
}

/// <summary>
/// The arithmetic, in the order and with the groupings Source's does it in.
///
/// <para>
/// THE GROUPING IS THE POINT, not a style. Floating point addition is not
/// associative, so <c>p1*k1 + p2*k2 + d1*k3 + d2*k4</c> accumulated in a
/// different order is a different number in the last bits, and the fixture
/// compares the origin BIT FOR BIT against what the C++ produced. Every
/// expression here is written the way <c>lerp_functions.h</c> writes it.
/// </para>
/// </summary>
internal static class InterpolationMath
{
    /// <summary>mathlib.h:632 — <c>A + (B - A) * flPercent</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float percent, float a, float b) => a + (b - a) * percent;

    /// <summary>lerp_functions.h:42.</summary>
    public static void LerpHermite(float t, float p0, float p1, float p2, out float result)
    {
        float d1 = p1 - p0;
        float d2 = p2 - p1;

        float tSqr = t * t;
        float tCube = t * tSqr;

        float output = p1 * (2 * tCube - 3 * tSqr + 1);
        output += p2 * (-2 * tCube + 3 * tSqr);
        output += d1 * (tCube - 2 * tSqr + t);
        output += d2 * (tCube - tSqr);

        result = output;
    }

    // M_PI_F, mathlib.h:294 -- (float)M_PI, and the conversions are computed
    // from IT rather than from the double, because that is what DEG2RAD and
    // RAD2DEG do: ( (float)(x) * (float)(M_PI_F / 180.f) ).
    private static readonly float MPiF = (float)Math.PI;
    private static readonly float DegToRad = MPiF / 180.0f;
    private static readonly float RadToDeg = 180.0f / MPiF;

    /// <summary>
    /// <c>Lerp&lt;QAngle&gt;</c>, mathlib.h:661 — to quaternions, slerp, back.
    /// </summary>
    public static void SlerpAngles(float percent, in InterpolationSample q1, in InterpolationSample q2,
                                   InterpolationArms arms, out float a, out float b, out float c)
    {
        // "Avoid precision errors" -- and it is not only precision: it is what
        // keeps a pair of identical facings out of acos() at all.
        if (q1.A.Equals(q2.A) && q1.B.Equals(q2.B) && q1.C.Equals(q2.C))
        {
            arms.AngleSlerpEqual++;
            a = q1.A; b = q1.B; c = q1.C;
            return;
        }

        Span<float> src = stackalloc float[4];
        Span<float> dest = stackalloc float[4];
        Span<float> result = stackalloc float[4];

        AngleQuaternion(q1.A, q1.B, q1.C, src);
        AngleQuaternion(q2.A, q2.B, q2.C, dest);

        if (QuaternionAlign(src, dest))
            arms.AngleSlerpFlipped++;

        QuaternionSlerpNoAlign(src, dest, percent, result);
        QuaternionAngles(result, out a, out b, out c);
    }

    /// <summary>mathlib_base.cpp's <c>AngleQuaternion</c>, non-X360 arm.</summary>
    private static void AngleQuaternion(float pitch, float yaw, float roll, Span<float> quat)
    {
        float sy = MathF.Sin(yaw * DegToRad * 0.5f), cy = MathF.Cos(yaw * DegToRad * 0.5f);
        float sp = MathF.Sin(pitch * DegToRad * 0.5f), cp = MathF.Cos(pitch * DegToRad * 0.5f);
        float sr = MathF.Sin(roll * DegToRad * 0.5f), cr = MathF.Cos(roll * DegToRad * 0.5f);

        float srXcp = sr * cp, crXsp = cr * sp;
        quat[0] = srXcp * cy - crXsp * sy;   // X
        quat[1] = crXsp * cy + srXcp * sy;   // Y

        float crXcp = cr * cp, srXsp = sr * sp;
        quat[2] = crXcp * sy - srXsp * cy;   // Z
        quat[3] = crXcp * cy + srXsp * sy;   // W
    }

    /// <summary>
    /// <c>QuaternionAlign</c>: q and −q are the same rotation, and slerping to
    /// the wrong one goes the long way round.
    /// </summary>
    /// <returns>Whether the destination was negated.</returns>
    private static bool QuaternionAlign(ReadOnlySpan<float> p, Span<float> q)
    {
        float a = 0, b = 0;
        for (int i = 0; i < 4; i++)
        {
            a += (p[i] - q[i]) * (p[i] - q[i]);
            b += (p[i] + q[i]) * (p[i] + q[i]);
        }

        if (a > b)
        {
            for (int i = 0; i < 4; i++)
                q[i] = -q[i];
            return true;
        }

        return false;
    }

    /// <summary>mathlib_base.cpp's <c>QuaternionSlerpNoAlign</c>.</summary>
    private static void QuaternionSlerpNoAlign(ReadOnlySpan<float> p, ReadOnlySpan<float> q, float t,
                                               Span<float> qt)
    {
        float cosom = p[0] * q[0] + p[1] * q[1] + p[2] * q[2] + p[3] * q[3];

        if ((1.0f + cosom) > 0.000001f)
        {
            float sclp, sclq;

            if ((1.0f - cosom) > 0.000001f)
            {
                // double, because the C++ calls acos/sin and not acosf/sinf.
                double omega = Math.Acos(cosom);
                double sinom = Math.Sin(omega);
                sclp = (float)(Math.Sin((1.0f - t) * omega) / sinom);
                sclq = (float)(Math.Sin(t * omega) / sinom);
            }
            else
            {
                sclp = 1.0f - t;
                sclq = t;
            }

            for (int i = 0; i < 4; i++)
                qt[i] = sclp * p[i] + sclq * q[i];
        }
        else
        {
            // The antipodal case: there is no shortest arc, so stock picks a
            // perpendicular and sweeps through it.
            Span<float> perpendicular = stackalloc float[4];
            perpendicular[0] = -q[1];
            perpendicular[1] = q[0];
            perpendicular[2] = -q[3];
            perpendicular[3] = q[2];

            float sclp = (float)Math.Sin((1.0f - t) * (0.5 * Math.PI));
            float sclq = (float)Math.Sin(t * (0.5 * Math.PI));

            // THREE and not four, which looks like a bug in Valve's code and is
            // reproduced rather than corrected: qt[3] is left holding whatever
            // the caller's buffer had. Ours is a fresh stack span, so it is the
            // slerp output's own previous content -- and this arm needs two
            // exactly opposite facings to be reached at all.
            for (int i = 0; i < 3; i++)
                qt[i] = sclp * p[i] + sclq * perpendicular[i];
        }
    }

    /// <summary>
    /// <c>QuaternionAngles</c> — through the matrix, as mathlib's <c>#if 1</c>
    /// arm does, because <c>MatrixAngles</c>' pole handling is what makes a
    /// pitch of ±90 come back as a facing rather than as a NaN.
    /// </summary>
    private static void QuaternionAngles(ReadOnlySpan<float> q, out float pitch, out float yaw, out float roll)
    {
        float qx = q[0], qy = q[1], qz = q[2], qw = q[3];

        // QuaternionMatrix, mathlib_base.cpp:1892. THE FIRST COLUMN IS COMPUTED
        // IN DOUBLE AND THE REST IN FLOAT -- `1.0 - 2.0 * q.y * q.y` against
        // `1.0f - 2.0f * q.x * q.x` three lines down. That is Valve's, it is
        // almost certainly a typo, and it is reproduced rather than tidied:
        // the whole point of this file is to produce the same bits.
        float forwardX = (float)(1.0 - 2.0 * qy * qy - 2.0 * qz * qz);
        float forwardY = (float)(2.0 * qx * qy + 2.0 * qw * qz);
        float forwardZ = (float)(2.0 * qx * qz - 2.0 * qw * qy);

        float leftX = 2.0f * qx * qy - 2.0f * qw * qz;
        float leftY = 1.0f - 2.0f * qx * qx - 2.0f * qz * qz;
        float leftZ = 2.0f * qy * qz + 2.0f * qw * qx;

        float upZ = 1.0f - 2.0f * qx * qx - 2.0f * qy * qy;

        // MatrixAngles, mathlib_base.cpp. Only the Z of `up` is extracted,
        // because only the Z is used.
        float xyDist = MathF.Sqrt(forwardX * forwardX + forwardY * forwardY);

        if (xyDist > 0.001f)
        {
            yaw = RadToDeg * MathF.Atan2(forwardY, forwardX);
            pitch = RadToDeg * MathF.Atan2(-forwardZ, xyDist);
            roll = RadToDeg * MathF.Atan2(leftZ, upZ);
        }
        else
        {
            // Forward is mostly Z: one degree of freedom is gone, so yaw comes
            // off `left` and roll is pinned at zero.
            yaw = RadToDeg * MathF.Atan2(-leftX, leftY);
            pitch = RadToDeg * MathF.Atan2(-forwardZ, xyDist);
            roll = 0f;
        }
    }
}

/// <summary>
/// EVERY CLIENT ENTITY'S HISTORY, AND THE ONE PASS OVER IT (plan_entity.md §3.9).
///
/// <para>
/// <b>Two moments a frame, and both are stages the engine already raises.</b>
/// Samples are filed at <see cref="ClientFrameStage.NetUpdatePostDataUpdateEnd"/>,
/// which is the moment every entity in the packet has been decoded and its
/// networked pose is sitting in the block head; the drawn pose is computed at
/// <see cref="ClientFrameStage.RenderStart"/>, which is where stock interpolates
/// its own entities (<c>OnRenderStart</c> →
/// <c>C_BaseEntity::InterpolateServerEntities</c>). Neither is a new ABI slot:
/// both arrive through 5b's single <c>ClientStage</c> crossing, which is exactly
/// what 5b said it was landing that crossing for.
/// </para>
///
/// <para>
/// <b>The cost does not scale with the entity count, and that is checkable.</b>
/// One outbound call per stage fetches the whole list of blocks; everything
/// after it is arithmetic over memory the engine and this code share. A map with
/// four hundred entities and a map with forty make the same two calls.
/// </para>
///
/// <para>
/// <b>Rings are keyed by ENTITY INDEX and validated by HANDLE.</b> An index is a
/// slot and slots are recycled; a handle is a slot plus a serial and is unique
/// for the life of the map. Finding a different handle in a slot is exactly the
/// "this is a new entity" signal that <c>DATA_UPDATE_CREATED</c> was on the
/// native side, and it takes the same action: throw the history away and seed it
/// with three copies of the arriving pose.
/// </para>
/// </summary>
public sealed class Interpolation
{
    /// <summary>
    /// How many entity slots have rings: <c>NUM_ENT_ENTRIES</c>,
    /// <c>public/const.h:78</c>. NOT <c>MAX_EDICTS</c> — the client's entity
    /// list is four times as long as the networked one and a handle's entry
    /// index addresses the whole of it.
    /// </summary>
    public const int MaxEntities = 8192;

    /// <summary>
    /// <c>ENT_ENTRY_MASK</c>, <c>public/const.h:83</c> — the entry index inside
    /// a packed <c>CBaseHandle</c>. Sixteen bits, not eleven: the serial number
    /// occupies the top sixteen and the entry index everything below.
    /// </summary>
    public const uint EntryMask = 0xFFFFu;

    /// <summary>
    /// <c>cl_extrapolate_amount</c>'s default, interpolatedvar.cpp:29. Read as a
    /// constant rather than as a convar because the arm that uses it is
    /// unreachable from this caller — see <see cref="InterpolationRing.Interpolate"/>.
    /// </summary>
    public const float ExtrapolateAmount = 0.25f;

    private readonly InterpolationRing?[] _rings = new InterpolationRing[MaxEntities * InterpolatedFields.Count];
    private readonly uint[] _handles = new uint[MaxEntities];
    private readonly float[] _lastFiled = new float[MaxEntities];

    /// <summary>Which arms have run. See <see cref="InterpolationArms"/>.</summary>
    public InterpolationArms Arms { get; } = new();

    /// <summary>
    /// How many samples have been filed since the level loaded, over every
    /// entity. THE READOUT THAT CAN FAIL: a build that quietly stopped
    /// interpolating would cost no crossings and show a zero here, and 1g's
    /// <c>status.think</c> is on the record as the reason a claim about cost
    /// needs a witness that the work happened.
    /// </summary>
    public long SamplesFiled { get; private set; }

    /// <summary>How many drawn poses have been written, over every entity.</summary>
    public long PosesWritten { get; private set; }

    /// <summary>What the last <see cref="SamplePass"/> filed.</summary>
    public int LastSamplePass { get; private set; }

    /// <summary>What the last <see cref="RenderPass"/> wrote.</summary>
    public int LastRenderPass { get; private set; }

    /// <summary>How many times the sample pass has run.</summary>
    public long SamplePasses { get; private set; }

    /// <summary>How many times the render pass has run.</summary>
    public long RenderPasses { get; private set; }

    /// <summary>Entities whose ring was reset because their slot changed hands.</summary>
    public long SlotsRebound { get; private set; }

    /// <summary>
    /// ENTITIES WHOSE RING WAS RESET BECAUSE THEY JUMPED — the other
    /// discontinuity, and until this counter existed there was no such thing.
    ///
    /// <para>
    /// A slot rebind is a DIFFERENT entity arriving in the same index, which is
    /// what <see cref="SlotsRebound"/> counts and what covers a create. A
    /// teleport is the SAME entity somewhere else, and the history has no way to
    /// tell that from a very fast move: it draws the whole journey. See
    /// <see cref="TeleportDiscontinuity"/> for the counter the server bumps and
    /// why it is a counter.
    /// </para>
    ///
    /// <para>
    /// IT IS A READOUT THAT CAN FAIL, and deliberately so. A build whose wire
    /// half was landed without this one, or the reverse, shows a zero here
    /// while every test stays green — which is precisely the shape
    /// <c>ContractBaseline</c>'s entry described. It is not the proof, though:
    /// the proof is a rendered measurement, because a counter that moves while
    /// nothing on screen changes is this project's standing failure.
    /// </para>
    /// </summary>
    public long Teleported { get; private set; }

    //-------------------------------------------------------------------------
    // THE RENDERED MEASUREMENT (tools/defect-teleport-gate).
    //
    // DEBT, WITH A REMOVAL OWNER. This is lane defect-teleport's instrument and
    // nothing else reads it. It goes when the teleport row is folded into
    // tools/live-gate, exactly as 8e-entfire's gate is booked to go.
    //
    // WHY IT IS HERE AND NOT A COUNTER OF TELEPORTS. "The counter moved" is
    // this project's standing false green: a build whose reset arm was deleted
    // would still bump, still network and still count. A SLIDE IS A RENDERED
    // SYMPTOM, so what is measured is the pose the frame was DRAWN with -- the
    // render tail, which is what GetRenderOrigin answers and what the renderer
    // reads per draw -- against the pose the packet delivered. The two are
    // equal for an entity standing still, differ for one being interpolated
    // towards a new place, and must be equal on the very first frame after a
    // JUMP. The control arm is the same journey with no jump declared, which
    // must still lag; without it a build that had stopped interpolating
    // everything would read zero here and look like a fix.
    //-------------------------------------------------------------------------

    /// <summary>The entity index the probe follows, or -1.</summary>
    public int WatchedIndex { get; private set; } = -1;

    /// <summary>Rendered frames the watched entity had a drawn pose written on.</summary>
    public long WatchFrames { get; private set; }

    /// <summary>...of which the drawn pose was NOT the pose the packet delivered.</summary>
    public long WatchLagging { get; private set; }

    /// <summary>The furthest the drawn pose was from the delivered one, in units.</summary>
    public float WatchMaxLag { get; private set; }

    /// <summary>And what it was on the last of them.</summary>
    public float WatchLastLag { get; private set; }

    /// <summary>How far the delivered pose has moved while the probe watched.</summary>
    public float WatchDelivered { get; private set; }

    /// <summary>
    /// Rendered frames on which the delivered pose CHANGED — how many packets
    /// the journey arrived in, as the client saw it.
    ///
    /// <para>
    /// THE ROW THAT SAYS WHAT THE OTHER NUMBERS MEAN, and it was added because
    /// the first live run of this gate could not be read without it. The
    /// control arm read 39 lagging frames and a maximum lag of only 10.7 units
    /// over a 500-unit journey, which is either "the entity slid a tenth of the
    /// way" or "the journey arrived in forty pieces and the drawn pose was one
    /// piece behind each time". Those are different findings and no other
    /// number here tells them apart.
    /// </para>
    /// </summary>
    public long WatchDeliveries { get; private set; }

    /// <summary>The largest single step the delivered pose took, in units.</summary>
    public float WatchBiggestDelivery { get; private set; }

    /// <summary>
    /// HOW FAR SHORT THE ENTITY WAS DRAWN ON THE FRAME ITS BIGGEST STEP ARRIVED
    /// ON — the ledger's own claim, as one number.
    ///
    /// <para>
    /// "Drawn sliding to where it went" IS this being large: the packet put the
    /// entity somewhere and the frame drew it somewhere else. A declared jump
    /// resets the history, so the drawn pose is the delivered one on that very
    /// frame and this reads zero.
    /// </para>
    /// </summary>
    public float WatchArrivalLag { get; private set; }

    /// <summary>
    /// Lagging rendered frames SINCE THE LAST DELIVERY — the ledger's claim as
    /// a count rather than as a distance, and the row a whole-window count
    /// cannot be.
    ///
    /// <para>
    /// A whole-window count answers a different question than it looks like it
    /// answers. Anything the entity did BEFORE the step under measurement is
    /// still being drawn out when the step lands, so a window that opens a
    /// fraction of a second after any other movement carries that movement's
    /// tail — measured, on the fourth run of this gate: a declared jump read 19
    /// lagging frames of a maximum 2.9 units, every one of them the last
    /// warm-up move finishing and none of them the jump. This counts from the
    /// arrival, which is the only interval the claim is about.
    /// </para>
    /// </summary>
    public long WatchLaggingSinceDelivery { get; private set; }

    /// <summary>...and the furthest it was from the delivered pose in them.</summary>
    public float WatchMaxLagSinceDelivery { get; private set; }

    /// <summary>
    /// Follow one entity, from zero. -1 stops. The counters are zeroed on every
    /// call so a driver measures ONE arm rather than a run.
    /// </summary>
    public void Watch(int index)
    {
        WatchedIndex = index;
        WatchFrames = WatchLagging = WatchDeliveries = WatchLaggingSinceDelivery = 0;
        WatchMaxLag = WatchLastLag = WatchDelivered = 0f;
        WatchBiggestDelivery = WatchArrivalLag = WatchMaxLagSinceDelivery = 0f;
        _watchLastDelivered = null;
    }

    private InterpolationSample? _watchLastDelivered;

    /// <summary>
    /// One rendered frame of the watched entity: what it was drawn at, against
    /// what the packet said.
    /// </summary>
    private void NoteWatched(int index, ReadOnlySpan<byte> block)
    {
        InterpolationRing ring = RingFor(index, WatchField);
        if (ring.Count == 0)
            return;

        InterpolationSample delivered = ring[0];

        ReadOnlySpan<byte> at = block.Slice(InterpolatedFields.All[WatchField].RenderOffset,
                                           3 * sizeof(float));
        float dx = BitConverter.ToSingle(at) - delivered.A;
        float dy = BitConverter.ToSingle(at.Slice(sizeof(float))) - delivered.B;
        float dz = BitConverter.ToSingle(at.Slice(2 * sizeof(float))) - delivered.C;

        float lag = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        WatchFrames++;
        WatchLastLag = lag;

        // THE DELIVERY IS NOTICED FIRST, because the since-delivery counters are
        // zeroed by it and this frame's own lag belongs AFTER the zeroing: on
        // the arrival frame itself the entity either is where the packet put it
        // or is not, and that is the whole question.
        bool arrived = NoteDelivery(delivered, lag);

        if (arrived)
        {
            WatchLaggingSinceDelivery = 0;
            WatchMaxLagSinceDelivery = 0f;
        }

        // A tenth of a unit. The pose is carried as an unscaled float and the
        // blend is float arithmetic, so an entity standing perfectly still reads
        // a lag of exactly zero -- but a threshold of zero would make this
        // instrument a report about rounding.
        if (lag > 0.1f)
        {
            WatchLagging++;
            WatchLaggingSinceDelivery++;

            if (lag > WatchMaxLag)
                WatchMaxLag = lag;

            if (lag > WatchMaxLagSinceDelivery)
                WatchMaxLagSinceDelivery = lag;
        }
    }

    /// <summary>
    /// Whether the delivered pose moved on this frame, accumulating what it did.
    /// </summary>
    private bool NoteDelivery(in InterpolationSample delivered, float lag)
    {
        InterpolationSample? previous = _watchLastDelivered;
        _watchLastDelivered = delivered;

        if (previous is not InterpolationSample was)
            return false;

        float mx = delivered.A - was.A;
        float my = delivered.B - was.B;
        float mz = delivered.C - was.C;
        float step = MathF.Sqrt(mx * mx + my * my + mz * mz);

        WatchDelivered += step;

        // A hundredth of a unit, for the same reason the lag threshold is a
        // tenth: the pose is float arithmetic and a step of exactly zero is the
        // only honest "it did not move".
        if (step <= 0.01f)
            return false;

        WatchDeliveries++;

        // The lag is recorded FOR THE BIGGEST STEP rather than for the first,
        // because a journey that arrives in pieces has no single arrival -- and
        // the biggest piece is the one a slide would be most visible on.
        if (step > WatchBiggestDelivery)
        {
            WatchBiggestDelivery = step;
            WatchArrivalLag = lag;
        }

        return true;
    }

    /// <summary>
    /// The field the probe measures: the origin, which is the one a slide is
    /// visible in. Field 0 by declaration order — see <see cref="InterpolatedFields"/>.
    /// </summary>
    private const int WatchField = 0;

    /// <summary>Forget everything. A level change recycles every index.</summary>
    public void Clear()
    {
        Array.Clear(_rings);
        Array.Clear(_handles);
        Array.Clear(_lastFiled);
        Arms.Clear();
        SamplesFiled = PosesWritten = SamplePasses = RenderPasses = SlotsRebound = 0;
        Teleported = 0;
        Watch(-1);
        LastSamplePass = LastRenderPass = 0;
    }

    /// <summary>The ring for one field of one entity, made on first use.</summary>
    public InterpolationRing RingFor(int index, int field)
    {
        if ((uint)index >= MaxEntities)
            throw new ArgumentOutOfRangeException(nameof(index));
        if ((uint)field >= (uint)InterpolatedFields.Count)
            throw new ArgumentOutOfRangeException(nameof(field));

        int i = index * InterpolatedFields.Count + field;
        return _rings[i] ??= new InterpolationRing(InterpolatedFields.All[field].Kind, Arms);
    }

    /// <summary>
    /// The deepest of one entity's rings, in samples; 0 when none was made.
    /// A reader's question: unlike <see cref="RingFor"/> it makes nothing.
    /// </summary>
    public int RingDepth(int index)
    {
        if ((uint)index >= MaxEntities)
            return 0;

        int depth = 0;
        for (int field = 0; field < InterpolatedFields.Count; field++)
            depth = Math.Max(depth, _rings[index * InterpolatedFields.Count + field]?.Count ?? 0);
        return depth;
    }

    /// <summary>One entity's ring for one field, or null when none was made. Makes nothing.</summary>
    public InterpolationRing? PeekRing(int index, int field)
        => (uint)index < MaxEntities && (uint)field < (uint)InterpolatedFields.Count
            ? _rings[index * InterpolatedFields.Count + field]
            : null;

    /// <summary>How many rings have actually been made — the preallocation's real size.</summary>
    public int LiveRings
    {
        get
        {
            int n = 0;
            foreach (InterpolationRing? ring in _rings)
                if (ring is not null)
                    n++;
            return n;
        }
    }

    /// <summary>
    /// Whether this slot is holding this entity, resetting the slot's rings if
    /// it is not.
    /// </summary>
    /// <returns>True if the slot was rebound, i.e. this handle is new here.</returns>
    public bool Rebind(int index, uint handle)
    {
        if (_handles[index] == handle)
            return false;

        _handles[index] = handle;
        _lastFiled[index] = 0f;

        for (int f = 0; f < InterpolatedFields.Count; f++)
            _rings[index * InterpolatedFields.Count + f]?.Clear();

        SlotsRebound++;
        return true;
    }

    /// <summary>The last change time filed for this slot, or 0.</summary>
    public float LastFiled(int index) => _lastFiled[index];

    /// <summary>
    /// File one entity's freshly decoded networked pose.
    ///
    /// <para>
    /// <paramref name="fresh"/> is what <c>bTeleported</c> was on the native
    /// side: a pose with no past to blend from. Without it a projectile spawning
    /// at the muzzle is drawn streaking in from wherever the last entity in this
    /// slot died.
    /// </para>
    /// </summary>
    public void NoteNetworked(int index, uint handle, float changeTime, float interpolationAmount,
                              float curTime, bool fresh, ReadOnlySpan<InterpolationSample> values)
    {
        bool rebound = Rebind(index, handle);

        for (int f = 0; f < InterpolatedFields.Count; f++)
        {
            InterpolationRing ring = RingFor(index, f);
            InterpolationSample v = values[f];

            if (fresh || rebound)
                ring.Reset(curTime, v.A, v.B, v.C);

            ring.NoteChanged(changeTime, interpolationAmount, curTime, v.A, v.B, v.C);
        }

        _lastFiled[index] = changeTime;
        SamplesFiled++;
    }

    /// <summary>The drawn value of one field of one entity.</summary>
    public bool Interpolate(int index, int field, float currentTime, float interpolationAmount,
                            out InterpolationSample drawn)
    {
        bool ok = RingFor(index, field).Interpolate(
            currentTime, interpolationAmount, allowExtrapolation: false, ExtrapolateAmount,
            out float a, out float b, out float c);

        drawn = new InterpolationSample(currentTime, a, b, c);
        return ok;
    }

    //-------------------------------------------------------------------------
    // The two passes, over the engine's own memory.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <see cref="ClientFrameStage.NetUpdatePostDataUpdateEnd"/>: every entity
    /// the packet moved, filed.
    ///
    /// <para>
    /// <b>Why here and not at render time.</b> The head holds the networked pose
    /// only until this frame's render pass writes the drawn one over it, so a
    /// sample read at render time is the value the PREVIOUS frame drew. Read
    /// here, one stage after the decoder wrote it, it is the packet's own value —
    /// and a second packet in the same frame raises this stage again, so no
    /// sample can be passed over.
    /// </para>
    ///
    /// <para>
    /// <b>What says a sample arrived</b> is <c>Client.SimulationTime</c>, which
    /// the shell writes from <c>engine-&gt;GetLastTimeStamp()</c> for every entity
    /// whose data changed and for no other. An entity the packet did not carry
    /// keeps the time it had and is skipped, which is exactly the entities
    /// <c>PostDataUpdate</c> was not called for.
    /// </para>
    /// </summary>
    public int SamplePass(IInterpolationBlocks blocks, float curTime)
    {
        SamplePasses++;

        int count = blocks.Begin(out float interpolationAmount);
        if (count <= 0)
            return 0;

        int filed = 0;
        Span<InterpolationSample> values = stackalloc InterpolationSample[InterpolatedFields.Count];

        for (int i = 0; i < count; i++)
        {
            Span<byte> block = blocks.Block(i);
            if (block.Length < EngineBlock.SizeBytes)
                continue;

            uint handle = ReadHandle(block);
            int index = (int)(handle & EntryMask);
            if ((uint)index >= MaxEntities)
                continue;

            float changeTime = ReadSimulationTime(block);
            bool fresh = _handles[index] != handle;

            // THE SECOND DISCONTINUITY, and the one this pass used to have no
            // notion of. `fresh` is a HANDLE REBIND -- a different entity in
            // this slot -- which covers a create and nothing else. An entity
            // that TELEPORTED is the same handle at a new pose, and to a history
            // of poses that is indistinguishable from a very fast move, so it
            // was drawn travelling the whole distance. The server says which it
            // was, in the entity's own block: see TeleportDiscontinuity.
            bool teleported = TeleportDiscontinuity.HasTeleported(block);

            // The one test that says "this entity was in the packet". An entity
            // the packet did not carry keeps the time it had, which is exactly
            // the set PostDataUpdate was not called for.
            //
            // A teleport is exempt from it. It should never matter -- a jump
            // moves the origin, and an origin that moved came with a new
            // simulation time -- but the cost of being wrong is an entity left
            // sliding for the rest of the map, and the cost of the extra test is
            // one comparison on the entities the pass was about to skip.
            if (!fresh && !teleported && changeTime.Equals(_lastFiled[index]))
                continue;

            Read(block, values);
            NoteNetworked(index, handle, changeTime, interpolationAmount, curTime,
                          fresh || teleported, values);

            if (teleported)
            {
                // AFTER the reset and never before. Acknowledging first would
                // leave the entity sliding for ever if anything between the two
                // returned early, and the two words are the only record there
                // is that the jump was dealt with.
                TeleportDiscontinuity.Acknowledge(block);
                Teleported++;
            }

            filed++;
        }

        LastSamplePass = filed;
        return filed;
    }

    /// <summary>
    /// <see cref="ClientFrameStage.RenderStart"/>: the drawn pose of every
    /// entity, into the block.
    ///
    /// <para>
    /// <b>Both the head and the render tail are written, and the pair is
    /// deliberate.</b> §3.2 gives the tail a pose of its own so that
    /// <c>GetRenderOrigin</c> and <c>GetCollisionOrigin</c> can both be one load
    /// without either lying, and this writes the drawn value there. It also
    /// writes the head, because that is where the native
    /// <c>CInterpolatedVar</c> wrote, and where the collision transform and the
    /// spatial partition still read from — and that is stock's shape rather than
    /// a step 6a could not finish: stock interpolates <c>m_vecOrigin</c> itself,
    /// so <c>C_BaseEntity</c>'s <c>GetRenderOrigin</c> and <c>CollisionProp</c>'s
    /// <c>GetCollisionOrigin</c> both answer the drawn pose.
    /// </para>
    ///
    /// <para>
    /// <b>Step 6b split the TRANSFORM, not the pose.</b>
    /// <c>RenderableToWorldTransform</c> answers <c>Client.RenderTransform</c>
    /// now and <c>CollisionToWorldTransform</c> answers <c>Head.WorldTransform</c>,
    /// so a renderable can be drawn where it is not — which is what the
    /// viewmodel's eye pin uses, writing the tail alone. This pass still writes
    /// both because for an interpolated entity the two poses are the same value.
    /// </para>
    /// </summary>
    /// <param name="blocks">The engine's interpolation list.</param>
    /// <param name="currentTime">The time to draw at.</param>
    /// <param name="interpolate">
    /// <see cref="InterpolationGate"/>'s answer for the frame. When it is
    /// closed every field is drawn at its newest sample, the value the server
    /// last sent: stock's <c>BaseInterpolatePart1</c>, which on
    /// <c>!IsInterpolationEnabled()</c> calls <c>MoveToLastReceivedPosition</c>
    /// and stops.
    /// </param>
    public int RenderPass(IInterpolationBlocks blocks, float currentTime, bool interpolate = true)
    {
        RenderPasses++;

        int count = blocks.Begin(out float interpolationAmount);
        if (count <= 0)
            return 0;

        int written = 0;

        for (int i = 0; i < count; i++)
        {
            Span<byte> block = blocks.Block(i);
            if (block.Length < EngineBlock.SizeBytes)
                continue;

            uint handle = ReadHandle(block);
            int index = (int)(handle & EntryMask);

            // An entity no sample has ever been filed for has no history to
            // draw a point out of, and seeding one HERE from the head would
            // seed it from whatever the last pass wrote. Skipped: the next
            // packet that carries it files its first sample.
            if ((uint)index >= MaxEntities || _handles[index] != handle)
                continue;

            for (int f = 0; f < InterpolatedFields.Count; f++)
            {
                if (!interpolate)
                {
                    // MoveToLastReceivedPosition: the newest sample, as filed.
                    if (PeekRing(index, f) is { Count: > 0 } ring)
                        Write(block, f, ring[0]);
                    continue;
                }

                if (!Interpolate(index, f, currentTime, interpolationAmount, out InterpolationSample drawn))
                    continue;

                Write(block, f, drawn);
            }

            if (index == WatchedIndex)
                NoteWatched(index, block);

            written++;
            PosesWritten++;
        }

        LastRenderPass = written;
        return written;
    }

    //-------------------------------------------------------------------------
    // The block, as bytes. SourceSharp.Client is compiled WITHOUT /unsafe --
    // "function-pointer interop lives in the ABI assembly and game code stays
    // safe" -- so the block arrives as a bounds-checked span and every read and
    // write below is one.
    //-------------------------------------------------------------------------

    private static readonly int HandleOffset =
        (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
        + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Handle));

    private static readonly int SimulationTimeOffset =
        (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
        + (int)Marshal.OffsetOf<EngineBlockClientTail>(nameof(EngineBlockClientTail.SimulationTime));

    /// <summary>The entity's packed <c>CBaseHandle</c>, at the very base of the block.</summary>
    public static uint ReadHandle(ReadOnlySpan<byte> block)
        => BitConverter.ToUInt32(block.Slice(HandleOffset, sizeof(uint)));

    /// <summary>
    /// The server time the last update was from — what the shell writes from
    /// <c>engine-&gt;GetLastTimeStamp()</c> for every entity whose data changed.
    /// </summary>
    public static float ReadSimulationTime(ReadOnlySpan<byte> block)
        => BitConverter.ToSingle(block.Slice(SimulationTimeOffset, sizeof(float)));

    private static void Read(ReadOnlySpan<byte> block, Span<InterpolationSample> values)
    {
        for (int f = 0; f < InterpolatedFields.Count; f++)
        {
            ReadOnlySpan<byte> at = block.Slice(InterpolatedFields.All[f].SourceOffset, 3 * sizeof(float));

            values[f] = new InterpolationSample(
                0f,
                BitConverter.ToSingle(at),
                BitConverter.ToSingle(at.Slice(sizeof(float))),
                BitConverter.ToSingle(at.Slice(2 * sizeof(float))));
        }
    }

    private static void Write(Span<byte> block, int field, in InterpolationSample drawn)
    {
        WriteTriple(block, InterpolatedFields.All[field].SourceOffset, drawn);
        WriteTriple(block, InterpolatedFields.All[field].RenderOffset, drawn);
    }

    private static void WriteTriple(Span<byte> block, int offset, in InterpolationSample value)
    {
        Span<byte> at = block.Slice(offset, 3 * sizeof(float));

        BitConverter.TryWriteBytes(at, value.A);
        BitConverter.TryWriteBytes(at.Slice(sizeof(float)), value.B);
        BitConverter.TryWriteBytes(at.Slice(2 * sizeof(float)), value.C);
    }
}

/// <summary>
/// WHAT THE INTERPOLATION PASS ACTUALLY DID, off a live client.
///
/// <para>
/// <b>It exists because the step's own claim is otherwise untestable.</b> 6a's
/// gate is "crossings per frame unchanged by entity count", and a build that
/// quietly stopped interpolating would satisfy it perfectly — the crossing
/// counters would read the same on a map with four hundred entities and on one
/// with forty, because nothing was happening on either. 1g hit exactly this and
/// added <c>status.think</c> for it; this is the client's equivalent. Read the
/// pass counts and the crossing counts together or neither means anything.
/// </para>
///
/// <para>
/// A console command rather than a <c>debug-api</c> status key because the debug-api
/// server is the server's, and this is client state — the same reason
/// <c>cs_cl_crossings</c> and <c>cs_cl_mirror_check</c> are commands.
/// </para>
/// </summary>
[ConCommand("cs_cl_interp",
            Help = "What the client interpolation pass did: passes, samples filed, " +
                   "poses written, rings live, and which arms ran.")]
public sealed class InterpolationReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly Interpolation _interpolation;

    /// <summary>The module's interpolation state, from its container.</summary>
    public InterpolationReportCommand(ILog log, Interpolation interpolation)
    {
        _log = log;
        _interpolation = interpolation;
    }

    public void Execute(string args)
    {
        Interpolation interp = _interpolation;

        _log.Msg($"[SourceSharp/Client] interp: {interp.SamplePasses} sample pass(es), "
              + $"{interp.RenderPasses} render pass(es)\n");
        _log.Msg($"[SourceSharp/Client] interp: {interp.SamplesFiled} sample(s) filed, "
              + $"{interp.PosesWritten} pose(s) written\n");
        _log.Msg($"[SourceSharp/Client] interp: last pass {interp.LastSamplePass} sampled / "
              + $"{interp.LastRenderPass} drawn, {interp.LiveRings} ring(s), "
              + $"{interp.SlotsRebound} slot(s) bound, "
              + $"{interp.Teleported} teleport(s)\n");

        // Only the arms that ran, and an absent name means zero — the same rule
        // the crossing table uses, so a long line does not hide the short one
        // that matters.
        string arms = string.Join(" ", interp.Arms.Rows()
            .Where(r => r.Count != 0)
            .Select(r => $"{r.Name}={r.Count}"));

        _log.Msg($"[SourceSharp/Client] interp arms: {(arms.Length == 0 ? "(none)" : arms)}\n");
    }
}

/// <summary>
/// <c>cs_cl_teleport_watch &lt;entity index&gt;</c> — follow one entity's DRAWN
/// pose, from zero. <c>-1</c> stops.
///
/// <para>
/// DEBT, WITH A REMOVAL OWNER: lane <c>defect-teleport</c>'s instrument, driven
/// by <c>tools/defect-teleport-gate</c> and read by nothing else. It goes when
/// that gate's rows are folded into <c>tools/live-gate</c>.
/// </para>
/// </summary>
[ConCommand("cs_cl_teleport_watch",
            Help = "Follow one entity index's drawn pose against the pose the packet " +
                   "delivered, from zero. -1 stops.")]
public sealed class TeleportWatchCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly Interpolation _interpolation;

    /// <summary>The module's interpolation state, from its container.</summary>
    public TeleportWatchCommand(ILog log, Interpolation interpolation)
    {
        _log = log;
        _interpolation = interpolation;
    }

    public void Execute(string args)
    {
        string word = args.Trim();

        if (!int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            _log.Msg("[SourceSharp/Client] teleport-probe: an entity index is required\n");
            return;
        }

        _interpolation.Watch(index);
        _log.Msg($"[SourceSharp/Client] teleport-probe: watching index {index}, counters zeroed\n");
    }
}

/// <summary>
/// <c>cs_cl_teleport_report</c> — what the watched entity was DRAWN at.
///
/// <para>
/// <c>lagging</c> is the row that matters and it is a count of RENDERED FRAMES,
/// not of teleports: frames on which the pose the renderer read was not the pose
/// the packet delivered, which is what "drawn sliding to where it went" IS. A
/// jump that was declared one resets the history, so the very first frame after
/// it is already at the destination and this count does not move.
/// </para>
/// </summary>
[ConCommand("cs_cl_teleport_report",
            Help = "The watched entity's drawn pose against the delivered one: " +
                   "frames, lagging frames, max lag, distance delivered.")]
public sealed class TeleportReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly Interpolation _interpolation;

    /// <summary>The module's interpolation state, from its container.</summary>
    public TeleportReportCommand(ILog log, Interpolation interpolation)
    {
        _log = log;
        _interpolation = interpolation;
    }

    public void Execute(string args)
    {
        Interpolation interp = _interpolation;

        _log.Msg(string.Format(CultureInfo.InvariantCulture,
            "[SourceSharp/Client] teleport-probe: index {0} frames {1} lagging {2} "
          + "maxlag {3:F3} lastlag {4:F3} delivered {5:F3} deliveries {6} "
          + "biggest {7:F3} arrivallag {8:F3} sincelagging {9} sincemaxlag {10:F3} "
          + "resets {11}\n",
            interp.WatchedIndex, interp.WatchFrames, interp.WatchLagging,
            interp.WatchMaxLag, interp.WatchLastLag, interp.WatchDelivered,
            interp.WatchDeliveries, interp.WatchBiggestDelivery,
            interp.WatchArrivalLag, interp.WatchLaggingSinceDelivery,
            interp.WatchMaxLagSinceDelivery, interp.Teleported));
    }
}
