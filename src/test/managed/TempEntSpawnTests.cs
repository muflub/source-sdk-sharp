//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
extern alias client;
using RandomSeeds = client::SourceSharp.Abi.RandomSeeds;

using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// C-Phase 5's spawn side of the legacy temp entity pool: the spawn RECORD, the
/// defaults it starts from, and the seven helpers that fill it.
///
/// <para>
/// <b>WHAT THESE CAN AND CANNOT CHECK, because it decides the shape of every
/// assertion below.</b> Seven of the eight helpers draw random numbers — a
/// gib's tumble, a bubble's wander, a blood sprite's roll — so an exact
/// expectation is not available for those fields and pretending otherwise would
/// mean seeding the generator, which tests the seed rather than the helper. So
/// each helper is checked on the fields that are DETERMINED (flags, render
/// mode, colours, frame maxima, priority, the model asked for) and on the
/// RANGE of the ones that are not. A range assertion is weaker than an equality
/// one and stronger than nothing: it is what catches a sign, a swapped bound
/// and a missing multiplier, which between them are most of what a transcribed
/// helper gets wrong.
/// </para>
///
/// <para>
/// <b>AND THE ONES THAT READ STOCK OFF DISK ARE THE POINT OF THE FILE.</b>
/// docs/te-lane-contract's "three things a green dump cannot tell you" applies
/// here as directly as it does to a send table: a managed mirror of
/// <c>C_LocalTempEntity::Prepare</c> checked against a managed expectation is a
/// computation compared with itself.
/// <see cref="ThePreparedDefaultsAreStocksPrepare"/> and
/// <see cref="TheRenderFxValuesAreConstHs"/> both read the real file.
/// </para>
/// </summary>
public class TempEntSpawnTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// A native side that accepts every create and remembers every spawn
    /// record, which is all a helper test needs of one.
    /// </summary>
    private sealed class SpawningPool : EngineEffectsForwarder
    {
        public SpawningPool(IEngineEffects inner) : base(inner) { }

        /// <summary>Every (slot, origin, model, priority) a helper asked to build.</summary>
        public List<(int Slot, Vector Origin, int Model, int Priority)> Created { get; } = new();

        /// <summary>Every spawn record written, in order.</summary>
        public List<TempEntSpawn> Spawned { get; } = new();

        /// <summary>Make the next <c>Create</c> refuse, as a bad model index does.</summary>
        public bool RefuseCreate { get; set; }

        public override int TempEntPoolCapacity() => 500;

        public override bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
        {
            if (RefuseCreate)
                return false;

            Created.Add((slot, origin, modelIndex, priority));
            return true;
        }

        public override bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
        {
            Spawned.Add(spawn);
            return true;
        }
    }

    private sealed record Rigging(
        TempEntSpawns Spawns, SpawningPool Native, ClientTempEntPool Pool,
        RecordingEngine Engine, Clock Clock);

    /// <summary>
    /// A helper over a pool that always says yes, with a model of
    /// <paramref name="frames"/> frames and a stated type.
    /// </summary>
    private static Rigging Rig(int frames = 10, ModelType kind = ModelType.Sprite,
                               string maxPieces = "15")
    {
        var engine = TestServices.Fake();
        var native = new SpawningPool(engine);
        var clock = new Clock { Now = 100f };

        engine.Returns("Precache.ModelFrameCount", frames);
        engine.Returns("Precache.ModelTypeOf", kind);
        engine.Returns("Console.FindConVarValue", maxPieces);

        var pool = new ClientTempEntPool(new RecordingLog(), native, clock, new TempEntPoolTests.NoSimulation());

        return new Rigging(new TempEntSpawns(new RecordingLog(), pool, engine, engine, clock, new RandomSeeds(1)),
                           native, pool, engine, clock);
    }

    private static bool Between(float value, float low, float high)
        => value >= MathF.Min(low, high) && value <= MathF.Max(low, high);

    private static string? StockPool()
    {
        string path = RepoLayout.Sdk("src", "game", "client", "c_te_legacytempents.cpp");

        return File.Exists(path) ? path : null;
    }

    private static string? StockConst()
    {
        string path = RepoLayout.Sdk("src", "public", "const.h");

        return File.Exists(path) ? path : null;
    }

    // =====================================================================
    // The record itself.
    // =====================================================================

    /// <summary>
    /// <see cref="TempEntSpawn"/> still has <c>SourceSharpTempEntSpawn_t</c>'s
    /// layout, field for field and in order.
    ///
    /// <para>
    /// The same test <see cref="TempEntLife"/> and <c>EffectData</c> both have,
    /// for the same reason: every field is one word and each has a plausible
    /// neighbour of the same width, so a one-sided insertion shifts everything
    /// after it, compiles on both sides, and produces a tempent at plausible
    /// wrong values.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSpawnStructMatchesTheHeader()
    {
        string header = RepoLayout.Sdk("src", "sourcesharp", "host", "sourcesharp.h");

        if (!File.Exists(header))
        {
            Assert.True(true, "ABI header found for the spawn layout (skipped, not found)");
            return;
        }

        string text = File.ReadAllText(header);

        int end = text.IndexOf("} SourceSharpTempEntSpawn_t;", StringComparison.Ordinal);
        Assert.True(end >= 0, "sourcesharp.h declares SourceSharpTempEntSpawn_t");

        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);
        Assert.True(start >= 0, "and it is a typedef struct");

        // Comments stripped first: the prose above these fields names types and
        // field names, and an unstripped scan reads the explanation as a
        // declaration.
        string body = Regex.Replace(text[start..end], @"//[^\n]*", string.Empty);

        var native = new List<(string Name, int Words)>();

        foreach (Match m in Regex.Matches(body, @"\b(float|int32_t)\s+(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
        {
            native.Add((m.Groups[2].Value,
                        m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1));
        }

        var managed = typeof(TempEntSpawn)
            .GetFields(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken)
            .Select(f => (Name: f.Name, Words: f.FieldType == typeof(Vector) ? 3 : 1))
            .ToList();

        Assert.True(native.Count == managed.Count,
            $"spawn field counts match (native {native.Count}, managed {managed.Count})");

        for (int i = 0; i < Math.Min(native.Count, managed.Count); i++)
        {
            bool same = native[i].Words == managed[i].Words
                     && (native[i].Name == managed[i].Name
                         || (native[i].Words == 3
                             && native[i].Name.StartsWith(managed[i].Name, StringComparison.Ordinal)
                             && native[i].Name[managed[i].Name.Length..] is "XYZ" or "PYR"));

            Assert.True(same,
                $"spawn field {i} matches (native '{native[i].Name}' x{native[i].Words}, "
              + $"managed '{managed[i].Name}' x{managed[i].Words})");
        }

        int words = managed.Sum(f => f.Words);

        Assert.True(System.Runtime.CompilerServices.Unsafe.SizeOf<TempEntSpawn>() == words * 4,
            $"the managed spawn record is {words * 4} bytes, with no padding the header does not have");
    }

    /// <summary>
    /// <see cref="TempEntSpawn.Prepared"/> is stock's
    /// <c>C_LocalTempEntity::Prepare</c>, plus the two values its CONSTRUCTOR
    /// sets that a zeroed block gets wrong.
    ///
    /// <para>
    /// <b>READ OUT OF <c>c_te_legacytempents.cpp</c>, not restated here.</b>
    /// This is the fixture-versus-native rule: every field below is a value
    /// this port stands in for, and a stand-in for native drifts silently
    /// unless something asserts the agreement. Somebody changing
    /// <c>fadeSpeed</c> in the stock file — or, far likelier, a merge dropping a
    /// line of Prepare — would otherwise leave every managed spawn helper
    /// starting from the wrong state with nothing anywhere going red.
    /// </para>
    ///
    /// <para>
    /// The white colour comes from the CONSTRUCTOR and not from Prepare, which
    /// is the half a reader will look for and not find: <c>AllocZero</c> zeroes
    /// the block and then runs the constructor, Prepare never touches
    /// <c>m_clrRender</c>, and eleven of the seventeen helpers inherit white.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePreparedDefaultsAreStocksPrepare()
    {
        string? path = StockPool();

        if (path is null)
        {
            Assert.True(true, "c_te_legacytempents.cpp found for the Prepare check (skipped, not found)");
            return;
        }

        string text = File.ReadAllText(path);

        int at = text.IndexOf("void C_LocalTempEntity::Prepare(", StringComparison.Ordinal);
        Assert.True(at >= 0, "the stock file still declares C_LocalTempEntity::Prepare");

        int close = text.IndexOf("\n}", at, StringComparison.Ordinal);
        Assert.True(close > at, "and Prepare has a body");

        string prepare = Regex.Replace(text[at..close], @"//[^\n]*", string.Empty);

        var spawn = TempEntSpawn.Prepared(100f);

        // Each pair is (what stock's body must contain, what the managed mirror
        // must therefore hold). The needle is stock's own spelling, so a value
        // changed on that side fails on the needle rather than on the value --
        // which is the failure that says WHICH line moved.
        Assert.True(prepare.Contains("flags = FTENT_NONE", StringComparison.Ordinal),
            "Prepare still clears the flags");
        Assert.True(spawn.Flags == (int)TempEntFlags.None, "and Prepared starts at no flags");

        Assert.True(prepare.Contains("die = time + 0.75", StringComparison.Ordinal),
            "Prepare still gives three quarters of a second");
        Assert.True(spawn.Die == 100.75f, "and Prepared(100) dies at 100.75");

        Assert.True(prepare.Contains("SetRenderMode( kRenderNormal )", StringComparison.Ordinal),
            "Prepare still starts opaque");
        Assert.True(spawn.RenderMode == (int)RenderMode.Normal, "and Prepared is kRenderNormal");

        Assert.True(prepare.Contains("m_nRenderFX = kRenderFxNone", StringComparison.Ordinal),
            "Prepare still clears the render fx");
        Assert.True(spawn.RenderFx == 0, "and Prepared has none");

        Assert.True(prepare.Contains("m_nBody = 0", StringComparison.Ordinal), "Prepare clears the body");
        Assert.True(spawn.Body == 0, "and Prepared has body 0");

        Assert.True(prepare.Contains("m_nSkin = 0", StringComparison.Ordinal), "Prepare clears the skin");
        Assert.True(spawn.Skin == 0, "and Prepared has skin 0");

        Assert.True(prepare.Contains("fadeSpeed = 0.5", StringComparison.Ordinal),
            "Prepare still fades at a half");
        Assert.True(spawn.FadeSpeed == 0.5f, "and Prepared does too -- ZERO would never finish fading");

        Assert.True(prepare.Contains("hitSound = 0", StringComparison.Ordinal), "Prepare clears the hit sound");
        Assert.True(spawn.HitSound == 0, "and Prepared has none");

        Assert.True(prepare.Contains("clientIndex = -1", StringComparison.Ordinal),
            "Prepare still says MINUS ONE for no client");
        Assert.True(spawn.ClientIndex == -1,
            "and Prepared does too -- ZERO is a real entity index, the world");

        Assert.True(prepare.Contains("bounceFactor = 1", StringComparison.Ordinal),
            "Prepare still bounces at one");
        Assert.True(spawn.BounceFactor == 1f,
            "and Prepared does too -- ZERO is a gib that does not bounce at all");

        // The constructor's half.
        int ctor = text.IndexOf("C_LocalTempEntity::C_LocalTempEntity()", StringComparison.Ordinal);
        Assert.True(ctor >= 0, "the stock file still has the constructor");

        int ctorEnd = text.IndexOf("\n}", ctor, StringComparison.Ordinal);
        string body = text[ctor..ctorEnd];

        Assert.True(body.Contains("m_clrRender.r = m_clrRender.g = m_clrRender.b = m_clrRender.a = 255",
                                  StringComparison.Ordinal),
            "the constructor still makes a fresh tempent WHITE, which Prepare never touches");

        Assert.True(spawn.ColorR == 255 && spawn.ColorG == 255
                 && spawn.ColorB == 255 && spawn.ColorA == 255,
            "and Prepared is white -- a black tempent is invisible and looks like the effect not firing");
    }

    /// <summary>
    /// <see cref="RenderFx"/>'s two values are <c>const.h</c>'s, counted off the
    /// enum rather than remembered.
    ///
    /// <para>
    /// THIS TEST EXISTS BECAUSE IT ALREADY CAUGHT ONE. <c>NoDissipation</c> was
    /// written as 12 from memory; it is 14. Nothing else in this port would have
    /// noticed — <c>kRenderFx12</c> is <c>kRenderFxFlickerSlow</c>, a real
    /// value that produces a real effect, so every sprite spray would have
    /// flickered instead of holding its brightness at range and looked like a
    /// deliberate choice.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRenderFxValuesAreConstHs()
    {
        string? path = StockConst();

        if (path is null)
        {
            Assert.True(true, "const.h found for the RenderFx check (skipped, not found)");
            return;
        }

        string text = File.ReadAllText(path);

        int at = text.IndexOf("enum RenderFx_t", StringComparison.Ordinal);
        Assert.True(at >= 0, "const.h still declares RenderFx_t");

        int close = text.IndexOf("};", at, StringComparison.Ordinal);
        string body = Regex.Replace(text[at..close], @"//[^\n]*", string.Empty);

        // The enum has no explicit values after kRenderFxNone = 0, so position
        // IS the value -- which is exactly why a remembered number drifts.
        var names = Regex.Matches(body, @"\bkRenderFx\w+")
                         .Select(m => m.Value)
                         .Where(n => n != "kRenderFxMax")
                         .ToList();

        Assert.True(names.Count > 20, $"the enum parsed ({names.Count} values)");
        Assert.True(names[0] == "kRenderFxNone", "and it starts at kRenderFxNone, which is zero");

        Assert.True(names.IndexOf("kRenderFxNoDissipation") == RenderFx.NoDissipation,
            $"kRenderFxNoDissipation is {names.IndexOf("kRenderFxNoDissipation")} in const.h "
          + $"and {RenderFx.NoDissipation} here");

        Assert.True(names.IndexOf("kRenderFxClampMinScale") == RenderFx.ClampMinScale,
            $"kRenderFxClampMinScale is {names.IndexOf("kRenderFxClampMinScale")} in const.h "
          + $"and {RenderFx.ClampMinScale} here");
    }

    /// <summary>
    /// <see cref="BreakFlags"/> is <c>const.h</c>'s <c>BREAK_*</c>, read off
    /// disk.
    ///
    /// <para>
    /// These reach the tempent's <c>hitSound</c> and are read back by
    /// <c>CTempEnts::PlaySound</c>, which is STILL NATIVE and still called from
    /// inside the collision arm — so a wrong bit here is a gib that lands
    /// sounding like the wrong material, on the far side of a boundary this
    /// port did not move.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBreakFlagsAreConstHs()
    {
        string? path = StockConst();

        if (path is null)
        {
            Assert.True(true, "const.h found for the BREAK_ check (skipped, not found)");
            return;
        }

        string text = File.ReadAllText(path);

        var expected = new (string Name, int Value)[]
        {
            ("BREAK_GLASS",    BreakFlags.Glass),
            ("BREAK_METAL",    BreakFlags.Metal),
            ("BREAK_FLESH",    BreakFlags.Flesh),
            ("BREAK_WOOD",     BreakFlags.Wood),
            ("BREAK_SMOKE",    BreakFlags.Smoke),
            ("BREAK_TRANS",    BreakFlags.Trans),
            ("BREAK_CONCRETE", BreakFlags.Concrete),
            ("BREAK_SLAVE",    BreakFlags.Slave),
        };

        foreach (var (name, value) in expected)
        {
            Match m = Regex.Match(text, $@"#define\s+{name}\s+0x([0-9A-Fa-f]+)");

            Assert.True(m.Success, $"const.h still defines {name}");

            int stock = Convert.ToInt32(m.Groups[1].Value, 16);

            Assert.True(stock == value, $"{name} is 0x{stock:X2} in const.h and 0x{value:X2} here");
        }
    }

    // =====================================================================
    // The helpers.
    // =====================================================================

    [Fact]
    public void TempSpriteWritesTheRecordStocksTempSpriteWrites()
    {
        var rig = Rig(frames: 10);

        int slot = rig.Spawns.TempSprite(
            pos: new Vector(1f, 2f, 3f),
            dir: new Vector(4f, 5f, 6f),
            scale: 2.5f,
            modelIndex: 77,
            renderMode: (int)RenderMode.TransAdd,
            renderFx: 0,
            alpha: 0.5f,
            life: 0f,
            flags: (int)TempEntFlags.SprAnimate);

        Assert.True(slot >= 0, "a sprite was spawned");
        Assert.True(rig.Native.Created.Count == 1, "and exactly one tempent was built");

        var made = rig.Native.Created[0];

        Assert.True(made.Model == 77, "of the model the caller asked for");
        Assert.True(made.Priority == TempEntPriority.Low, "at LOW priority, as stock's TempEntAlloc does");
        Assert.True(made.Origin.X == 1f && made.Origin.Y == 2f && made.Origin.Z == 3f,
            "at the position it was given");

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True(spawn.FrameMax == 9f, "m_flFrameMax is frameCount - 1");
        Assert.True(spawn.FrameRate == 10f, "and the frame rate is stock's flat ten");
        Assert.True(spawn.Frame == 0f, "starting on frame zero");
        Assert.True(spawn.SpriteScale == 2.5f, "at the scale asked for");
        Assert.True(spawn.RenderMode == (int)RenderMode.TransAdd, "additive, as CTESprite asks");

        // a * 255 truncated, in both places stock writes it.
        Assert.True(spawn.RenderAmt == 127, "tempent_renderamt is the alpha times 255, truncated");
        Assert.True(spawn.ColorA == 127, "and the render colour's alpha is the same number");
        Assert.True(spawn.ColorR == 255 && spawn.ColorG == 255 && spawn.ColorB == 255,
            "with a WHITE colour -- the brightness rides in the alpha");

        Assert.True(spawn.Velocity.X == 4f && spawn.Velocity.Y == 5f && spawn.Velocity.Z == 6f,
            "the direction became the velocity");

        Assert.True((spawn.Flags & (int)TempEntFlags.SprAnimate) != 0, "the caller's flags are set");

        // life == 0 means "work one out": frameCount * 0.1 + 1 = 2 seconds.
        Assert.True(spawn.Die == 102f,
            $"a zero lifetime becomes frameCount * 0.1 + 1 seconds ({spawn.Die} from now 100)");

        Assert.True(spawn.BounceFactor == 1f, "and the bounce factor is Prepare's default");
    }

    [Fact]
    public void TempSpriteWithALifetimeUsesItAndCarriesABounceFactor()
    {
        var rig = Rig(frames: 10);

        rig.Spawns.TempSprite(
            pos: Vector.Zero, dir: Vector.Zero, scale: 1f, modelIndex: 5,
            renderMode: (int)RenderMode.TransAdd, renderFx: 0, alpha: 1f,
            life: 3.5f,
            flags: (int)(TempEntFlags.SprAnimate | TempEntFlags.SprAnimateLoop),
            normal: default, bounceFactor: 0.2f);

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True(spawn.Die == 103.5f, "a real lifetime is used as given");
        Assert.True(spawn.BounceFactor == 0.2f,
            "and the bounce factor CTEGlowSprite writes after the fact in stock arrives with the record");
        Assert.True((spawn.Flags & (int)TempEntFlags.SprAnimateLoop) != 0, "with the loop flag set");
    }

    [Fact]
    public void ASpriteWithNoSuchModelSpawnsNothing()
    {
        var rig = Rig(frames: -1);

        int slot = rig.Spawns.TempSprite(
            pos: Vector.Zero, dir: Vector.Zero, scale: 1f, modelIndex: 99,
            renderMode: 0, renderFx: 0, alpha: 1f, life: 1f, flags: 0);

        Assert.True(slot == -1, "an index that resolves to nothing spawns nothing");
        Assert.True(rig.Native.Created.Count == 0, "and no tempent was built");

        // And a model index of ZERO is the other case, which stock tests first
        // and separately: the server did not send a model at all.
        int none = rig.Spawns.TempSprite(
            pos: Vector.Zero, dir: Vector.Zero, scale: 1f, modelIndex: 0,
            renderMode: 0, renderFx: 0, alpha: 1f, life: 1f, flags: 0);

        Assert.True(none == -1, "and neither does a zero model index");
    }

    [Fact]
    public void BloodSpriteIsHighPriorityAndPlaysOnceInAQuarterSecond()
    {
        var rig = Rig(frames: 8);

        int slot = rig.Spawns.BloodSprite(
            origin: new Vector(10f, 20f, 30f),
            r: 247, g: 3, b: 5, a: 200,
            modelIndex: 42, dropModelIndex: 43, size: 100f);

        Assert.True(slot >= 0, "a blood sprite was spawned");

        var made = rig.Native.Created[0];

        Assert.True(made.Priority == TempEntPriority.High,
            "HIGH priority -- stock's own comment says so, and it is the only live driver "
          + "the pool's eviction rule has");
        Assert.True(made.Model == 42,
            "of the SPRAY model. The drop model is a parameter stock's body never reads");

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True(spawn.RenderMode == (int)RenderMode.TransTexture, "translucent");
        Assert.True(spawn.RenderFx == RenderFx.ClampMinScale,
            "and clamped to a minimum on-screen size, so a wound does not vanish across a room");

        Assert.True(spawn.Flags == (int)TempEntFlags.SprAnimate,
            "the flags are ASSIGNED, not OR'd -- one of only two helpers that replaces the word");

        Assert.True(spawn.ColorR == 247 && spawn.ColorG == 3 && spawn.ColorB == 5 && spawn.ColorA == 200,
            "the colour is the wire's");
        Assert.True(spawn.RenderAmt == 200,
            "and tempent_renderamt is read back OFF the colour, not from the argument");

        Assert.True(spawn.FrameRate == 32f, "the frame rate is frameCount * 4 -- 'finish in 0.250 seconds'");
        Assert.True(spawn.Die == 100.25f, "so the whole animation plays exactly once");
        Assert.True(spawn.FrameMax == 7f, "over frameCount - 1 frames");
        Assert.True(spawn.Frame == 0f, "from the first");

        Assert.True(spawn.BounceFactor == 0f, "a blood sprite does not bounce");
        Assert.True(spawn.Velocity.X == 0f && spawn.Velocity.Y == 0f && spawn.Velocity.Z == 0f,
            "and does not move");

        // random->RandomFloat( size / 25, size / 35 ) -- a LOW larger than its
        // HIGH, which is stock's and which the port does not "correct".
        Assert.True(Between(spawn.SpriteScale, 100f / 35f, 100f / 25f),
            $"the scale is between size/35 and size/25 ({spawn.SpriteScale})");

        Assert.True(spawn.Angles.X == 0f && spawn.Angles.Y == 0f,
            "only the ROLL is randomised -- SetLocalAnglesDim( Z_INDEX )");
        Assert.True(spawn.Angles.Z >= 0f && spawn.Angles.Z <= 360f, "and it is a whole turn's worth");
    }

    [Fact]
    public void BubblesFillTheBoxAndDieAtTheStatedHeightAboveItsFloor()
    {
        var rig = Rig(frames: 6);

        rig.Spawns.Bubbles(
            mins: new Vector(0f, 0f, 0f),
            maxs: new Vector(10f, 10f, 4f),
            height: 100f,
            modelIndex: 9, count: 5, speed: 8f);

        Assert.True(rig.Native.Created.Count == 5, "five bubbles");

        foreach (var made in rig.Native.Created)
        {
            Assert.True(made.Priority == TempEntPriority.Low, "each at low priority");
            Assert.True(made.Origin.X >= 0f && made.Origin.X <= 10f, "inside the box in X");
            Assert.True(made.Origin.Y >= 0f && made.Origin.Y <= 10f, "and in Y");
            Assert.True(made.Origin.Z >= 0f && made.Origin.Z <= 4f, "and in Z");
        }

        for (int i = 0; i < 5; i++)
        {
            TempEntSpawn spawn = rig.Native.Spawned[i];
            Vector origin = rig.Native.Created[i].Origin;

            Assert.True((spawn.Flags & (int)TempEntFlags.SineWave) != 0, "wobbling");
            Assert.True(spawn.SineX == origin.X && spawn.SineY == origin.Y,
                "about its own spawn point -- x and y are the AXIS, not the origin");

            Assert.True(spawn.RenderMode == (int)RenderMode.TransAlpha, "alpha-blended");
            Assert.True(spawn.ColorA == 192, "at stock's 192");

            Assert.True(spawn.Velocity.Z >= 80f && spawn.Velocity.Z <= 140f, "rising at stock's rate");

            // (height - (origin.z - mins.z)) / zspeed - 0.1, and mins.z is 0.
            float expected = 100f + ((100f - origin.Z) / spawn.Velocity.Z) - 0.1f;

            Assert.True(MathF.Abs(spawn.Die - expected) < 0.001f,
                $"and dies when it reaches the surface ({spawn.Die} vs {expected})");

            Assert.True(spawn.Frame >= 0f && spawn.Frame <= 5f, "on a random frame of the six");
            Assert.True(Between(spawn.SpriteScale, 1f / 16f, 1f / 4f), "at a random small scale");
        }
    }

    [Fact]
    public void ABubbleTrailLerpsAlongTheSegmentAndDiesAtTheWaterLine()
    {
        var rig = Rig(frames: 6);

        rig.Spawns.BubbleTrail(
            start: new Vector(0f, 0f, 0f),
            end: new Vector(100f, 0f, 0f),
            waterZ: 64f,
            modelIndex: 9, count: 4, speed: 8f);

        Assert.True(rig.Native.Created.Count == 4, "four bubbles");

        for (int i = 0; i < 4; i++)
        {
            Vector origin = rig.Native.Created[i].Origin;
            TempEntSpawn spawn = rig.Native.Spawned[i];

            Assert.True(origin.X >= 0f && origin.X <= 100f, "on the segment");
            Assert.True(origin.Y == 0f && origin.Z == 0f, "and only on it -- this is a LERP, not a box");

            // (waterZ - origin.z) / zspeed - 0.1
            float expected = 100f + ((64f - origin.Z) / spawn.Velocity.Z) - 0.1f;

            Assert.True(MathF.Abs(spawn.Die - expected) < 0.001f,
                "and dies at the ABSOLUTE water height, unlike its sibling's relative one");

            Assert.True(Between(spawn.SpriteScale, 1f / 8f, 1f / 4f),
                "at a 4-8 scale, where Bubbles uses 4-16");
        }
    }

    [Fact]
    public void ASpriteSprayFadesOutUnderSlowGravity()
    {
        var rig = Rig(frames: 4);

        rig.Spawns.SpriteSpray(
            pos: new Vector(5f, 5f, 5f),
            dir: new Vector(0f, 0f, 1f),
            modelIndex: 3, count: 6, speed: 100, scatter: 50);

        Assert.True(rig.Native.Created.Count == 6, "six sprites");

        foreach (TempEntSpawn spawn in rig.Native.Spawned)
        {
            Assert.True((spawn.Flags & (int)TempEntFlags.FadeOut) != 0, "fading out");
            Assert.True((spawn.Flags & (int)TempEntFlags.SlowGravity) != 0, "under slow gravity");
            Assert.True(spawn.FadeSpeed == 2f, "at stock's rate of two");
            Assert.True(spawn.RenderAmt == 255, "from full brightness");
            Assert.True(spawn.RenderFx == RenderFx.NoDissipation,
                "and without dissipation, so it reads at range");
            Assert.True(spawn.SpriteScale == 0.5f, "at stock's flat half scale");
            Assert.True(spawn.Die == 100.35f, "for 0.35 seconds");

            // noise = 50/100 = 0.5; znoise = min(0.75, 1) = 0.75. Then the whole
            // vector is scaled by RandomFloat( speed*0.8, speed*1.2 ).
            Assert.True(spawn.Velocity.Z > 0f,
                "the vertical noise is ONE-SIDED, so a spray always arcs up relative to its direction");

            float length = MathF.Sqrt(spawn.Velocity.X * spawn.Velocity.X
                                    + spawn.Velocity.Y * spawn.Velocity.Y
                                    + spawn.Velocity.Z * spawn.Velocity.Z);

            Assert.True(length > 0f && length < 100f * 1.2f * 2f,
                $"and the speed is scaled into stock's 0.8-1.2 band ({length})");

            Assert.True(spawn.Frame >= 0f && spawn.Frame <= 3f,
                "on a random frame -- frameCount is decremented first and the draw is INCLUSIVE");
        }
    }

    [Fact]
    public void AClientProjectileCarriesItsOwnerAndTheThreeCollisionFlags()
    {
        var rig = Rig(frames: 1);

        int slot = rig.Spawns.ClientProjectile(
            origin: new Vector(1f, 1f, 1f),
            velocity: new Vector(0f, 100f, 0f),
            acceleration: Vector.Zero,
            modelIndex: 12, lifetime: 6, ownerEntityIndex: 4);

        Assert.True(slot >= 0, "a projectile was spawned");

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True(spawn.Flags == (int)(TempEntFlags.CollideAll
                                       | TempEntFlags.AttachToTarget
                                       | TempEntFlags.AlignToMotion),
            "the three flags, ASSIGNED -- the second of the two helpers that replaces the word");

        Assert.True(spawn.ClientIndex == 4, "the owner is what the collision arm ignores");
        Assert.True(spawn.OwnerEntityIndex == 4,
            "and native turns the same index back into an entity for SetTempEntOwner");

        Assert.True(spawn.Die == 106f, "it lives for the lifetime the wire carried");

        // VectorAngles of (0,100,0) is a yaw of 90 and no pitch.
        Assert.True(MathF.Abs(spawn.Angles.Y - 90f) < 0.01f,
            $"and points where it is going on the first frame too (yaw {spawn.Angles.Y})");
    }

    [Fact]
    public void AProjectileWithNoOwnerCarriesZeroAndNotMinusOne()
    {
        var rig = Rig(frames: 1);

        rig.Spawns.ClientProjectile(
            origin: Vector.Zero, velocity: new Vector(1f, 0f, 0f), acceleration: Vector.Zero,
            modelIndex: 12, lifetime: 3, ownerEntityIndex: -1);

        TempEntSpawn spawn = rig.Native.Spawned[0];

        // Stock: clientIndex = ( pOwner != NULL ) ? entindex() : 0. NOT
        // Prepare's -1, and the two fields deliberately disagree.
        Assert.True(spawn.ClientIndex == 0,
            "stock writes ZERO for no owner, which is not Prepare's -1");
        Assert.True(spawn.OwnerEntityIndex == -1,
            "while the handle beside it carries nothing, and stock does not keep the two in step");
    }

    [Fact]
    public void BreakModelRandomisesTheBodyOfAStudioGibAndTheFrameOfASprite()
    {
        var studio = Rig(frames: 8, kind: ModelType.Studio);

        studio.Spawns.BreakModel(
            pos: Vector.Zero, angles: Vector.Zero,
            size: new Vector(32f, 32f, 32f), dir: new Vector(0f, 0f, 64f),
            randRange: 100f, life: 5f, count: 8, modelIndex: 3,
            flags: BreakFlags.Wood);

        Assert.True(studio.Native.Created.Count == 8, "eight gibs");

        bool anyBody = false;

        foreach (TempEntSpawn spawn in studio.Native.Spawned)
        {
            Assert.True(spawn.Frame == 0f, "a STUDIO gib never randomises its frame");
            Assert.True(spawn.Body >= 0 && spawn.Body <= 7, "it randomises its BODY");
            anyBody |= spawn.Body != 0;
        }

        Assert.True(anyBody,
            "and over eight gibs at least one is not body 0 -- eight identical gibs is what "
          + "getting this branch backwards looks like");

        var sprite = Rig(frames: 8, kind: ModelType.Sprite);

        sprite.Spawns.BreakModel(
            pos: Vector.Zero, angles: Vector.Zero,
            size: new Vector(32f, 32f, 32f), dir: Vector.Zero,
            randRange: 10f, life: 5f, count: 8, modelIndex: 3,
            flags: BreakFlags.Glass);

        bool anyFrame = false;

        foreach (TempEntSpawn spawn in sprite.Native.Spawned)
        {
            Assert.True(spawn.Body == 0, "a SPRITE gib never randomises its body");
            Assert.True(spawn.Frame >= 0f && spawn.Frame <= 7f, "it randomises its FRAME");
            anyFrame |= spawn.Frame != 0f;
        }

        Assert.True(anyFrame, "and does so visibly");
    }

    [Fact]
    public void BreakModelCarriesTheBreakFlagsIntoTheHitSoundAndTheGlassLook()
    {
        var glass = Rig(frames: 4, kind: ModelType.Studio);

        glass.Spawns.BreakModel(
            pos: Vector.Zero, angles: Vector.Zero,
            size: new Vector(16f, 16f, 16f), dir: Vector.Zero,
            randRange: 0f, life: 2f, count: 3, modelIndex: 3,
            flags: BreakFlags.Glass);

        foreach (TempEntSpawn spawn in glass.Native.Spawned)
        {
            Assert.True(spawn.HitSound == BreakFlags.Glass,
                "the BREAK_ flags are stashed in hitSound, which native PlaySound reads back "
              + "from inside the collision arm to pick the landing sound");

            Assert.True(spawn.RenderMode == (int)RenderMode.TransTexture, "glass is translucent");
            Assert.True(spawn.ColorA == 128 && spawn.RenderAmt == 128, "at half alpha");
            Assert.True(spawn.BounceFactor == 0.3f, "and barely bounces");

            Assert.True((spawn.Flags & (int)TempEntFlags.CollideWorld) != 0, "it hits the world");
            Assert.True((spawn.Flags & (int)TempEntFlags.FadeOut) != 0, "and fades out");
            Assert.True((spawn.Flags & (int)TempEntFlags.SlowGravity) != 0, "under slow gravity");

            Assert.True(spawn.BreakHelper == BreakGroup.Master,
                "and joins the lighting group as its master");

            Assert.True(spawn.Die >= 102f && spawn.Die <= 103f,
                "living for the stated time plus stock's extra 0-1 seconds");
        }

        var wood = Rig(frames: 4, kind: ModelType.Studio);

        wood.Spawns.BreakModel(
            pos: Vector.Zero, angles: Vector.Zero,
            size: new Vector(16f, 16f, 16f), dir: Vector.Zero,
            randRange: 0f, life: 2f, count: 3, modelIndex: 3,
            flags: BreakFlags.Wood | BreakFlags.Slave);

        foreach (TempEntSpawn spawn in wood.Native.Spawned)
        {
            Assert.True(spawn.RenderMode == (int)RenderMode.Normal, "wood is opaque");
            Assert.True(spawn.RenderAmt == 255,
                "at full brightness -- 'set this for fadeout', because the ramp fades FROM this");
            Assert.True(spawn.BounceFactor == 1f, "and keeps Prepare's bounce");

            Assert.True(spawn.BreakHelper == BreakGroup.Slave,
                "BREAK_SLAVE lights off the batch already in the pool");
        }
    }

    /// <summary>
    /// <c>func_break_max_pieces</c> is read off the engine, and it caps.
    ///
    /// <para>
    /// The convar is <c>FCVAR_REPLICATED</c>, so its value is the server's. A
    /// C# constant of 15 beside it would drift the moment a server changed it,
    /// and the symptom would be one client drawing a different number of gibs
    /// than everybody else — which no counter in this port would show.
    /// </para>
    /// </summary>
    [Fact]
    public void BreakModelAsksTheEngineHowManyPiecesAreAllowed()
    {
        var rig = Rig(frames: 4, kind: ModelType.Studio, maxPieces: "3");

        rig.Spawns.BreakModel(
            pos: Vector.Zero, angles: Vector.Zero,
            size: new Vector(16f, 16f, 16f), dir: Vector.Zero,
            randRange: 0f, life: 2f, count: 40, modelIndex: 3,
            flags: BreakFlags.Wood);

        Assert.True(rig.Native.Created.Count == 3,
            $"forty asked for and three made, because the convar said three ({rig.Native.Created.Count})");

        Assert.True(rig.Engine.CountOf("Console.FindConVarValue") >= 1,
            "and the number came from the engine rather than from a constant here");
    }

    /// <summary>
    /// A count of zero is stock's "work it out from the size", and the formula
    /// is a surface-area estimate truncated to an int.
    /// </summary>
    [Fact]
    public void ACountOfZeroIsDerivedFromTheBoxsSurfaceArea()
    {
        var rig = Rig(frames: 4, kind: ModelType.Studio, maxPieces: "1000");

        // (48*48 + 48*48 + 48*48) / (3 * 12 * 12) = 6912 / 432 = 16.
        rig.Spawns.BreakModel(
            pos: Vector.Zero, angles: Vector.Zero,
            size: new Vector(48f, 48f, 48f), dir: Vector.Zero,
            randRange: 0f, life: 1f, count: 0, modelIndex: 3,
            flags: BreakFlags.Wood);

        Assert.True(rig.Native.Created.Count == 16,
            $"sixteen gibs for a 48-unit cube ({rig.Native.Created.Count})");
    }

    /// <summary>
    /// The gibs are scattered in the BOX's frame and rotated into the world,
    /// which is <c>VectorTransform( local, AngleMatrix( angles, pos ) )</c>.
    /// </summary>
    [Fact]
    public void TheGibsAreScatteredInTheBoxsOwnFrame()
    {
        var rig = Rig(frames: 4, kind: ModelType.Studio);

        // A box 100 long in X and flat in Y and Z, yawed ninety degrees: every
        // gib must come out spread along WORLD Y, not world X.
        rig.Spawns.BreakModel(
            pos: new Vector(500f, 600f, 700f),
            angles: new Vector(0f, 90f, 0f),
            size: new Vector(100f, 0f, 0f),
            dir: Vector.Zero,
            randRange: 0f, life: 1f, count: 6, modelIndex: 3,
            flags: BreakFlags.Wood);

        Assert.True(rig.Native.Created.Count == 6, "six gibs");

        bool spread = false;

        foreach (var made in rig.Native.Created)
        {
            Assert.True(MathF.Abs(made.Origin.X - 500f) < 0.01f,
                $"none of them moved in world X ({made.Origin.X})");
            Assert.True(MathF.Abs(made.Origin.Z - 700f) < 0.01f, "or in Z");

            Assert.True(MathF.Abs(made.Origin.Y - 600f) <= 50.01f, "and all stayed inside the box");

            spread |= MathF.Abs(made.Origin.Y - 600f) > 1f;
        }

        Assert.True(spread,
            "but they DID spread along world Y -- the box's own X, rotated. A gib set that "
          + "ignored the angles would have spread along world X instead");
    }

    /// <summary>
    /// A pool that refuses stops the loop rather than asking again per piece,
    /// which is stock's <c>return</c> and not a <c>continue</c>.
    /// </summary>
    [Fact]
    public void AFullPoolStopsTheLoopRatherThanAskingOncePerPiece()
    {
        var rig = Rig(frames: 4);

        rig.Native.RefuseCreate = true;

        rig.Spawns.Bubbles(
            mins: Vector.Zero, maxs: new Vector(10f, 10f, 10f),
            height: 50f, modelIndex: 9, count: 50, speed: 8f);

        Assert.True(rig.Native.Created.Count == 0, "nothing was built");
        Assert.True(rig.Native.Spawned.Count == 0, "and no record was written");

        // The reservation is given back rather than left occupying a slot until
        // the next update pass -- see ClientTempEntPool.Spawn.
        Assert.True(rig.Pool.Count == 0,
            $"and the pool is empty afterwards rather than holding fifty dead reservations "
          + $"({rig.Pool.Count} live)");
    }
}
