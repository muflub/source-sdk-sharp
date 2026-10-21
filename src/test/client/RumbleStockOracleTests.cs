using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Input;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed rumble mixer (<see cref="ClientRumble"/>) against stock's
/// <c>CRumbleEffects</c> (c_rumble.cpp), recorded live
/// (<c>Fixtures/rumble-stock.tsv</c>, README beside it): the waveform table,
/// then 400 frames of a scripted schedule (every effect, every flag, channel
/// contention, the screen shake, output off and on). Bit-exact.
/// </summary>
public class RumbleStockOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static List<string[]> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "rumble-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock rumble mixer is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(rows.Count(r => r[0] == "U") >= 400 && rows.Count(r => r[0] == "W") == ClientRumble.EffectCount, "the oracle parsed");
        return rows;
    }

    private sealed class Motors : IInputDevices
    {
        public readonly List<(float L, float R)> Sent = [];
        public int Stops;
        public bool GetRawMouseAccumulators(out int x, out int y) { x = y = 0; return false; }
        public int GetAnalogValue(int analogCode) => 0;
        public int JoystickCount => 0;
        public bool IsButtonDown(int buttonCode) => false;
        public ISteamControllerApi? SteamController => null;
        public bool IsSteamControllerActive => false;
        public void ActivateSteamControllerActionSet(int actionSet) { }
        public void SetRumble(float left, float right) => Sent.Add((left, right));
        public void StopRumble() => Stops++;
    }

    /// <summary>Answers the draws stock made, in order, and fails a draw stock did not make.</summary>
    private sealed class RecordedRandom(Queue<float> draws) : IUniformRandom
    {
        public float RandomFloat(float min, float max)
        {
            Assert.Equal(0.1f, min);
            Assert.Equal(1.0f, max);
            Assert.True(draws.Count > 0, "managed drew a random amplitude stock did not");
            return draws.Dequeue();
        }

        public int RandomInt(int min, int max) => throw new InvalidOperationException("the mixer draws floats only");
    }

    [Fact]
    public void TheWaveformTableIsStocksOverrunIncluded()
    {
        var mixer = new ClientRumble(new Motors(), new InputCvars(), new RecordedRandom(new Queue<float>()));
        var problems = new List<string>();
        foreach (string[] w in Stock().Where(r => r[0] == "W"))
        {
            int index = I(w[1]);
            ClientRumble.Waveform wave = mixer.Waveforms[index];
            if (wave.Samples != I(w[2]))
                problems.Add($"waveform {index}: {wave.Samples} samples vs stock {w[2]}");
            for (int i = 0; i < ClientRumble.WaveSamples; i++)
            {
                string[] lr = w[3 + i].Split(',');
                if (Bits(wave.Left[i]) != Bits(F(lr[0])) || Bits(wave.Right[i]) != Bits(F(lr[1])))
                    problems.Add($"waveform {index} sample {i}: {wave.Left[i]:R},{wave.Right[i]:R} vs stock {w[3 + i]}");
            }
        }

        // The overrun is in the positive arm: PHYSCANNON_LOW's count is 0.25f's bits.
        Assert.Equal(1048576000, mixer.Waveforms[ClientRumble.PhyscannonLow].Samples);
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(5)));
    }

    [Fact]
    public void EveryFrameDrivesTheMotorsAsStockDid()
    {
        List<string[]> rows = Stock();
        var motors = new Motors();
        var cvars = new InputCvars();
        var draws = new Queue<float>(rows.Where(r => r[0] == "RR").Select(r => F(r[1])));
        var mixer = new ClientRumble(motors, cvars, new RecordedRandom(draws));

        // The probe stopped everything before recording, as the mixer is fresh.
        mixer.StopAllEffects();

        var problems = new List<string>();
        int frames = 0, moving = 0, channelRows = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            switch (r[0])
            {
                case "V":
                    cvars[r[1]].Set(r[2]);
                    break;
                case "E":
                    mixer.StartEffect(I(r[2]), I(r[3]), I(r[4]), F(r[1]));
                    break;
                case "SS":
                    mixer.UpdateScreenShakeRumble(F(r[1]));
                    break;
                case "EN":
                    mixer.SetOutputEnabled(r[1] == "1");
                    break;
                case "U":
                {
                    frames++;
                    mixer.UpdateEffects(F(r[1]), r[2] == "1");
                    (float left, float right) = mixer.LastOutput;
                    if (Bits(left) != Bits(F(r[4])) || Bits(right) != Bits(F(r[5])))
                        problems.Add($"frame {frames} at {r[1]}: motors {left:R},{right:R} vs stock {r[4]},{r[5]}");
                    if (left != 0 || right != 0)
                        moving++;
                    break;
                }
                case "CH":
                {
                    channelRows++;
                    ClientRumble.Channel c = mixer.Channels[I(r[1])];
                    bool inUse = r[2] == "1";
                    if (c.InUse != inUse || (inUse && (c.Waveform != I(r[3]) || Bits(c.StartTime) != Bits(F(r[4])) ||
                                                      c.Priority != I(r[5]) || c.Flags != I(r[6]) || Bits(c.Scale) != Bits(F(r[7])))))
                        problems.Add($"frame {frames} channel {r[1]}: managed in_use={c.InUse} wave={c.Waveform} start={c.StartTime:R} flags={c.Flags} scale={c.Scale:R} vs stock {string.Join(' ', r.Skip(2))}");
                    break;
                }
            }
        }

        Assert.True(frames >= 400 && channelRows >= 1200, $"replayed {frames} frames, {channelRows} channel rows");
        Assert.True(moving >= 100, $"only {moving} frames drove a motor: the schedule did not reach the mixer");
        Assert.Empty(draws);
        Assert.True(problems.Count == 0, $"{problems.Count} differences:\n{string.Join("\n", problems.Take(5))}");
    }

    [Fact]
    public void ADeadOrAbsentLocalPlayerStopsTheMotors()
    {
        var motors = new Motors();
        var mixer = new ClientRumble(motors, new InputCvars(), new RecordedRandom(new Queue<float>()));
        mixer.StartEffect(ClientRumble.FlatBoth, 0, 0, 1.0f);

        mixer.Frame(1.05f, paused: false, localPlayerAlive: true);
        Assert.Equal((1.0f, 1.0f), motors.Sent[^1]);

        mixer.Frame(1.1f, paused: false, localPlayerAlive: false);
        Assert.Equal(1, motors.Stops);
        Assert.False(mixer.Channels[0].InUse);
    }
}
