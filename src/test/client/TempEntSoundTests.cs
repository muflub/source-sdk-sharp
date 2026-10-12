using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The legacy tempents' bounce sounds against stock's, recorded:
/// <c>Fixtures/tempents-sound-stock.tsv</c> (README-tempents-stock.md) holds
/// every CTempEnts::PlaySound of a session (live gibs and shell casings, and a
/// battery over every hit sound, nine vertical speeds and three damps) with
/// the random draws it made, the script parameters the engine answered and
/// what it played. The managed bounce, asked the same, must draw the same,
/// ask for the same script and play the same, bit for bit.
/// </summary>
public class TempEntSoundTests
{
    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tempents-sound-stock.tsv");
        Assert.True(File.Exists(path), "the recorded bounces are missing: " + path);
        return File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
    }

    private static string At(string[] row, string key, int i = 0)
    {
        for (int k = 1; k < row.Length; k++)
        {
            if (row[k].StartsWith(key + "=", StringComparison.Ordinal))
                return i == 0 ? row[k][(key.Length + 1)..] : row[k + i];
        }
        throw new KeyNotFoundException($"{key} in '{string.Join(' ', row)}'");
    }

    private static float F(string[] r, string k, int i = 0) => float.Parse(At(r, k, i), CultureInfo.InvariantCulture);
    private static int I(string[] r, string k) => int.Parse(At(r, k), CultureInfo.InvariantCulture);
    private static Vector V(string[] r, string k) => new(F(r, k), F(r, k, 1), F(r, k, 2));

    // The engine's sound scripts, answering as the recording did, and what was played.
    private sealed class RecordedSounds : IFxGameSounds
    {
        public string[]? Params;
        public string[]? Emit;
        public readonly List<string> Wrong = [];
        public int Asked, Emitted;

        public bool Parameters(string script, out GameSoundParams p)
        {
            Asked++;
            p = default;
            if (Params is null)
            {
                Wrong.Add($"asked for {script}, stock asked for nothing");
                return false;
            }
            if (At(Params, "name") != script)
                Wrong.Add($"asked for {script}, stock for {At(Params, "name")}");
            p = new GameSoundParams(Emit is null ? "" : At(Emit, "sound"), F(Params, "vol"), I(Params, "pitch"), I(Params, "lo"),
                                    I(Params, "hi"), I(Params, "level"), I(Params, "chan"));
            return true;
        }

        public void EmitAtPosition(in GameSoundParams p, float volume, int pitch, in Vector origin)
        {
            Emitted++;
            if (Emit is null)
            {
                Wrong.Add("played a bounce stock did not");
                return;
            }
            if (!volume.Equals(F(Emit, "vol")) || pitch != I(Emit, "pitch") || p.SoundLevel != I(Emit, "level")
                || p.Channel != I(Emit, "chan") || !origin.Equals(V(Emit, "o")) || p.Wave != At(Emit, "sound"))
                Wrong.Add($"played {p.Wave} vol {volume:G9} pitch {pitch} at {origin}, stock {string.Join(' ', Emit[1..])}");
        }
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<string[]> rows = Recorded();

        Assert.True(rows.Count(r => r[0] == "tps-in") == 960, "960 bounces");
        Assert.True(rows.Count(r => r[0] == "tps-emit") == 53, "53 played (the positive arm)");
        Assert.True(rows.Any(r => r[0] == "tps-params" && At(r, "name") == "Bounce.Shell"), "a shell casing's script was asked");
    }

    [Fact]
    public void EveryRecordedBounceSoundsAsStocksDid()
    {
        List<string[]> rows = Recorded();
        var wrong = new List<string>();
        int n = 0, played = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "tps-in")
                continue;
            n++;
            string[] r = rows[i];
            var rig = new FxTestRig();
            var sounds = new RecordedSounds();
            int j = i + 1;
            for (; j < rows.Count && rows[j][0] != "tps-in"; j++)
            {
                switch (rows[j][0])
                {
                    case "fxr": rig.Enqueue(rows[j]); break;
                    case "tps-params": sounds.Params = rows[j]; break;
                    case "tps-emit": sounds.Emit = rows[j]; break;
                }
            }

            new TempEntSounds(rig, sounds).PlaySound(I(r, "hit"), F(r, "damp"), V(r, "vel"), V(r, "o"));
            played += sounds.Emitted;

            if (sounds.Params is not null && sounds.Asked == 0)
                sounds.Wrong.Add("stock asked for the script's parameters and managed did not");
            if (sounds.Emit is not null && sounds.Emitted == 0)
                sounds.Wrong.Add("stock played a bounce and managed did not");
            if (rig.Pending != 0)
                sounds.Wrong.Add($"{rig.Pending} draws stock made and managed did not");
            foreach (string w in rig.Wrong.Concat(sounds.Wrong))
                wrong.Add($"bounce {n} (hit {I(r, "hit")}): {w}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} disagreements over {n} bounces: " + string.Join(" | ", wrong.Take(6)));
        Assert.True(played == 53, $"the replay played every recorded bounce, played {played}");
    }

    [Fact]
    public void ShrapnelAndConcreteNameTheirScripts()
    {
        Assert.Equal("Bounce.Shrapnel", TempEntSounds.ScriptFor(TempEntSounds.BounceShrap, out bool s1));
        Assert.Equal("Bounce.Concrete", TempEntSounds.ScriptFor(TempEntSounds.BounceConcrete, out bool s2));
        Assert.False(s1 || s2, "neither is a shell casing");
    }
}
