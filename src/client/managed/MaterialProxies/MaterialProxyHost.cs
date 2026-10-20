using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>What one live proxy has done, for the proxies route.</summary>
public sealed record MaterialProxyReading(int Handle, string Name, string Material, bool Initialised, long Binds,
                                          IReadOnlyList<string> LastOutput);

/// <summary>Per name: how many live, binds in all and in the last whole frame, and binds stock left unanswered.</summary>
public sealed record MaterialProxyNameReading(string Name, int Live, long Binds, int BindsLastFrame, int Unanswered);

/// <summary>What the proxies route reads of the host (ClientDebugProxies).</summary>
public interface IMaterialProxyReadings
{
    long UnknownBinds { get; }

    long DroppedOps { get; }

    IReadOnlyList<MaterialProxyReading> Read();

    IReadOnlyList<MaterialProxyNameReading> ReadNames();
}

/// <summary>
/// The material proxies native's trampoline asks (plan_client_novalve §2
/// cluster 1, lane c5-proxies): one managed <see cref="MaterialProxy"/> per
/// engine-held proxy, by the handle native issued, made by
/// <see cref="MaterialProxyFactory"/> from the name the material asked for.
///
/// <para>
/// RELOAD (§3.8). Native keeps the proxies and their var and texture slots;
/// this carries, per handle, the block and every answer the proxy's Init got
/// (each FindVar and FindTexture, in order), and the state a bind changes.
/// The new build re-runs each proxy's Init against those answers (no native
/// call: the slots are native's already) and restores the state, so every
/// bound proxy answers through the new code from the next bind.
/// </para>
/// </summary>
public sealed class MaterialProxyHost : IClientMaterialProxies, IMaterialProxyReadings, IReloadParticipant
{
    private readonly IClientMaterialProxyExecutor _exec;
    private readonly IProxyUniform _uniform;
    private readonly Func<bool> _matStub;
    private readonly MaterialProxyFactory _factory;
    private readonly GaussianRandom _gaussian;
    private readonly Dictionary<int, Entry> _proxies = [];
    private readonly Dictionary<string, NameStats> _names = new(StringComparer.Ordinal);
    private readonly BindContext _bind;
    private int _frame = int.MinValue;

    public MaterialProxyHost(IClientMaterialProxyExecutor exec, IEngineHost engine, IEngineConsole console, ILog log,
                             MatStubConVar matStub, IPostProxyValues post, Render.Shadows.IShadowProxySource shadows)
        : this(exec, new EngineUniform(engine),
               () => int.TryParse(console.FindConVarValue("developer"), out int level) && level >= 1, log,
               () => matStub.Value, post, shadows)
    {
    }

    /// <param name="uniform">The engine's uniform stream (<c>random</c>), which randomgaussian draws from too.</param>
    /// <param name="developer">Whether <c>developer</c> is 1 or more.</param>
    /// <param name="matStub">Whether <c>mat_stub</c> is on.</param>
    internal MaterialProxyHost(IClientMaterialProxyExecutor exec, IProxyUniform uniform, Func<bool> developer, ILog log,
                               Func<bool>? matStub = null, IPostProxyValues? post = null,
                               Render.Shadows.IShadowProxySource? shadows = null)
    {
        _matStub = matStub ?? (() => false);
        _exec = exec;
        _uniform = uniform;
        IProxyMath math = new StockProxyMath();
        _factory = new MaterialProxyFactory(log, math, developer, post, shadows);
        _gaussian = new GaussianRandom(math);
        _bind = new BindContext(this);
    }

    private sealed class EngineUniform(IEngineHost engine) : IProxyUniform
    {
        public float RandomFloat(float lo, float hi) => engine.RandomFloat(lo, hi);
    }

    /// <summary>The names this client answers.</summary>
    public IReadOnlyCollection<string> Names => _factory.Names;

    /// <summary>Binds of a handle this build has no proxy for (a refused Init, or a reload that lost one).</summary>
    public long UnknownBinds { get; private set; }

    /// <summary>Setter calls a proxy asked for past what native carries in one bind.</summary>
    public long DroppedOps { get; private set; }

    public bool Init(int handle, string name, string material, IReadOnlyList<ProxyKeyData> keys)
    {
        var entry = new Entry(handle, name, material, keys);
        _proxies[handle] = entry;
        MaterialProxy? proxy = _factory.Create(name);
        if (proxy is null)
            return false;

        var init = new LiveInit(this, entry);
        entry.Initialised = proxy.Init(init);
        entry.Proxy = proxy;
        Stats(name).Live++;
        return entry.Initialised;
    }

    public void Bind(int handle, ProxyBindFrame frame)
    {
        if (!_proxies.TryGetValue(handle, out Entry? entry) || entry.Proxy is null || !entry.Initialised)
        {
            UnknownBinds++;
            return;
        }

        ProxyBindIn input = frame.In;
        if (input.FrameCount != _frame)
        {
            foreach (NameStats s in _names.Values)
            {
                s.LastFrame = s.ThisFrame;
                s.ThisFrame = 0;
            }
            _frame = input.FrameCount;
        }

        _bind.Begin(entry, frame);
        try
        {
            entry.Proxy.Bind(_bind);
        }
        finally
        {
            _bind.End();
        }

        entry.Binds++;
        NameStats stats = Stats(entry.Name);
        stats.Binds++;
        stats.ThisFrame++;
        DroppedOps += frame.Dropped;
        entry.RecordOutput(frame);
    }

    public void Release(int handle)
    {
        if (_proxies.Remove(handle, out Entry? entry) && entry.Proxy is not null)
            Stats(entry.Name).Live--;
    }

    public bool MatStubEnabled() => _matStub();

    /// <summary>The live proxies, for the proxies route.</summary>
    public IReadOnlyList<MaterialProxyReading> Read()
        => _proxies.Values.OrderBy(e => e.Handle)
                   .Select(e => new MaterialProxyReading(e.Handle, e.Name, e.Material, e.Initialised, e.Binds, e.LastOutput))
                   .ToList();

    /// <summary>Per name, for the proxies route.</summary>
    public IReadOnlyList<MaterialProxyNameReading> ReadNames()
        => _factory.Names.OrderBy(n => n, StringComparer.Ordinal)
                   .Select(n =>
                   {
                       NameStats s = Stats(n);
                       int unanswered = _proxies.Values.Where(e => e.Name == n).Sum(e => e.Proxy switch
                       {
                           ToggleTextureProxy t => t.Unanswered,
                           PupilProxy p => p.Unanswered,
                           _ => 0,
                       });
                       return new MaterialProxyNameReading(n, s.Live, s.Binds, s.LastFrame, unanswered);
                   })
                   .ToList();

    private NameStats Stats(string name)
    {
        if (!_names.TryGetValue(name, out NameStats? s))
            _names[name] = s = new NameStats();
        return s;
    }

    public void Save(ReloadWriter writer)
    {
        _gaussian.Save(writer);
        writer.Write(_proxies.Count);
        foreach (Entry e in _proxies.Values.OrderBy(e => e.Handle))
        {
            writer.Write(e.Handle);
            writer.Write(e.Name);
            writer.Write(e.Material);
            writer.Write(e.Keys.Count);
            foreach (ProxyKeyData k in e.Keys)
            {
                writer.Write(k.Name);
                writer.Write(k.Type);
                writer.Write(k.String);
                writer.Write(k.Float);
                writer.Write(k.Int);
                writer.Write(k.ScanCount);
                writer.Write(k.ScanValue);
                writer.Write(k.Atof);
                writer.Write(k.Atoi);
            }
            writer.Write(e.Answers.Count);
            foreach (InitAnswer a in e.Answers)
            {
                writer.Write(a.Texture);
                writer.Write(a.Name);
                writer.Write(a.Complain);
                writer.Write(a.Slot);
                writer.Write(a.Flag);
            }
            writer.Write(e.Initialised);
            writer.Write(e.Binds);
            var state = new ReloadWriter();
            e.Proxy?.SaveState(state);
            writer.Write(state.ToArray());
        }
    }

    public void Restore(ReloadReader reader)
    {
        _gaussian.Restore(reader);
        _proxies.Clear();
        _names.Clear();
        int count = reader.ReadInt();
        for (int i = 0; i < count; i++)
        {
            int handle = reader.ReadInt();
            string name = reader.ReadString();
            string material = reader.ReadString();
            int keyCount = reader.ReadInt();
            var keys = new List<ProxyKeyData>(keyCount);
            for (int k = 0; k < keyCount; k++)
            {
                keys.Add(new ProxyKeyData(reader.ReadString(), reader.ReadInt(), reader.ReadString(), reader.ReadFloat(),
                                          reader.ReadInt(), reader.ReadInt(), reader.ReadFloat(), reader.ReadFloat(),
                                          reader.ReadInt()));
            }
            var entry = new Entry(handle, name, material, keys);
            int answerCount = reader.ReadInt();
            for (int a = 0; a < answerCount; a++)
                entry.Answers.Add(new InitAnswer(reader.ReadBool(), reader.ReadString(), reader.ReadBool(), reader.ReadInt(), reader.ReadBool()));
            bool initialised = reader.ReadBool();
            entry.Binds = reader.ReadInt();
            byte[] state = reader.ReadBytes();

            _proxies[handle] = entry;
            MaterialProxy? proxy = _factory.Create(name);
            if (proxy is null)
                continue;

            // The same Init, answered from what the old build was told.
            var replay = new ReplayInit(entry);
            bool again = proxy.Init(replay);
            entry.Proxy = proxy;
            entry.Initialised = initialised && again && replay.Exact;
            if (state.Length > 0)
                proxy.RestoreState(new ReloadReader(state));
            Stats(name).Live++;
        }
    }

    /// <summary>One FindVar (Texture false) or FindTexture (true) Init made, and what it was told.</summary>
    private sealed record InitAnswer(bool Texture, string Name, bool Complain, int Slot, bool Flag);

    private sealed class NameStats
    {
        public int Live;
        public long Binds;
        public int ThisFrame;
        public int LastFrame;
    }

    private sealed class Entry(int handle, string name, string material, IReadOnlyList<ProxyKeyData> keys)
    {
        public int Handle { get; } = handle;
        public string Name { get; } = name;
        public string Material { get; } = material;
        public IReadOnlyList<ProxyKeyData> Keys { get; } = keys;
        public List<InitAnswer> Answers { get; } = [];
        public MaterialProxy? Proxy { get; set; }
        public bool Initialised { get; set; }
        public int Binds { get; set; }
        public IReadOnlyList<string> LastOutput { get; private set; } = [];

        public ProxyKeyValues KeyValues() => new(Keys.Select(k => new ProxyKey(k.Name, (ProxyKeyType)k.Type, k.String,
                                                                               k.Float, k.Int, k.ScanCount, k.ScanValue,
                                                                               k.Atof, k.Atoi)));

        public void RecordOutput(ProxyBindFrame frame)
        {
            var ops = new string[frame.OpCount];
            for (int i = 0; i < ops.Length; i++)
                ops[i] = Describe(frame.Op(i));
            LastOutput = ops;
        }

        private static string Describe(ProxyOp op)
        {
            var sb = new StringBuilder();
            sb.Append(((ProxyOpKind)op.Kind).ToString()).Append(' ').Append(op.Slot);
            int floats = (ProxyOpKind)op.Kind switch
            {
                ProxyOpKind.Float or ProxyOpKind.VecComponent => 1,
                ProxyOpKind.VecN => Math.Clamp(op.Arg, 0, 4),
                ProxyOpKind.Vec3 => 3,
                ProxyOpKind.Matrix => 16,
                _ => 0,
            };
            if ((ProxyOpKind)op.Kind is ProxyOpKind.Int or ProxyOpKind.VecN or ProxyOpKind.VecComponent or ProxyOpKind.Texture)
                sb.Append(' ').Append(op.Arg);
            for (int i = 0; i < floats; i++)
                sb.Append(' ').Append(op.Value(i).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    /// <summary>Init against the engine: each answer recorded for the reload.</summary>
    private sealed class LiveInit(MaterialProxyHost host, Entry entry) : IProxyInit
    {
        public string MaterialName => entry.Material;

        public ProxyKeyValues Keys { get; } = entry.KeyValues();

        public int FindVar(string name, bool complain, out bool found)
        {
            int slot = host._exec.FindVar(entry.Handle, name, complain, out found);
            entry.Answers.Add(new InitAnswer(false, name, complain, slot, found));
            return slot;
        }

        public int FindTexture(string name, string group, bool complain, out bool isError)
        {
            int slot = host._exec.FindTexture(entry.Handle, name, group, complain, out isError);
            entry.Answers.Add(new InitAnswer(true, name, complain, slot, isError));
            return slot;
        }
    }

    /// <summary>Init again after a reload: the old build's answers, in order; a different question is not exact.</summary>
    private sealed class ReplayInit(Entry entry) : IProxyInit
    {
        private int _next;

        public bool Exact { get; private set; } = true;

        public string MaterialName => entry.Material;

        public ProxyKeyValues Keys { get; } = entry.KeyValues();

        private InitAnswer? Next(bool texture, string name, bool complain)
        {
            if (_next >= entry.Answers.Count)
            {
                Exact = false;
                return null;
            }
            InitAnswer a = entry.Answers[_next++];
            if (a.Texture != texture || a.Name != name || a.Complain != complain)
                Exact = false;
            return a;
        }

        public int FindVar(string name, bool complain, out bool found)
        {
            InitAnswer? a = Next(false, name, complain);
            found = a?.Flag ?? false;
            return a?.Slot ?? -1;
        }

        public int FindTexture(string name, string group, bool complain, out bool isError)
        {
            InitAnswer? a = Next(true, name, complain);
            isError = a?.Flag ?? false;
            return a?.Slot ?? -1;
        }
    }

    /// <summary>One bind's view: the frame's buffers and the engine reads by this proxy's handle.</summary>
    private sealed class BindContext(MaterialProxyHost host) : IProxyBind
    {
        private Entry? _entry;
        private ProxyBindFrame? _frame;
        private ProxyEntity? _entity;

        private Entry E => _entry ?? throw new InvalidOperationException("no bind in progress");
        private ProxyBindFrame F => _frame ?? throw new InvalidOperationException("no bind in progress");

        public void Begin(Entry entry, ProxyBindFrame frame)
        {
            _entry = entry;
            _frame = frame;
            _entity = null;
        }

        public void End()
        {
            _entry = null;
            _frame = null;
            _entity = null;
        }

        public float CurTime => F.In.CurTime;
        public float FrameTime => F.In.FrameTime;
        public int MaxClients => F.In.MaxClients;
        public nint Arg => F.In.Arg;

        public ProxyVar Var(int slot) => F.Var(slot);

        public int Texture(int slot, out string? name) => host._exec.VarTexture(E.Handle, slot, out name);

        public ProxyEntity Entity()
        {
            if (_entity is { } cached)
                return cached;
            if (Arg == 0)
                return (_entity = ProxyEntity.None).Value;
            ProxyEntityRaw raw = host._exec.BindEntity(E.Handle);
            _entity = new ProxyEntity(true, raw.HasUnknown != 0, raw.EntIndex, raw.Origin, raw.RenderAlpha, raw.IsNpc);
            return _entity.Value;
        }

        public Vector ViewOrigin()
        {
            Span<float> v = stackalloc float[3];
            host._exec.ReadView(0, v);
            return new Vector(v[0], v[1], v[2]);
        }

        public void WaterLod(out float start, out float end)
        {
            Span<float> v = stackalloc float[2];
            host._exec.ReadView(1, v);
            start = v[0];
            end = v[1];
        }

        public bool WorldBounds(out Vector mins, out Vector maxs)
        {
            Span<float> v = stackalloc float[6];
            bool ok = host._exec.ReadView(2, v);
            mins = new Vector(v[0], v[1], v[2]);
            maxs = new Vector(v[3], v[4], v[5]);
            return ok;
        }

        public ParticleBindLight ParticleLight()
        {
            Span<float> v = stackalloc float[23];
            host._exec.ReadView(3, v);
            VMatrix m = default;
            for (int i = 0; i < 16; i++)
                m[i] = v[7 + i];
            return new ParticleBindLight(new Vector(v[0], v[1], v[2]), new Vector(v[3], v[4], v[5]), v[6], m);
        }

        public bool PlayerCustomFile(int player, out uint crc) => host._exec.PlayerCustomFile(player, out crc);

        public int FindTexture(string name, string group, bool complain)
            => host._exec.FindTexture(E.Handle, name, group, complain, out _);

        public float RandomFloat(float lo, float hi) => host._uniform.RandomFloat(lo, hi);

        public float RandomGaussian(float mean, float stdDev) => host._gaussian.RandomFloat(host._uniform, mean, stdDev);

        public void NotShipped(string capability, string reason) => host._exec.NotShipped(capability, reason);

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

        public void SetFloat(int slot, float value) => F.Add(ProxyOpKind.Float, slot, 0, [value]);

        public void SetInt(int slot, int value) => F.Add(ProxyOpKind.Int, slot, value, []);

        public void SetVec(int slot, ReadOnlySpan<float> values, int count) => F.Add(ProxyOpKind.VecN, slot, count, values);

        public void SetVec3(int slot, float x, float y, float z) => F.Add(ProxyOpKind.Vec3, slot, 0, [x, y, z]);

        public void SetVecComponent(int slot, float value, int component) => F.Add(ProxyOpKind.VecComponent, slot, component, [value]);

        public void SetMatrix(int slot, in VMatrix matrix) => F.Add(ProxyOpKind.Matrix, slot, 0, matrix);

        public void SetTexture(int slot, int textureSlot) => F.Add(ProxyOpKind.Texture, slot, textureSlot, []);

    }
}
