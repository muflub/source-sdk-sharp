using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The shared sound envelope controller against stock's CLIENT
/// <c>CSoundControllerImp</c> (game/shared/soundenvelope.cpp), recorded live
/// (<c>Fixtures/soundenvelope-stock.tsv</c>, README beside it): one patch on
/// the local player through a fixed script (two ramps and a queued pitch
/// command at the start, a volume ramp, a three-point pitch envelope, another
/// queued command, a second Play and a fade-out), its volume and pitch read
/// back over 200 frames. Bit-exact; the controller is the one both sides use
/// (<see cref="SoundEnvelopeController"/>).
/// </summary>
public class SoundEnvelopeOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private static List<string[]> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "soundenvelope-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock envelope controller is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(rows.Count(r => r[0] == "W") >= 150 && rows.Count(r => r[0] == "C") >= 8, "the oracle parsed");
        return rows;
    }

    private static void Apply(SoundEnvelopeController c, string[] r)
    {
        switch (r[2])
        {
            case "create": c.Create(1); break;
            case "play": c.Play(1, F(r[3]), F(r[4]), 0f); break;
            case "volume": c.ChangeVolume(1, F(r[3]), F(r[4])); break;
            case "pitch": c.ChangePitch(1, F(r[3]), F(r[4])); break;
            case "command": c.CommandAdd(1, F(r[3]), int.Parse(r[4], CultureInfo.InvariantCulture), F(r[5]), F(r[6])); break;
            case "fadeout": c.FadeOut(1, F(r[3]), false); break;
            case "envelope":
                float total = c.PlayEnvelope(1, SoundEnvelopeController.CommandChangePitch,
                                             [140, 140, 0.2f, 0.2f, 60, 60, 0.3f, 0.3f, 110, 110, 0.15f, 0.15f], 3);
                Assert.True(Bits(total) == Bits(F(r[3])), $"the envelope's length {total:R} vs stock {r[3]}");
                break;
            default: Assert.Fail("an unknown command " + r[2]); break;
        }
    }

    private static SoundEnvelopeController New(Clock clock)
    {
        var engine = TestServices.Fake();
        engine.Returns("Sound.SoundPatchState.entIndex", 1);
        engine.Returns("Sound.SoundPatchState.filterActive", true);
        engine.Ignores("Sound.SoundPatchEmit", "Sound.SoundPatchStop");
        // Every envelope point's min equals its max, so stock's draw answers the
        // point whatever the stream: amplitude, then duration, per point.
        var draws = new Queue<float>([140f, 0.2f, 60f, 0.3f, 110f, 0.15f]);
        engine.ReturnsEachCall("Host.RandomFloat", () => draws.Dequeue());
        return new SoundEnvelopeController(engine, clock, engine, engine);
    }

    [Fact]
    public void TwoHundredFramesReplayBitForBit()
    {
        var rows = Stock();
        var clock = new Clock();
        var c = New(clock);
        int frames = 0;

        // The client's controller updates in its game system's Update, which
        // runs before the frame's clock advances: the update in frame k sees
        // frame k-1's curtime. The script started right after one at its own
        // time, so the first frame's step is zero long.
        float lastFrame = F(rows.First(r => r[0] == "C")[1]);
        clock.Now = lastFrame;
        c.SystemUpdate();
        var pending = new List<string[]>();
        foreach (var r in rows)
        {
            if (r[0] == "C")
            {
                pending.Add(r);
                continue;
            }

            clock.Now = lastFrame;
            c.SystemUpdate();
            foreach (var cmd in pending)
            {
                clock.Now = F(cmd[1]);
                Apply(c, cmd);
            }

            pending.Clear();
            lastFrame = F(r[1]);
            Assert.True(Bits(c.GetVolume(1)) == Bits(F(r[3])) && Bits(c.GetPitch(1)) == Bits(F(r[4])),
                $"frame {frames} at {r[1]}: volume {c.GetVolume(1):R} pitch {c.GetPitch(1):R}, stock {r[3]} {r[4]}");
            frames++;
        }

        Assert.True(frames >= 150, $"{frames} frames replayed");
    }
}
