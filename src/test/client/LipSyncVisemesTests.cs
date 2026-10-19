using System.Buffers.Binary;
using System.Text;

using SourceSharp.Client.LipSync;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The viseme pass's behaviours one at a time, from stock's c_baseflex.cpp and
/// sentence.cpp (<see cref="LipSyncOracleTests"/> replays the recorded stock
/// run as a whole).
/// </summary>
public class LipSyncVisemesTests
{
    // A .vfe image in studio.h's layout: settings[phoneme] = weights (key, weight).
    private static FlexSettingFile Vfe(string[] keys, Dictionary<int, (int Key, float Weight)[]> settings, int indexes = 8)
    {
        var order = settings.Keys.OrderBy(k => k).ToList();
        int settingsAt = 108;
        int indexAt = settingsAt + order.Count * 24;
        int at = indexAt + indexes * 4;
        var weightsAt = new List<int>();
        foreach (int p in order)
        {
            weightsAt.Add(at);
            at += settings[p].Length * 12;
        }
        int keysAt = at;
        at += keys.Length * 4;
        var namesAt = new List<int>();
        foreach (string k in keys)
        {
            namesAt.Add(at);
            at += k.Length + 1;
        }
        byte[] img = new byte[at];
        void Put(int o, int v) => BinaryPrimitives.WriteInt32LittleEndian(img.AsSpan(o), v);
        Put(0, ('V' << 16) + ('F' << 8) + 'E');
        Put(76, order.Count);
        Put(80, settingsAt);
        Put(88, indexes);
        Put(92, indexAt);
        Put(96, keys.Length);
        Put(100, keysAt);
        for (int p = 0; p < indexes; p++)
            Put(indexAt + p * 4, order.IndexOf(p));
        for (int i = 0; i < order.Count; i++)
        {
            int s = settingsAt + i * 24;
            Put(s + 8, settings[order[i]].Length);
            Put(s + 20, weightsAt[i] - s);
            for (int w = 0; w < settings[order[i]].Length; w++)
            {
                Put(weightsAt[i] + w * 12, settings[order[i]][w].Key);
                Put(weightsAt[i] + w * 12 + 4, BitConverter.SingleToInt32Bits(settings[order[i]][w].Weight));
            }
        }
        for (int k = 0; k < keys.Length; k++)
        {
            Put(keysAt + k * 4, namesAt[k]);
            Encoding.ASCII.GetBytes(keys[k]).CopyTo(img, namesAt[k]);
        }
        return new FlexSettingFile(img);
    }

    private sealed class Files : IPhonemeFileSource
    {
        public readonly Dictionary<string, FlexSettingFile> ByName = [];
        public int Lookups;

        public FlexSettingFile? Find(string name)
        {
            Lookups++;
            return ByName.GetValueOrDefault(name);
        }
    }

    // Phoneme 3 on key "jaw" with weight 1 in each present class.
    private static Files Classes(bool normal = true, bool weak = false, bool strong = false)
    {
        var files = new Files();
        var one = new Dictionary<int, (int, float)[]> { [3] = [(0, 1.0f)] };
        if (normal)
            files.ByName["phonemes"] = Vfe(["jaw"], one);
        if (weak)
            files.ByName["phonemes_weak"] = Vfe(["jaw"], one);
        if (strong)
            files.ByName["phonemes_strong"] = Vfe(["jaw"], one);
        return files;
    }

    private static readonly VisemeModel Lod0 = new(true, 0, 0x400);

    // One phoneme 3 from 0 to 1 s, flat emphasis at the given value (samples
    // every 0.25 s, so the spline's neighbours are flat too: past the track's
    // ends it bends toward the neutral 0.5).
    private static VoiceSource Speaking(float elapsed, float emphasis = 0.5f, int code = 3)
        => new(false, new Sentence([new RuntimePhoneme(code, 0.0f, 1.0f)],
            [new(0.0f, emphasis), new(0.25f, emphasis), new(0.5f, emphasis), new(0.75f, emphasis), new(1.0f, emphasis)]), 1.0f, elapsed);

    private static float[] Run(Files files, VoiceSource voice, VisemeModel? model = null, VisemeSettings? settings = null)
    {
        var weights = new float[16];
        new Visemes(files, new FlexControllerRegistry()).Process([voice], model ?? Lod0, settings ?? VisemeSettings.Stock, weights);
        return weights;
    }

    [Fact]
    public void TheStockConVarDefaultsAreZeroPointZeroEightAndTwo()
    {
        Assert.Equal(new VisemeSettings(0.0f, 0.08f, 2), VisemeSettings.Stock);
    }

    [Fact]
    public void ASentenceWithoutSamplesIsNeutral()
    {
        Assert.Equal(0.5f, new Sentence([], []).Intensity(0.3f, 1.0f));
    }

    [Fact]
    public void IntensityIsHeldToZeroToOne()
    {
        var s = new Sentence([], [new EmphasisSample(0.1f, 0.0f), new EmphasisSample(0.2f, 1.0f), new EmphasisSample(0.3f, 0.0f)]);
        for (float t = 0.0f; t < 0.5f; t += 0.01f)
        {
            float v = s.Intensity(t, 0.5f);
            Assert.InRange(v, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void IntensityPassesThroughASampleItSitsOn()
    {
        var s = new Sentence([], [new EmphasisSample(0.25f, 0.75f), new EmphasisSample(0.5f, 0.25f)]);
        Assert.Equal(0.75f, s.Intensity(0.25f, 1.0f), 5);
    }

    [Fact]
    public void ANeutralPhonemeUnderTheFilterAddsItsWholeWeight()
    {
        // Emphasis 0.5: the normal class at 2 x 0.5; the phoneme covers the whole filter: scale 1.
        float[] w = Run(Classes(), Speaking(0.5f));
        Assert.Equal(1.0f, w[0], 5);
    }

    [Fact]
    public void ThePartlyCoveredFilterScalesTheWeightByTheOverlap()
    {
        // No crossfade (no vertex LOD): the filter stays 0.08 s; the phoneme ends 0.04 s in.
        float[] w = Run(Classes(), Speaking(0.96f), new VisemeModel(true, 0, 0));
        Assert.Equal(0.5f, w[0], 4);
    }

    [Fact]
    public void AMissingRequiredClassSkipsThePhoneme()
    {
        Assert.Equal(0.0f, Run(Classes(normal: false, weak: true, strong: true), Speaking(0.5f))[0]);
    }

    [Fact]
    public void APhonemeTheFileDoesNotIndexAddsNothing()
    {
        Assert.Equal(0.0f, Run(Classes(), Speaking(0.5f, code: 5))[0]);
    }

    [Fact]
    public void StrongEmphasisCrossfadesIntoTheStrongClass()
    {
        // 0.8: frac = 0.2 / 0.4 = 0.5; normal 0.5 x 2 x 0.6 = 0.6, strong 0.5: 1.1 of the shared key.
        Assert.Equal(1.1f, Run(Classes(strong: true), Speaking(0.5f, emphasis: 0.8f))[0], 5);
    }

    [Fact]
    public void StrongEmphasisWithoutAStrongClassStopsAtTwiceTheThreshold()
    {
        Assert.Equal(1.2f, Run(Classes(), Speaking(0.5f, emphasis: 0.9f))[0], 5);
    }

    [Fact]
    public void WeakEmphasisCrossfadesIntoTheWeakClass()
    {
        // 0.2: frac = 0.2 / 0.4 = 0.5; normal 0.5 x 2 x 0.4 = 0.4, weak 0.5: 0.9.
        Assert.Equal(0.9f, Run(Classes(weak: true), Speaking(0.5f, emphasis: 0.2f))[0], 5);
    }

    [Fact]
    public void WeakEmphasisWithoutAWeakClassStopsAtTwiceTheThreshold()
    {
        Assert.Equal(0.8f, Run(Classes(), Speaking(0.5f, emphasis: 0.1f))[0], 5);
    }

    [Fact]
    public void AClassAtZeroAmountIsNotAddedAtAll()
    {
        // Neutral emphasis leaves the valid weak class at amount 0: stock skips
        // it, so its key's weight keeps even the sign of a -0.
        var files = new Files();
        files.ByName["phonemes"] = Vfe(["jaw"], new Dictionary<int, (int, float)[]> { [3] = [(0, 1.0f)] });
        files.ByName["phonemes_weak"] = Vfe(["lip"], new Dictionary<int, (int, float)[]> { [3] = [(0, 1.0f)] });
        var weights = new float[16];
        weights[0] = -0.0f;   // "lip": the weak file is found first, so it is global 0
        new Visemes(files, new FlexControllerRegistry()).Process([Speaking(0.5f)], Lod0, VisemeSettings.Stock, weights);
        Assert.Equal(BitConverter.SingleToInt32Bits(-0.0f), BitConverter.SingleToInt32Bits(weights[0]));
    }

    [Fact]
    public void AVoiceThatIgnoresPhonemesAddsNothing()
    {
        VoiceSource v = Speaking(0.5f) with { IgnorePhonemes = true };
        Assert.Equal(0.0f, Run(Classes(), v)[0]);
    }

    [Fact]
    public void AVoiceWithoutASentenceAddsNothing()
    {
        VoiceSource v = Speaking(0.5f) with { Sentence = null };
        Assert.Equal(0.0f, Run(Classes(), v)[0]);
    }

    [Fact]
    public void AVoiceTwoSecondsPastItsSentenceIsSkipped()
    {
        // A phoneme at 3 s, past length 1 + 2: still under the time, but the voice is done.
        var s = new Sentence([new RuntimePhoneme(3, 2.9f, 3.2f)], []);
        Assert.Equal(0.0f, Run(Classes(), new VoiceSource(false, s, 1.0f, 3.0f))[0]);
        Assert.NotEqual(0.0f, Run(Classes(), new VoiceSource(false, s, 1.0f, 2.95f))[0]);
    }

    [Fact]
    public void NoModelAddsNothing()
    {
        Assert.Equal(0.0f, Run(Classes(), Speaking(0.5f), new VisemeModel(false, 0, 0x400))[0]);
    }

    [Fact]
    public void ThePhonemeDelayShiftsTheTime()
    {
        // At 0.5 s with a 0.52 s delay the time is -0.02: the phoneme covers 0.06 of the 0.08 filter.
        float[] w = Run(Classes(), Speaking(0.5f), new VisemeModel(true, 0, 0), new VisemeSettings(0.52f, 0.08f, 2));
        Assert.Equal(0.75f, w[0], 4);
    }

    // Two touching phonemes, the time 0.1 s before the first one's end: a
    // crossfade widens the filter to min(next end - t, length) = 0.5 s.
    private static float TouchingSecond(VisemeModel model, int snap)
    {
        var files = new Files();
        files.ByName["phonemes"] = Vfe(["a", "b"], new Dictionary<int, (int, float)[]> { [1] = [(0, 1.0f)], [2] = [(1, 1.0f)] });
        var s = new Sentence([new RuntimePhoneme(1, 0.0f, 0.5f), new RuntimePhoneme(2, 0.5f, 1.5f)], []);
        var weights = new float[16];
        var registry = new FlexControllerRegistry();
        new Visemes(files, registry).Process([new VoiceSource(false, s, 1.5f, 0.4f)], model, new VisemeSettings(0.0f, 0.08f, snap), weights);
        return weights[1];
    }

    [Fact]
    public void ACrossfadeReachesIntoTheTouchingNextPhoneme()
    {
        // Filter 0.5 from 0.4: the second covers 0.4 of it.
        Assert.Equal(0.8f, TouchingSecond(Lod0, 2), 4);
    }

    [Fact]
    public void WithoutACrossfadeTheNextPhonemeIsBeyondTheFilter()
    {
        Assert.Equal(0.0f, TouchingSecond(new VisemeModel(true, 0, 0), 4));
    }

    [Fact]
    public void ThePhonemeSnapDecidesTheCrossfadeByVertexLod()
    {
        Assert.Equal(0.0f, TouchingSecond(new VisemeModel(true, 0, 0x800), 1));
        Assert.NotEqual(0.0f, TouchingSecond(new VisemeModel(true, 0, 0x800), 2));
        Assert.Equal(0.0f, TouchingSecond(new VisemeModel(true, 0, 0x2000), 3));
        Assert.NotEqual(0.0f, TouchingSecond(new VisemeModel(true, 0, 0x2000), 4));
    }

    [Fact]
    public void TheModelCanForceTheCrossfade()
    {
        Assert.NotEqual(0.0f, TouchingSecond(new VisemeModel(true, Visemes.ForcePhonemeCrossfade, 0), 0));
    }

    [Fact]
    public void AGapLimitsTheCrossfadeToTheNextStart()
    {
        // Next starts at 0.6 (a 0.1 s gap): the filter widens to 0.2 from 0.4 and stops at its start.
        var files = new Files();
        files.ByName["phonemes"] = Vfe(["a", "b"], new Dictionary<int, (int, float)[]> { [1] = [(0, 1.0f)], [2] = [(1, 1.0f)] });
        var s = new Sentence([new RuntimePhoneme(1, 0.0f, 0.5f), new RuntimePhoneme(2, 0.6f, 1.5f)], []);
        var weights = new float[16];
        new Visemes(files, new FlexControllerRegistry()).Process([new VoiceSource(false, s, 1.5f, 0.4f)], Lod0, VisemeSettings.Stock, weights);
        Assert.Equal(0.0f, weights[1]);
        Assert.Equal(0.5f, weights[0], 4);
    }

    [Fact]
    public void AClassFileIsLookedUpOnceEvenWhenMissing()
    {
        var files = Classes();
        var visemes = new Visemes(files, new FlexControllerRegistry());
        var weights = new float[16];
        visemes.Process([Speaking(0.5f)], Lod0, VisemeSettings.Stock, weights);
        visemes.Process([Speaking(0.6f)], Lod0, VisemeSettings.Stock, weights);
        Assert.Equal(3, files.Lookups);
    }

    [Fact]
    public void TheRegistryMatchesNamesIgnoringAsciiCase()
    {
        var r = new FlexControllerRegistry();
        Assert.Equal(0, r.Add("jaw_drop"));
        Assert.Equal(1, r.Add("lip"));
        Assert.Equal(0, r.Add("JAW_DROP"));
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void AFullRegistryAnswersOnePastTheEndAndAddsNothing()
    {
        var r = new FlexControllerRegistry();
        for (int i = 0; i < FlexControllerRegistry.Capacity; i++)
            r.Add("c" + i.ToString("x4", System.Globalization.CultureInfo.InvariantCulture));   // hex: decimal 0..299 would warm the runtime's number-string cache UiAllocationTests' control needs cold
        Assert.Equal(FlexControllerRegistry.Capacity, r.Add("one_more"));
        Assert.Equal(FlexControllerRegistry.Capacity, r.Count);
    }

    [Fact]
    public void FileKeysRegisterInOrderWhenTheFileIsFirstFound()
    {
        var files = new Files();
        files.ByName["phonemes"] = Vfe(["x", "y", "z"], new Dictionary<int, (int, float)[]> { [3] = [(2, 1.0f)] });
        var registry = new FlexControllerRegistry();
        registry.Add("z");
        var weights = new float[16];
        new Visemes(files, registry).Process([Speaking(0.5f)], Lod0, VisemeSettings.Stock, weights);
        Assert.Equal(["z", "x", "y"], new[] { registry.Name(0), registry.Name(1), registry.Name(2) });
        Assert.Equal(1.0f, weights[0], 5);
    }

    [Fact]
    public void TheFileIndexAnswersMinusOneOutsideItAndForAnEmptyEntry()
    {
        FlexSettingFile f = Vfe(["k"], new Dictionary<int, (int, float)[]> { [2] = [(0, 0.5f)] }, indexes: 4);
        Assert.Equal(0, f.IndexedSetting(2));
        Assert.Equal(-1, f.IndexedSetting(1));
        Assert.Equal(-1, f.IndexedSetting(4));
        Assert.Equal(-1, f.IndexedSetting(-1));
        Assert.Equal([new FlexWeight(0, 0.5f)], f.Weights(0));
        Assert.Equal("k", f.LocalName(0));
    }
}
