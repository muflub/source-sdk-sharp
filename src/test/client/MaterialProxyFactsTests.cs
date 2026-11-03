using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The material proxy arms the recording (proxies-stock.tsv.gz) cannot reach on
/// dm_lockdown, held offline against stock's own code as written: every
/// entity's render alpha there is 255 and none is an NPC, no player has a
/// spray, no recorded Init reads a FLOAT or INT key back as text, and the
/// entity facts this build cannot answer (spawn time, health, texture frame,
/// eye lighting) never answer. Each number below is worked from the stock
/// source line it names.
/// </summary>
public class MaterialProxyFactsTests
{
    internal sealed class FakeInit(params ProxyKey[] keys) : IProxyInit
    {
        public readonly List<string> Asked = [];
        public readonly HashSet<string> Missing = new(StringComparer.Ordinal);
        public bool TextureIsError;

        public string MaterialName => "fake";

        public ProxyKeyValues Keys { get; } = new(keys);

        public int FindVar(string name, bool complain, out bool found)
        {
            Asked.Add(name);
            found = !Missing.Contains(name);
            return Asked.Count - 1;
        }

        public int FindTexture(string name, string group, bool complain, out bool isError)
        {
            Asked.Add("tex:" + name);
            isError = TextureIsError;
            return 100;
        }
    }

    internal sealed class FakeBind : IProxyBind
    {
        public readonly Dictionary<int, ProxyVar> Vars = [];
        public readonly List<string> Ops = [];
        public readonly List<string> Textures = [];
        public readonly Dictionary<string, int> TextureAnswers = [];
        public int NotShippedCount;
        public ProxyEntity Ent = ProxyEntity.None;
        public Vector View { get; set; }
        public uint Crc;
        public Vector? Lighting;

        public float CurTime { get; set; }
        public float FrameTime { get; set; }
        public int MaxClients { get; set; } = 16;
        public nint Arg { get; set; }

        public ProxyVar Var(int slot) => Vars.GetValueOrDefault(slot);

        public int Texture(int slot, out string? name)
        {
            name = "tex";
            return 10;
        }

        public ProxyEntity Entity() => Ent;

        public Vector ViewOrigin() => View;

        public void WaterLod(out float start, out float end) => start = end = 0;

        public bool WorldBounds(out Vector mins, out Vector maxs)
        {
            mins = maxs = Vector.Zero;
            return false;
        }

        public ParticleBindLight Particle;
        public int ParticleReads;
        public readonly List<(int Slot, float[] Values)> VecSets = [];

        public ParticleBindLight ParticleLight()
        {
            ParticleReads++;
            return Particle;
        }

        public bool PlayerCustomFile(int player, out uint crc)
        {
            crc = Crc;
            return true;
        }

        public int FindTexture(string name, string group, bool complain)
        {
            Textures.Add(name);
            return TextureAnswers.GetValueOrDefault(name, -1);
        }

        public float RandomFloat(float lo, float hi) => lo;

        public float RandomGaussian(float mean, float stdDev) => mean;

        public void NotShipped(string capability, string reason) => NotShippedCount++;

        public bool TextureFrameIndex(out int frame)
        {
            frame = 0;
            return false;
        }

        public bool EyeLighting(out Vector eye, out Vector forward, out Vector lighting)
        {
            eye = forward = Vector.Zero;
            lighting = Lighting ?? Vector.Zero;
            return Lighting is not null;
        }

        public void SetFloat(int slot, float value) => Ops.Add($"{slot} float {value:R}");

        public void SetInt(int slot, int value) => Ops.Add($"{slot} int {value}");

        public void SetVec(int slot, ReadOnlySpan<float> values, int count)
        {
            Ops.Add($"{slot} vecn {count}");
            VecSets.Add((slot, values[..count].ToArray()));
        }

        public void SetVec3(int slot, float x, float y, float z) => Ops.Add($"{slot} vec3 {x:R} {y:R} {z:R}");

        public void SetVecComponent(int slot, float value, int component) => Ops.Add($"{slot} veccomp {component} {value:R}");

        public void SetMatrix(int slot, in VMatrix matrix) => Ops.Add($"{slot} matrix");

        public void SetTexture(int slot, int textureSlot) => Ops.Add($"{slot} texture {textureSlot}");
    }

    private static ProxyKey Str(string name, string text, int scanCount = 0, float scan = 0, float atof = 0, int atoi = 0)
        => new(name, ProxyKeyType.String, text, 0, 0, scanCount, scan, atof, atoi);

    private static readonly ProxyEntity AnEntity = new(true, true, 5, new Vector(10, 0, 0), 128, 1);

    [Fact]
    public void GetStringRewritesAFloatKeyAsItsSixDecimalText()
    {
        // KeyValues::GetString, TYPE_FLOAT: Q_snprintf( buf, "%f", m_flValue ); SetString( buf ).
        var key = new ProxyKey("rate", ProxyKeyType.Float, "", 0.1234567f, 0, 0, 0, 0, 0);

        Assert.Equal(0.1234567f, key.GetFloat());
        Assert.Equal("0.123457", key.GetString());
        Assert.Equal(ProxyKeyType.String, key.Type);
        Assert.Equal(0.123457f, key.GetFloat());
        Assert.Equal(0, key.GetInt());
    }

    [Fact]
    public void GetStringRewritesAnIntKeyAsItsDecimalText()
    {
        var key = new ProxyKey("n", ProxyKeyType.Int, "", 0, -42, 0, 0, 0, 0);

        Assert.Equal(-42f, key.GetFloat());
        Assert.Equal("-42", key.GetString());
        Assert.Equal(ProxyKeyType.String, key.Type);
        Assert.Equal(-42, key.GetInt());
    }

    [Fact]
    public void AFloatInputThatWasReadAsTextUsesTheTextsValue()
    {
        // CFloatInput::Init: a STRING section is sscanf'd; a FLOAT one is GetFloat'd.
        var section = new ProxyKey("sinePeriod", ProxyKeyType.Float, "", 0.1234567f, 0, 0, 0, 0, 0);
        _ = section.GetString();
        var init = new FakeInit(section);
        var input = new ProxyFloatInput();

        Assert.True(input.Init(init, "sinePeriod", 1));
        Assert.Equal(0.123457f, input.Get(new FakeBind()));
    }

    [Fact]
    public void TheHaloFadesInFromAQuarterAndTheBeamFadesOut()
    {
        // lamphalo: fade = |z|; < 0.25 -> 0; else MIN( (fade - 0.25) * 1.35, 1.0f ).
        // lampbeam: fade = 1.0 - |z|. The origin is straight above the view.
        float Fade(bool halo, float dz, float dx)
        {
            var proxy = new LampProxy(halo);
            Assert.True(proxy.Init(new FakeInit()));
            var bind = new FakeBind { Arg = 1, Ent = AnEntity with { Origin = new Vector(dx, 0, dz) } };
            proxy.Bind(bind);
            Assert.Single(bind.Ops);
            return float.Parse(bind.Ops[0].Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.Equal(0f, Fade(true, 1, 4.2f));                         // |z| = 0.2316
        Assert.Equal((0.255 - 0.25) * 1.35, Fade(true, 1, 3.7919f), 4); // |z| = 0.2550, just past the quarter
        Assert.Equal((float)((0.6f - 0.25) * 1.35), Fade(true, 3, 4), 6); // |z| = 0.6
        Assert.Equal(1f, Fade(true, 1, 0));                            // (1 - 0.25) * 1.35 = 1.0125 -> 1
        Assert.Equal((float)(1.0 - 0.6f), Fade(false, 3, 4), 6);
    }

    [Fact]
    public void IsNpcAnswersTheScaleForAnNpcZeroForAnythingElseAndNothingUnresolved()
    {
        var init = new FakeInit(Str("resultVar", "$r"), Str("scale", "0.5", 1, 0.5f, 0.5f, 0));
        var proxy = new IsNpcProxy();
        Assert.True(proxy.Init(init));

        var npc = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(npc);
        var other = new FakeBind { Arg = 1, Ent = AnEntity with { IsNpc = 0 } };
        proxy.Bind(other);
        var unresolved = new FakeBind { Arg = 1, Ent = AnEntity with { IsNpc = -1 } };
        proxy.Bind(unresolved);

        Assert.Equal(["0 float 0.5"], npc.Ops);
        Assert.Equal(["0 float 0"], other.Ops);
        Assert.Empty(unresolved.Ops);
    }

    [Fact]
    public void AlphaWritesTheRenderAlphaAndLeavesAnUnansweredBindAlone()
    {
        var proxy = new AlphaProxy();
        Assert.True(proxy.Init(new FakeInit()));

        var half = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(half);
        var unanswered = new FakeBind { Arg = 1, Ent = AnEntity with { RenderAlpha = -1 } };
        proxy.Bind(unanswered);
        var notEntity = new FakeBind { Arg = 1, Ent = AnEntity with { EntIndex = -1 } };
        proxy.Bind(notEntity);

        Assert.Equal(["0 float 128"], half.Ops);
        Assert.Empty(unanswered.Ops);
        Assert.Empty(notEntity.Ops);
    }

    [Fact]
    public void HealthIsADeclaredRefusalForAnythingBoundAndSilentForNull()
    {
        var proxy = new HealthProxy();
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var entity = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(entity);
        var none = new FakeBind { Arg = 0, Ent = AnEntity };
        proxy.Bind(none);

        Assert.Equal(1, entity.NotShippedCount);
        Assert.Empty(entity.Ops);
        Assert.Equal(0, none.NotShippedCount);
    }

    [Fact]
    public void TheEntityTimedAnimationsNameTheMissingSpawnTimeAndKeepTheirFrame()
    {
        var init = new FakeInit(Str("animatedTextureVar", "$basetexture"), Str("animatedTextureFrameNumVar", "$frame"));
        var proxy = new AnimatedOffsetTextureProxy();
        Assert.True(proxy.Init(init));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity, CurTime = 1.0f, FrameTime = 0.1f };
        bind.Vars[0] = new ProxyVar { Type = (int)MaterialVarType.Texture };
        proxy.Bind(bind);

        // The offset stayed 0: frame = 15 * 1.0 = 15, 15 % 10 = 5.
        Assert.Equal(1, bind.NotShippedCount);
        Assert.Equal(["1 int 5"], bind.Ops);
    }

    [Fact]
    public void ToggleTextureCountsTheFrameIndexItCouldNotRead()
    {
        var init = new FakeInit(Str("toggleTextureVar", "$basetexture"), Str("toggleTextureFrameNumVar", "$frame"));
        var proxy = new ToggleTextureProxy();
        Assert.True(proxy.Init(init));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity };
        bind.Vars[0] = new ProxyVar { Type = (int)MaterialVarType.Texture };
        proxy.Bind(bind);

        Assert.Empty(bind.Ops);
        Assert.Equal(1, proxy.Unanswered);
    }

    [Fact]
    public void ThePupilFollowsTheEyeLightingAtTheCloseRate()
    {
        // proxypupil.cpp: intensity = (0.299 r + 0.587 g + 0.114 b) * 0.5 = 0.9 for
        // white 1.8; the last was 0.5, so the close rate (0.1) times 0.5 s caps
        // it at 0.55; frame = (int)( 10 * 0.55 ) = 5.
        var init = new FakeInit(Str("TextureVar", "$basetexture"), Str("TextureFrameNumVar", "$frame"));
        var proxy = new PupilProxy(new StandardErrorLog());
        Assert.True(proxy.Init(init));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity, FrameTime = 0.5f, Lighting = new Vector(1.8f, 1.8f, 1.8f) };
        bind.Vars[0] = new ProxyVar { Type = (int)MaterialVarType.Texture };
        bind.Vars[2] = new ProxyVar { Float = 0.5f };
        proxy.Bind(bind);

        Assert.Equal(["1 int 5", "2 float 0.55"], bind.Ops);
        Assert.Equal(0, proxy.Unanswered);

        var dark = new FakeBind { Arg = 1, Ent = AnEntity };
        dark.Vars[0] = new ProxyVar { Type = (int)MaterialVarType.Texture };
        proxy.Bind(dark);
        Assert.Empty(dark.Ops);
        Assert.Equal(1, proxy.Unanswered);
    }

    [Fact]
    public void ASprayTextureIsNamedByItsCrcBytesInMemoryOrder()
        => Assert.Equal("temp/44332211", PlayerLogoProxy.TextureName(0x11223344));

    [Fact]
    public void ASprayNotYetDownloadedDrawsTheDefaultAndIsLookedUpAgain()
    {
        var proxy = new PlayerLogoProxy();
        var init = new FakeInit();
        Assert.True(proxy.Init(init));
        Assert.Equal(["$basetexture", "tex:" + PlayerLogoProxy.DefaultDecal], init.Asked);

        var miss = new FakeBind { Arg = 3, Crc = 0x11223344 };
        proxy.Bind(miss);
        Assert.Equal(["0 texture 100"], miss.Ops);

        var arrived = new FakeBind { Arg = 3, Crc = 0x11223344 };
        arrived.TextureAnswers["temp/44332211"] = 7;
        proxy.Bind(arrived);
        Assert.Equal(["0 texture 7"], arrived.Ops);

        var cached = new FakeBind { Arg = 3, Crc = 0x11223344 };
        proxy.Bind(cached);
        Assert.Equal(["0 texture 7"], cached.Ops);

        Assert.Equal(["temp/44332211"], miss.Textures);
        Assert.Equal(["temp/44332211"], arrived.Textures);
        Assert.Empty(cached.Textures);
        Assert.Equal([0, 2, 1], proxy.Outcomes);
    }

    [Fact]
    public void APlayerWithNoSprayOrAnIndexOutOfRangeBindsNothing()
    {
        var proxy = new PlayerLogoProxy();
        Assert.True(proxy.Init(new FakeInit()));

        foreach (nint index in new nint[] { 0, -1, 17 })
        {
            var bind = new FakeBind { Arg = index, Crc = 5 };
            proxy.Bind(bind);
            Assert.Empty(bind.Ops);
        }
        var noSpray = new FakeBind { Arg = 4, Crc = 0 };
        proxy.Bind(noSpray);
        Assert.Empty(noSpray.Ops);
        Assert.Equal([1, 0, 0], proxy.Outcomes);
    }

    [Fact]
    public void PlayerLogoRefusesAnErrorDefaultTexture()
    {
        var proxy = new PlayerLogoProxy();
        Assert.False(proxy.Init(new FakeInit { TextureIsError = true }));
    }

    [Fact]
    public void StateABindChangesSurvivesAReloadRoundTrip()
    {
        var proxy = new PlayerLogoProxy();
        Assert.True(proxy.Init(new FakeInit()));
        var bind = new FakeBind { Arg = 3, Crc = 9 };
        bind.TextureAnswers[PlayerLogoProxy.TextureName(9)] = 11;
        proxy.Bind(bind);

        var writer = new ReloadWriter();
        proxy.SaveState(writer);
        var restored = new PlayerLogoProxy();
        Assert.True(restored.Init(new FakeInit()));
        restored.RestoreState(new ReloadReader(writer.ToArray()));

        var after = new FakeBind { Arg = 3, Crc = 9 };
        restored.Bind(after);
        Assert.Equal(["0 texture 11"], after.Ops);
        Assert.Empty(after.Textures);
        Assert.Equal([0, 2, 0], restored.Outcomes);
    }

    //-------------------------------------------------------------------------
    // proxyplayer.cpp's seven, held against the stock file as written. The
    // console asked for two of them by name on ctf_2fort (proxy "PlayerTeamMatch"
    // not found / proxy "PlayerProximity" not found); the rest of the file's
    // exposed names landed with them. The eyes are a fake here — the service's
    // own arms over the real directory and resource are PlayerProxyEyesTests.
    //-------------------------------------------------------------------------

    private sealed class FakeEyes : IPlayerProxyEyes
    {
        public ClientSsPlayer? Player;
        public Vector? Angles;
        public readonly Dictionary<int, int> Teams = [];
        public readonly Dictionary<int, float> Speeds = [];

        public ClientSsPlayer? LocalPlayer() => Player;
        public Vector? LocalAngles() => Angles;
        public int TeamNumber(int entityIndex) => Teams.GetValueOrDefault(entityIndex);
        public float? EntitySpeed(int entityIndex) => Speeds.TryGetValue(entityIndex, out float speed) ? speed : null;
    }

    /// <summary>The local player the eyes answer, positioned and moving as named.</summary>
    private static ClientSsPlayer Local(Vector origin, Vector velocity = default)
    {
        var engine = Fake();
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0000u | 1) };
        player.BindIndex(1);
        player.State.BindLocal();
        player.State.Origin = origin;
        player.State.Velocity = velocity;
        return player;
    }

    [Fact]
    public void TheProximityScalesTheDistanceToTheBoundEntity()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero) };
        var proxy = new PlayerProximityProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity with { Origin = new Vector(100, 0, 0) } };
        proxy.Bind(bind);

        // :56-60: delta.Length() * 0.002 = 100 * 0.002.
        Assert.Equal(["0 float 0.2"], bind.Ops);
    }

    [Fact]
    public void TheProximityScaleComesFromTheBlock()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero) };
        var proxy = new PlayerProximityProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"), Str("scale", "0.01", 0, 0, 0.01f))));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity with { Origin = new Vector(100, 0, 0) } };
        proxy.Bind(bind);

        Assert.Equal(["0 float 1"], bind.Ops);
    }

    [Fact]
    public void TheProximityWritesNothingWithoutALocalPlayerAndNothingUnbound()
    {
        var proxy = new PlayerProximityProxy(new FakeEyes());
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bound = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(bound);
        var unbound = new FakeBind { Arg = 0, Ent = AnEntity };
        proxy.Bind(unbound);

        Assert.Empty(bound.Ops);
        Assert.Empty(unbound.Ops);
    }

    [Fact]
    public void TheProximitySkipsAnArgumentThatResolvesToNoEntity()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero) };
        var proxy = new PlayerProximityProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var notEntity = new FakeBind { Arg = 1, Ent = AnEntity with { EntIndex = -1 } };
        proxy.Bind(notEntity);

        Assert.Empty(notEntity.Ops);
    }

    [Fact]
    public void TheTeamMatchIsOneWhenTheTeamsAgreeAndZeroWhenTheyDoNot()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero) };
        var proxy = new PlayerTeamMatchProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity };
        eyes.Teams[5] = 2;
        eyes.Teams[1] = 2;
        proxy.Bind(bind);
        eyes.Teams[1] = 3;
        proxy.Bind(bind);

        // :104: (pEntity->GetTeamNumber() == pPlayer->GetTeamNumber()) ? 1.0 : 0.0.
        Assert.Equal(["0 float 1", "0 float 0"], bind.Ops);
    }

    [Fact]
    public void TheTeamMatchTreatsAnEntityThatIsNotAPlayerAsUnassigned()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero) };
        var proxy = new PlayerTeamMatchProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        eyes.Teams[1] = 3;   // the local player is BLU; the bound entity answers no row
        var bind = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(bind);

        Assert.Equal(["0 float 0"], bind.Ops);
    }

    [Fact]
    public void TheViewSquareslyAheadIsTheScaleAndDirectlyBehindItsNegation()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero), Angles = new Vector(0, 0, 0) };
        var proxy = new PlayerViewProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var ahead = new FakeBind { Arg = 1, Ent = AnEntity with { Origin = new Vector(100, 0, 0) } };
        proxy.Bind(ahead);
        var behind = new FakeBind { Arg = 1, Ent = AnEntity with { Origin = new Vector(-100, 0, 0) } };
        proxy.Bind(behind);

        // :156: DotProduct(forward, delta) * 2 — default scale.
        Assert.Equal(["0 float 2"], ahead.Ops);
        Assert.Equal(["0 float -2"], behind.Ops);
    }

    [Fact]
    public void TheViewCountsThePitchIntoTheForward()
    {
        // AngleVectors(90, 0, 0): forward = (0, 0, -1) — looking straight down.
        var eyes = new FakeEyes { Player = Local(Vector.Zero), Angles = new Vector(90, 0, 0) };
        var proxy = new PlayerViewProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var below = new FakeBind { Arg = 1, Ent = AnEntity with { Origin = new Vector(0, 0, -100) } };
        proxy.Bind(below);

        Assert.Equal(["0 float 2"], below.Ops);
    }

    [Fact]
    public void TheViewWritesNothingBeforeTheViewHasPublishedAngles()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero), Angles = null };
        var proxy = new PlayerViewProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(bind);

        Assert.Empty(bind.Ops);
    }

    [Fact]
    public void TheSpeedScalesTheLocalVelocity()
    {
        var eyes = new FakeEyes { Player = Local(Vector.Zero, new Vector(300, 400, 0)) };
        var proxy = new PlayerSpeedProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind();
        proxy.Bind(bind);

        // :197: GetLocalVelocity().Length() * 0.005 = 500 * 0.005. Stock touches
        // no bind argument here (:189-203) — the bind can be nothing at all.
        Assert.Equal(["0 float 2.5"], bind.Ops);
    }

    [Fact]
    public void TheSpeedWritesNothingWithoutALocalPlayer()
    {
        var proxy = new PlayerSpeedProxy(new FakeEyes());
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(bind);

        Assert.Empty(bind.Ops);
    }

    [Fact]
    public void ThePositionWritesTheLocalOriginScaledAsThreeFloats()
    {
        var eyes = new FakeEyes { Player = Local(new Vector(200, 100, 50)) };
        var proxy = new PlayerPositionProxy(eyes);
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind();
        proxy.Bind(bind);

        // :240-241: VectorMultiply(WorldSpaceCenter, 0.005); SetVecValue(res, 3)
        // — the array setter. (Center: see the port's divergence remark.)
        Assert.Equal(["0 vecn 3"], bind.Ops);
        Assert.Equal(0, bind.VecSets[0].Slot);
        Assert.Equal([1f, 0.5f, 0.25f], bind.VecSets[0].Values);
    }

    [Fact]
    public void TheEntitySpeedWritesTheBoundPlayersVelocityAsAPlainFloat()
    {
        var proxy = new EntitySpeedProxy(new FakeEyes { Speeds = { [5] = 50f } });
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bind = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(bind);

        // :271 is m_pResult->SetFloatValue — not SetFloatResult's vector arm.
        Assert.Equal(["0 float 50"], bind.Ops);
    }

    [Fact]
    public void TheEntitySpeedRefusesAnEntityWithNothingToReadAndStaysSilentUnbound()
    {
        var proxy = new EntitySpeedProxy(new FakeEyes());
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"))));

        var bound = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(bound);
        var unbound = new FakeBind { Arg = 0, Ent = AnEntity };
        proxy.Bind(unbound);

        Assert.Equal(1, bound.NotShippedCount);
        Assert.Empty(bound.Ops);
        Assert.Equal(0, unbound.NotShippedCount);
    }

    [Fact]
    public void TheEntityRandomIsADeclaredRefusalForAnythingBoundAndSilentUnbound()
    {
        var proxy = new EntityRandomProxy();
        Assert.True(proxy.Init(new FakeInit(Str("resultVar", "$r"), Str("scale", "2", 1, 2, 2, 0))));

        var entity = new FakeBind { Arg = 1, Ent = AnEntity };
        proxy.Bind(entity);
        var unbound = new FakeBind { Arg = 0, Ent = AnEntity };
        proxy.Bind(unbound);

        Assert.Equal(1, entity.NotShippedCount);
        Assert.Empty(entity.Ops);
        Assert.Equal(0, unbound.NotShippedCount);
    }

    [Fact]
    public void TheEntityRandomRefusesWhenItsScaleNamesAMissingVar()
    {
        // :300: CFloatInput::Init fails the proxy — a scale spelled as a var
        // name the material does not have. (An absent scale key is the plain
        // default, which answers true: AFloatInputThatWasReadAsText... holds
        // that arm already.)
        var init = new FakeInit(Str("resultVar", "$r"), Str("scale", "$noise"));
        init.Missing.Add("$noise");

        var proxy = new EntityRandomProxy();
        Assert.False(proxy.Init(init));
    }
}
