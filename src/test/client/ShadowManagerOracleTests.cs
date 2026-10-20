using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Render.Shadows;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed shadow manager (<see cref="ShadowManager"/>, its allocator and
/// its two proxies) against stock's CClientShadowMgr, run offline from the
/// pristine clientshadowmgr.cpp by
/// src/test/oracle/native/shadowmgr_oracle_test.cpp
/// (<c>Fixtures/shadowmgr-stock.tsv</c>, README beside it). S1 of
/// docs/plans/c7_shadow_manager.md.
///
/// <para>
/// THE GOLDEN CARRIES ITS OWN STIMULUS: every thing is described (T) before it
/// is used, every setting (CV, ENV) before it is read, every call (C) before
/// it is made. So the replay here builds the same world, answers as the
/// README says the stand-ins answer, makes the same calls on the managed
/// manager, and writes what stock wrote: every call into the engine, the leaf
/// system, the renderables, the materials and the render context (E), every
/// call's answer (R) and every console line (M), floats as bits.
/// </para>
///
/// <para>
/// The two logs are compared between the scenario's stimulus rows (which the
/// replay echoes): within one stretch, each kind of row in stock's order, and
/// also every row in stock's order. The questions (Q) are the stand-ins'
/// answers, which the replay gives through its own seams; stock's assertions
/// (A, AL) are not the manager's.
/// </para>
/// </summary>
public class ShadowManagerOracleTests
{
    private const int Invalid = 0xFFFF;

    // The rows the replay acts on and echoes: the scenario's stimulus.
    private static readonly HashSet<string> Anchors = ["X", "ENV", "F", "CV", "T", "RCV", "C", "END"];

    // Rows only stock writes: its stand-ins' answers and its assertions.
    private static readonly HashSet<string> NotCompared = ["Q", "A", "AL", "L"];

    internal static string[] Golden()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "shadowmgr-stock.tsv");
        Assert.True(File.Exists(path), path + " is missing");
        return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').ToArray();
    }

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static float H(string s) => BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    internal static string X(float f) => unchecked((uint)BitConverter.SingleToInt32Bits(f)).ToString("x8", CultureInfo.InvariantCulture);

    private static Vector Vec(string s)
    {
        string[] p = s.Split(',');
        return new Vector(H(p[0]), H(p[1]), H(p[2]));
    }

    private static QAngle Ang(string s)
    {
        string[] p = s.Split(',');
        return new QAngle(H(p[0]), H(p[1]), H(p[2]));
    }

    internal static string V(in Vector v) => X(v.X) + "," + X(v.Y) + "," + X(v.Z);

    internal static string A(in QAngle a) => X(a.Pitch) + "," + X(a.Yaw) + "," + X(a.Roll);

    internal static string M(in ShadowMatrix m)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
            sb.Append(i == 0 ? "" : ",").Append(X(m[i]));
        return sb.ToString();
    }

    private static string Leaves(ReadOnlySpan<int> leaves)
    {
        var sb = new StringBuilder();
        sb.Append(leaves.Length.ToString(CultureInfo.InvariantCulture)).Append(':');
        for (int i = 0; i < leaves.Length; i++)
            sb.Append(i == 0 ? "" : ",").Append(leaves[i].ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static int[] ParseLeaves(string s)
    {
        string list = s[(s.IndexOf(':') + 1)..];
        return list.Length == 0 ? [] : list.Split(',').Select(I).ToArray();
    }

    internal sealed class Thing
    {
        public int Id;
        public bool HasModel;
        public int ModelType;
        public Vector Origin, Mins, Maxs, RttMins, RttMaxs, AbsMins, AbsMaxs, CastDirection;
        public QAngle Angles;
        public ShadowMatrix3x4 Transform;
        public bool CastDistanceFlag, CastDirectionFlag;
        public float CastDistance;
        public int Parent, FirstChild, NextPeer, ReceiveFlags, ShadowHandle, Instance, Body, Skin;
        public bool Dirty, BonesOk, DirtyOnOrigin, Gone;

        public nint Pointer => (nint)(0x10000000L + Id * 0x100L);
    }

    private sealed class EngineShadow
    {
        public bool Live;
        public ShadowEngineInfo Info;
        public ShadowFrustum Frustum;
        public int NextFree = -1;
    }

    private sealed class LeafShadow
    {
        public int UserId;
        public int Flags;
        public int[] Leaves = [];
        public bool Live = true;
    }

    /// <summary>A ConVar as tier1 keeps it: its string, float and int; a change the manager makes is an E row.</summary>
    private sealed class Var(string value)
    {
        public string Text = value;
        public int Int = (int)ParseFloat(value);
        public float Float = ParseFloat(value);

        private static float ParseFloat(string s) => (float)double.Parse(s, CultureInfo.InvariantCulture);
    }

    /// <summary>The scenario's world, every seam the manager is built with, and the leaf system that calls it back.</summary>
    internal sealed class Replay : IShadowEngine, IShadowMaterials, IShadowRenderContext, IShadowWorld, IShadowRenderables, IShadowDebugOverlay,
                                   IShadowLeafSystem, IShadowSettings, IShadowConsole, IShadowProxySource
    {
        public readonly List<string> Log = [];
        public ShadowManager Manager;

        private readonly Dictionary<int, Thing> _things = [];
        private readonly Dictionary<nint, Thing> _byPointer = [];
        private readonly List<(int Leaf, int Thing, int Kind)> _receivers = [];
        private readonly List<EngineShadow> _engineShadows = [];
        private int _engineFirstFree = -1;
        private readonly List<LeafShadow> _leafShadows = [];
        private readonly List<string> _resourceNames = ["-"];
        private readonly List<int> _resourceWidths = [0];
        private readonly Dictionary<string, Var> _vars = new(StringComparer.Ordinal)
        {
            ["r_flashlightdrawfrustum"] = new("0"),
            ["r_flashlightmodels"] = new("1"),
            ["r_shadowrendertotexture"] = new("0"),
            ["r_flashlight_version2"] = new("0"),
            ["r_flashlightdepthtexture"] = new("1"),
            ["r_flashlightdepthres"] = new("1024"),
            ["r_threaded_client_shadow_manager"] = new("0"),
            ["r_shadows"] = new("1"),
            ["r_shadowmaxrendered"] = new("32"),
            ["r_shadows_gamecontrol"] = new("-1"),
            ["cl_drawshadowtexture"] = new("0"),
            ["cl_shadowtextureoverlaysize"] = new("256"),
        };
        private readonly Dictionary<string, MaterialProxy> _proxies = [];
        private readonly Dictionary<string, List<string>> _proxyVars = [];

        private int _leafCount;
        private float _leafWidth;
        private Vector _viewMins, _viewMaxs, _ambient;
        private float _ppu;
        private int _dxLevel = 90;
        private bool _supportsDepth = true;
        private int _maxRenderTarget = 2048;
        private int _fog;
        private int _heightClip;
        private bool _tools;
        private int _frame;
        private int _redirtyDepth;

        public Replay()
        {
            Manager = NewManager();
        }

        private ShadowManager NewManager() => new(this, this, this, this, this, this, this, this, this);

        /// <summary>A hot reload: the manager saved, a new one built, and the save restored into it.</summary>
        public int Reload()
        {
            var writer = new SourceSharp.Client.ReloadWriter();
            Manager.Save(writer);
            byte[] bytes = writer.ToArray();
            Manager = NewManager();
            var reader = new SourceSharp.Client.ReloadReader(bytes);
            Manager.Restore(reader);
            Assert.True(reader.AtEnd, "the restore left bytes unread");
            return bytes.Length;
        }

        private void Emit(params object[] fields) => Log.Add(string.Join('\t', fields.Select(f => Convert.ToString(f, CultureInfo.InvariantCulture))));

        private Thing ThingOf(nint p) => _byPointer.TryGetValue(p, out Thing? t) ? t : throw new InvalidOperationException($"no thing at {p:X}");

        private string R(nint p) => p == 0 ? "-" : "r" + ThingOf(p).Id.ToString(CultureInfo.InvariantCulture);

        private static string Mo(nint model) => model == 0 ? "-" : "m" + ((model - 0x1000) / 16).ToString(CultureInfo.InvariantCulture);

        private string Res(int id) => id <= 0 || id >= _resourceNames.Count ? "-" : _resourceNames[id];

        private int Resource(string name, int width)
        {
            _resourceNames.Add(name);
            _resourceWidths.Add(width);
            return _resourceNames.Count - 1;
        }

        //---------------------------------------------------------------------
        // The stimulus.
        //---------------------------------------------------------------------

        public void Run(string line)
        {
            string[] f = line.Split('\t');
            string kind = f[0];
            if (Anchors.Contains(kind))
                Log.Add(line);
            switch (kind)
            {
                case "T": DescribeThing(f); break;
                case "ENV": SetEnv(f); break;
                case "F": _frame = I(f[1]); break;
                case "CV":
                    _vars[f[1]] = new Var(f[2]);
                    break;
                case "RCV": _receivers.Add((I(f[1]), I(f[2]), I(f[3]))); break;
                case "C": Call(f); break;
            }
        }

        private void DescribeThing(string[] f)
        {
            int id = I(f[1]);
            if (!_things.TryGetValue(id, out Thing? t))
            {
                t = new Thing { Id = id };
                _things[id] = t;
                _byPointer[t.Pointer] = t;
            }
            t.HasModel = f[2] == "1";
            t.ModelType = I(f[3]);
            t.Origin = Vec(f[4]);
            t.Angles = Ang(f[5]);
            t.Mins = Vec(f[6]);
            t.Maxs = Vec(f[7]);
            t.RttMins = Vec(f[8]);
            t.RttMaxs = Vec(f[9]);
            t.AbsMins = Vec(f[10]);
            t.AbsMaxs = Vec(f[11]);
            string[] m = f[12].Split(',');
            for (int i = 0; i < 12; i++)
                t.Transform[i] = H(m[i]);
            t.CastDistanceFlag = f[13] == "1";
            t.CastDistance = H(f[14]);
            t.CastDirectionFlag = f[15] == "1";
            t.CastDirection = Vec(f[16]);
            t.Parent = I(f[17]);
            t.FirstChild = I(f[18]);
            t.NextPeer = I(f[19]);
            t.ReceiveFlags = I(f[20]);
            t.ShadowHandle = I(f[21]);
            t.Dirty = f[22] == "1";
            t.Instance = I(f[23]);
            t.Body = I(f[24]);
            t.Skin = I(f[25]);
            t.BonesOk = f[26] == "1";
            t.DirtyOnOrigin = f[27] == "1";
            t.Gone = f[28] == "1";
        }

        private void SetEnv(string[] f)
        {
            switch (f[1])
            {
                case "leaves": _leafCount = I(f[2]); _leafWidth = H(f[3]); break;
                case "viewbox": _viewMins = Vec(f[2]); _viewMaxs = Vec(f[3]); break;
                case "ambient": _ambient = Vec(f[2]); break;
                case "ppu": _ppu = H(f[2]); break;
                case "dxlevel": _dxLevel = I(f[2]); break;
                case "supportsdepth": _supportsDepth = f[2] == "1"; break;
                case "maxrendertarget": _maxRenderTarget = I(f[2]); break;
                case "fog": _fog = I(f[2]); break;
                case "heightclip": _heightClip = I(f[2]); break;
                case "tools": _tools = f[2] == "1"; break;
                default: throw new InvalidOperationException("an ENV row of kind " + f[1]);
            }
        }

        private int _spotlightTexture;

        private FlashlightState State(string[] f, int at)
        {
            Vector origin = Vec(f[at]);
            string[] q = f[at + 1].Split(',');
            string[] att = f[at + 6].Split(',');
            string[] col = f[at + 7].Split(',');
            if (_spotlightTexture == 0)
                _spotlightTexture = Resource(f[at + 8], 128);
            return new FlashlightState
            {
                OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z,
                OrientationX = H(q[0]), OrientationY = H(q[1]), OrientationZ = H(q[2]), OrientationW = H(q[3]),
                NearZ = H(f[at + 2]), FarZ = H(f[at + 3]),
                HorizontalFovDegrees = H(f[at + 4]), VerticalFovDegrees = H(f[at + 5]),
                QuadraticAtten = H(att[0]), LinearAtten = H(att[1]), ConstantAtten = H(att[2]),
                ColorR = H(col[0]), ColorG = H(col[1]), ColorB = H(col[2]), ColorA = H(col[3]),
                Texture = Res(_spotlightTexture) == f[at + 8] ? _spotlightTexture : throw new InvalidOperationException("a second spotlight texture"),
                EnableShadows = I(f[at + 9]),
                TextureFrame = I(f[at + 10]),
                ShadowMapResolution = H(f[at + 11]),
                ShadowAtten = H(f[at + 12]),
                ShadowSlopeScaleDepthBias = H(f[at + 13]),
                ShadowDepthBias = H(f[at + 14]),
            };
        }

        private string StateText(in FlashlightState s) =>
            string.Join('\t', V(new Vector(s.OriginX, s.OriginY, s.OriginZ)),
                        X(s.OrientationX) + "," + X(s.OrientationY) + "," + X(s.OrientationZ) + "," + X(s.OrientationW), X(s.NearZ), X(s.FarZ),
                        X(s.HorizontalFovDegrees), X(s.VerticalFovDegrees), X(s.QuadraticAtten) + "," + X(s.LinearAtten) + "," + X(s.ConstantAtten),
                        X(s.ColorR) + "," + X(s.ColorG) + "," + X(s.ColorB) + "," + X(s.ColorA), Res(s.Texture),
                        s.EnableShadows.ToString(CultureInfo.InvariantCulture), s.TextureFrame.ToString(CultureInfo.InvariantCulture),
                        X(s.ShadowMapResolution), X(s.ShadowAtten), X(s.ShadowSlopeScaleDepthBias), X(s.ShadowDepthBias));

        private static ShadowViewSetup View(string[] f, int at) => new()
        {
            Origin = Vec(f[at]), Angles = Ang(f[at + 1]), ZNear = H(f[at + 2]), ZFar = H(f[at + 3]), Fov = H(f[at + 4]), AspectRatio = H(f[at + 5]),
        };

        private uint EntityOf(int id) => (uint)id | (1u << 16);

        private void Call(string[] f)
        {
            ShadowManager m = Manager;
            switch (f[1])
            {
                case "Init": Emit("R", m.Init() ? 1 : 0); break;
                case "Shutdown": m.Shutdown(); break;
                case "LevelInitPreEntity": m.LevelInitPreEntity(); break;
                case "LevelShutdownPostEntity": m.LevelShutdownPostEntity(); break;
                case "PreRender": m.PreRender(); break;
                case "AdvanceFrame": m.AdvanceFrame(); break;
                case "CreateShadow": Emit("R", m.CreateShadow(EntityOf(I(f[2])), I(f[3]))); break;
                case "DestroyShadow": m.DestroyShadow(I(f[2])); break;
                case "CreateFlashlight": Emit("R", m.CreateFlashlight(State(f, 2))); break;
                case "UpdateFlashlightState": m.UpdateFlashlightState(I(f[2]), State(f, 3)); break;
                case "DestroyFlashlight": m.DestroyFlashlight(I(f[2])); break;
                case "UpdateProjectedTexture": m.UpdateProjectedTexture(I(f[2]), f[3] == "1"); break;
                case "AddToDirtyShadowListHandle": m.AddToDirtyShadowList(I(f[2]), f[3] == "1"); break;
                case "AddToDirtyShadowList": m.AddToDirtyShadowList(_things[I(f[2])].Pointer, f[3] == "1"); break;
                case "MarkRenderToTextureShadowDirty": m.MarkRenderToTextureShadowDirty(I(f[2])); break;
                case "AddShadowToReceiver": m.AddShadowToReceiver(I(f[2]), _things[I(f[3])].Pointer, I(f[4])); break;
                case "RemoveAllShadowsFromReceiver": m.RemoveAllShadowsFromReceiver(_things[I(f[2])].Pointer, I(f[3])); break;
                case "ComputeShadowTextures": m.ComputeShadowTextures(ParseLeaves(f[8])); break;
                case "ComputeShadowDepthTextures": m.ComputeShadowDepthTextures(View(f, 2)); break;
                case "UnlockAllShadowDepthTextures": m.UnlockAllShadowDepthTextures(); break;
                case "RenderShadowTexture": m.RenderShadowTexture(I(f[2]), I(f[3])); break;
                case "SetShadowDirection": m.SetShadowDirection(Vec(f[2])); break;
                case "GetShadowDirection": Emit("R", V(m.GetShadowDirection())); break;
                case "SetShadowColor": m.SetShadowColor((byte)I(f[2]), (byte)I(f[3]), (byte)I(f[4])); break;
                case "SetShadowDistance": m.SetShadowDistance(H(f[2])); break;
                case "SetShadowBlobbyCutoffArea": m.SetShadowBlobbyCutoffArea(H(f[2])); break;
                case "SetFalloffBias": m.SetFalloffBias(I(f[2]), (byte)I(f[3])); break;
                case "SetFlashlightLightWorld": m.SetFlashlightLightWorld(I(f[2]), f[3] == "1"); break;
                case "SetFlashlightTarget": m.SetFlashlightTarget(I(f[2]), f.Length > 3 ? EntityOf(I(f[3])) : ShadowManager.InvalidEntity); break;
                case "SetShadowsDisabled": m.SetShadowsDisabled(f[2] == "1"); break;
                case "Restore": m.RestoreRenderState(); break;
                case "Command": Command(f[2]); break;
                case "ProxyInit": ProxyInit(f[2], f.Length > 3 && f[3].Length > 0 ? f[3].Split(',').ToList() : []); break;
                case "ProxyBind": ProxyBind(f[2], I(f[3])); break;
                default: throw new InvalidOperationException("a call " + f[1]);
            }
        }

        private void Command(string line)
        {
            string[] args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            ShadowManager m = Manager;
            switch (args[0])
            {
                case "r_shadowdir": m.CommandShadowDir(args); break;
                case "r_shadowangles": m.CommandShadowAngles(args); break;
                case "r_shadowcolor": m.CommandShadowColor(args); break;
                case "r_shadowdist": m.CommandShadowDist(args); break;
                case "r_shadowblobbycutoff": m.CommandShadowBlobbyCutoff(args); break;
                case "r_shadowbreak": m.CommandShadowBreak(); break;
                default: throw new InvalidOperationException("a command " + args[0]);
            }
        }

        //---------------------------------------------------------------------
        // The proxies, as the material system makes and binds them.
        //---------------------------------------------------------------------

        private sealed class ProxyInitOver(List<string> vars) : IProxyInit
        {
            public string MaterialName => "shadow";

            public ProxyKeyValues Keys { get; } = new([]);

            public int FindVar(string name, bool complain, out bool found)
            {
                int i = vars.IndexOf(name);
                found = i >= 0;
                return i >= 0 ? i : 1000;
            }

            public int FindTexture(string name, string group, bool complain, out bool isError) => throw new InvalidOperationException("Init asked for a texture");
        }

        private sealed class ProxyBindOver(Replay replay, List<string> vars, nint arg) : IProxyBind
        {
            private string Name(int slot) => slot >= 0 && slot < vars.Count ? vars[slot] : "-";

            public float CurTime => 0;
            public float FrameTime => 0;
            public int MaxClients => 1;
            public nint Arg => arg;

            public ProxyVar Var(int slot) => throw new InvalidOperationException();
            public int Texture(int slot, out string? name) => throw new InvalidOperationException();
            public ProxyEntity Entity() => throw new InvalidOperationException();
            public Vector ViewOrigin() => throw new InvalidOperationException();
            public void WaterLod(out float start, out float end) => throw new InvalidOperationException();
            public bool WorldBounds(out Vector mins, out Vector maxs) => throw new InvalidOperationException();
            public ParticleBindLight ParticleLight() => throw new InvalidOperationException();
            public bool PlayerCustomFile(int player, out uint crc) => throw new InvalidOperationException();

            public int FindTexture(string name, string group, bool complain)
            {
                Assert.Equal("_rt_Shadows", name);
                return 1;
            }

            public float RandomFloat(float lo, float hi) => throw new InvalidOperationException();
            public float RandomGaussian(float mean, float stdDev) => throw new InvalidOperationException();
            public void NotShipped(string capability, string reason) => throw new InvalidOperationException();
            public bool TextureFrameIndex(out int frame) => throw new InvalidOperationException();
            public bool EyeLighting(out Vector eye, out Vector forward, out Vector lighting) => throw new InvalidOperationException();

            public void SetFloat(int slot, float value) => replay.Emit("E", "var.SetFloatValue", Name(slot), X(value));
            public void SetInt(int slot, int value) => replay.Emit("E", "var.SetIntValue", Name(slot), value);

            public void SetVec(int slot, ReadOnlySpan<float> values, int count)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                    sb.Append(i == 0 ? "" : ",").Append(X(values[i]));
                replay.Emit("E", "var.SetVecValue", Name(slot), count, sb.ToString());
            }

            public void SetVec3(int slot, float x, float y, float z) => replay.Emit("E", "var.SetVecValue3", Name(slot));
            public void SetVecComponent(int slot, float value, int component) => throw new InvalidOperationException();

            public void SetMatrix(int slot, in VMatrix matrix)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < 16; i++)
                    sb.Append(i == 0 ? "" : ",").Append(X(matrix[i]));
                replay.Emit("E", "var.SetMatrixValue", Name(slot), sb.ToString());
            }

            public void SetTexture(int slot, int textureSlot) => replay.Emit("E", "var.SetTextureValue", Name(slot), textureSlot == 1 ? "_rt_Shadows" : "-");
        }

        private void ProxyInit(string name, List<string> vars)
        {
            MaterialProxy proxy = name == "Shadow" ? new ShadowProxy(this) : new ShadowModelProxy(this);
            bool ok = proxy.Init(new ProxyInitOver(vars));
            Emit("R", ok ? 1 : 0);
            _proxies[name] = proxy;
            _proxyVars[name] = vars;
        }

        private void ProxyBind(string name, int handle) => _proxies[name].Bind(new ProxyBindOver(this, _proxyVars[name], handle));

        // The proxies reach whichever manager the replay holds now (a reload replaces it).
        public int AtlasPage => Manager.AtlasPage;

        public bool TryGetShadowInfo(int clientShadowHandle, out ShadowEngineInfo info) => Manager.TryGetShadowInfo(clientShadowHandle, out info);

        //---------------------------------------------------------------------
        // IShadowSettings and IShadowConsole.
        //---------------------------------------------------------------------

        private void SetVar(string name, int value)
        {
            Var v = _vars[name];
            if (v.Int == value)
                return;
            v.Int = value;
            v.Float = value;
            v.Text = value.ToString(CultureInfo.InvariantCulture);
            Emit("E", "cvar.Set", name, value);
        }

        public bool RenderToTexture => _vars["r_shadowrendertotexture"].Int != 0;
        public bool FlashlightDepthTexture => _vars["r_flashlightdepthtexture"].Int != 0;
        public void SetFlashlightDepthTexture(int value) => SetVar("r_flashlightdepthtexture", value);
        public int FlashlightDepthResolution => _vars["r_flashlightdepthres"].Int;
        public void SetFlashlightDepthResolution(int value) => SetVar("r_flashlightdepthres", value);
        public int Shadows => _vars["r_shadows"].Int;
        public int ShadowMaxRendered => _vars["r_shadowmaxrendered"].Int;
        public int ShadowsGameControl => _vars["r_shadows_gamecontrol"].Int;
        public void SetShadowsGameControl(int value) => SetVar("r_shadows_gamecontrol", value);
        public bool FlashlightModels => _vars["r_flashlightmodels"].Int != 0;
        public int FlashlightVersion2 => _vars["r_flashlight_version2"].Int;
        public bool FlashlightDrawFrustum => _vars["r_flashlightdrawfrustum"].Int != 0;
        public int DrawShadowTexture => _vars["cl_drawshadowtexture"].Int;

        public void Msg(string text) => Emit("M", 0, text);

        public void Warning(string text) => Emit("M", 1, text);

        //---------------------------------------------------------------------
        // IShadowEngine: the engine's shadow manager, keeping what it is told.
        //---------------------------------------------------------------------

        public int CreateShadowEx(int material, int modelMaterial, int bindProxy, int creationFlags)
        {
            int h;
            if (_engineFirstFree >= 0)
            {
                h = _engineFirstFree;
                _engineFirstFree = _engineShadows[h].NextFree;
            }
            else
            {
                h = _engineShadows.Count;
                _engineShadows.Add(new EngineShadow());
            }
            var s = new EngineShadow { Live = true };
            s.Info.TexSizeX = 1.0f;
            s.Info.TexSizeY = 1.0f;
            _engineShadows[h] = s;
            Emit("E", "sm.CreateShadowEx", Res(material), Res(modelMaterial), bindProxy, creationFlags, "=>" + h.ToString(CultureInfo.InvariantCulture));
            return h;
        }

        public void DestroyShadow(int shadow)
        {
            Emit("E", "sm.DestroyShadow", shadow);
            _engineShadows[shadow].Live = false;
            _engineShadows[shadow].NextFree = _engineFirstFree;
            _engineFirstFree = shadow;
        }

        public void SetShadowMaterial(int shadow, int material, int modelMaterial, int bindProxy) =>
            Emit("E", "sm.SetShadowMaterial", shadow, Res(material), Res(modelMaterial), bindProxy);

        public void ProjectShadow(int shadow, in Vector origin, in Vector projectionDir, in ShadowMatrix worldToShadow, float sizeX, float sizeY,
                                  ReadOnlySpan<int> leaves, float maxHeight, float falloffOffset, float falloffAmount, in Vector casterOrigin)
        {
            Emit("E", "sm.ProjectShadow", shadow, V(origin), V(projectionDir), M(worldToShadow), X(sizeX) + "," + X(sizeY), Leaves(leaves),
                 X(maxHeight), X(falloffOffset), X(falloffAmount), V(casterOrigin));
            ref ShadowEngineInfo i = ref _engineShadows[shadow].Info;
            i.WorldToShadow = worldToShadow;
            i.MaxDist = maxHeight;
            i.FalloffOffset = falloffOffset;
            i.FalloffAmount = falloffAmount;
        }

        public void ProjectFlashlight(int shadow, in ShadowMatrix worldToShadow, ReadOnlySpan<int> leaves)
        {
            Emit("E", "sm.ProjectFlashlight", shadow, M(worldToShadow), Leaves(leaves));
            EngineShadow s = _engineShadows[shadow];
            s.Info.WorldToShadow = worldToShadow;
            ShadowMath.AABBFromProjectionMatrix(worldToShadow, out Vector mins, out Vector maxs);
            s.Frustum[0] = ShadowMath.Plane(0, new Vector(1, 0, 0), mins.X);
            s.Frustum[1] = ShadowMath.Plane(1, new Vector(0, 1, 0), mins.Y);
            s.Frustum[2] = ShadowMath.Plane(2, new Vector(0, 0, 1), mins.Z);
            s.Frustum[3] = ShadowMath.Plane(0, new Vector(-1, 0, 0), -maxs.X);
            s.Frustum[4] = ShadowMath.Plane(1, new Vector(0, -1, 0), -maxs.Y);
            s.Frustum[5] = ShadowMath.Plane(2, new Vector(0, 0, -1), -maxs.Z);
        }

        public void GetInfo(int shadow, out ShadowEngineInfo info) => info = _engineShadows[shadow].Info;

        public void GetFlashlightFrustum(int shadow, out ShadowFrustum frustum) => frustum = _engineShadows[shadow].Frustum;

        public void AddShadowToBrushModel(int shadow, nint model, in Vector origin, in QAngle angles) =>
            Emit("E", "sm.AddShadowToBrushModel", shadow, Mo(model), V(origin), A(angles));

        public void RemoveAllShadowsFromBrushModel(nint model) => Emit("E", "sm.RemoveAllShadowsFromBrushModel", Mo(model));

        public void SetShadowTexCoord(int shadow, float x, float y, float w, float h)
        {
            Emit("E", "sm.SetShadowTexCoord", shadow, X(x), X(y), X(w), X(h));
            ref ShadowEngineInfo i = ref _engineShadows[shadow].Info;
            i.TexOriginX = x;
            i.TexOriginY = y;
            i.TexSizeX = w;
            i.TexSizeY = h;
        }

        public void AddShadowToModel(int shadow, int modelInstance) => Emit("E", "sm.AddShadowToModel", shadow, modelInstance);

        public void RemoveAllShadowsFromModel(int modelInstance) => Emit("E", "sm.RemoveAllShadowsFromModel", modelInstance);

        public void ClearExtraClipPlanes(int shadow) => Emit("E", "sm.ClearExtraClipPlanes", shadow);

        public void AddExtraClipPlane(int shadow, in Vector normal, float dist) => Emit("E", "sm.AddExtraClipPlane", shadow, V(normal), X(dist));

        public void EnableShadow(int shadow, bool enable) => Emit("E", "sm.EnableShadow", shadow, enable ? 1 : 0);

        public void SetFalloffBias(int shadow, byte bias)
        {
            Emit("E", "sm.SetFalloffBias", shadow, bias);
            _engineShadows[shadow].Info.FalloffBias = bias;
        }

        public void UpdateFlashlightState(int shadow, in FlashlightState state) => Emit("E", "sm.UpdateFlashlightState", shadow, StateText(state));

        public void AddFlashlightRenderable(int shadow, nint renderable) => Emit("E", "sm.AddFlashlightRenderable", shadow, R(renderable));

        public void SetFlashlightDepthTexture(int shadow, int texture, byte stencilBit) => Emit("E", "sm.SetFlashlightDepthTexture", shadow, Res(texture), stencilBit);

        public void SetFlashlightRenderState(int shadow) => Emit("E", "sm.SetFlashlightRenderState", shadow);

        public void AddShadowToStaticProp(int shadow, nint renderable) => Emit("E", "sp.AddShadowToStaticProp", shadow, R(renderable));

        public void RemoveAllShadowsFromStaticProp(nint renderable) => Emit("E", "sp.RemoveAllShadowsFromStaticProp", R(renderable));

        //---------------------------------------------------------------------
        // IShadowMaterials.
        //---------------------------------------------------------------------

        public int InitMaterial(string name, string textureGroup)
        {
            Emit("E", "mat.Init", name, textureGroup);
            return Resource(name, 0);
        }

        public void ShutdownMaterial(int material) => Emit("E", "mat.Shutdown", Res(material));

        public void ColorModulate(int material, float r, float g, float b) => Emit("E", "mat.ColorModulate", Res(material), X(r), X(g), X(b));

        public int InitRenderTarget(string name, int width, int height, int sizeMode, int format, int depth, bool hdr)
        {
            int actual = Math.Min(width, _maxRenderTarget);
            Emit("E", "tex.InitRenderTarget", name, width, height, sizeMode, format, depth, hdr ? 1 : 0, "=>" + actual.ToString(CultureInfo.InvariantCulture));
            return Resource(name, actual);
        }

        public void ShutdownTexture(int texture) => Emit("E", "tex.Shutdown", Res(texture));

        public int ActualWidth(int texture) => _resourceWidths[texture];

        public int ActualHeight(int texture) => _resourceWidths[texture];

        public bool SupportsShadowDepthTextures() => _supportsDepth;

        public int ShadowDepthTextureFormat() => 31;

        public int NullTextureFormat() => 36;

        public void BeginRenderTargetAllocation() => Emit("E", "mat.BeginRenderTargetAllocation");

        public void EndRenderTargetAllocation() => Emit("E", "mat.EndRenderTargetAllocation");

        public void UncacheUnusedMaterials() => Emit("E", "mat.UncacheUnusedMaterials");

        public void AddRestoreFunc() => Emit("E", "mat.AddRestoreFunc");

        public void RemoveRestoreFunc() => Emit("E", "mat.RemoveRestoreFunc", 1);

        public int DXSupportLevel() => _dxLevel;

        public bool ToolsMode() => _tools;

        //---------------------------------------------------------------------
        // IShadowRenderContext.
        //---------------------------------------------------------------------

        public float ComputePixelDiameterOfSphere(in Vector center, float radius) => radius * _ppu;

        public int GetFogMode() => _fog;

        public void FogMode(int mode)
        {
            Emit("E", "ctx.FogMode", mode);
            _fog = mode;
        }

        public int GetHeightClipMode() => _heightClip;

        public void SetHeightClipMode(int mode)
        {
            Emit("E", "ctx.SetHeightClipMode", mode);
            _heightClip = mode;
        }

        public void ClearColor4ub(byte r, byte g, byte b, byte a) => Emit("E", "ctx.ClearColor4ub", r, g, b, a);

        public void ClearColor3ub(byte r, byte g, byte b) => Emit("E", "ctx.ClearColor3ub", r, g, b);

        public void MatrixMode(int mode) => Emit("E", "ctx.MatrixMode", mode);

        public void PushMatrix() => Emit("E", "ctx.PushMatrix");

        public void PopMatrix() => Emit("E", "ctx.PopMatrix");

        public void LoadIdentity() => Emit("E", "ctx.LoadIdentity");

        public void LoadMatrix(in ShadowMatrix matrix) => Emit("E", "ctx.LoadMatrix", M(matrix));

        public void Scale(float x, float y, float z) => Emit("E", "ctx.Scale", X(x), X(y), X(z));

        private static string G17(double d) => d.ToString("G17", CultureInfo.InvariantCulture);

        public void Ortho(double left, double top, double right, double bottom, double zNear, double zFar) =>
            Emit("E", "ctx.Ortho", G17(left), G17(top), G17(right), G17(bottom), G17(zNear), G17(zFar));

        public void PushRenderTargetAndViewport(int texture) => Emit("E", "ctx.PushRenderTargetAndViewport", Res(texture));

        public void PopRenderTargetAndViewport() => Emit("E", "ctx.PopRenderTargetAndViewport");

        public void ClearBuffers(bool color, bool depth, bool stencil) => Emit("E", "ctx.ClearBuffers", color ? 1 : 0, depth ? 1 : 0, stencil ? 1 : 0);

        public void Viewport(int x, int y, int width, int height) => Emit("E", "ctx.Viewport", x, y, width, height);

        public void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias) =>
            Emit("E", "ctx.SetShadowDepthBiasFactors", X(slopeScaleDepthBias), X(depthBias));

        public void DrawTextureQuad(int material, float width, float height)
        {
            Emit("E", "ctx.Bind", Res(material));
            Emit("E", "ctx.GetDynamicMesh", 1);
            Emit("E", "mesh.Begin", 7, 1);
            (float X, float Y, float S, float T)[] corners = [(0, 0, 0, 0), (width, 0, 1, 0), (width, height, 1, 1), (0, height, 0, 1)];
            foreach (var c in corners)
            {
                Emit("E", "mesh.Position3f", X(c.X), X(c.Y), X(0.0f));
                Emit("E", "mesh.TexCoord2f", 0, X(c.S), X(c.T));
                Emit("E", "mesh.Color4ub", 0, 0, 0, 0);
                Emit("E", "mesh.AdvanceVertex");
            }
            Emit("E", "mesh.End");
            Emit("E", "mesh.Draw", -1, 0);
        }

        public bool DrawModelShadow(nint renderable, int body, int skin)
        {
            bool ok = ThingOf(renderable).BonesOk;
            Emit("E", "mr.DrawModelShadowSetup", R(renderable), body, skin, ok ? 1 : 0);
            if (ok)
                Emit("E", "mr.DrawModelShadow", R(renderable));
            return ok;
        }

        public void DrawBrushModelShadow(nint renderable) => Emit("E", "rv.DrawBrushModelShadow", R(renderable));

        public void UpdateShadowDepthTexture(int renderTarget, int depthTexture, in ShadowDepthView v) =>
            Emit("E", "view.UpdateShadowDepthTexture", Res(renderTarget), Res(depthTexture),
                 $"xywh={v.X},{v.Y},{v.Width},{v.Height}", "fov=" + X(v.Fov) + "," + X(v.FovViewmodel), "origin=" + V(v.Origin), "angles=" + A(v.Angles),
                 "z=" + X(v.ZNear) + "," + X(v.ZFar), "zvm=" + X(v.ZNearViewmodel) + "," + X(v.ZFarViewmodel), "aspect=" + X(v.AspectRatio),
                 "ortho=" + (v.Ortho ? 1 : 0), "bloom=" + (v.DoBloomAndToneMapping ? 1 : 0));

        //---------------------------------------------------------------------
        // IShadowWorld: leaves along x, the view box, the frame.
        //---------------------------------------------------------------------

        public bool CullBox(in Vector mins, in Vector maxs) =>
            !(mins.X < _viewMaxs.X && maxs.X > _viewMins.X && mins.Y < _viewMaxs.Y && maxs.Y > _viewMins.Y && mins.Z < _viewMaxs.Z && maxs.Z > _viewMins.Z);

        public Vector AmbientLightColor() => _ambient;

        private void LeavesOverX(float x0, float x1, List<int> leaves)
        {
            for (int i = 0; i < _leafCount; i++)
            {
                float l0 = i * _leafWidth, l1 = (i + 1) * _leafWidth;
                if (x0 < l1 && x1 > l0)
                    leaves.Add(i);
            }
        }

        public void EnumerateLeavesAlongRay(in Vector start, in Vector delta, in Vector extents, List<int> leaves)
        {
            float a = start.X, b = start.X + delta.X;
            LeavesOverX(Math.Min(a, b) - extents.X, Math.Max(a, b) + extents.X, leaves);
        }

        public void EnumerateLeavesInBox(in Vector mins, in Vector maxs, List<int> leaves) => LeavesOverX(mins.X, maxs.X, leaves);

        public int FrameCount => _frame;

        //---------------------------------------------------------------------
        // IShadowRenderables: the things.
        //---------------------------------------------------------------------

        public nint RenderableOf(uint entity)
        {
            if (entity == ShadowManager.InvalidEntity)
                return 0;
            return _things.TryGetValue((int)(entity & 0xFFFF), out Thing? t) && !t.Gone ? t.Pointer : 0;
        }

        public Vector RenderOrigin(nint renderable)
        {
            Thing t = ThingOf(renderable);
            if (t.DirtyOnOrigin && _redirtyDepth == 0)
            {
                ++_redirtyDepth;
                Emit("E", "r.redirty", R(renderable));
                Manager.AddToDirtyShadowList(renderable, false);
                --_redirtyDepth;
            }
            return t.Origin;
        }

        public QAngle RenderAngles(nint renderable) => ThingOf(renderable).Angles;

        public void RenderableToWorldTransform(nint renderable, out ShadowMatrix3x4 transform) => transform = ThingOf(renderable).Transform;

        public int ShadowHandle(nint renderable) => ThingOf(renderable).ShadowHandle;

        public nint Model(nint renderable)
        {
            Thing t = ThingOf(renderable);
            return t.HasModel ? (nint)(0x1000 + t.Id * 16 + t.ModelType) : 0;
        }

        public int ModelType(nint model) => model == 0 ? 0 : (int)((model - 0x1000) % 16);

        public int Body(nint renderable) => ThingOf(renderable).Body;

        public int Skin(nint renderable) => ThingOf(renderable).Skin;

        public void RenderBoundsWorldspace(nint renderable, out Vector mins, out Vector maxs)
        {
            Thing t = ThingOf(renderable);
            mins = t.AbsMins;
            maxs = t.AbsMaxs;
        }

        public void ShadowRenderBounds(nint renderable, int shadowType, out Vector mins, out Vector maxs)
        {
            Thing t = ThingOf(renderable);
            bool rtt = shadowType == ShadowTypes.RenderToTexture || shadowType == ShadowTypes.RenderToTextureDynamic;
            mins = rtt ? t.RttMins : t.Mins;
            maxs = rtt ? t.RttMaxs : t.Maxs;
        }

        public bool ShouldReceiveProjectedTextures(nint renderable, int flags) => (ThingOf(renderable).ReceiveFlags & flags) != 0;

        public bool ShadowCastDistance(nint renderable, int shadowType, ref float distance)
        {
            Thing t = ThingOf(renderable);
            if (t.CastDistanceFlag)
                distance = t.CastDistance;
            return t.CastDistanceFlag;
        }

        public bool ShadowCastDirection(nint renderable, int shadowType, ref Vector direction)
        {
            Thing t = ThingOf(renderable);
            if (t.CastDirectionFlag)
                direction = t.CastDirection;
            return t.CastDirectionFlag;
        }

        public bool IsShadowDirty(nint renderable) => ThingOf(renderable).Dirty;

        public void MarkShadowDirty(nint renderable, bool dirty)
        {
            Emit("E", "r.MarkShadowDirty", R(renderable), dirty ? 1 : 0);
            ThingOf(renderable).Dirty = dirty;
        }

        private nint Pointer(int id) => id >= 0 && _things.TryGetValue(id, out Thing? t) ? t.Pointer : 0;

        public nint ShadowParent(nint renderable) => Pointer(ThingOf(renderable).Parent);

        public nint FirstShadowChild(nint renderable) => Pointer(ThingOf(renderable).FirstChild);

        public nint NextShadowPeer(nint renderable) => Pointer(ThingOf(renderable).NextPeer);

        public void CreateModelInstance(nint renderable)
        {
            Emit("E", "r.CreateModelInstance", R(renderable));
            Thing t = ThingOf(renderable);
            if (t.Instance == Invalid)
                t.Instance = 100 + t.Id;
        }

        public int ModelInstance(nint renderable) => ThingOf(renderable).Instance;

        public int NetworkableEntIndex(nint renderable) => -1;

        /// <summary>The move hierarchy (a flashlight target's children): none in the golden's world, set by the facts.</summary>
        public readonly Dictionary<int, int> MoveFirstChild = [];

        public readonly Dictionary<int, int> MoveNextPeer = [];

        public nint FirstMoveChild(nint renderable) => Pointer(MoveFirstChild.GetValueOrDefault(ThingOf(renderable).Id, -1));

        public nint NextMovePeer(nint renderable) => Pointer(MoveNextPeer.GetValueOrDefault(ThingOf(renderable).Id, -1));

        /// <summary>A thing's renderable, for the facts.</summary>
        public nint RenderableOfThing(int id) => _things[id].Pointer;

        /// <summary>A thing as the replay holds it now, for the facts.</summary>
        public Thing ThingById(int id) => _things[id];

        //---------------------------------------------------------------------
        // IShadowDebugOverlay.
        //---------------------------------------------------------------------

        public void AddLineOverlay(in Vector origin, in Vector dest, int r, int g, int b, bool noDepthTest, float duration) =>
            Emit("E", "dbg.AddLineOverlay", V(origin), V(dest), r, g, b, noDepthTest ? 1 : 0, X(duration));

        public void AddTextOverlay(in Vector origin, int lineOffset, string text)
        {
            // A renderable's address, named as the oracle names it.
            if (ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong p) && _byPointer.ContainsKey((nint)p))
                text = "renderable " + R((nint)p);
            Emit("E", "dbg.AddTextOverlay", V(origin), lineOffset, text);
        }

        //---------------------------------------------------------------------
        // IShadowLeafSystem: each shadow's leaves, the receivers offered on a
        // projection, the shadows offered on an enumeration (the README's rules).
        //---------------------------------------------------------------------

        public int AddShadow(int userId, int flags)
        {
            _leafShadows.Add(new LeafShadow { UserId = userId, Flags = flags });
            int h = _leafShadows.Count - 1;
            Emit("E", "leaf.AddShadow", userId, flags, "=>" + h.ToString(CultureInfo.InvariantCulture));
            return h;
        }

        public void RemoveShadow(int handle)
        {
            Emit("E", "leaf.RemoveShadow", handle);
            _leafShadows[handle].Live = false;
            _leafShadows[handle].Leaves = [];
        }

        private void Offer(int handle, ReadOnlySpan<int> leaves)
        {
            var offered = new HashSet<int>();
            foreach (int leaf in leaves)
            {
                foreach (var r in _receivers)
                {
                    if (r.Leaf != leaf || !offered.Add(r.Thing))
                        continue;
                    if (!_things.TryGetValue(r.Thing, out Thing? t))
                        continue;
                    Emit("E", "leaf.receiver", _leafShadows[handle].UserId, R(t.Pointer), r.Kind);
                    Manager.AddShadowToReceiver(_leafShadows[handle].UserId, t.Pointer, r.Kind);
                }
            }
        }

        public void ProjectShadow(int handle, ReadOnlySpan<int> leaves)
        {
            Emit("E", "leaf.ProjectShadow", handle, Leaves(leaves));
            _leafShadows[handle].Leaves = leaves.ToArray();
            Offer(handle, leaves);
        }

        public void ProjectFlashlight(int handle, ReadOnlySpan<int> leaves)
        {
            Emit("E", "leaf.ProjectFlashlight", handle, Leaves(leaves));
            _leafShadows[handle].Leaves = leaves.ToArray();
            Offer(handle, leaves);
        }

        public void EnumerateShadowsInLeaves(ReadOnlySpan<int> leaves, IShadowLeafEnumerator enumerator)
        {
            Emit("E", "leaf.EnumerateShadowsInLeaves", Leaves(leaves));
            var offered = new HashSet<int>();
            foreach (int leaf in leaves)
            {
                for (int h = 0; h < _leafShadows.Count; h++)
                {
                    LeafShadow s = _leafShadows[h];
                    if (!s.Live || offered.Contains(h) || Array.IndexOf(s.Leaves, leaf) < 0)
                        continue;
                    offered.Add(h);
                    Emit("E", "leaf.enum", s.UserId);
                    enumerator.EnumShadow(s.UserId);
                }
            }
        }
    }

    // The two logs, cut at the stimulus rows, the rows stock alone writes left out.
    internal static List<string> Canonical(IEnumerable<string> lines) =>
        lines.Where(l => l.Length > 0 && l[0] != '#' && !NotCompared.Contains(l.Split('\t')[0])).ToList();

    internal static List<string> Replayed(out List<string> golden, Func<string, bool>? reloadAfter = null, Replay? replay = null)
    {
        string[] lines = Golden();
        replay ??= new Replay();
        foreach (string line in lines)
        {
            replay.Run(line);
            if (reloadAfter != null && reloadAfter(line))
                replay.Reload();
        }
        golden = Canonical(lines);
        return Canonical(replay.Log);
    }

    private static bool Is(string line, params string[] kinds) => kinds.Contains(line.Split('\t')[0]);

    private static string What(string line)
    {
        string[] f = line.Split('\t');
        return f[0] == "E" && f.Length > 1 ? f[1] : f[0];
    }

    /// <summary>Row for row, every row in stock's order.</summary>
    internal static void AssertSame(List<string> golden, List<string> replay, Func<string, bool> which, int atLeast, string what)
    {
        var g = golden.Where(which).ToList();
        var r = replay.Where(which).ToList();
        Assert.True(g.Count >= atLeast, $"the golden holds only {g.Count} {what} rows");
        for (int i = 0; i < Math.Max(g.Count, r.Count); i++)
        {
            string a = i < g.Count ? g[i] : "<end>", b = i < r.Count ? r[i] : "<end>";
            if (a != b)
            {
                var context = new StringBuilder();
                for (int k = Math.Max(0, i - 6); k < Math.Min(g.Count, i + 3); k++)
                    context.Append("\n  golden ").Append(g[k][..Math.Min(g[k].Length, 160)]);
                for (int k = Math.Max(0, i - 6); k < Math.Min(r.Count, i + 3); k++)
                    context.Append("\n  managed ").Append(r[k][..Math.Min(r[k].Length, 160)]);
                Assert.Fail($"{what} row {i}: stock wrote\n  {a[..Math.Min(a.Length, 240)]}\nand the managed manager\n  {b[..Math.Min(b.Length, 240)]}{context}");
            }
        }
    }

    /// <summary>Between stimulus rows, each kind of row in stock's order.</summary>
    internal static void AssertSameByKind(List<string> golden, List<string> replay)
    {
        static List<List<string>> Stretches(List<string> rows)
        {
            var output = new List<List<string>>();
            var stretch = new List<string>();
            foreach (string row in rows)
            {
                if (Anchors.Contains(row.Split('\t')[0]))
                {
                    output.Add(stretch);
                    output.Add([row]);
                    stretch = [];
                }
                else
                {
                    stretch.Add(row);
                }
            }
            output.Add(stretch);
            return output;
        }

        List<List<string>> g = Stretches(golden), r = Stretches(replay);
        Assert.Equal(g.Count, r.Count);
        for (int s = 0; s < g.Count; s++)
        {
            foreach (string kind in g[s].Concat(r[s]).Select(What).Distinct())
            {
                var a = g[s].Where(l => What(l) == kind).ToList();
                var b = r[s].Where(l => What(l) == kind).ToList();
                if (!a.SequenceEqual(b))
                {
                    string anchor = s > 0 ? g[s - 1].FirstOrDefault() ?? "" : "";
                    int i = 0;
                    while (i < Math.Min(a.Count, b.Count) && a[i] == b[i])
                        i++;
                    Assert.Fail($"after {anchor[..Math.Min(anchor.Length, 120)]}: the {kind} rows differ at {i}: stock {(i < a.Count ? a[i][..Math.Min(a[i].Length, 200)] : "<end>")}, managed {(i < b.Count ? b[i][..Math.Min(b[i].Length, 200)] : "<end>")}");
                }
            }
        }
    }

    [Fact]
    public void EachKindBetweenTheScenariosCallsIsStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSameByKind(golden, replay);
    }

    [Fact]
    public void TheWholeRunIsStocksRowForRow()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, _ => true, 20000, "any");
    }

    // A hot reload after every call changes nothing stock would see.
    [Fact]
    public void TheManagerSurvivesAHotReloadAfterEveryCall()
    {
        int reloads = 0;
        List<string> replay = Replayed(out List<string> golden, line =>
        {
            bool at = line.StartsWith("C\t", StringComparison.Ordinal);
            reloads += at ? 1 : 0;
            return at;
        });
        Assert.True(reloads >= 2000, $"only {reloads} reloads");
        AssertSame(golden, replay, _ => true, 20000, "any");
    }

    [Fact]
    public void TheShadowsTheirKindsAndTheirRetirementAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "R") || What(l) is "sm.CreateShadowEx" or "sm.DestroyShadow" or "leaf.AddShadow" or "leaf.RemoveShadow", 3000, "shadow");
    }

    [Fact]
    public void TheModesAndTheConVarsAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l) is "cvar.Set" or "mat.Init" or "mat.Shutdown" or "mat.ColorModulate" or "tex.InitRenderTarget" or "tex.Shutdown"
                                      or "mat.AddRestoreFunc" or "mat.RemoveRestoreFunc" or "mat.UncacheUnusedMaterials", 40, "mode");
    }

    [Fact]
    public void TheProjectionsAreStocksToTheBit()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l) is "sm.ProjectShadow" or "sm.ProjectFlashlight" or "sm.AddExtraClipPlane" or "leaf.ProjectShadow"
                                      or "leaf.ProjectFlashlight" or "sm.UpdateFlashlightState", 1000, "projection");
    }

    [Fact]
    public void TheReceiversAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l) is "leaf.receiver" or "sm.AddShadowToBrushModel" or "sm.AddFlashlightRenderable" or "sm.AddShadowToModel"
                                      or "sp.AddShadowToStaticProp" or "r.CreateModelInstance" or "sm.RemoveAllShadowsFromBrushModel"
                                      or "sp.RemoveAllShadowsFromStaticProp" or "sm.RemoveAllShadowsFromModel", 300, "receiver");
    }

    [Fact]
    public void TheRenderToTexturePassIsStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l).StartsWith("ctx.", StringComparison.Ordinal) || What(l) is "mr.DrawModelShadowSetup" or "mr.DrawModelShadow"
                                      or "rv.DrawBrushModelShadow" or "sm.SetShadowTexCoord" or "sm.SetShadowMaterial" or "leaf.enum", 1000, "render-to-texture");
    }

    [Fact]
    public void TheDepthTexturesAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l) is "sm.SetFlashlightDepthTexture" or "view.UpdateShadowDepthTexture" or "sm.SetFlashlightRenderState"
                                      or "ctx.SetShadowDepthBiasFactors", 1000, "depth");
    }

    [Fact]
    public void TheDirtyMarksAndTheConsoleAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l) is "r.MarkShadowDirty" or "r.redirty" or "sm.EnableShadow" || Is(l, "M"), 1000, "dirty");
    }

    [Fact]
    public void TheProxiesBindsAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l).StartsWith("var.", StringComparison.Ordinal), 20, "proxy");
    }

    [Fact]
    public void TheDebugOverlaysAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => What(l) is "dbg.AddLineOverlay" or "dbg.AddTextOverlay" || What(l).StartsWith("mesh.", StringComparison.Ordinal), 30, "debug");
    }
}
