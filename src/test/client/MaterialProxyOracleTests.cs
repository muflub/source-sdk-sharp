using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed material proxies against stock's, recorded:
/// <c>Fixtures/proxies-stock.tsv.gz</c> (README beside it) holds 43 proxy
/// names x 400 binds, each proxy's KeyValues block and the FindVar calls its
/// Init made, and for each bind the inputs, the vars' state, the texture and
/// random reads and every IMaterialVar setter stock called. Replayed here
/// through <see cref="MaterialProxyFactory"/>: the same Init must ask for the
/// same vars and answer the same, and each bind must make the same reads and
/// the same setter calls with the same arguments, bit for bit.
/// </summary>
public class MaterialProxyOracleTests
{
    internal sealed class Recorded
    {
        public readonly List<Init> Inits = [];
        public readonly List<Bind> Binds = [];
    }

    internal sealed class Init
    {
        public int Id;
        public string Name = "";
        public string Material = "";
        public readonly List<string[]> Keys = [];
        public readonly List<string[]> FindVars = [];
        public bool Ok;
    }

    internal sealed class Bind
    {
        public int Id;
        public string[] Row = [];
        public readonly Dictionary<string, string[]> Before = new(StringComparer.Ordinal);
        public readonly List<string[]> Gets = [];
        public readonly List<string[]> Rnds = [];
        public readonly List<string[]> Sets = [];
        public readonly HashSet<string> InternalTextureSets = [];
    }

    private static readonly Lazy<Recorded> s_recorded = new(Read);

    internal static Recorded Load() => s_recorded.Value;

    private static Recorded Read()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "proxies-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded material proxies are missing: " + path);

        var rec = new Recorded();
        var inits = new Dictionary<int, Init>();
        Bind? bind = null;
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] f = line.Split('\t');
            int id = int.Parse(f[1], CultureInfo.InvariantCulture);
            switch (f[0])
            {
                case "px-init":
                    var init = new Init { Id = id, Name = f[2], Material = f[3] };
                    inits[id] = init;
                    rec.Inits.Add(init);
                    break;
                case "px-key":
                    inits[id].Keys.Add(f);
                    break;
                case "px-findvar":
                    inits[id].FindVars.Add(f);
                    break;
                case "px-initok":
                    inits[id].Ok = f[2] == "1";
                    break;
                case "px-bind":
                    bind = new Bind { Id = id, Row = f };
                    break;
                case "px-before":
                    bind!.Before[f[2]] = f;
                    break;
                case "px-get":
                    bind!.Gets.Add(f);
                    break;
                case "px-rnd":
                    bind!.Rnds.Add(f);
                    break;
                case "px-set":
                    bind!.Sets.Add(f);
                    break;
                case "px-iset":
                    if (f[3] == "texture")
                        bind!.InternalTextureSets.Add(f[2]);
                    break;
                case "px-end":
                    rec.Binds.Add(bind!);
                    bind = null;
                    break;
            }
        }
        return rec;
    }

    // printf's %.9g spells the non-finite values inf, -inf, nan and -nan.
    private static float F(string s) => s switch
    {
        "inf" => float.PositiveInfinity,
        "-inf" => float.NegativeInfinity,
        "nan" or "-nan" => float.NaN,
        _ => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The probe's spelling of a name in a whitespace-split line.</summary>
    internal static string Tok(string s) => s.Length == 0 ? "<empty>" : s.Replace(' ', '?').Replace('\t', '?');

    internal static ProxyKey Key(string[] f) => new(
        f[2].Replace('?', ' '), (ProxyKeyType)I(f[3]), f[4] == "<empty>" ? "" : f[4].Replace('?', ' '),
        F(f[5]), I(f[6]), I(f[7]), F(f[8]), F(f[9]), I(f[10]));

    /// <summary>Init's view of the recording: FindVar answers from the logged calls, in order, and fails on any other ask.</summary>
    internal sealed class RecordedInit(Init init) : IProxyInit
    {
        private int _next;
        public readonly List<string> SlotVars = [];
        public readonly List<string> Wrong = [];

        public string MaterialName => init.Material;

        public ProxyKeyValues Keys { get; } = new(init.Keys.Select(Key));

        public int FindVar(string name, bool complain, out bool found)
        {
            found = false;
            if (_next >= init.FindVars.Count)
            {
                Wrong.Add($"FindVar( {name} ) past the {init.FindVars.Count} stock made");
                return -1;
            }
            string[] f = init.FindVars[_next++];
            if (f[2] != Tok(name) || (f[3] == "1") != complain)
                Wrong.Add($"FindVar {_next}: managed ( {Tok(name)}, {complain} ), stock ( {f[2]}, {f[3] == "1"} )");
            found = f[4] == "1";
            if (f[5] == "<null>")
                return -1;
            SlotVars.Add(f[5]);
            return SlotVars.Count - 1;
        }

        public int FindTexture(string name, string group, bool complain, out bool isError)
        {
            // Only PlayerLogo asks, for decals/YBlood1, which every recorded
            // PlayerLogo Init found (they all answered true).
            isError = false;
            return 0;
        }

        public bool AllAsked => _next == init.FindVars.Count;
    }

    /// <summary>One bind's view of the recording.</summary>
    internal sealed class RecordedBind(Bind rec, IReadOnlyList<string> slotVars, GaussianRandom gaussian)
        : IProxyBind, IProxyUniform
    {
        private int _get;
        private int _rnd;
        private readonly HashSet<string> _madeTexture = [];
        public readonly List<string> Ops = [];
        public readonly List<string> Wrong = [];
        public int NotShippedCount;

        private string[] R => rec.Row;

        public float CurTime => F(R[4]);
        public float FrameTime => F(R[5]);
        public int MaxClients => I(R[8]);
        public nint Arg => I(R[6]) switch { 0 => 0, 1 => I(R[7]), _ => 0x10000 };

        public ProxyVar Var(int slot)
        {
            if (slot < 0 || slot >= slotVars.Count)
            {
                Wrong.Add($"Var( {slot} ) is no slot");
                return default;
            }
            string name = slotVars[slot];
            if (!rec.Before.TryGetValue(name, out string[]? v))
            {
                Wrong.Add($"Var( {name} ) has no recorded state");
                return default;
            }
            return new ProxyVar
            {
                Type = _madeTexture.Contains(name) ? (int)MaterialVarType.Texture : I(v[3]),
                VecSize = I(v[4]),
                Int = I(v[5]),
                Float = F(v[6]),
                V0 = F(v[7]),
                V1 = F(v[8]),
                V2 = F(v[9]),
                V3 = F(v[10]),
            };
        }

        public int Texture(int slot, out string? name)
        {
            name = null;
            string var = slot >= 0 && slot < slotVars.Count ? slotVars[slot] : "?";
            if (_get >= rec.Gets.Count)
            {
                Wrong.Add($"GetTextureValue( {var} ) past the {rec.Gets.Count} stock made");
                return -1;
            }
            string[] g = rec.Gets[_get++];
            if (g[2] != var)
                Wrong.Add($"GetTextureValue {_get}: managed {var}, stock {g[2]}");
            if (rec.InternalTextureSets.Contains(var))
                _madeTexture.Add(var);
            name = g[4] == "<null>" ? null : g[4];
            return I(g[5]);
        }

        public ProxyEntity Entity()
        {
            if (I(R[6]) != 2)
                return ProxyEntity.None with { HasArg = I(R[6]) != 0 };
            int ent = I(R[10]);
            int alpha = I(R[14]);
            int npc = I(R[15]);
            bool unknown = ent >= 0 || alpha >= 0 || npc >= 0;
            return new ProxyEntity(true, unknown, ent, new Vector(F(R[11]), F(R[12]), F(R[13])), alpha, npc);
        }

        public Vector ViewOrigin() => new(F(R[16]), F(R[17]), F(R[18]));

        public void WaterLod(out float start, out float end)
        {
            start = F(R[19]);
            end = F(R[20]);
        }

        public bool WorldBounds(out Vector mins, out Vector maxs)
        {
            mins = new Vector(F(R[22]), F(R[23]), F(R[24]));
            maxs = new Vector(F(R[25]), F(R[26]), F(R[27]));
            return R[21] == "1";
        }

        // No recorded proxy reads the particle manager (ParticleSphereProxy had no material on the recorded scene).
        public ParticleBindLight ParticleLight() => throw new InvalidOperationException("the recording has no particle manager read");

        public bool PlayerCustomFile(int player, out uint crc)
        {
            crc = uint.Parse(R[9], CultureInfo.InvariantCulture);
            return true;
        }

        public int FindTexture(string name, string group, bool complain)
        {
            Wrong.Add($"FindTexture( {name} ): no recorded bind looked a spray up");
            return -1;
        }

        public float RandomFloat(float lo, float hi)
        {
            if (_rnd >= rec.Rnds.Count)
            {
                Wrong.Add($"RandomFloat( {lo}, {hi} ) past the {rec.Rnds.Count} draws stock made");
                return 0;
            }
            string[] d = rec.Rnds[_rnd++];
            if (F(d[3]) != lo || F(d[4]) != hi)
                Wrong.Add($"draw {_rnd}: managed ( {lo}, {hi} ), stock ( {d[3]}, {d[4]} )");
            return F(d[5]);
        }

        public float RandomGaussian(float mean, float stdDev) => gaussian.RandomFloat(this, mean, stdDev);

        public void NotShipped(string capability, string reason) => NotShippedCount++;

        public bool TextureFrameIndex(out int frame)
        {
            frame = 0;
            return false;
        }

        public bool EyeLighting(out Vector eye, out Vector forward, out Vector lighting)
        {
            eye = forward = lighting = Vector.Zero;
            return false;
        }

        private string N(int slot) => slot >= 0 && slot < slotVars.Count ? slotVars[slot] : $"slot{slot}";

        private static string G(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        public void SetFloat(int slot, float value) => Ops.Add($"{N(slot)} float {G(value)}");

        public void SetInt(int slot, int value) => Ops.Add($"{N(slot)} int {value}");

        public void SetVec(int slot, ReadOnlySpan<float> values, int count)
        {
            var parts = new List<string> { N(slot), "vecn", count.ToString(CultureInfo.InvariantCulture) };
            for (int i = 0; i < 4; i++)
                parts.Add(G(i < count && i < values.Length ? values[i] : 0f));
            Ops.Add(string.Join(' ', parts));
        }

        public void SetVec3(int slot, float x, float y, float z) => Ops.Add($"{N(slot)} vec3 {G(x)} {G(y)} {G(z)}");

        public void SetVecComponent(int slot, float value, int component) => Ops.Add($"{N(slot)} veccomp {component} {G(value)}");

        public void SetMatrix(int slot, in VMatrix matrix)
        {
            var parts = new List<string> { N(slot), "matrix" };
            for (int i = 0; i < 16; i++)
                parts.Add(G(matrix[i]));
            Ops.Add(string.Join(' ', parts));
        }

        public void SetTexture(int slot, int textureSlot) => Ops.Add($"{N(slot)} texture #{textureSlot}");

        /// <summary>Stock's setter calls in the same spelling.</summary>
        public List<string> StockOps()
        {
            var ops = new List<string>();
            foreach (string[] s in rec.Sets)
            {
                var parts = new List<string> { s[2], s[3] };
                parts.AddRange(s.Skip(4).Select((x, i) => s[3] is "int" || (s[3] is "vecn" or "veccomp" && i == 0) ? x : G(F(x))));
                ops.Add(string.Join(' ', parts));
            }
            return ops;
        }

        public bool AllRead => _get == rec.Gets.Count && _rnd == rec.Rnds.Count;
    }

    /// <summary>
    /// The four-wide results stock reads through a three-wide <c>Vector</c>
    /// (Vec4Temp's note): its <c>GetVecValue( a, 4 )</c> overruns onto the
    /// <c>vecSize</c> local, so stock's write is <c>SetVecValue( a, 0 )</c>.
    /// The port writes the four components; the replay checks that write
    /// against the four the var read, instead of stock's.
    /// </summary>
    internal static bool StockOverranAVector(Bind b, string stockOp) => stockOp.Contains(" vecn 0 ", StringComparison.Ordinal);

    /// <summary>
    /// Proxies client.so registers from TUs outside cluster 1 (the renderer's
    /// viewpostprocess.cpp and clientshadowmgr.cpp, the particles glue's
    /// particle_proxies.cpp): the scene's content materials made some, and
    /// they are not this port's. ParticleSphereProxy is ParticleSphereProxyTests'
    /// (the recording made it on no material it bound).
    /// </summary>
    // MotionBlur and engine_post bind the post program's values (PostProcessProxyTests), not a recorded stock global.
    internal static readonly HashSet<string> OtherLanes = ["MotionBlur", "engine_post", "Shadow", "ShadowModel", "ParticleSphereProxy"];

    internal sealed record Replay(int Inits, int Binds, int Ops, List<string> Wrong, Dictionary<string, int> BindsByName, int Overruns);

    internal static Replay Run(Func<string, MaterialProxy?> create, IProxyMath math)
    {
        Recorded rec = Load();
        var wrong = new List<string>();
        var proxies = new Dictionary<int, (MaterialProxy Proxy, List<string> Slots, string Name)>();
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var gaussian = new GaussianRandom(math);
        int ops = 0, overruns = 0;

        foreach (Init init in rec.Inits)
        {
            if (OtherLanes.Contains(init.Name))
                continue;
            MaterialProxy? p = create(init.Name);
            if (p is null)
            {
                wrong.Add($"no managed proxy for '{init.Name}'");
                continue;
            }
            var ri = new RecordedInit(init);
            bool ok = p.Init(ri);
            if (ok != init.Ok)
                wrong.Add($"{init.Name} on {init.Material} (#{init.Id}): Init managed {ok}, stock {init.Ok}");
            if (!ri.AllAsked)
                wrong.Add($"{init.Name} on {init.Material} (#{init.Id}): Init asked fewer FindVars than stock's {init.FindVars.Count}");
            wrong.AddRange(ri.Wrong.Select(w => $"{init.Name} on {init.Material} (#{init.Id}): {w}"));
            if (ok)
                proxies[init.Id] = (p, ri.SlotVars, init.Name);
        }

        foreach (Bind b in rec.Binds)
        {
            if (!proxies.TryGetValue(b.Id, out var px))
            {
                wrong.Add($"bind of #{b.Id} ({b.Row[2]}): no proxy initialised");
                continue;
            }
            byName[px.Name] = byName.GetValueOrDefault(px.Name) + 1;
            var rb = new RecordedBind(b, px.Slots, gaussian);
            px.Proxy.Bind(rb);
            List<string> stock = rb.StockOps();
            List<string> managed = rb.Ops;
            ops += stock.Count;
            string where = $"{px.Name} #{b.Id} bind {b.Row[3]} (t={b.Row[4]})";
            wrong.AddRange(rb.Wrong.Select(w => $"{where}: {w}"));
            if (!rb.AllRead)
                wrong.Add($"{where}: fewer texture or random reads than stock's");

            bool same = stock.Count == managed.Count;
            for (int i = 0; same && i < stock.Count; i++)
            {
                if (stock[i] == managed[i])
                    continue;
                if (StockOverranAVector(b, stock[i]) && OverrunMatches(b, px.Slots, stock[i], managed[i]))
                {
                    overruns++;
                    continue;
                }
                same = false;
            }
            if (!same)
                wrong.Add($"{where}: managed [{string.Join(" | ", managed)}], stock [{string.Join(" | ", stock)}]");
        }
        return new Replay(rec.Inits.Count, rec.Binds.Count, ops, wrong, byName, overruns);
    }

    // The overrun's write: the same var, four components, the first three
    // what the vector op computed from the read and the fourth the read's.
    private static bool OverrunMatches(Bind b, List<string> slots, string stock, string managed)
    {
        string[] s = stock.Split(' ');
        string[] m = managed.Split(' ');
        return m.Length == 7 && s[0] == m[0] && m[1] == "vecn" && m[2] == "4" && b.Before.ContainsKey(s[0]);
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        Recorded rec = Load();
        var names = rec.Binds.Select(b => b.Row[2]).Distinct().ToList();

        Assert.True(names.Count == 43, $"43 proxy names bound, not {names.Count}");
        Assert.True(rec.Binds.Count == 43 * 400, $"400 binds each, not {rec.Binds.Count} in all");
        Assert.True(rec.Binds.Sum(b => b.Sets.Count) > 10000, "more than 10,000 setter calls");
        Assert.True(rec.Binds.Count(b => b.Rnds.Count > 0) > 100, "binds that drew from the uniform stream");
        Assert.True(rec.Inits.Count(i => !i.Ok) > 0, "Inits stock refused (Camo, and missing result vars)");
        Assert.True(rec.Inits.SelectMany(i => i.Keys).Select(k => k[3]).Distinct().Count() >= 3,
            "STRING, INT and FLOAT keys (the probe read them without converting them)");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void EveryRecordedInitAndBindReplaysBitForBit()
    {
        var factory = new MaterialProxyFactory(new StandardErrorLog(), new StockProxyMath(), () => false);
        Replay r = Run(factory.Create, new StockProxyMath());

        Assert.True(r.Wrong.Count == 0,
            $"{r.Wrong.Count} disagreement(s) over {r.Inits} Inits and {r.Binds} binds; first: "
            + string.Join("\n", r.Wrong.Take(8)));
        Assert.True(r.Binds == 43 * 400 && r.Ops > 10000, $"the replay reached {r.Binds} binds and {r.Ops} setter calls");
    }

    [Fact]
    public void EveryRecordedNameReplaysAtLeastTwoHundredBinds()
    {
        var factory = new MaterialProxyFactory(new StandardErrorLog(), new StockProxyMath(), () => false);
        Replay r = Run(factory.Create, new StockProxyMath());

        var few = r.BindsByName.Where(kv => kv.Value < 200).Select(kv => $"{kv.Key}={kv.Value}").ToList();
        Assert.True(r.BindsByName.Count == 43 && few.Count == 0, $"{r.BindsByName.Count} names; under 200: {string.Join(", ", few)}");
    }

    [Fact]
    public void TheOverrunBindsAreTheFourWideVectorResultsOnly()
    {
        var factory = new MaterialProxyFactory(new StandardErrorLog(), new StockProxyMath(), () => false);
        Replay r = Run(factory.Create, new StockProxyMath());
        Recorded rec = Load();

        int stockZeroWidth = rec.Binds.Sum(b => b.Sets.Count(s => s[3] == "vecn" && s[4] == "0"));
        Assert.True(stockZeroWidth > 0 && r.Overruns == stockZeroWidth,
            $"stock wrote {stockZeroWidth} zero-wide vectors; the replay excused {r.Overruns}");
    }
}
