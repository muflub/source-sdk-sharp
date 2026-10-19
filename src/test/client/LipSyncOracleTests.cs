using System.Globalization;

using SourceSharp.Client.LipSync;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed viseme pass (<see cref="Visemes"/>, <see cref="Sentence"/>)
/// against stock's C_BaseFlex::ProcessVisemes and CSentence::GetIntensity, run
/// offline from the pristine c_baseflex.cpp and sentence.cpp by
/// src/test/oracle/native/lipsync_oracle_test.cpp
/// (<c>Fixtures/lipsync-stock.tsv</c>, README beside it). Every frame's global
/// flex weights and every intensity are compared by their bits, and the
/// registry's size after each frame with stock's.
/// </summary>
public class LipSyncOracleTests
{
    private const int Globals = 12;

    private static float Bits(string hex) => BitConverter.Int32BitsToSingle(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string Hex(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    // Compared by bits, and every message is built only on a failure: formatting
    // thousands of row numbers would fill the runtime's small-number string
    // cache, which UiAllocationTests' negative control relies on being cold.
    private static bool Same(string hex, float f) => BitConverter.SingleToInt32Bits(Bits(hex)) == BitConverter.SingleToInt32Bits(f);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private sealed class Files : IPhonemeFileSource
    {
        public readonly Dictionary<string, FlexSettingFile> ByName = [];

        public FlexSettingFile? Find(string name) => ByName.GetValueOrDefault(name);
    }

    private sealed class Voice
    {
        public bool Ignore, HasSentence;
        public float Length;
        public readonly List<RuntimePhoneme> Phonemes = [];
        public readonly List<EmphasisSample> Samples = [];
        public Sentence? Sentence;
    }

    private sealed class Totals
    {
        public int Cases, Frames, Moved, Intensities;
    }

    private static Totals Replay()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "lipsync-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock viseme pass is missing: " + path);

        var totals = new Totals();
        Files files = new();
        FlexControllerRegistry registry = new();
        Visemes? visemes = null;
        VisemeModel model = default;
        VisemeSettings settings = default;
        List<Voice> voices = [];
        VoiceSource[] frame = [];
        float[] weights = new float[1024];
        int line = 0;

        void Start()
        {
            foreach (Voice v in voices)
                v.Sentence ??= new Sentence(v.Phonemes, v.Samples);
            visemes ??= new Visemes(files, registry);
        }

        foreach (string text in File.ReadLines(path))
        {
            line++;
            if (text.Length == 0 || text[0] == '#')
                continue;
            string[] f = text.Split('\t');
            switch (f[0])
            {
                case "C":
                    totals.Cases++;
                    files = new Files();
                    registry = new FlexControllerRegistry();
                    visemes = null;
                    voices = [];
                    model = new VisemeModel(f[2] == "1", I(f[3]), I(f[4]));
                    settings = new VisemeSettings(Bits(f[6]), Bits(f[7]), I(f[5]));
                    break;
                case "F":
                    files.ByName[f[1]] = new FlexSettingFile(Convert.FromHexString(f[2]));
                    break;
                case "V":
                    voices.Add(new Voice { Ignore = f[2] == "1", HasSentence = f[3] == "1", Length = Bits(f[4]) });
                    break;
                case "P":
                    voices[^1].Phonemes.Add(new RuntimePhoneme(I(f[1]), Bits(f[2]), Bits(f[3])));
                    break;
                case "E":
                    voices[^1].Samples.Add(new EmphasisSample(Bits(f[1]), Bits(f[2])));
                    break;
                case "T":
                    Start();
                    frame = new VoiceSource[voices.Count];
                    for (int v = 0; v < voices.Count; v++)
                    {
                        Voice vo = voices[v];
                        frame[v] = new VoiceSource(vo.Ignore, vo.HasSentence ? vo.Sentence : null, vo.Length, Bits(f[2 + v]));
                    }
                    break;
                case "I":
                {
                    VoiceSource vs = frame[I(f[1])];
                    float got = vs.Sentence!.Intensity(vs.Elapsed - settings.PhonemeDelay, vs.SentenceLength);
                    if (!Same(f[2], got))
                        Assert.Fail($"line {line}: GetIntensity is {f[2]} in stock, {Hex(got)} here");
                    totals.Intensities++;
                    break;
                }
                case "B":
                    Array.Clear(weights);
                    for (int g = 0; g < Globals; g++)
                        weights[g] = Bits(f[1 + g]);
                    break;
                case "W":
                {
                    float[] before = weights[..Globals];
                    visemes!.Process(frame, model, settings, weights);
                    if (registry.Count != I(f[1]))
                        Assert.Fail($"line {line}: stock had {f[1]} global flex controllers, here {registry.Count}");
                    bool moved = false;
                    for (int g = 0; g < Globals; g++)
                    {
                        if (!Same(f[2 + g], weights[g]))
                            Assert.Fail($"line {line}: global flex weight {g} is {f[2 + g]} in stock, {Hex(weights[g])} here");
                        moved |= BitConverter.SingleToInt32Bits(weights[g]) != BitConverter.SingleToInt32Bits(before[g]);
                    }
                    for (int g = Globals; g < weights.Length; g++)
                        if (weights[g] != 0.0f)
                            Assert.Fail($"line {line}: global flex weight {g} moved past the recorded span");
                    totals.Frames++;
                    totals.Moved += moved ? 1 : 0;
                    break;
                }
                default:
                    Assert.Fail($"line {line}: unknown row '{f[0]}'");
                    break;
            }
        }
        return totals;
    }

    [Fact]
    public void EveryFrameAddsTheWeightsStockAdded()
    {
        Totals totals = Replay();
        Assert.Equal(120, totals.Cases);
        Assert.Equal(120 * 48, totals.Frames);
        // The stimulus reached the subject: hundreds of frames moved a weight.
        Assert.True(totals.Moved > 500, $"only {totals.Moved} frames moved a weight");
        Assert.True(totals.Intensities > 5000, $"only {totals.Intensities} intensities were compared");
    }
}
