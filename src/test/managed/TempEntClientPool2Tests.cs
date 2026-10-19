extern alias client;
using RandomSeeds = client::SourceSharp.Abi.RandomSeeds;
using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// C-Phase 5's ten client halves — the last of the eleven pool-bound temp
/// entities, everything lane C4 could not reach because it ends in a SPAWN
/// HELPER rather than in a walk of the pool's list.
///
/// <para>
/// <b>WHAT THESE ARE FOR, given that the schema of all ten is pinned
/// elsewhere.</b> The send-table tests read stock's own <c>.cpp</c> off disk and
/// catch a wrong-but-consistent width; nothing there says the CLIENT decodes
/// the blob at those offsets, or that it reaches the right helper, or that it
/// hands that helper its arguments in the right order. Those are three
/// different failures with one symptom — an effect that does not appear — and
/// the third of them is the one this group is most exposed to:
/// <c>CTEBreakModel</c>'s send table declares model index, randomization, count
/// and the helper takes randomization, time, count, model index. Two ints in
/// adjacent positions transposed would compile, would run, and would draw a
/// plausible number of gibs of the wrong model.
/// </para>
///
/// <para>
/// <b>AND ONE OF THE TEN DRAWS NOTHING IN THIS RIG.</b> <c>CTEPhysicsProp</c>
/// makes a clientside physics prop (ClientPhysicsProps, since 2026-10-18);
/// the rig has no client vphysics, and the player is checked for SAYING SO,
/// once per level.
/// </para>
///
/// <para>
/// <b>IT WAS TWO.</b> <c>CTEFizz</c> ended at behaviour ledger row C6 and now
/// fizzes: lane FIZZ added <c>ClientEntityModelBounds</c> and wrote
/// <c>TempEntSpawns.FizzEffect</c>, and the row is closed. What is asserted
/// here is the ROUTE — the blob reaches the helper and the helper spawns — and
/// <c>TempEntFizzTests</c> is where the numbers it spawns with are held against
/// stock's own <c>.cpp</c>.
/// </para>
/// </summary>
public class TempEntClientPool2Tests
{
    /// <summary>A native side that accepts every create and remembers every record.</summary>
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

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 100f;
    }

    private sealed record Rigging(
        ClientTempEnts Players, TempEntRegistry Registry, SpawningPool Native,
        RecordingEngine Engine, TempEntSpawns Spawns, RecordingLog Log, Puffs Blood);

    /// <summary>FX_Blood's calls, as the blood sprite makes them.</summary>
    private sealed class Puffs : client::SourceSharp.Client.Effects.IImpactBlood
    {
        public readonly List<(Vector Pos, Vector Dir, float R, float G, float B, float A)> Calls = [];

        public int Blood(Vector pos, Vector dir, float r, float g, float b, float a)
        {
            Calls.Add((pos, dir, r, g, b, a));
            return 1;
        }
    }

    /// <summary>
    /// The real registry over the real shared declarations, the real pool, the
    /// real helpers, and the ten real players — nothing stubbed between the blob
    /// and the spawn record.
    /// </summary>
    private static Rigging Rig(int frames = 10, ModelType kind = ModelType.Sprite)
    {
        var log = new RecordingLog();
        var engine = Fake();

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);
        engine.Returns("Precache.ModelFrameCount", frames);
        engine.Returns("Precache.ModelTypeOf", kind);
        engine.Returns("Console.FindConVarValue", "15");

        var registry = new TempEntRegistry(log);
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        var native = new SpawningPool(engine);
        var clock = new Clock();
        var pool = new ClientTempEntPool(log, native, clock, new TempEntPoolTests.NoSimulation());
        var spawns = new TempEntSpawns(log, pool, engine, engine, clock, new RandomSeeds(1));

        var players = new ClientTempEnts(log, registry);

        players.Add("CTESprite", new ClientTeSprite(spawns));
        players.Add("CTEGlowSprite", new ClientTeGlowSprite(spawns));
        players.Add("CTESpriteSpray", new ClientTeSpriteSpray(spawns));
        var puffs = new Puffs();
        players.Add("CTEBloodSprite", new ClientTeBloodSprite(spawns, puffs));
        players.Add("CTEBubbles", new ClientTeBubbles(spawns));
        players.Add("CTEBubbleTrail", new ClientTeBubbleTrail(spawns));
        players.Add("CTEBreakModel", new ClientTeBreakModel(spawns));
        players.Add("CTEClientProjectile", new ClientTeClientProjectile(spawns));
        players.Add("CTEFizz", new ClientTeFizz(log, spawns, engine));
        players.Add("CTEPhysicsProp", new ClientTePhysicsProp(log));

        return new Rigging(players, registry, native, engine, spawns, log, puffs);
    }

    private static void Send(Rigging rig, string className, Action<TempEntWriter> fill)
    {
        TempEntType type = rig.Registry.Find(className)!;

        Assert.True(type is not null, $"{className} is a registered temp entity");

        var blob = new byte[type!.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        fill(writer);

        rig.Players.Dispatch(type.Handle, blob);
    }

    // =====================================================================
    // The eight that draw.
    // =====================================================================

    [Fact]
    public void ASpriteDecodesToAnAdditiveOneShotSpriteAtTheWiresPlace()
    {
        var rig = Rig(frames: 10);

        Send(rig, "CTESprite", w =>
        {
            w.Vector("Origin", new Vector(-100f, 250f, 64f));
            w.Int("ModelIndex", 91);
            w.Float("Scale", 3f);
            w.Int("Brightness", 255);
        });

        Assert.True(rig.Native.Created.Count == 1, "one sprite");

        var made = rig.Native.Created[0];

        Assert.True(made.Model == 91, "of the model the wire named");
        Assert.True(made.Origin.X == -100f && made.Origin.Y == 250f && made.Origin.Z == 64f,
            "where the wire said");

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True(spawn.RenderMode == (int)RenderMode.TransAdd, "additive");
        Assert.True(spawn.ColorA == 255,
            "the brightness came through the alpha -- 255/255 * 255 back to 255");
        Assert.True(spawn.SpriteScale == 3f, "at the scale it carried");
        Assert.True((spawn.Flags & (int)TempEntFlags.SprAnimate) != 0, "animating");
        Assert.True((spawn.Flags & (int)TempEntFlags.SprAnimateLoop) == 0,
            "but NOT looping -- that is CTEGlowSprite's, and it is the difference between them");

        // CTESprite passes life = 0, so the helper works one out.
        Assert.True(spawn.Die == 102f,
            "and a zero lifetime became one full animation plus a second, not zero seconds");
    }

    [Fact]
    public void AGlowSpriteLoopsForItsStatedLifeAndBarelyBounces()
    {
        var rig = Rig(frames: 10);

        Send(rig, "CTEGlowSprite", w =>
        {
            w.Vector("Origin", new Vector(0f, 0f, 0f));
            w.Int("ModelIndex", 12);
            w.Float("Scale", 2f);
            w.Float("Life", 4f);
            w.Int("Brightness", 128);
        });

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True((spawn.Flags & (int)TempEntFlags.SprAnimateLoop) != 0,
            "looping, which is what makes a stated lifetime necessary");
        Assert.True(spawn.Die == 104f, "for the four seconds the wire carried");

        Assert.True(spawn.BounceFactor == 0.2f,
            "and stock's after-the-fact bounceFactor arrives with the record. It is read by "
          + "Frame's collision arm, which is still native, so it is not one this build ignores");

        Assert.True(spawn.ColorA == 128, "at half brightness");
    }

    [Fact]
    public void ASpriteSprayAppliesBothOfStocksScalingsAndNeitherTwice()
    {
        var rig = Rig(frames: 5);

        Send(rig, "CTESpriteSpray", w =>
        {
            w.Vector("Origin", new Vector(1f, 2f, 3f));
            w.Vector("Direction", new Vector(0f, 0f, 1f));
            w.Int("ModelIndex", 33);
            w.Float("Noise", 0.5f);
            w.Int("Count", 7);
            w.Int("Speed", 200);
        });

        Assert.True(rig.Native.Created.Count == 7, "seven sprites -- the COUNT, not the speed");

        foreach (var made in rig.Native.Created)
            Assert.True(made.Model == 33, "of the model the wire named");

        // speed * 0.2 = 40, then the helper scales the velocity by
        // RandomFloat( 32, 48 ). A missing scaling would put every length above
        // 160; a doubled one would put every length below 10.
        foreach (TempEntSpawn spawn in rig.Native.Spawned)
        {
            float length = MathF.Sqrt(spawn.Velocity.X * spawn.Velocity.X
                                    + spawn.Velocity.Y * spawn.Velocity.Y
                                    + spawn.Velocity.Z * spawn.Velocity.Z);

            Assert.True(length > 10f && length < 120f,
                $"the speed was divided by five exactly once ({length})");

            // noise 0.5 -> iRand 50 -> noise 0.5 again. The sideways component
            // is dir + RandomFloat(-0.5, 0.5), scaled -- so it cannot exceed
            // half the scale factor. A missing hundred would make it zero.
            Assert.True(MathF.Abs(spawn.Velocity.X) <= 48f * 0.5f + 0.01f,
                $"and the noise survived its round trip through an int ({spawn.Velocity.X})");
        }

        bool scattered = rig.Native.Spawned.Any(s => MathF.Abs(s.Velocity.X) > 0.01f);

        Assert.True(scattered,
            "and it is not zero -- a dropped `* 100.0` would make every spray a straight line");
    }

    [Fact]
    public void ABloodSpriteFiresBOTHHalvesOffOneOffset()
    {
        var rig = Rig(frames: 8);

        // BY THE MEMBERS' NAMES. DT_TEBloodSprite has dropped its WireNames, so
        // stock's m_vecOrigin/m_vecDirection/r/g/b/a/m_nSprayModel/m_nDropModel/
        // m_nSize are Origin/Direction/R/G/B/A/SprayModel/DropModel/Size -- and
        // the four colours are a CASE change, which TempEntWriter's ordinal
        // lookup is not forgiving about.
        Send(rig, "CTEBloodSprite", w =>
        {
            w.Vector("Origin", new Vector(0f, 0f, 0f));
            w.Vector("Direction", new Vector(1f, 0f, 0f));
            w.Int("ColorR", 247);
            w.Int("ColorG", 0);
            w.Int("ColorB", 0);
            w.Int("ColorA", 255);
            w.Int("SprayModel", 61);
            w.Int("DropModel", 62);
            w.Int("Size", 24);
        });

        // Half one: the pool's sprite.
        Assert.True(rig.Native.Created.Count == 1, "the sprite half fired");
        Assert.True(rig.Native.Created[0].Priority == TempEntPriority.High, "at high priority");
        Assert.True(rig.Native.Created[0].Model == 61,
            "of the SPRAY model -- the drop model is a parameter stock's helper never reads");

        // Half two: the particles, which are not the pool's at all.
        Assert.True(rig.Blood.Calls.Count == 1,
            "and so did the PUFF, which is FX_Blood's and touches no tempent. Dropping it "
          + "would have halved every bullet wound in the game");

        var puff = rig.Blood.Calls[0];

        // origin + direction * 4, computed ONCE and given to both.
        var offset = new Vector(4f, 0f, 0f);

        Assert.True(rig.Native.Created[0].Origin.X == offset.X
                 && rig.Native.Created[0].Origin.Y == offset.Y
                 && rig.Native.Created[0].Origin.Z == offset.Z,
            "the sprite is four units along the direction");

        Vector at = puff.Pos;

        Assert.True(at.X == offset.X && at.Y == offset.Y && at.Z == offset.Z,
            "and the puff is at the SAME place -- one offset, applied once, as stock computes it");

        Assert.True(puff.R == 247 && puff.A == 255,
            "with the wire's colour");
    }

    [Fact]
    public void BubblesAndABubbleTrailReadTheirThirdFloatDifferently()
    {
        // THE TWO SIBLINGS, in one test, because the thing worth pinning is the
        // difference: m_fHeight is RELATIVE to the box floor and m_flWaterZ is
        // an ABSOLUTE world height, and the two props sit in the same position
        // of two nearly identical tables.
        var rig = Rig(frames: 4);

        Send(rig, "CTEBubbles", w =>
        {
            w.Vector("Mins", new Vector(0f, 0f, 1000f));
            w.Vector("Maxs", new Vector(4f, 4f, 1000f));
            w.Int("ModelIndex", 21);
            w.Float("Height", 100f);
            w.Int("Count", 3);
            w.Float("Speed", 8f);
        });

        Assert.True(rig.Native.Created.Count == 3, "three bubbles");

        foreach (var made in rig.Native.Created)
            Assert.True(made.Origin.Z == 1000f, "in a flat box a thousand units up");

        foreach (TempEntSpawn spawn in rig.Native.Spawned)
        {
            // height 100 relative to a floor at z=1000: the bubble has a
            // hundred units to climb, so it lives about 100/zspeed seconds. If
            // the height were read as a world Z the answer would be about
            // -900/zspeed, which is a NEGATIVE lifetime.
            Assert.True(spawn.Die > 100f,
                $"and each lives a positive time ({spawn.Die} from now 100). A height read as a "
              + "world Z would give a bubble in a high box a lifetime in the past");

            Assert.True(spawn.Die < 102f, "and about a hundred units' worth of it, not a thousand");
        }

        var trail = Rig(frames: 4);

        Send(trail, "CTEBubbleTrail", w =>
        {
            w.Vector("Mins", new Vector(0f, 0f, 0f));
            w.Vector("Maxs", new Vector(64f, 0f, 0f));
            w.Int("ModelIndex", 21);
            w.Float("WaterZ", 100f);
            w.Int("Count", 3);
            w.Float("Speed", 8f);
        });

        foreach (var made in trail.Native.Created)
        {
            Assert.True(made.Origin.X >= 0f && made.Origin.X <= 64f,
                "the trail's two vectors are a SEGMENT, whatever the props are named");
            Assert.True(made.Origin.Y == 0f && made.Origin.Z == 0f,
                "so nothing is off the line -- a box read would have spread it");
        }

        foreach (TempEntSpawn spawn in trail.Native.Spawned)
        {
            // waterZ 100, origin z 0: a hundred units to climb.
            float expected = 100f + (100f / spawn.Velocity.Z) - 0.1f;

            Assert.True(MathF.Abs(spawn.Die - expected) < 0.001f,
                "and the water height is ABSOLUTE, measured from the bubble's own z");
        }
    }

    [Fact]
    public void BreakModelHandsTheHelperItsArgumentsInTheRightOrder()
    {
        // THE TRANSPOSITION TEST. The table declares model index, then
        // randomization, then count; the helper takes randomization, time,
        // count, model index. Distinct values everywhere so a swap cannot land
        // on a plausible number.
        var rig = Rig(frames: 6, kind: ModelType.Studio);

        Send(rig, "CTEBreakModel", w =>
        {
            w.Vector("Origin", new Vector(10f, 20f, 30f));
            w.Vector("Rotation", new Vector(0f, 0f, 0f));
            w.Vector("Size", new Vector(8f, 8f, 8f));
            w.Vector("Velocity", new Vector(0f, 0f, 50f));
            w.Int("ModelIndex", 123);
            w.Int("Randomization", 40);
            w.Int("Count", 7);
            w.Float("Time", 3f);
            w.Int("Flags", BreakFlags.Wood);
        });

        Assert.True(rig.Native.Created.Count == 7,
            $"SEVEN gibs -- the count, not the randomization (40) or the model (123) "
          + $"({rig.Native.Created.Count})");

        foreach (var made in rig.Native.Created)
        {
            Assert.True(made.Model == 123,
                "of model 123 -- not 40, not 7, which is what a transposition would give");

            // Scattered within half the size of the origin.
            Assert.True(MathF.Abs(made.Origin.X - 10f) <= 4.01f, "scattered inside the box in X");
            Assert.True(MathF.Abs(made.Origin.Z - 30f) <= 4.01f, "and in Z");
        }

        foreach (TempEntSpawn spawn in rig.Native.Spawned)
        {
            Assert.True(spawn.HitSound == BreakFlags.Wood, "carrying the break type for PlaySound");

            // life 3 plus 0-1 seconds. If m_fTime and m_nRandomization were
            // swapped this would be 40-41 seconds.
            Assert.True(spawn.Die >= 103f && spawn.Die <= 104f,
                $"living three seconds plus stock's extra 0-1 ({spawn.Die} from now 100). Forty "
              + "would mean the lifetime and the randomization changed places");

            // randRange 40, dir z 50: z velocity is 50..90, x and y are -40..40.
            Assert.True(spawn.Velocity.Z >= 50f && spawn.Velocity.Z <= 90.01f,
                $"thrown along the wire's velocity, spread ONE-SIDED upward ({spawn.Velocity.Z})");
            Assert.True(MathF.Abs(spawn.Velocity.X) <= 40.01f,
                "and symmetrically sideways by the randomization");

            Assert.True(spawn.Body >= 0 && spawn.Body <= 5,
                "with a body drawn from the model's frame count, this being a studio model");
        }
    }

    [Fact]
    public void AClientProjectileTakesTheIndexOutOfItsHandleAndNotTheSerial()
    {
        var rig = Rig(frames: 1);

        Send(rig, "CTEClientProjectile", w =>
        {
            w.Vector("Origin", new Vector(5f, 0f, 5f));
            w.Vector("Velocity", new Vector(100f, 0f, 0f));
            w.Int("ModelIndex", 44);
            w.Int("LifeTime", 9);

            // Entry index 6 with serial 3: packed as 6 | (3 << 11) = 6150. A
            // reader that took the whole word would answer 6150, which is not
            // an entity index at all and would be handed to cl_entitylist.
            w.Int("Owner", 6 | (3 << 11));
        });

        Assert.True(rig.Native.Created.Count == 1, "one projectile");

        TempEntSpawn spawn = rig.Native.Spawned[0];

        Assert.True(spawn.OwnerEntityIndex == 6,
            $"the owner is entity SIX, out of the low eleven bits ({spawn.OwnerEntityIndex}). "
          + "6150 would be the packed word read whole");
        Assert.True(spawn.ClientIndex == 6, "and the collision arm ignores the same entity");

        Assert.True(spawn.Die == 109f, "it lives nine seconds");

        Assert.True(spawn.Flags == (int)(TempEntFlags.CollideAll
                                       | TempEntFlags.AttachToTarget
                                       | TempEntFlags.AlignToMotion),
            "with the three flags Frame's still-native collision arm reads");

        Assert.True(spawn.Acceleration.X == 0f && spawn.Acceleration.Y == 0f
                 && spawn.Acceleration.Z == 0f,
            "and no acceleration, which is vec3_origin at both of stock's call sites");
    }

    // =====================================================================
    // The two that do not, and say so.
    // =====================================================================

    /// <summary>
    /// <c>CTEFizz</c> arrives and bubbles come out of the volume it named —
    /// behaviour ledger row C6, closed.
    ///
    /// <para>
    /// THE ROUTE, WHICH IS WHAT THIS GROUP IS FOR: the blob decodes, the entity
    /// index is asked for its MODEL bounds and its angles, and the helper fills
    /// pool slots. For four hundred commits this arrived and was dropped — first
    /// silently, at <c>cl_entitylist-&gt;GetEnt</c>, then out loud — and a fizz
    /// that draws nothing is indistinguishable from one that was never sent.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFizzSpreadsBubblesAcrossTheEntitysModelBounds()
    {
        var rig = Rig();

        // THE MODEL'S BOX, and it is deliberately NOT centred on the origin and
        // deliberately not a cube: the collision box this must not be reading
        // is a symmetric ±16 cube, so a rectangle that could be confused with
        // one would let the wrong read pass.
        rig.Engine.Returns("WorldQuery.ClientEntityModelBounds", true);
        rig.Engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", new Vector(100f, 200f, 300f));
        rig.Engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", new Vector(140f, 220f, 380f));
        rig.Engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", Vector.Zero);

        Send(rig, "CTEFizz", w =>
        {
            w.Int("Entity", 23);
            w.Int("ModelIndex", 88);
            w.Int("Density", 6);
            w.Int("Current", 40);
        });

        Assert.True(rig.Engine.Calls.Single(c => c.Member == "WorldQuery.ClientEntityModelBounds")
                                    .ArgAs<int>(0) == 23,
            "the entity the wire named is the entity that was asked");

        Assert.True(rig.Native.Created.Count == 7,
            "density 6 is SEVEN bubbles: stock's count is density + 1, so a volume with no "
          + "density key set still emits one");

        Assert.True(rig.Native.Created.All(c => c.Model == 88),
            "each is the bubble sprite the wire named, not the brush's own model");

        Assert.True(rig.Native.Created.All(c => c.Origin.Z == 300f),
            "and each starts on the FLOOR of the model box — mins.z, not the middle and not "
          + "the collision box's centre");

        Assert.True(rig.Native.Created.All(c => c.Origin.X >= 100f && c.Origin.X <= 139f
                                             && c.Origin.Y >= 200f && c.Origin.Y <= 219f),
            "spread across the model's own rectangle, which is the volume the mapper drew");
    }

    /// <summary>
    /// <c>CTEPhysicsProp</c> with no client vphysics to make the prop in (the
    /// rig has none): nothing is drawn, and that is said once per level, with
    /// what it carried. With vphysics the prop is ClientPhysicsProps'
    /// (ClientPhysicsPropsTests).
    /// </summary>
    [Fact]
    public void ThePhysicsPropWithNoClientVphysicsSaysSoOncePerLevel()
    {
        var rig = Rig();

        var said = new List<string>();
        rig.Log.Line = (message, _) => said.Add(message);

        try
        {
            for (int i = 0; i < 4; i++)
            {
                Send(rig, "CTEPhysicsProp", w =>
                {
                    w.Vector("Origin", new Vector(7f, 8f, 9f));
                    w.Int("ModelIndex", 55);
                    w.Int("Skin", 2);
                    w.Int("Flags", 1);
                    w.Int("Effects", 0);
                });
            }

            Assert.True(rig.Native.Created.Count == 0, "no tempent, which is stock's answer too");

            Assert.True(said.Count(m => m.Contains("CTEPhysicsProp")) == 1, "four arrivals, one line");

            Assert.True(said.Any(m => m.Contains("no client vphysics")),
                "and it names what is missing");
            Assert.True(said.Any(m => m.Contains("model 55") && m.Contains("skin 2")),
                "and what it carried, because if this ever fires that is the first question");

            rig.Players.LevelInit();

            Send(rig, "CTEPhysicsProp", w => w.Int("ModelIndex", 55));

            Assert.True(said.Count(m => m.Contains("CTEPhysicsProp")) == 2, "said again next level");
        }
        finally
        {
        }
    }

    /// <summary>
    /// All ten register by attribute and none of them collides.
    ///
    /// <para>
    /// The registry discovers by <c>[ClientTempEnt]</c> and nothing else — there
    /// is no shared list — so a class whose attribute names a table that does
    /// not exist registers happily and is never routed to. This walks the ten
    /// names against the real declarations.
    /// </para>
    /// </summary>
    [Fact]
    public void AllTenPairWithARealTable()
    {
        var rig = Rig();

        foreach (string name in new[]
        {
            "CTESprite", "CTEGlowSprite", "CTESpriteSpray", "CTEBloodSprite",
            "CTEBubbles", "CTEBubbleTrail", "CTEBreakModel", "CTEClientProjectile",
            "CTEFizz", "CTEPhysicsProp",
        })
        {
            Assert.True(rig.Registry.Find(name) is not null,
                $"{name} is a declared temp entity, so the player's attribute names a real table");
            Assert.True(rig.Players.PlayerNames.Contains(name),
                $"and a managed player is registered for it");
        }
    }
}
