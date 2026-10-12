extern alias client;
using RandomSeeds = client::SourceSharp.Abi.RandomSeeds;

using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;

using Xunit;

using SourceSharp.Server;
using SourceSharp.Tests;

namespace SourceSharp.OracleTests;

/// <summary>
/// The ITempEnts arms the legacy tempents' thunk keeps as no-caller stubs
/// (shell_legacy_tempents_client.cpp), each held to stock's own body.
///
/// <para>
/// THE NEEDLES ARE STOCK'S SPELLING, read out of the pristine
/// <c>c_te_legacytempents.cpp</c> (the upstream clone, valve-sdk.lock), so a
/// constant changed on either side fails on the line that moved.
/// </para>
/// </summary>
public class TempEntNoCallerArmsOracleTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private sealed class SpawningPool : EngineEffectsForwarder
    {
        public SpawningPool(IEngineEffects inner) : base(inner) { }

        public List<(int Slot, Vector Origin, int Model, int Priority)> Created { get; } = new();

        public List<TempEntSpawn> Spawned { get; } = new();

        public override int TempEntPoolCapacity() => 500;

        public override bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
        {
            Created.Add((slot, origin, modelIndex, priority));
            return true;
        }

        public override bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
        {
            Spawned.Add(spawn);
            return true;
        }
    }

    private sealed class NoSimulation : IClientTempEntSimulation
    {
        public bool Step(int slot, float frameTime, int frameNumber, int frameCount, ref TempEntLife life)
            => throw new InvalidOperationException("this rig has no physics in it");

        public void Forget(int slot) { }
        public void LevelInit() { }
    }

    private sealed record Rigging(TempEntSpawns Spawns, SpawningPool Native, ClientTempEntPool Pool,
                                  RecordingLog Log);

    private static Rigging Rig(int frames = 10, ModelType kind = ModelType.Sprite)
    {
        var engine = TestServices.Fake();
        var native = new SpawningPool(engine);
        var clock = new Clock { Now = 100f };
        var log = new RecordingLog();

        engine.Returns("Precache.ModelFrameCount", frames);
        engine.Returns("Precache.ModelTypeOf", kind);

        var pool = new ClientTempEntPool(new RecordingLog(), native, clock, new NoSimulation());

        return new Rigging(new TempEntSpawns(log, pool, engine, engine, clock, new RandomSeeds(1)),
                           native, pool, log);
    }

    /// <summary>Stock's body of <c>CTempEnts::<paramref name="name"/></c>, comments and whitespace squeezed.</summary>
    private static string StockBody(string name)
    {
        string text = ValveSdk.Read("src/game/client/c_te_legacytempents.cpp");
        Match m = Regex.Match(text, @"\n[^\n]*CTempEnts::" + Regex.Escape(name) + @"\s*\(");
        Assert.True(m.Success, $"stock still defines CTempEnts::{name}");

        int end = text.IndexOf("\n}", m.Index, StringComparison.Ordinal);
        Assert.True(end > m.Index, $"and CTempEnts::{name} has a body");

        string body = Regex.Replace(text[m.Index..end], @"//[^\n]*", string.Empty);
        return Regex.Replace(body, @"\s+", string.Empty);
    }

    private static void Has(string body, string needle, string what)
        => Assert.True(body.Contains(Regex.Replace(needle, @"\s+", string.Empty), StringComparison.Ordinal),
            $"stock's body says `{needle}` ({what})");

    [Fact]
    public void RicochetSpriteIsStocksGlowThatFadesAtOnceOnARollStep()
    {
        string stock = StockBody("RicochetSprite");
        Has(stock, "SetRenderMode( kRenderGlow );", "glow");
        Has(stock, "m_nRenderFX = kRenderFxNoDissipation;", "no dissipation");
        Has(stock, "SetRenderColorA( 200 );", "alpha 200");
        Has(stock, "tempent_renderamt = 200;", "render amount 200");
        Has(stock, "flags = FTENT_FADEOUT;", "the flags ASSIGNED");
        Has(stock, "fadeSpeed = 8;", "fade speed 8");
        Has(stock, "die = gpGlobals->curtime;", "dying from now");
        Has(stock, "SetLocalAnglesDim( Z_INDEX, 45 * random->RandomInt( 0, 7 ) );", "the roll step");

        var rig = Rig();
        int slot = rig.Spawns.RicochetSprite(new Vector(1f, 2f, 3f), modelIndex: 7, duration: 5f, scale: 0.5f);

        Assert.True(slot >= 0 && rig.Native.Spawned.Count == 1, "one sprite was spawned");
        var made = rig.Native.Created[0];
        Assert.True(made.Model == 7 && made.Priority == TempEntPriority.Low, "of the model, at LOW priority");
        Assert.True(made.Origin.X == 1f && made.Origin.Y == 2f && made.Origin.Z == 3f, "at the position");

        TempEntSpawn s = rig.Native.Spawned[0];
        Assert.True(s.RenderMode == (int)RenderMode.Glow && s.RenderFx == RenderFx.NoDissipation, "glow, no dissipation");
        Assert.True(s.ColorA == 200 && s.RenderAmt == 200, $"alpha and amount 200: {s.ColorA}, {s.RenderAmt}");
        Assert.True(s.Flags == (int)TempEntFlags.FadeOut, $"exactly FTENT_FADEOUT: 0x{s.Flags:x}");
        Assert.True(s.FadeSpeed == 8f && s.Die == 100f, $"fade speed 8 from now: {s.FadeSpeed}, {s.Die}");
        Assert.True(s.SpriteScale == 0.5f && s.Frame == 0f, "at the scale, from frame 0");
        Assert.True(s.Angles.X == 0f && s.Angles.Y == 0f && s.Angles.Z % 45f == 0f && s.Angles.Z is >= 0f and <= 315f,
            $"rolled by a 45-degree step and nothing else: {s.Angles}");
    }

    [Fact]
    public void DefaultSpritePlaysOneAnimationPassAtItsFrameRate()
    {
        string stock = StockBody("DefaultSprite");
        Has(stock, "if ( gpGlobals->frametime == 0.0 ) return NULL;", "not while paused");
        Has(stock, "modelinfo->GetModelType( pSprite ) != mod_sprite", "sprites only");
        Has(stock, "pTemp->flags |= FTENT_SPRANIMATE;", "animated");
        Has(stock, "if ( framerate == 0 ) framerate = 10;", "ten by default");
        Has(stock, "pTemp->die = gpGlobals->curtime + (float)frameCount / framerate;", "one pass");

        var rig = Rig(frames: 8);
        Assert.True(rig.Spawns.DefaultSprite(new Vector(0f, 0f, 5f), 9, 0f, 0.016f, out PendingTempEnt p),
            "a sprite record is made");

        Assert.True(p.Spawn.FrameMax == 7f && p.Spawn.SpriteScale == 1f && p.Spawn.Frame == 0f, "frames 0..7 at scale 1");
        Assert.True((p.Spawn.Flags & (int)TempEntFlags.SprAnimate) != 0, "animated");
        Assert.True(p.Spawn.FrameRate == 10f && p.Spawn.Die == 100.8f, $"ten a second for one pass: {p.Spawn.FrameRate}, {p.Spawn.Die}");

        Assert.True(rig.Spawns.Commit(in p) >= 0 && rig.Native.Created[0].Model == 9, "and it builds");
    }

    [Fact]
    public void DefaultSpriteMakesNothingWhilePausedOrForAModelThatIsNotASprite()
    {
        var paused = Rig();
        Assert.False(paused.Spawns.DefaultSprite(Vector.Zero, 9, 10f, 0f, out _), "not while the frame time is 0");

        var studio = Rig(kind: ModelType.Studio);
        Assert.False(studio.Spawns.DefaultSprite(Vector.Zero, 9, 10f, 0.016f, out _), "not for a studio model");
        Assert.Contains("No Sprite 9!", studio.Log.Text);
    }

    [Theory]
    [InlineData(ExplosionFlags.NoAdditive, (int)RenderMode.Normal, 255)]
    [InlineData(ExplosionFlags.DrawAlpha, (int)RenderMode.TransAlpha, 180)]
    [InlineData(0, (int)RenderMode.TransAdd, 180)]
    public void SpriteExplodeChoosesStocksBlendAndLiftsTheSprite(int flags, int mode, int alpha)
    {
        string stock = StockBody("Sprite_Explode");
        Has(stock, "if ( flags & TE_EXPLFLAG_NOADDITIVE ) { pTemp->SetRenderMode( kRenderNormal ); pTemp->SetRenderColorA( 255 );", "opaque");
        Has(stock, "else if( flags & TE_EXPLFLAG_DRAWALPHA ) { pTemp->SetRenderMode( kRenderTransAlpha ); pTemp->SetRenderColorA( 180 );", "alpha");
        Has(stock, "else { pTemp->SetRenderMode( kRenderTransAdd ); pTemp->SetRenderColorA( 180 );", "additive");
        Has(stock, "pTemp->SetVelocity( Vector( 0, 0, 8 ) );", "drifting up at 8");
        Has(stock, "pTemp->SetLocalOriginDim( Z_INDEX, pTemp->GetLocalOriginDim( Z_INDEX ) + 10 );", "lifted 10");

        var rig = Rig();
        Assert.True(rig.Spawns.DefaultSprite(new Vector(0f, 0f, 5f), 9, 10f, 0.016f, out PendingTempEnt p), "a record");
        rig.Spawns.SpriteExplode(ref p, 3f, flags);

        Assert.True(p.Spawn.RenderMode == mode && p.Spawn.ColorA == alpha, $"mode {p.Spawn.RenderMode}, alpha {p.Spawn.ColorA}");
        Assert.True(p.Spawn.RenderFx == 0 && p.Spawn.Velocity.Z == 8f && p.Origin.Z == 15f && p.Spawn.SpriteScale == 3f,
            "no fx, up at 8, lifted 10, at the scale");
        Assert.True(p.Spawn.Angles.Z == 0f, "and not rolled without ROTATE");
    }

    [Fact]
    public void SpriteExplodeRollsTheSpriteUnderRotate()
    {
        Has(StockBody("Sprite_Explode"), "SetLocalAnglesDim( Z_INDEX, random->RandomInt( 0, 360 ) );", "the roll");

        bool rolled = false;
        for (int seed = 0; seed < 4 && !rolled; seed++)
        {
            var rig = Rig();
            for (int i = 0; i < 8; i++)
            {
                Assert.True(rig.Spawns.DefaultSprite(Vector.Zero, 9, 10f, 0.016f, out PendingTempEnt p), "a record");
                rig.Spawns.SpriteExplode(ref p, 1f, ExplosionFlags.Rotate);
                Assert.True(p.Spawn.Angles.Z is >= 0f and <= 360f, $"a roll of 0..360: {p.Spawn.Angles.Z}");
                rolled |= p.Spawn.Angles.Z != 0f;
            }
        }
        Assert.True(rolled, "ROTATE rolled at least one of the sprites");
    }

    [Fact]
    public void SpriteSmokeIsADarkGreyRisingWindBlownSprite()
    {
        string stock = StockBody("Sprite_Smoke");
        Has(stock, "pTemp->SetRenderMode( kRenderTransAlpha );", "alpha");
        Has(stock, "pTemp->SetVelocity( Vector( 0, 0, 30 ) );", "rising at 30");
        Has(stock, "int iColor = random->RandomInt(20,35);", "a grey of 20..35");
        Has(stock, "GetLocalOriginDim( Z_INDEX ) + 20 );", "lifted 20");
        Has(stock, "pTemp->flags = FTENT_WINDBLOWN;", "the flags ASSIGNED");

        var rig = Rig();
        Assert.True(rig.Spawns.DefaultSprite(new Vector(0f, 0f, 5f), 9, 10f, 0.016f, out PendingTempEnt p), "a record");
        rig.Spawns.SpriteSmoke(ref p, 2f);

        TempEntSpawn s = p.Spawn;
        Assert.True(s.RenderMode == (int)RenderMode.TransAlpha && s.RenderFx == 0, "alpha, no fx");
        Assert.True(s.ColorR == s.ColorG && s.ColorG == s.ColorB && s.ColorR is >= 20 and <= 35 && s.ColorA == 255,
            $"one grey of 20..35, opaque: {s.ColorR} {s.ColorG} {s.ColorB} {s.ColorA}");
        Assert.True(s.Velocity.Z == 30f && p.Origin.Z == 25f && s.SpriteScale == 2f, "up at 30, lifted 20, at the scale");
        Assert.True(s.Flags == (int)TempEntFlags.WindBlown, $"exactly FTENT_WINDBLOWN (SPRANIMATE gone): 0x{s.Flags:x}");
    }

    [Fact]
    public void SpriteTrailSpacesItsSpritesEvenlyAndKicksEachOne()
    {
        string stock = StockBody("Sprite_Trail");
        Has(stock, "flAmplitude /= 256.0;", "amplitude in 256ths");
        Has(stock, "VectorMA( vecStart, i / (nCount - 1.0), vecDelta, vecPos );", "evenly spaced");
        Has(stock, "pTemp->flags |= FTENT_COLLIDEWORLD | FTENT_SPRCYCLE | FTENT_FADEOUT | FTENT_SLOWGRAVITY;", "the flags");
        Has(stock, "vecVel.x += random->RandomInt( -127,128 ) * flAmplitude;", "the kick");
        Has(stock, "pTemp->die = gpGlobals->curtime + flLife + random->RandomFloat( 0, 4 );", "the life");

        var rig = Rig(frames: 4);
        rig.Spawns.SpriteTrail(Vector.Zero, new Vector(10f, 0f, 0f), modelIndex: 3, count: 3, life: 2f,
                               size: 0.25f, amplitude: 256f, renderAmt: 150, speed: 100f);

        Assert.True(rig.Native.Created.Count == 3, $"three sprites: {rig.Native.Created.Count}");
        float[] xs = rig.Native.Created.Select(c => c.Origin.X).ToArray();
        Assert.True(xs[0] == 0f && xs[1] == 5f && xs[2] == 10f, $"at 0, 5 and 10: {string.Join(", ", xs)}");

        foreach (TempEntSpawn s in rig.Native.Spawned)
        {
            // amplitude 256 -> 1 per step, so each axis is kicked by -127..128.
            Assert.True(s.Velocity.X is >= 100f - 127f and <= 100f + 128f
                        && s.Velocity.Y is >= -127f and <= 128f && s.Velocity.Z is >= -127f and <= 128f,
                $"along the line at 100 with a kick of -127..128: {s.Velocity}");
            Assert.True(s.Flags == (int)(TempEntFlags.CollideWorld | TempEntFlags.SprCycle
                                         | TempEntFlags.FadeOut | TempEntFlags.SlowGravity), $"the flags: 0x{s.Flags:x}");
            Assert.True(s.RenderMode == (int)RenderMode.Glow && s.RenderFx == RenderFx.NoDissipation
                        && s.RenderAmt == 150 && s.SpriteScale == 0.25f, "glow at the amount and size");
            Assert.True(s.FrameMax == 3f && s.Frame is >= 0f and <= 3f, $"a random frame of 0..3: {s.Frame}");
            Assert.True(s.Die is >= 102f and <= 106f, $"dying 0..4 s after the life: {s.Die}");
        }
    }

    [Fact]
    public void RocketFlareIsAOneHundredthOfASecondGlowOnARandomFrame()
    {
        string stock = StockBody("RocketFlare");
        Has(stock, "engine->LoadModel( \"sprites/animglow01.vmt\" );", "the model");
        Has(stock, "pTemp->m_flFrameRate = 1.0;", "frame rate 1");
        Has(stock, "pTemp->m_flFrame = random->RandomInt( 0, nframeCount - 1);", "a random frame");
        Has(stock, "pTemp->die = gpGlobals->curtime + 0.01;", "a hundredth of a second");
        Assert.Equal("sprites/animglow01.vmt", TempEntSpawns.RocketFlareModel);

        var rig = Rig();
        int slot = rig.Pool.Reserve(TempEntPriority.Low);
        Assert.True(slot >= 0 && rig.Spawns.RocketFlareInto(slot, 6), "written into the reserved slot");

        TempEntSpawn s = rig.Native.Spawned[0];
        Assert.True(s.FrameMax == 5f && s.Frame is >= 0f and <= 5f && s.FrameRate == 1f, "frames 0..5 at 1 a second");
        Assert.True(s.RenderMode == (int)RenderMode.Glow && s.RenderFx == RenderFx.NoDissipation && s.RenderAmt == 255,
            "glow, full amount");
        Assert.True(s.SpriteScale == 1f && s.Die == 100.01f, $"scale 1, die at now + 0.01: {s.Die}");
    }

    [Fact]
    public void SpawnTempModelAddsTheCallersFlagsAndTumbles()
    {
        string stock = StockBody("SpawnTempModel");
        Has(stock, "pTemp->m_nBody = 0;", "body 0");
        Has(stock, "pTemp->flags |= iFlags;", "the flags ADDED");
        Has(stock, "pTemp->m_vecTempEntAngVelocity[0] = random->RandomFloat(-255,255);", "a tumble");
        Has(stock, "pTemp->die = gpGlobals->curtime + flLifeTime;", "the life");

        var rig = Rig();
        int slot = rig.Pool.Reserve(TempEntPriority.Low);
        Assert.True(slot >= 0 && rig.Spawns.SpawnTempModelInto(slot, new Vector(10f, 20f, 0f), new Vector(0f, 0f, 1f),
                                                               30f, (int)TempEntFlags.NeverDie), "written");

        TempEntSpawn s = rig.Native.Spawned[0];
        Assert.True(s.Angles.X == 10f && s.Angles.Y == 20f && s.Body == 0, "at the angles, body 0");
        Assert.True(s.Flags == (int)TempEntFlags.NeverDie, $"Prepare's none plus the caller's: 0x{s.Flags:x}");
        Assert.True(Math.Abs(s.AngularVelocity.X) <= 255f && Math.Abs(s.AngularVelocity.Y) <= 255f
                    && Math.Abs(s.AngularVelocity.Z) <= 255f && s.AngularVelocity.X != 0f, $"tumbling: {s.AngularVelocity}");
        Assert.True(s.RenderMode == (int)RenderMode.Normal && s.RenderAmt == 255, "opaque");
        Assert.True(s.Velocity.Z == 1f && s.Die == 130f, $"moving as asked, for the life: {s.Die}");
    }

    /// <summary>
    /// HL1EjectBrass and CSEjectBrass do NOTHING in an hl2mp client, in stock
    /// too: each picks its model only under its own mod's define and returns on
    /// a NULL model, and the client defines neither. The thunk's warn-and-draw-
    /// nothing stub is therefore stock's behaviour, not a gap.
    /// </summary>
    [Fact]
    public void TheOtherModsEjectBrassArmsDoNothingInAnHl2mpClientInStockToo()
    {
        string hl1 = StockBody("HL1EjectBrass");
        Has(hl1, "#if defined( HL1_CLIENT_DLL )", "the model is HL1's alone");
        Has(hl1, "if ( pModel == NULL ) return;", "and nothing without it");
        Assert.True(hl1.IndexOf("pModel=m_pHL1Shell", StringComparison.Ordinal)
                    < hl1.IndexOf("#endif", StringComparison.Ordinal), "the only model assignment is inside the #if");

        string cs = StockBody("CSEjectBrass");
        Has(cs, "#if defined ( CSTRIKE_DLL ) || defined ( SDK_DLL )", "the model is CS's or the SDK's alone");
        Has(cs, "if ( pModel == NULL ) return;", "and nothing without it");

        // That the client defines none of the three is
        // ClientTempEntDefinesTests.TheClientIsNotTheHl1CsOrSdkClient's half.
    }

    // =====================================================================
    // SpawnTempModel's one hl2mp caller: c_stickybolt.cpp's BoltImpact.
    // =====================================================================

    private sealed class Spawner : ITempModelSpawner
    {
        public List<(string Model, Vector Origin, Vector Angles, Vector Velocity, float Life, int Flags)> Calls { get; } = new();

        public int SpawnTempModel(string model, in Vector origin, in Vector angles, in Vector velocity, float life, int flags)
        {
            Calls.Add((model, origin, angles, velocity, life, flags));
            return Calls.Count - 1;
        }
    }

    private static List<(string Model, Vector Origin, Vector Angles, Vector Velocity, float Life, int Flags)> Bolt(
        int maxClients, int surfaceFlags, Vector origin, Vector normal)
    {
        var engine = TestServices.Fake();
        nint raw = System.Runtime.InteropServices.Marshal.AllocHGlobal(
            System.Runtime.InteropServices.Marshal.SizeOf<CGlobalVarsRaw>());
        try
        {
            for (int i = 0; i < System.Runtime.InteropServices.Marshal.SizeOf<CGlobalVarsRaw>(); i++)
                System.Runtime.InteropServices.Marshal.WriteByte(raw, i, 0);
            System.Runtime.InteropServices.Marshal.WriteInt32(raw,
                (int)System.Runtime.InteropServices.Marshal.OffsetOf<CGlobalVarsRaw>(nameof(CGlobalVarsRaw.maxClients)),
                maxClients);

            engine.Returns("Environment.Globals", new Globals(raw));
            engine.Returns("WorldQuery.ClientTraceLine",
                new ClientTraceResult(true, 0.5f, origin, Vector.Zero, 0, 0, 0, surfaceFlags));

            var spawner = new Spawner();
            var effect = new client::SourceSharp.Client.Effects.CrossbowBoltEffect(spawner, engine, engine, new RecordingLog());
            Assert.Equal("BoltImpact", effect.EffectName);

            effect.Dispatch(new EffectData { Origin = origin, Normal = normal });
            return spawner.Calls;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(raw);
        }
    }

    [Fact]
    public void BoltImpactLeavesStocksBoltEightUnitsBackPointingAlongTheShot()
    {
        string bolt = Regex.Replace(Regex.Replace(ValveSdk.Read("src/game/client/c_stickybolt.cpp"), @"//[^\n]*", ""), @"\s+", "");
        Has(bolt, "DECLARE_CLIENT_EFFECT( \"BoltImpact\", StickyBoltCallback );", "the effect name");
        Has(bolt, "engine->LoadModel( \"models/crossbow_bolt.mdl\" );", "the model");
        Has(bolt, "if ( gpGlobals->maxClients > 1 ) { tempents->SpawnTempModel( pModel, vecOrigin - vecDirection * 8, vAngles, Vector(0, 0, 0 ), 30.0f, FTENT_NONE );",
            "30 seconds in multiplayer");
        Has(bolt, "tempents->SpawnTempModel( pModel, vecOrigin - vecDirection * 8, vAngles, Vector(0, 0, 0 ), 1, FTENT_NEVERDIE );",
            "forever in single player");
        Assert.Equal("models/crossbow_bolt.mdl", client::SourceSharp.Client.Effects.CrossbowBoltEffect.BoltModel);

        var mp = Bolt(maxClients: 16, surfaceFlags: 0, new Vector(100f, 0f, 0f), new Vector(0f, 1f, 0f));
        Assert.True(mp.Count == 1, $"one bolt: {mp.Count}");
        Assert.True(mp[0].Model == "models/crossbow_bolt.mdl", "the bolt model");
        Assert.True(mp[0].Origin.X == 100f && mp[0].Origin.Y == -8f && mp[0].Origin.Z == 0f, $"8 units back: {mp[0].Origin}");
        Assert.True(mp[0].Angles.X == 0f && mp[0].Angles.Y == 90f, $"pointing along +Y: {mp[0].Angles}");
        Assert.True(mp[0].Life == 30f && mp[0].Flags == (int)TempEntFlags.None, "for 30 s, no flags");
        Assert.True(mp[0].Velocity.X == 0f && mp[0].Velocity.Y == 0f && mp[0].Velocity.Z == 0f, "still");

        var sp = Bolt(maxClients: 1, surfaceFlags: 0, Vector.Zero, new Vector(1f, 0f, 0f));
        Assert.True(sp.Count == 1 && sp[0].Life == 1f && sp[0].Flags == (int)TempEntFlags.NeverDie,
            "single player: life 1, FTENT_NEVERDIE");
    }

    [Fact]
    public void BoltImpactOnTheSkyLeavesNothing()
    {
        string bolt = Regex.Replace(Regex.Replace(ValveSdk.Read("src/game/client/c_stickybolt.cpp"), @"//[^\n]*", ""), @"\s+", "");
        Has(bolt, "UTIL_TraceLine( vecOrigin, vecOrigin + vecDirection * 16, MASK_SOLID_BRUSHONLY, NULL, COLLISION_GROUP_NONE, &tr );",
            "a 16-unit brush trace");
        Has(bolt, "if ( tr.surface.flags & SURF_SKY ) return;", "out on the sky, before the bolt");

        Assert.True(Bolt(16, surfaceFlags: 0x0004, Vector.Zero, new Vector(1f, 0f, 0f)).Count == 0, "no bolt in the sky");
        Assert.True(Bolt(16, surfaceFlags: 0x0002, Vector.Zero, new Vector(1f, 0f, 0f)).Count == 1, "but one on any other surface");
    }
}
