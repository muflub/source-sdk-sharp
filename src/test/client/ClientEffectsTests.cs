using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's IEffects decisions against stock's, recorded:
/// <c>Fixtures/ieffects-stock.tsv</c> (README beside it) holds 205 calls into
/// stock's <c>CEffectsClient</c>, each with the suppression state it saw, the
/// random draw it made and the <c>FX_*</c> routine it called. Replayed through
/// <see cref="ClientEffects"/> with the same state and the same draw, every
/// call must suppress or draw as stock did and reach the same routine with the
/// same arguments, bit for bit.
/// </summary>
public class ClientEffectsTests
{
    private sealed class Rig : IClientDustPuff, IFxRoutines, IClientSparks, IFxSuppression
    {
        public readonly List<(string Name, float[] Args)> Calls = [];
        public bool CanPredictAnswer = true;
        public int RecipientAnswer = 1;
        public bool NormalWasDirection = true;

        private void Add(string name, params float[] args) => Calls.Add((name, args));

        private void Smoke(Vector o, QAngle a, float scale, int n, Color32 c, int alpha)
            => Add("FX_Smoke", o.X, o.Y, o.Z, a.Pitch, a.Yaw, a.Roll, scale, n, c.R, c.G, c.B, c.A, alpha);

        public void ElectricSpark(Vector p, int magnitude, int trail, Vector? d)
            => Add("FX_ElectricSpark", p.X, p.Y, p.Z, magnitude, trail, d is null ? 0 : 1,
                   d?.X ?? 0f, d?.Y ?? 0f, d?.Z ?? 0f);

        public int Puff(Vector o, Vector d, float size, float speed)
        {
            Add("FX_Dust", o.X, o.Y, o.Z, d.X, d.Y, d.Z, size, speed);
            return 1;
        }

        private void Muzzle(string routine, Vector o, QAngle a, float scale) => Add(routine, o.X, o.Y, o.Z, a.Pitch, a.Yaw, a.Roll, scale);

        public void MetalSpark(Vector p, Vector d, Vector normal)
        {
            NormalWasDirection &= normal.X == d.X && normal.Y == d.Y && normal.Z == d.Z;
            Add("FX_MetalSpark", p.X, p.Y, p.Z, d.X, d.Y, d.Z);
        }

        public void EnergySplash(Vector p, Vector n, bool explosive)
            => Add("FX_EnergySplash", p.X, p.Y, p.Z, n.X, n.Y, n.Z, explosive ? 1 : 0);

        // IEffects passes FX_MetalSpark's default scale.
        void IClientSparks.MetalSpark(Vector p, Vector d, Vector normal, int scale)
        {
            Assert.Equal(1, scale);
            MetalSpark(p, d, normal);
        }

        // IEffects has no gauss explosion; only the temp entity draws one.
        void IClientSparks.GaussExplosion(Vector p, Vector d, int type) => Add("FX_GaussExplosion", p.X, p.Y, p.Z);

        // The spark routines IEffects ends in (fx_sparks.cpp's, C# since lane c5-particles).

        public void RicochetSound(Vector p) => Add("FX_RicochetSound", p.X, p.Y, p.Z);

        // fx.cpp's routines IEffects ends in (C# since lane c5-particles).
        void IFxRoutines.MuzzleEffect(Vector o, QAngle a, float scale) => Muzzle("FX_MuzzleEffect", o, a, scale);

        void IFxRoutines.GunshipMuzzleEffect(Vector o, QAngle a, float scale) => Muzzle("FX_GunshipMuzzleEffect", o, a, scale);

        void IFxRoutines.StriderMuzzleEffect(Vector o, QAngle a, float scale) => Muzzle("FX_StriderMuzzleEffect", o, a, scale);

        void IFxRoutines.MuzzleEffectAttached(float scale, uint handle, int attachment, bool oneFrame)
            => Assert.Fail("IEffects has no attached muzzle flash");

        void IFxRoutines.Smoke(Vector o, QAngle a, float scale, int n, Color32? c, int alpha)
        {
            Assert.True(c is not null, "IEffects' smoke always passes its colour");
            Smoke(o, a, scale, n, c!.Value, alpha);
        }

        public bool CanPredict() => CanPredictAnswer;

        public int RecipientCount(Vector origin) => RecipientAnswer;
    }

    private sealed record Call(string Method, string[] Args, bool? CanPredict, int? Recipients, bool? SoundSuppressed,
                               int? Draw, List<string[]> Fx);

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Vector V(string[] a, int at) => new(F(a[at]), F(a[at + 1]), F(a[at + 2]));

    private static List<Call> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "ieffects-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock IEffects is missing: " + path);

        var calls = new List<Call>();
        Call? open = null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "call":
                    open = new Call(c[1], c[2..], null, null, null, null, []);
                    calls.Add(open);
                    break;
                case "suppress":
                    Assert.True(open is not null, "a suppress row before any call");
                    calls[^1] = open = open! with
                    {
                        CanPredict = c[1] == "cp=1",
                        Recipients = int.Parse(c[2]["rc=".Length..], CultureInfo.InvariantCulture),
                        SoundSuppressed = c[3] == "sound=1",
                    };
                    break;
                case "draw":
                    Assert.True(c[1] == "RandomInt" && c[2] == "20" && c[3] == "35", $"an unexpected draw: '{line}'");
                    calls[^1] = open = open! with { Draw = int.Parse(c[4], CultureInfo.InvariantCulture) };
                    break;
                case "fx":
                    open!.Fx.Add(c[1..]);
                    break;
                case "arm":
                    open = null;
                    break;
                default:
                    Assert.Fail($"not a fixture row: '{line}'");
                    break;
            }
        }

        return calls;
    }

    private static void Play(ClientEffects effects, Call call)
    {
        string[] a = call.Args;
        switch (call.Method)
        {
            case "Smoke":
                effects.Smoke(V(a, 0), int.Parse(a[3], CultureInfo.InvariantCulture), F(a[4]), F(a[5]));
                break;
            case "Sparks":
                effects.Sparks(V(a, 0), int.Parse(a[3], CultureInfo.InvariantCulture), int.Parse(a[4], CultureInfo.InvariantCulture),
                               a[5] == "1" ? V(a, 6) : null);
                break;
            case "Dust":
                effects.Dust(V(a, 0), V(a, 3), F(a[6]), F(a[7]));
                break;
            case "MuzzleFlash":
                effects.MuzzleFlash(V(a, 0), new QAngle(F(a[3]), F(a[4]), F(a[5])), F(a[6]), int.Parse(a[7], CultureInfo.InvariantCulture));
                break;
            case "MetalSparks":
                effects.MetalSparks(V(a, 0), V(a, 3));
                break;
            case "EnergySplash":
                effects.EnergySplash(V(a, 0), V(a, 3), a[6] == "1");
                break;
            case "Ricochet":
                effects.Ricochet(V(a, 0), V(a, 3));
                break;
            case "SuppressEffectsSounds":
                effects.SuppressEffectsSounds(a[0] == "1");
                break;
            default:
                Assert.Fail("a recorded call this fact cannot play: " + call.Method);
                break;
        }
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<Call> calls = Recorded();

        Assert.True(calls.Count == 207, $"207 calls recorded (205 effects, 2 sound switches), read {calls.Count}");
        Assert.True(calls.Count(c => c.CanPredict == false) == 9, "nine under SuppressEvents (the suppressed arm)");
        Assert.True(calls.Count(c => c.Fx.Count > 0) == 196, "196 drew (the positive arm)");
        Assert.True(calls.Any(c => c.Method == "MuzzleFlash" && c.Fx.Any(f => f[0] == "FX_StriderMuzzleEffect")),
                    "every muzzle-flash arm is in it");
        Assert.True(calls.Any(c => c.Method == "Ricochet" && c.SoundSuppressed == true && c.Fx.Count == 1),
                    "and a ricochet with its sound suppressed");
    }

    [Fact]
    public void EveryRecordedCallDecidesAndDrawsAsStockDid()
    {
        var rig = new Rig();
        RecordingEngine engine = TestServices.Fake();
        var effects = new ClientEffects(rig, rig, rig, rig, engine, engine);

        var wrong = new List<string>();
        int index = 0;

        foreach (Call call in Recorded())
        {
            index++;
            rig.Calls.Clear();
            int msgsBefore = engine.CountOf("Console.Msg");

            if (call.CanPredict is bool cp)
                rig.CanPredictAnswer = cp;
            if (call.Recipients is int rc)
                rig.RecipientAnswer = rc;
            if (call.Draw is int draw)
                engine.Returns("Host.RandomInt", draw);

            if (call.SoundSuppressed is bool sound && sound != effects.EffectsSoundsSuppressed)
                wrong.Add($"#{index} {call.Method}: stock's sound flag was {sound}, managed holds {effects.EffectsSoundsSuppressed}");

            Play(effects, call);

            // The Msg-no-case row is stock's console line, which is managed's too.
            var expected = call.Fx.Where(f => f[0] != "Msg-no-case").ToList();
            int noCase = call.Fx.Count - expected.Count;
            if (engine.CountOf("Console.Msg") - msgsBefore != noCase)
                wrong.Add($"#{index} {call.Method}: {noCase} no-case line(s) in stock, {engine.CountOf("Console.Msg") - msgsBefore} in managed");

            if (expected.Count != rig.Calls.Count)
            {
                wrong.Add($"#{index} {call.Method}: stock made {expected.Count} FX call(s), managed {rig.Calls.Count}");
                continue;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                string[] want = expected[i];
                var (name, got) = rig.Calls[i];
                bool same = want[0] == name && want.Length - 1 == got.Length;
                for (int k = 0; same && k < got.Length; k++)
                    same = BitConverter.SingleToInt32Bits(F(want[k + 1])) == BitConverter.SingleToInt32Bits(got[k]);
                if (!same)
                    wrong.Add($"#{index} {call.Method}: stock {string.Join(' ', want)}, managed {name} {string.Join(' ', got.Select(g => g.ToString("R", CultureInfo.InvariantCulture)))}");
            }
        }

        Assert.True(rig.NormalWasDirection, "FX_MetalSpark's surface normal is the direction, as stock passes it");
        Assert.True(wrong.Count == 0, $"{wrong.Count} recorded call(s) disagree: " + string.Join("; ", wrong.Take(8)));
    }

    [Fact]
    public void AFilterThatReachesNobodySuppressesTheEffect()
    {
        // Not in the recording (every call in a joined session has a viewer);
        // stock's `if ( !filter.GetRecipientCount() ) return true;`, read from
        // EffectsClient.cpp.
        var rig = new Rig { RecipientAnswer = 0 };
        RecordingEngine engine = TestServices.Fake();
        var effects = new ClientEffects(rig, rig, rig, rig, engine, engine);

        effects.Sparks(new Vector(1f, 2f, 3f), 1, 1, null);

        Assert.True(rig.Calls.Count == 0 && effects.Suppressed == 1, "nothing drawn, counted suppressed");
    }
}
