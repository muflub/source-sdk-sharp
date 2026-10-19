using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="MaterialProxyHost"/> and <see cref="MaterialProxyClientBinding"/>,
/// the way native's trampoline drives them (lane c5-proxies): Init by handle
/// with the block, binds through native's buffers, release, and a reload that
/// carries every proxy's Init answers and bind state without asking native
/// again.
/// </summary>
public unsafe class MaterialProxyHostTests
{
    /// <summary>A material native would answer for: its vars by name, slots handed out in ask order.</summary>
    internal sealed class FakeExecutor : IClientMaterialProxyExecutor
    {
        public readonly HashSet<string> Vars = new(StringComparer.OrdinalIgnoreCase) { "$r", "$basetexture", "$frame", "$v" };
        public readonly Dictionary<string, int> TextureSlots = new(StringComparer.Ordinal);
        public readonly List<string> NotShippedNames = [];
        public int FindVars;
        public int FindTextures;
        public int Frames = 8;
        private int _nextSlot;

        public int FindVar(int handle, string name, bool complain, out bool found)
        {
            FindVars++;
            found = Vars.Contains(name);
            return _nextSlot++;
        }

        public int FindTexture(int handle, string name, string group, bool complain, out bool isError)
        {
            FindTextures++;
            isError = false;
            return TextureSlots.TryGetValue(name, out int slot) ? slot : -1;
        }

        public int VarTexture(int handle, int slot, out string? name)
        {
            name = "tex";
            return Frames;
        }

        public ProxyEntityRaw BindEntity(int handle) => new() { HasUnknown = 1, EntIndex = 3, RenderAlpha = 90, IsNpc = 0 };

        public bool ReadView(int kind, Span<float> values)
        {
            values.Clear();
            return true;
        }

        public bool PlayerCustomFile(int player, out uint crc)
        {
            crc = 0x0a0b0c0d;
            return true;
        }

        public void NotShipped(string capability, string reason) => NotShippedNames.Add(capability);
    }

    internal sealed class QueueUniform(params float[] draws) : IProxyUniform
    {
        private readonly Queue<float> _draws = new(draws);
        public int Drawn;

        public float RandomFloat(float lo, float hi)
        {
            Drawn++;
            return _draws.Count > 0 ? _draws.Dequeue() : lo;
        }
    }

    private static ProxyKeyData Key(string name, string text) => new(name, 1, text, 0, 0, 0, 0, 0, 0);

    private static ProxyKeyData Num(string name, float value) => new(name, 3, "", value, 0, 0, 0, 0, 0);

    /// <summary>One bind through native-shaped buffers; the ops it asked for.</summary>
    internal static List<ProxyOp> Bind(IClientMaterialProxies host, int handle, float curTime, int vars, ProxyVar seed,
                                       nint arg = 0, int maxOps = 16, int frame = 1)
    {
        var input = new ProxyBindIn { CurTime = curTime, FrameTime = 0.1f, MaxClients = 16, FrameCount = frame, VarCount = vars, Arg = arg };
        ProxyVar* v = stackalloc ProxyVar[Math.Max(vars, 1)];
        for (int i = 0; i < vars; i++)
            v[i] = seed;
        ProxyOp* ops = stackalloc ProxyOp[maxOps];
        var f = new ProxyBindFrame();
        f.Begin(&input, v, ops, maxOps);
        host.Bind(handle, f);
        var result = new List<ProxyOp>();
        for (int i = 0; i < f.OpCount; i++)
            result.Add(f.Op(i));
        f.End();
        return result;
    }

    private static MaterialProxyHost Host(FakeExecutor exec, IProxyUniform? uniform = null)
        => new(exec, uniform ?? new QueueUniform(), () => false, new StandardErrorLog());

    [Fact]
    public void ABindWritesTheSetterCallsTheProxyDecidedIntoNativesBuffer()
    {
        var host = Host(new FakeExecutor());
        Assert.True(host.Init(7, "Sine", "m", [Key("resultVar", "$r"), Num("sinePeriod", 2)]));

        // sin( 2 pi * 0.5 / 2 ) * 0.5 + 0.5 = 1: SetFloatValue( 1 ), and nothing
        // after it: stock's tool record (op kind 8) is deleted, SFM is dead
        // (user ruling 2026-10-19).
        List<ProxyOp> ops = Bind(host, 7, 0.5f, 1, new ProxyVar { Type = (int)MaterialVarType.Float });

        Assert.Single(ops);
        Assert.Equal((int)ProxyOpKind.Float, ops[0].Kind);
        Assert.Equal(0, ops[0].Slot);
        Assert.Equal(1.0f, ops[0].Value(0));
        Assert.DoesNotContain(ops, o => o.Kind == 8);
        Assert.Equal(1, host.ReadNames().Single(n => n.Name == "Sine").Binds);
    }

    [Fact]
    public void AnUnknownHandleOrARefusedInitBindsNothingAndIsCounted()
    {
        var exec = new FakeExecutor();
        var host = Host(exec);
        Assert.False(host.Init(1, "Sine", "m", [Key("resultVar", "$missing")]));
        Assert.False(host.Init(2, "PlayerProximity", "m", []));

        Assert.Empty(Bind(host, 1, 0, 1, default));
        Assert.Empty(Bind(host, 2, 0, 1, default));
        Assert.Empty(Bind(host, 99, 0, 1, default));
        Assert.Equal(3, host.UnknownBinds);
    }

    [Fact]
    public void OpsPastNativesBufferAreDroppedAndCounted()
    {
        var host = Host(new FakeExecutor());
        Assert.True(host.Init(3, "Sine", "m", [Key("resultVar", "$r")]));

        // Sine asks one setter call; a buffer of none drops it. (It asked two
        // until the tools' record, the second, was deleted: SFM is dead, user
        // ruling 2026-10-19.)
        List<ProxyOp> ops = Bind(host, 3, 0.25f, 1, new ProxyVar { Type = (int)MaterialVarType.Float }, maxOps: 0);

        Assert.Empty(ops);
        Assert.Equal(1, host.DroppedOps);
    }

    [Fact]
    public void BindsPerFrameAreCountedPerName()
    {
        var host = Host(new FakeExecutor());
        Assert.True(host.Init(4, "Sine", "m", [Key("resultVar", "$r")]));
        Assert.True(host.Init(5, "Sine", "m2", [Key("resultVar", "$r")]));

        Bind(host, 4, 0, 1, default, frame: 10);
        Bind(host, 5, 0, 1, default, frame: 10);
        Bind(host, 4, 0, 1, default, frame: 11);

        MaterialProxyNameReading sine = host.ReadNames().Single(n => n.Name == "Sine");
        Assert.Equal(2, sine.Live);
        Assert.Equal(3, sine.Binds);
        Assert.Equal(2, sine.BindsLastFrame);

        host.Release(5);
        Assert.Equal(1, host.ReadNames().Single(n => n.Name == "Sine").Live);
        Assert.Single(host.Read());
    }

    [Fact]
    public void AReloadReplaysEveryInitFromItsAnswersAndCarriesTheBindState()
    {
        // Before: a Sine, a spray decal whose spray arrived (its cache), and a
        // GaussianNoise whose first draw left the second of the pair pending.
        var oldExec = new FakeExecutor();
        oldExec.TextureSlots[PlayerLogoProxy.DefaultDecal] = 4;
        oldExec.TextureSlots[PlayerLogoProxy.TextureName(0x0a0b0c0d)] = 9;
        var oldHost = Host(oldExec, new QueueUniform(0.75f, 0.25f));
        Assert.True(oldHost.Init(10, "Sine", "m", [Key("resultVar", "$r"), Num("sinePeriod", 3)]));
        Assert.True(oldHost.Init(11, "PlayerLogo", "decals/playerlogo01", []));
        Assert.True(oldHost.Init(12, "GaussianNoise", "m", [Key("resultVar", "$r")]));
        var floatVar = new ProxyVar { Type = (int)MaterialVarType.Float };
        Assert.Equal((int)ProxyOpKind.Texture, Bind(oldHost, 11, 1, 1, default, arg: 2)[0].Kind);
        Bind(oldHost, 12, 1, 1, floatVar);

        var writer = new ReloadWriter();
        oldHost.Save(writer);

        var newExec = new FakeExecutor();
        var newUniform = new QueueUniform();
        var newHost = Host(newExec, newUniform);
        var reader = new ReloadReader(writer.ToArray());
        newHost.Restore(reader);

        Assert.True(reader.AtEnd, "the host read back every byte it wrote");
        Assert.Equal(0, newExec.FindVars + newExec.FindTextures);

        // The same answers as the old build for the same bind.
        List<ProxyOp> before = Bind(oldHost, 10, 1.25f, 1, floatVar);
        List<ProxyOp> after = Bind(newHost, 10, 1.25f, 1, floatVar);
        Assert.Equal(before.Select(o => (o.Kind, o.Slot, o.Value(0))), after.Select(o => (o.Kind, o.Slot, o.Value(0))));

        // The spray is still cached: the player's texture, no lookup.
        List<ProxyOp> spray = Bind(newHost, 11, 2, 1, default, arg: 2);
        Assert.Equal((int)ProxyOpKind.Texture, spray[0].Kind);
        Assert.Equal(9, spray[0].Arg);
        Assert.Equal(0, newExec.FindTextures);

        // The Gaussian's pending value crossed: no draw, the old build's next answer.
        float oldNext = Bind(oldHost, 12, 2, 1, floatVar)[0].Value(0);
        float newNext = Bind(newHost, 12, 2, 1, floatVar)[0].Value(0);
        Assert.Equal(0, newUniform.Drawn);
        Assert.Equal(oldNext, newNext);
        Assert.Equal(3, newHost.ReadNames().Sum(n => n.Live));
    }

    [Fact]
    public void MatStubAnswersTheConVarEachFrame()
    {
        bool on = false;
        var host = new MaterialProxyHost(new FakeExecutor(), new QueueUniform(), () => false, new StandardErrorLog(), () => on);
        var binding = new MaterialProxyClientBinding(new Crossings(), new StandardErrorLog(), host);

        Assert.Equal(0, binding.MatStubEnabled());
        on = true;
        Assert.Equal(1, binding.MatStubEnabled());
    }

    [Fact]
    public void TheBindingHandsInitTheBlockAndBindNativesBuffers()
    {
        var exec = new FakeExecutor();
        var host = Host(exec);
        var binding = new MaterialProxyClientBinding(new Crossings(), new StandardErrorLog(), host);

        nint name = Marshal.StringToCoTaskMemUTF8("Sine");
        nint material = Marshal.StringToCoTaskMemUTF8("models/x");
        nint keyName = Marshal.StringToCoTaskMemUTF8("resultVar");
        nint keyText = Marshal.StringToCoTaskMemUTF8("$r");
        try
        {
            var key = new ProxyKeyRaw { Name = (byte*)keyName, String = (byte*)keyText, Type = 1 };
            Assert.Equal(1, binding.ProxyInit(5, (byte*)name, (byte*)material, &key, 1));
            Assert.Equal(1, exec.FindVars);

            var input = new ProxyBindIn { CurTime = 0.5f, FrameCount = 1, VarCount = 1 };
            var v = new ProxyVar { Type = (int)MaterialVarType.Float };
            ProxyOp* ops = stackalloc ProxyOp[4];
            Assert.Equal(1, binding.ProxyBind(5, &input, &v, ops, 4));
            Assert.Equal((int)ProxyOpKind.Float, ops[0].Kind);
            Assert.Equal(0, binding.ProxyBind(5, null, &v, ops, 4));
            Assert.Equal("models/x", host.Read().Single().Material);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
            Marshal.FreeCoTaskMem(material);
            Marshal.FreeCoTaskMem(keyName);
            Marshal.FreeCoTaskMem(keyText);
        }
    }
}
