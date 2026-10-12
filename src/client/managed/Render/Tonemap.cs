//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Render;

/// <summary>A luminance bucket's occlusion query, as CHistogram_entry_t keeps it.</summary>
public enum HistogramState
{
    Initial = 0,
    FirstQueryInFlight,
    QueryInFlight,
    QueryDone,
}

/// <summary>
/// One of the auto-exposure histogram's buckets (viewpostprocess.cpp's
/// CHistogram_entry_t, less the engine's query handle, which native keeps per
/// bucket index).
/// </summary>
public struct HistogramBucket
{
    public HistogramState State;
    public int FrameQueued;
    public int Pixels;
    public int PixelsInRange;
    public float MinLum, MaxLum;
    public float MinX, MinY, MaxX, MaxY;

    /// <summary>A finished query, or a repeat whose last result is still standing.</summary>
    public readonly bool ContainsValidData => State == HistogramState.QueryDone || State == HistogramState.QueryInFlight;
}

/// <summary>
/// The frame's viewport and what IssueQuery reads besides the bucket: the
/// flashlight, the exposure centre-region cvars (mat_exposure_center_region_x/_y
/// and their _flashlight twins) and the tonemap scale the compare material
/// gets ($C0_Z: the engine's linear scale with float HDR, else 1).
/// </summary>
public readonly record struct HistogramView(
    int X, int Y, int Width, int Height,
    bool FlashlightOn,
    float CenterRegionX, float CenterRegionY,
    float CenterRegionXFlashlight, float CenterRegionYFlashlight,
    float ToneScale);

/// <summary>
/// A query to issue: draw dev/lumcompare with $C0_X = <see cref="TestMin"/>,
/// $C0_Y = <see cref="TestMax"/> and $C0_Z = <see cref="ToneScale"/> over the
/// rectangle (X0, Y0)-(X1, Y1) inside an occlusion query on
/// <see cref="Bucket"/>'s handle (DrawScreenSpaceRectangle's destination is
/// X0, Y0, 1 + X1 - X0, 1 + Y1 - Y0; its source is the same rectangle over
/// <see cref="Width"/> by <see cref="Height"/>).
/// </summary>
public readonly record struct LuminanceQuery(
    int Bucket, float TestMin, float TestMax, float ToneScale,
    int X0, int Y0, int X1, int Y1, int Width, int Height);

/// <summary>The engine's answer to a bucket's query: pixels drawn, or -1 while it is not finished.</summary>
public interface IOcclusionReadback
{
    int PixelsRendered(int bucket);
}

/// <summary>
/// CLuminanceHistogramSystem (viewpostprocess.cpp): the auto-exposure
/// histogram's bucket ranges, which bucket's query to issue and read back each
/// frame, and the target tonemap scalar the readbacks give. The queries
/// themselves are the engine's, carried out by native per bucket index.
/// Float arithmetic in stock's order (tonemap-stock.tsv.gz).
/// </summary>
public sealed class LuminanceHistogram
{
    /// <summary>N_LUMINANCE_RANGES: the original algorithm's buckets.</summary>
    public const int Ranges = 31;

    /// <summary>N_LUMINANCE_RANGES_NEW: the new algorithm's buckets.</summary>
    public const int RangesNew = 17;

    private const int MaxQueriesPerFrame = 1;

    private readonly HistogramBucket[] _buckets = new HistogramBucket[Ranges];
    private int _curQueryFrame;
    private int _bucketAlgorithm = -1;
    private bool _firstTime = true;

    /// <summary>The buckets, read-only (for the histogram display and facts).</summary>
    public ReadOnlySpan<HistogramBucket> Buckets => _buckets;

    /// <summary>
    /// UpdateLuminanceRanges' mod list: the games that fall back to the
    /// original algorithm. Stock's loop stops at three, so hl1 never matches.
    /// </summary>
    public static bool ForcesOriginalAlgorithm(string gameDirectory)
    {
        ReadOnlySpan<string> mods = ["dod", "cstrike", "lostcoast", "hl1"];
        for (int i = 0; i < 3; i++)
        {
            if (gameDirectory.Length >= mods[i].Length &&
                gameDirectory.AsSpan(gameDirectory.Length - mods[i].Length).Equals(mods[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// UpdateLuminanceRanges: when the algorithm changed, every bucket starts
    /// over with its range. Returns the algorithm in force: the first call
    /// moves a mod on the list to the original one, and the caller sets
    /// mat_tonemap_algorithm to match.
    /// </summary>
    public int UpdateLuminanceRanges(int algorithm, string gameDirectory)
    {
        if (_bucketAlgorithm == algorithm)
            return algorithm;
        _bucketAlgorithm = algorithm;

        if (_firstTime)
        {
            _firstTime = false;
            if (ForcesOriginalAlgorithm(gameDirectory))
            {
                algorithm = 0;
                _bucketAlgorithm = 0;
            }
        }

        int ranges = algorithm == 1 ? RangesNew : Ranges;
        _curQueryFrame = 0;
        for (int bucket = 0; bucket < ranges; bucket++)
        {
            ref HistogramBucket e = ref _buckets[bucket];
            e.State = HistogramState.Initial;
            e.MinX = 0;
            e.MaxX = 1;
            e.MinY = 0;
            e.MaxY = 1;
            if (bucket != ranges - 1)
            {
                if (algorithm == 0)
                {
                    // A logarithmic ramp: more range in the low buckets.
                    // exp of FLerp's float is expf; the -0.01 is a double.
                    e.MinLum = (float)(-0.01 + MathF.Exp(FLerp((float)Math.Log(.01), (float)Math.Log(.01 + 1), 0, ranges - 1, bucket)));
                    e.MaxLum = (float)(-0.01 + MathF.Exp(FLerp((float)Math.Log(.01), (float)Math.Log(.01 + 1), 0, ranges - 1, bucket + 1)));
                }
                else
                {
                    // An even split, bent to give the low range a few more buckets.
                    e.MinLum = (float)bucket / (float)(ranges - 1);
                    e.MaxLum = (float)(bucket + 1) / (float)(ranges - 1);
                    e.MinLum = e.MinLum > 0.0f ? MathF.Pow(e.MinLum, 1.5f) : e.MinLum;
                    e.MaxLum = e.MaxLum > 0.0f ? MathF.Pow(e.MaxLum, 1.5f) : e.MaxLum;
                }
            }
            else
            {
                // The last bucket counts every pixel: the scale for boards whose
                // queries count more than the pixels drawn (AA).
                e.MinLum = 0;
                e.MaxLum = 100000.0f;
            }
        }
        return algorithm;
    }

    /// <summary>
    /// Update: reads back the queries old enough to have finished and issues
    /// at most one query, the first bucket never queried or else the one whose
    /// answer is oldest. Returns the algorithm in force (see <see cref="UpdateLuminanceRanges"/>).
    /// </summary>
    public int Update(int algorithm, string gameDirectory, in HistogramView view, IOcclusionReadback readback, List<LuminanceQuery> issued)
    {
        algorithm = UpdateLuminanceRanges(algorithm, gameDirectory);

        int issuedThisFrame = 0;
        _curQueryFrame++;

        int ranges = algorithm == 1 ? RangesNew : Ranges;
        for (int i = 0; i < ranges; i++)
        {
            ref HistogramBucket e = ref _buckets[i];
            switch (e.State)
            {
                case HistogramState.Initial:
                    if (issuedThisFrame < MaxQueriesPerFrame)
                    {
                        issued.Add(IssueQuery(i, view));
                        issuedThisFrame++;
                    }
                    break;

                case HistogramState.FirstQueryInFlight:
                case HistogramState.QueryInFlight:
                    if (_curQueryFrame > e.FrameQueued + 2)
                    {
                        int np = readback.PixelsRendered(i);
                        if (np != -1)
                        {
                            e.PixelsInRange = np;
                            e.State = HistogramState.QueryDone;
                        }
                    }
                    break;
            }
        }

        while (issuedThisFrame < MaxQueriesPerFrame)
        {
            int oldest = -1;
            for (int i = 0; i < ranges; i++)
            {
                if (_buckets[i].State == HistogramState.QueryDone &&
                    (oldest == -1 || _buckets[i].FrameQueued < _buckets[oldest].FrameQueued))
                    oldest = i;
            }
            if (oldest == -1)
                break;
            issued.Add(IssueQuery(oldest, view));
            issuedThisFrame++;
        }
        return algorithm;
    }

    /// <summary>
    /// The buckets the next <see cref="Update"/> will read back (a bit each):
    /// those in flight that will be two frames old by then. Native reads
    /// exactly these at the next post crossing, as stock's Update would.
    /// </summary>
    public int DueNextFrame(int algorithm)
    {
        int ranges = algorithm == 1 ? RangesNew : Ranges;
        int mask = 0;
        for (int i = 0; i < ranges; i++)
        {
            HistogramState state = _buckets[i].State;
            if ((state == HistogramState.FirstQueryInFlight || state == HistogramState.QueryInFlight) &&
                _curQueryFrame + 1 > _buckets[i].FrameQueued + 2)
                mask |= 1 << i;
        }
        return mask;
    }

    // CHistogram_entry_t::IssueQuery's decisions: the compare range and the
    // rectangle, the bucket's area shrunk to the exposure centre region.
    private LuminanceQuery IssueQuery(int bucket, in HistogramView v)
    {
        ref HistogramBucket e = ref _buckets[bucket];
        float testMin = e.MinLum;
        float testMax = e.MaxLum == 1.0f ? 10000.0f : e.MaxLum;

        int xMin = (int)FLerp(v.X, v.X + v.Width - 1, 0, 1, e.MinX);
        int xMax = (int)FLerp(v.X, v.X + v.Width - 1, 0, 1, e.MaxX);
        int yMin = (int)FLerp(v.Y, v.Y + v.Height - 1, 0, 1, e.MinY);
        int yMax = (int)FLerp(v.Y, v.Y + v.Height - 1, 0, 1, e.MaxY);

        float widthScale, heightScale;
        if (v.FlashlightOn)
        {
            widthScale = 0.5f * (1.0f - v.CenterRegionXFlashlight);
            heightScale = 0.5f * (1.0f - v.CenterRegionYFlashlight);
        }
        else
        {
            widthScale = 0.5f * (1.0f - v.CenterRegionX);
            heightScale = 0.5f * (1.0f - v.CenterRegionY);
        }
        int skipX = (int)((1 + xMax - xMin) * widthScale);
        int skipY = (int)((1 + yMax - yMin) * heightScale);

        e.Pixels = (1 + xMax - xMin) * (1 + yMax - yMin);

        xMin += skipX;
        yMin += skipY;
        xMax -= skipX;
        yMax -= skipY;

        e.State = e.State == HistogramState.Initial ? HistogramState.FirstQueryInFlight : HistogramState.QueryInFlight;
        e.FrameQueued = _curQueryFrame;
        return new LuminanceQuery(bucket, testMin, testMax, v.ToneScale, xMin, yMin, xMax, yMax, v.Width, v.Height);
    }

    /// <summary>
    /// FindLocationOfPercentBrightPixels: where, as a luminance from 0 to 1, the
    /// brightest <paramref name="percentBrightPixels"/>% of the pixels begin;
    /// -1 when the histogram is not full yet. With a target, a border in the
    /// target's own bucket snaps to the target (a sticky bin).
    /// </summary>
    public float FindLocationOfPercentBrightPixels(int algorithm, float percentBrightPixels, float percentTargetToSnapToIfInSameBin = -1.0f)
    {
        if (algorithm != 1)
            return -1.0f;

        int totalValidPixels = 0;
        for (int i = 0; i < RangesNew - 1; i++)
        {
            if (_buckets[i].ContainsValidData)
                totalValidPixels += _buckets[i].PixelsInRange;
        }
        if (totalValidPixels == 0)
            return -1.0f;

        float totalPercentRangeTested = 0.0f;
        float totalPercentPixelsTested = 0.0f;
        for (int i = RangesNew - 2; i >= 0; i--)
        {
            ref HistogramBucket e = ref _buckets[i];
            if (!e.ContainsValidData)
                return -1.0f;

            float pixelPercentNeeded = percentBrightPixels / 100.0f - totalPercentPixelsTested;
            float thisBinPercentOfTotalPixels = (float)e.PixelsInRange / (float)totalValidPixels;
            float thisBinLuminanceRange = e.MaxLum - e.MinLum;
            if (thisBinPercentOfTotalPixels >= pixelPercentNeeded)
            {
                if (percentTargetToSnapToIfInSameBin >= 0.0f &&
                    e.MinLum <= percentTargetToSnapToIfInSameBin / 100.0f && e.MaxLum >= percentTargetToSnapToIfInSameBin / 100.0f)
                    return percentTargetToSnapToIfInSameBin / 100.0f;

                float percentOfThesePixelsNeeded = pixelPercentNeeded / thisBinPercentOfTotalPixels;
                float percentLocationOfBorder = 1.0f - (totalPercentRangeTested + thisBinLuminanceRange * percentOfThesePixelsNeeded);
                return Max(e.MinLum, Min(e.MaxLum, percentLocationOfBorder));
            }

            totalPercentPixelsTested += thisBinPercentOfTotalPixels;
            totalPercentRangeTested += thisBinLuminanceRange;
        }
        return -1.0f;
    }

    /// <summary>
    /// GetTargetTonemapScalar: the scale that would put the bright pixels at
    /// the target, relative to <paramref name="lastScale"/> (the engine's
    /// current linear scale), or the original algorithm's average-luminance
    /// target. <paramref name="idealForDebug"/> does not snap to the target's bin.
    /// </summary>
    public float TargetTonemapScalar(int algorithm, float percentBrightPixels, float percentTarget, float minAvgLum, float lastScale, bool idealForDebug = false)
    {
        if (algorithm == 1)
        {
            float location = idealForDebug
                ? FindLocationOfPercentBrightPixels(algorithm, percentBrightPixels)
                : FindLocationOfPercentBrightPixels(algorithm, percentBrightPixels, percentTarget);
            if (location < 0.0f)
                location = percentTarget / 100.0f;
            location = Max(0.0001f, location);

            float target = percentTarget / 100.0f / location;

            float averageLocation = FindLocationOfPercentBrightPixels(algorithm, 50.0f);
            if (averageLocation > 0.0f)
            {
                float target2 = minAvgLum / 100.0f / averageLocation;
                if (target2 > target)
                    target = target2;
            }

            target *= lastScale;
            return Max(0.001f, target);
        }

        float averageLuminance;
        float total = 0;
        int totalPixels = 0;
        float scaleValue = 1.0f;
        ref HistogramBucket last = ref _buckets[Ranges - 1];
        if (last.ContainsValidData)
            scaleValue = last.Pixels * (1.0f / last.PixelsInRange);
        if (!float.IsFinite(scaleValue))
            scaleValue = 1.0f;

        for (int i = 0; i < Ranges - 1; i++)
        {
            ref HistogramBucket e = ref _buckets[i];
            if (e.ContainsValidData)
            {
                total += scaleValue * e.PixelsInRange * ((e.MinLum + e.MaxLum) / 2);
                totalPixels += e.Pixels;
            }
        }
        if (totalPixels > 0)
            averageLuminance = (float)(total * (1.0 / totalPixels));
        else
            averageLuminance = 0.5f;

        averageLuminance = Max(0.0001f, averageLuminance);
        return (float)(0.005 / averageLuminance);
    }

    private static float FLerp(float f1, float f2, float i1, float i2, float x) => f1 + (f2 - f1) * (x - i1) / (i2 - i1);

    private static float Max(float a, float b) => a > b ? a : b;

    private static float Min(float a, float b) => a < b ? a : b;
}

/// <summary>What SetToneMapScale asks of the render context this frame.</summary>
public enum ToneMapActionKind
{
    /// <summary>A non-finite scale: nothing.</summary>
    None,

    /// <summary>ResetToneMappingScale(Value), and mat_hdr_tonemapscale = Value (a forced scale).</summary>
    Reset,

    /// <summary>
    /// SetGoalToneMappingScale(Value), then, with <see cref="ToneMapAction.Averaged"/>,
    /// SetGoalToneMappingScale(Average); mat_hdr_tonemapscale is the last one set.
    /// </summary>
    Goal,
}

/// <summary>SetToneMapScale's calls on the render context.</summary>
public readonly record struct ToneMapAction(ToneMapActionKind Kind, float Value, bool Averaged, float Average);

/// <summary>
/// SetToneMapScale and ResetToneMapping (viewpostprocess.cpp): the goal scale
/// is a weighted average of the last ten frames' targets once there are ten,
/// the newest and oldest weighing most, clamped to the exposure range.
/// </summary>
public sealed class ToneMapScale
{
    private const int Samples = 10;

    private readonly float[] _average = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];
    private int _inAverage;

    /// <summary>ResetToneMapping's half: the average starts over (the caller resets the engine's scale).</summary>
    public void Reset() => _inAverage = 0;

    /// <summary>
    /// One frame's new target. <paramref name="forcedScale"/> is
    /// mat_force_tonemap_scale; mat_fullbright 1 forces 1.
    /// </summary>
    public ToneMapAction Set(float newValue, float minValue, float maxValue, float forcedScale, int fullbright)
    {
        if (!float.IsFinite(newValue))
            return new ToneMapAction(ToneMapActionKind.None, 0f, false, 0f);

        if (fullbright == 1)
            forcedScale = 1.0f;
        if (forcedScale > 0.0f)
            return new ToneMapAction(ToneMapActionKind.Reset, forcedScale, false, 0f);

        if (_inAverage < Samples)
        {
            _average[_inAverage++] = newValue;
        }
        else
        {
            for (int i = 0; i < Samples - 1; i++)
                _average[i] = _average[i + 1];
            _average[Samples - 1] = newValue;
        }

        if (_inAverage != Samples)
            return new ToneMapAction(ToneMapActionKind.Goal, newValue, false, 0f);

        float avg = 0.0f;
        float sumWeights = 0;
        const int samplePt = Samples / 2;
        for (int i = 0; i < Samples; i++)
        {
            float weight = (float)(Math.Abs(i - samplePt) * (1.0 / (Samples / 2)));
            sumWeights += weight;
            avg += weight * _average[i];
        }
        avg = (float)(avg * (1.0 / sumWeights));
        avg = maxValue < (minValue > avg ? minValue : avg) ? maxValue : (minValue > avg ? minValue : avg);
        return new ToneMapAction(ToneMapActionKind.Goal, newValue, true, avg);
    }
}

/// <summary>
/// GetBloomAmount's inputs: the hardware, the map, and the bloom cvars
/// (mat_hdr_level, mat_force_bloom, mat_disable_bloom, building_cubemaps,
/// mat_fullbright, mat_bloomamount_rate, mat_bloomscale or the map's custom
/// scale, mat_non_hdr_bloom_scalefactor, mat_bloom_scalefactor_scalar).
/// </summary>
public readonly record struct BloomInputs(
    int DxSupportLevel, int HdrType, int HdrLevel, bool MapHasHdrLighting,
    int ForceBloom, int DisableBloom, bool BuildingCubemaps, int Fullbright, bool SrgbBlocksBloom,
    float Rate, float BloomScale, bool UseCustomBloomScale, float CustomBloomScale,
    float NonHdrScaleFactor, float ScaleFactorScalar);

/// <summary>
/// GetBloomAmount and GetCurrentBloomScale (viewpostprocess.cpp): the bloom
/// amount eases towards the scale at mat_bloomamount_rate per frame while
/// bloom is on.
/// </summary>
public sealed class BloomAmount
{
    private float _current = 1.0f;

    /// <summary>HDR_TYPE_NONE.</summary>
    public const int HdrTypeNone = 0;

    public float Next(in BloomInputs b)
    {
        if (b.DxSupportLevel < 80)
            return 0.0f;

        bool enabled = b.HdrLevel >= 1;
        if (!b.MapHasHdrLighting)
            enabled = false;
        if (b.ForceBloom != 0)
            enabled = true;
        if (b.DisableBloom != 0)
            enabled = false;
        if (b.BuildingCubemaps)
            enabled = false;
        if (b.Fullbright == 1)
            enabled = false;
        if (b.SrgbBlocksBloom)
            enabled = false;

        float amount = 0.0f;
        if (enabled)
        {
            float scale = b.UseCustomBloomScale ? b.CustomBloomScale : b.BloomScale;
            _current = scale * b.Rate + (1.0f - b.Rate) * _current;
            amount = _current;
        }

        if (b.HdrType == HdrTypeNone)
            amount *= b.NonHdrScaleFactor;
        amount *= b.ScaleFactorScalar;
        return amount;
    }
}

/// <summary>GetExposureRange (viewpostprocess.cpp): the map's range overrides the cvars'.</summary>
public static class ExposureRange
{
    public static (float Min, float Max) Of(bool useCustomMin, float customMin, bool useCustomMax, float customMax,
        float cvarMin, float cvarMax, int uncapExposure)
    {
        float min = useCustomMin && customMin > 0.0f ? customMin : cvarMin;
        float max = useCustomMax && customMax > 0.0f ? customMax : cvarMax;
        if (uncapExposure != 0)
        {
            max = 20.0f;
            min = 0.0f;
        }
        if (min > max)
            max = min;
        return (min, max);
    }

    /// <summary>DoPreBloomTonemapping's clamp of the target into the range, never below 0.001.</summary>
    public static float Clamp(float target, float min, float max)
    {
        float clamped = min > (max < target ? max : target) ? min : (max < target ? max : target);
        return 0.001f > clamped ? 0.001f : clamped;
    }
}
