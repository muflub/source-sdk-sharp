using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The legacy tempents' muzzle flashes against stock's, recorded:
/// <c>Fixtures/tempents-muzzle-stock.tsv</c> (README-tempents-stock.md) holds
/// every MuzzleFlash call of a battery over every type, both overloads, first
/// person and not, with each emitter stock made and set up, each particle it
/// added (material and place) and every field the variant left on it, the
/// attachments and answers the engine gave, the elights, and every random
/// draw. The managed flashes, answered the same, must make the same calls in
/// the same order and leave the same particles, bit for bit.
/// </summary>
public class MuzzleFlashTests
{
    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tempents-muzzle-stock.tsv");
        Assert.True(File.Exists(path), "the recorded muzzle flashes are missing: " + path);
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
    private static int I(string[] r, string k, int i = 0) => int.Parse(At(r, k, i), CultureInfo.InvariantCulture);
    private static Vector V(string[] r, string k) => new(F(r, k), F(r, k, 1), F(r, k, 2));
    private static bool Same(in Vector a, in Vector b) => a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

    /// <summary>The engine, answering as the recording did and checking each call against the next recorded row.</summary>
    private sealed class RecordedEmitters(List<string[]> rows) : IFxEmitters, IFxFlashReads, IFxRoutines, IMuzzleFlashLight
    {
        private int _next;
        public readonly List<string> Wrong = [];
        public readonly List<(bool Trail, Vector Pos, FxSimpleParticle S, FxTrailParticle T, bool Set)> Particles = [];
        private int _ids;

        // The next row that answers or checks a call; markers and dumps are skipped.
        private string[]? Take(string want)
        {
            while (_next < rows.Count && rows[_next][0] is "tmf-begin" or "tmf-end" or "tmf-simple" or "tmf-trail" or "fxr")
                _next++;
            if (_next >= rows.Count || rows[_next][0] != want)
            {
                Wrong.Add($"managed called {want}, stock's next was {(_next < rows.Count ? string.Join(' ', rows[_next]) : "nothing")}");
                return null;
            }
            return rows[_next++];
        }

        public int Remaining()
        {
            while (_next < rows.Count && rows[_next][0] is "tmf-begin" or "tmf-end" or "tmf-simple" or "tmf-trail" or "fxr")
                _next++;
            return rows.Count - _next;
        }

        public int Create(FxEmitterKind kind, string name, uint handle, int attachment, int flags)
        {
            string[]? r = Take("tmf-create");
            string want = kind switch
            {
                FxEmitterKind.Simple => "CSimpleEmitter",
                FxEmitterKind.LocalSpace => "CLocalSpaceEmitter",
                FxEmitterKind.Ember => "CEmberEffect",
                _ => "CTrailParticles",
            };
            if (r is not null && (At(r, "kind") != want || At(r, "name") != name))
                Wrong.Add($"created {want} {name}, stock {At(r, "kind")} {At(r, "name")}");
            return ++_ids;
        }

        public void DrawBeforeViewModel(int emitter)
        {
            string[]? r = Take("tmf-cfg");
            if (r is not null && r[1] != "drawbeforeviewmodel")
                Wrong.Add($"drew before the view model where stock set {string.Join(' ', r[1..])}");
        }

        public void SetBBox(int emitter, in Vector mins, in Vector maxs)
        {
            string[]? r = Take("tmf-cfg");
            if (r is null)
                return;
            Vector o = V(r, "origin");
            var four = new Vector(4, 4, 4);
            if (!Same(mins, new Vector(o.X - four.X, o.Y - four.Y, o.Z - four.Z)) || !Same(maxs, new Vector(o.X + 4, o.Y + 4, o.Z + 4)))
                Wrong.Add($"box {mins}..{maxs}, stock about {o}");
        }

        public void SetSortOrigin(int emitter, in Vector origin)
        {
            string[]? r = Take("tmf-cfg");
            if (r is not null && !Same(origin, V(r, "sortorigin")))
                Wrong.Add($"sort origin {origin}, stock {V(r, "sortorigin")}");
        }

        public void SetTrailFlag(int emitter, int flag)
        {
            string[]? r = Take("tmf-cfg");
            if (r is not null && flag != I(r, "flag"))
                Wrong.Add($"trail flag {flag}, stock {I(r, "flag")}");
        }

        public void SetCollisionGravity(int emitter, float gravity)
        {
            string[]? r = Take("tmf-cfg");
            if (r is not null && !gravity.Equals(F(r, "gravity")))
                Wrong.Add($"gravity {gravity}, stock {F(r, "gravity")}");
        }

        public int Add(int emitter, string material, in Vector position)
        {
            string[]? r = Take("tmf-add");
            if (r is null)
                return -1;
            if (At(r, "mat") != material || !Same(position, V(r, "pos")))
                Wrong.Add($"added {material} at {position}, stock {At(r, "mat")} at {V(r, "pos")}");
            if (I(r, "ok") == 0)
                return -1;
            Particles.Add((false, position, default, default, false));
            return Particles.Count - 1;
        }

        public void SetSimple(int emitter, int particle, in FxSimpleParticle fields)
            => Particles[particle] = (false, Particles[particle].Pos, fields, default, true);

        public void SetTrail(int emitter, int particle, in FxTrailParticle fields)
            => Particles[particle] = (true, Particles[particle].Pos, default, fields, true);

        public void Release(int emitter)
        {
        }

        public bool AttachmentTransform(uint handle, int attachment, out Vector origin, out QAngle angles)
        {
            string[]? r = Take("tmf-att");
            origin = r is null ? default : V(r, "origin");
            angles = r is null ? default : new QAngle(F(r, "angles"), F(r, "angles", 1), F(r, "angles", 2));
            return true;
        }

        public bool AttachmentMatrixOrigin(uint handle, int attachment, out Vector origin)
        {
            string[]? r = Take("tmf-attm");
            origin = r is not null && I(r, "ok") != 0 ? V(r, "origin") : default;
            return r is not null && I(r, "ok") != 0;
        }

        public bool RenderableAttachment(uint handle, int attachment, out Vector origin, out QAngle angles)
        {
            origin = default;
            angles = default;
            string[]? r = Take("tmf-renderable");
            if (r is null || r[1] != "1")
                return false;
            string[]? a = Take("tmf-att");
            if (a is null)
                return false;
            origin = V(a, "origin");
            angles = new QAngle(F(a, "angles"), F(a, "angles", 1), F(a, "angles", 2));
            return true;
        }

        public int EntityIndex(uint handle)
        {
            string[]? r = Take("tmf-light");
            return r is not null && I(r, "ent") != 0 ? I(r, "entindex") : -1;
        }

        public void Elight(int key, in Vector origin, byte r, byte g, byte b, sbyte exponent, float radius, float decay, float die)
        {
            string[]? e = Take("tmf-elight");
            if (e is null)
                return;
            if (key != I(e, "key") || !Same(origin, V(e, "origin")) || r != I(e, "col") || g != I(e, "col", 1) || b != I(e, "col", 2)
                || exponent != I(e, "col", 3) || !radius.Equals(F(e, "radius")) || !decay.Equals(F(e, "decay")) || !die.Equals(F(e, "die")))
                Wrong.Add($"elight {key} at {origin} r {radius} decay {decay} die {die}, stock {string.Join(' ', e[1..])}");
        }

        public Vector VectorRandom(float lo, float hi)
        {
            string[]? r = Take("tmf-vrand");
            if (r is null)
                return default;
            if (!lo.Equals(F(r, "lo")) || !hi.Equals(F(r, "hi")))
                Wrong.Add($"Vector::Random({lo}, {hi}), stock ({F(r, "lo")}, {F(r, "hi")})");
            return V(r, "v");
        }

        public void MuzzleEffectAttached(float scale, uint handle, int attachment, bool oneFrame)
        {
            string[]? r = Take("tmf-fx");
            if (r is null)
                return;
            string text = string.Join(' ', r[1..]);
            string want = $"FX_MuzzleEffectAttached( {scale.ToString("0.0#", CultureInfo.InvariantCulture)}f, hEntity, attachmentIndex{(oneFrame ? ", NULL, true" : "")} );";
            if (text != want)
                Wrong.Add($"{want}, stock {text}");
        }

        // Only the attached flash is reached from the temp entities' flashes.
        void IFxRoutines.MuzzleEffect(Vector origin, QAngle angles, float scale) => Wrong.Add("FX_MuzzleEffect");

        void IFxRoutines.GunshipMuzzleEffect(Vector origin, QAngle angles, float scale) => Wrong.Add("FX_GunshipMuzzleEffect");

        void IFxRoutines.StriderMuzzleEffect(Vector origin, QAngle angles, float scale) => Wrong.Add("FX_StriderMuzzleEffect");

        void IFxRoutines.Smoke(Vector origin, QAngle angles, float scale, int numParticles, Color32? color, int alpha) => Wrong.Add("FX_Smoke");

        void IFxRoutines.RicochetSound(Vector position) => Wrong.Add("FX_RicochetSound");

        public int MaterialsCached() => 3;

        public bool On
        {
            get
            {
                string[]? r = Take("tmf-lightcvar");
                return r is not null && r[1] == "1";
            }
        }

    }

    private sealed class Clock(float now) : IGameClock
    {
        public float Now => now;
    }

    private sealed class NullLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<string[]> rows = Recorded();

        Assert.True(rows.Count(r => r[0] is "tmf-call1" or "tmf-call2") == 73, "73 calls");
        Assert.True(rows.Count(r => r[0] == "tmf-begin") == 40, "40 variants");
        Assert.True(rows.Count(r => r[0] is "tmf-simple" or "tmf-trail") == 219, "219 particles");
        Assert.True(rows.Count(r => r[0] == "tmf-elight") == 4, "four elights");
        Assert.True(rows.Count(r => r[0] == "tmf-vrand") > 0, "Vector::Random was drawn");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void EveryRecordedFlashIsDrawnAsStockDrewIt()
    {
        List<string[]> rows = Recorded();
        var wrong = new List<string>();
        int calls = 0, particles = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            string[] call = rows[i];
            if (call[0] is not ("tmf-call1" or "tmf-call2"))
                continue;
            calls++;

            int j = i + 1;
            while (j < rows.Count && rows[j][0] is not ("tmf-call1" or "tmf-call2"))
                j++;
            List<string[]> group = rows.GetRange(i + 1, j - i - 1);

            var rig = new FxTestRig();
            foreach (string[] r in group.Where(r => r[0] == "fxr"))
                rig.Enqueue(r);

            // The elight's die time is curtime + 0.05: answer curtime from the row.
            string[]? light = group.FirstOrDefault(r => r[0] == "tmf-elight");
            var fx = new RecordedEmitters(group);
            var flashes = new MuzzleFlashes(rig, fx, fx, fx, fx, new Clock(light is null ? 0f : F(light, "t")), new NullLog());

            uint handle = (uint)I(call, "h");
            bool fp = I(call, "fp") != 0;
            if (call[0] == "tmf-call1")
                flashes.MuzzleFlash(I(call, "type"), handle, I(call, "att"), fp);
            else
                flashes.MuzzleFlashAt(I(call, "type"), handle, fp);

            var stock = group.Where(r => r[0] is "tmf-simple" or "tmf-trail").ToList();
            if (stock.Count != fx.Particles.Count)
                fx.Wrong.Add($"{fx.Particles.Count} particles, stock {stock.Count}");
            for (int k = 0; k < Math.Min(stock.Count, fx.Particles.Count); k++)
            {
                particles++;
                string[] s = stock[k];
                var p = fx.Particles[k];
                bool ok;
                if (s[0] == "tmf-trail")
                {
                    var t = p.T;
                    ok = p.Trail && t.Lifetime.Equals(F(s, "life")) && t.DieTime.Equals(F(s, "die")) && Same(t.Velocity, V(s, "vel"))
                         && t.R == I(s, "col") && t.G == I(s, "col", 1) && t.B == I(s, "col", 2) && t.A == I(s, "col", 3)
                         && t.Length.Equals(F(s, "len")) && t.Width.Equals(F(s, "width")) && Same(p.Pos, V(s, "pos"));
                }
                else
                {
                    var q = p.S;
                    ok = !p.Trail && q.Lifetime.Equals(F(s, "life")) && q.DieTime.Equals(F(s, "die")) && Same(q.Velocity, V(s, "vel"))
                         && q.R == I(s, "col") && q.G == I(s, "col", 1) && q.B == I(s, "col", 2) && q.StartAlpha == I(s, "sa")
                         && q.EndAlpha == I(s, "ea") && q.StartSize == I(s, "ss") && q.EndSize == I(s, "es")
                         && q.Roll.Equals(F(s, "roll")) && q.RollDelta.Equals(F(s, "rd")) && Same(p.Pos, V(s, "pos"));
                }
                if (!ok)
                    fx.Wrong.Add($"particle {k}: stock {string.Join(' ', s[1..])}");
            }

            if (fx.Remaining() != 0)
                fx.Wrong.Add($"{fx.Remaining()} recorded calls managed did not make");
            if (rig.Pending != 0)
                fx.Wrong.Add($"{rig.Pending} draws stock made and managed did not");
            foreach (string w in rig.Wrong.Concat(fx.Wrong))
                wrong.Add($"call {calls} ({string.Join(' ', call)}): {w}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} disagreements over {calls} calls: " + string.Join(" | ", wrong.Take(6)));
        Assert.True(particles == 219, $"every recorded particle was compared, {particles}");
    }
}
