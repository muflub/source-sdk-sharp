using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Soundscapes;
using SourceSharp.Shared.Soundscapes;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed client soundscape system against stock's
/// <c>C_SoundscapeSystem</c>, recorded live (<c>Fixtures/soundscape-stock.tsv</c>,
/// README beside it): 1000 frames of forced soundscapes, pushes, a flush and a
/// stop over the mod's scripts and <c>soundscapes_c2probe.txt</c>. Every engine
/// call is compared field by field, floats bit-exact, frame by frame; every
/// draw is answered from the recording and must ask for the same range, in
/// the same order.
/// </summary>
public class SoundscapeOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    // The executors, recorded as rows in the fixture's own shape.
    private sealed class Audio : ISoundscapeAudio, ISoundscapeSounds
    {
        public readonly List<object[]> Rows = new();
        public Vector Origin, Forward, Right;

        public void EmitAmbient(string wave, float volume, int pitch, int flags) => Rows.Add(["A", wave, volume, pitch, flags]);

        public void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin)
            => Rows.Add(["E", wave, volume, soundLevel, pitch, flags, origin.X, origin.Y, origin.Z]);

        public void StopWorld(string wave) => Rows.Add(["S", wave]);

        public void SetRoomType(int roomType) => Rows.Add(["D", "room", roomType]);

        public void SetPlayerDsp(int dspType) => Rows.Add(["D", "player", dspType]);

        public void SetConVar(string name, string value)
            => Rows.Add(["V", name, "set", float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : value]);

        public void RevertConVar(string name) => Rows.Add(["V", name, "revert"]);

        public void MainView(out Vector origin, out Vector forward, out Vector right)
        {
            origin = Origin;
            forward = Forward;
            right = Right;
        }

        public bool MakingResLists => false;

        public void TouchSoundFile(string wave) => Assert.Fail("no reslist touch outside -makereslists: " + wave);
    }

    private static List<string[]> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "soundscape-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock soundscape system is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(rows.Count(r => r[0] == "F") == 1000 && rows.Count(r => r[0] == "E") > 1000
                    && rows.Count(r => r[0] == "A") > 500 && rows.Count(r => r[0] == "RF") > 100,
                    "the oracle parsed");
        return rows;
    }

    internal static SoundscapeLibrary Library()
    {
        string scripts = RepoLayout.Repo("game", "mod_test");
        string probe = RepoLayout.Repo("src", "test", "client", "Fixtures", "soundscapes_c2probe.txt");
        var library = SoundscapeLibrary.Load(path =>
        {
            if (path == "scripts/soundscapes_dm_lockdown.txt")
                return File.ReadAllText(probe);
            string file = Path.Combine(scripts, path);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }, "dm_lockdown");
        Assert.True(library.IndexOf("c2probe.full") >= 0 && library.IndexOf("e3_c17_01_battle") >= 0,
                    "the scripts the recording plays are in the library");
        return library;
    }

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    // One field: an exact string, an exact int, or a float by its bits.
    private static bool Same(string expected, object actual) => actual switch
    {
        float f => BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(F(expected)),
        int n => n == I(expected),
        string s => s == expected,
        _ => false,
    };

    private static string Show(object[] row) => string.Join(" ", row.Select(o => o is float f ? f.ToString("R", CultureInfo.InvariantCulture) : o.ToString()));

    [Fact]
    public void TheManagedSystemReplaysStockBitExact()
    {
        var rows = Stock();
        var engine = TestServices.Fake();
        engine.Returns("Console.FindConVarValue", "0");

        // Draws answer from the recording, in order, and must ask what stock asked.
        var draws = new Queue<string[]>(rows.Where(r => r[0] is "RF" or "RI"));
        int frame = 0;
        object? Draw(string tag)
        {
            Assert.True(draws.Count > 0, $"frame {frame}: a draw stock did not make");
            string[] d = draws.Dequeue();
            var call = engine.Calls[^1];
            Assert.True(d[0] == tag, $"frame {frame}: drew {tag}({call.Args[0]}, {call.Args[1]}), stock drew {string.Join(" ", d)}");
            Assert.True(Same(d[1], call.Args[0]!) && Same(d[2], call.Args[1]!),
                        $"frame {frame}: {tag}({call.Args[0]}, {call.Args[1]}), stock {string.Join(" ", d)}");
            return tag == "RF" ? F(d[3]) : (object)I(d[3]);
        }
        engine.ReturnsEachCall("Host.RandomFloat", () => Draw("RF"));
        engine.ReturnsEachCall("Host.RandomInt", () => Draw("RI"));

        var clock = new Clock();
        var audio = new Audio();
        var library = Library();
        var system = new ClientSoundscapes(audio, audio, clock, engine, engine);
        system.LevelInit(library);

        // Group the rows by frame: an F row opens one, a Q row closes it.
        int i = 0;
        int compared = 0;
        string? name = null;
        bool afterReset = false;
        while (i < rows.Count)
        {
            string[] f = rows[i++];
            Assert.True(f[0] == "F", "a frame opens with its F row: " + string.Join(" ", f));
            frame = I(f[1]);
            clock.Now = F(f[2]);
            audio.Origin = V(f, 4);
            audio.Forward = V(f, 7);
            audio.Right = V(f, 10);
            audio.Rows.Clear();

            var expected = new List<string[]>();
            while (rows[i][0] != "Q")
            {
                string[] r = rows[i++];
                switch (r[0])
                {
                    case "X":
                        afterReset |= r[1] == "push";
                        Act(system, library, r, ref name);
                        break;
                    case "RF":
                    case "RI":
                        break;
                    default:
                        // The rows before the first push stop what the
                        // server's soundscape had running; the replay starts
                        // empty, after them.
                        if (afterReset)
                            expected.Add(r);
                        break;
                }
            }

            system.Update(F(f[3]), F(f[13]));

            Assert.True(audio.Rows.Count == expected.Count,
                        $"frame {frame}: {audio.Rows.Count} engine call(s), stock made {expected.Count}:\n  managed: "
                        + string.Join("\n  managed: ", audio.Rows.Select(Show)) + "\n  stock:   "
                        + string.Join("\n  stock:   ", expected.Select(e => string.Join(" ", e))));
            for (int k = 0; k < expected.Count; k++)
            {
                object[] a = audio.Rows[k];
                string[] e = expected[k];
                bool same = a.Length == e.Length && Enumerable.Range(0, e.Length).All(j => j == 0 ? (string)a[0] == e[0] : Same(e[j], a[j]));
                Assert.True(same, $"frame {frame} call {k}: managed {Show(a)}, stock {string.Join(" ", e)}");
                compared++;
            }

            string[] q = rows[i++];
            if (afterReset)
            {
                // current soundscape compared by name, the live index counts the VPK scripts
                int current = I(q[1]);
                Assert.True((current < 0) == (system.CurrentSoundscape < 0)
                            && I(q[2]) == system.LoopingCount && I(q[3]) == system.RandomCount && I(q[4]) == system.LoopingSoundId,
                            $"frame {frame}: managed current {system.CurrentSoundscape} looping {system.LoopingCount} random {system.RandomCount} id {system.LoopingSoundId}, stock {string.Join(" ", q)}");
            }
        }

        Assert.True(draws.Count == 0, $"{draws.Count} draw(s) stock made and the managed system did not");
        Assert.True(compared > 1700, $"the replay compared {compared} engine calls");
    }

    private static void Act(ClientSoundscapes system, SoundscapeLibrary library, string[] r, ref string? name)
    {
        switch (r[1])
        {
            case "reset":
                break;
            case "name":
                name = r[2];
                break;
            case "push":
                int index = name is not null ? library.IndexOf(name) : I(r[2]);
                name = null;
                var positions = new Vector[ClientSoundscapes.LocalSoundCount];
                positions[0] = V(r, 5);
                system.Push(index, I(r[3]), I(r[4]), positions);
                break;
            case "force":
                system.Force(r[2], F(r[3]));
                break;
            case "flush":
                system.Flush(library);
                break;
            case "stop":
                system.Stop();
                break;
            case "dropped":
                Assert.Fail("the recording dropped a server push: " + string.Join(" ", r));
                break;
            default:
                Assert.Fail("an unknown action " + r[1]);
                break;
        }
    }
}
