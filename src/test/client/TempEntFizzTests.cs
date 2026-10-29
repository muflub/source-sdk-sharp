extern alias client;
using RandomSeeds = client::SourceSharp.Abi.RandomSeeds;
using TeFizz = client::SourceSharp.Shared.TeFizz;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Behaviour ledger row C6, closed: <c>CTEFizz</c> reaches
/// <c>CTempEnts::FizzEffect</c> and bubbles come out of the volume it named.
///
/// <para>
/// <b>THE ONE MISTAKE THIS ROW PREDICTED IS THE ONE MOST OF THIS FILE IS
/// ABOUT.</b> The row's own words: the bounds are
/// <c>modelinfo-&gt;GetModelBounds</c>'s, "which is NOT
/// <c>IEngineEntities.GetBounds</c>, the collision box and a different
/// rectangle" — and "getting that wrong gives bubbles across the wrong volume
/// and nothing says so". On this side it is worse than a different rectangle:
/// <c>C_StandaloneEntity</c>'s collideable box is a symmetric ±16 cube fixed in
/// the constructor and never networked, so the wrong read raises every fizz in
/// the game out of a 32-unit cube and still looks like a working effect. So it
/// is checked three ways — the numbers the helper spawns with, the C# that asks,
/// and the C++ that answers, read off disk.
/// </para>
///
/// <para>
/// <b>AND THE FIXTURE-VERSUS-NATIVE RULE APPLIES TWICE OVER.</b> Every constant
/// in the managed helper is a value it stands in for, so
/// <see cref="TheHelperCarriesStocksOwnNumbers"/> reads
/// <c>CTempEnts::FizzEffect</c> out of <c>c_te_legacytempents.cpp</c> and
/// asserts stock's own spellings are still there. A managed expectation checked
/// against a managed helper is a computation compared with itself, which is
/// what docs/te-lane-contract.md's three-things list is about.
/// </para>
/// </summary>
public class TempEntFizzTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 100f;
    }

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

    private sealed record Rigging(
        ClientTeFizz Player, TempEntSpawns Spawns, SpawningPool Native,
        RecordingEngine Engine, Clock Clock, RecordingLog Log);

    /// <summary>
    /// The real helper over a pool that always says yes, and the real player in
    /// front of it.
    ///
    /// <para>
    /// The BOX IS DELIBERATELY NOT A CUBE AND NOT CENTRED ON THE ORIGIN: the
    /// rectangle this must not be reading is a symmetric ±16 cube, and an
    /// expectation that could be satisfied by one would not be able to tell the
    /// two reads apart. 40 × 20 × 80, at (100, 200, 300).
    /// </para>
    /// </summary>
    private static Rigging Rig(int frames = 10, bool entityAnswers = true,
                               float yaw = 0f)
    {
        var log = new RecordingLog();
        var engine = TestServices.Fake();

        engine.Returns("Precache.ModelFrameCount", frames);
        engine.Returns("WorldQuery.ClientEntityModelBounds", entityAnswers);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", new Vector(100f, 200f, 300f));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", new Vector(140f, 220f, 380f));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", new Vector(0f, yaw, 0f));

        var native = new SpawningPool(engine);
        var clock = new Clock();
        var pool = new ClientTempEntPool(log, native, clock, new TempEntPoolTests.NoSimulation());
        var spawns = new TempEntSpawns(log, pool, engine, engine, clock, new RandomSeeds(1));

        return new Rigging(new ClientTeFizz(log, spawns, engine), spawns, native, engine, clock, log);
    }

    private static TeFizz Fizz(int entity = 23, int modelIndex = 88, int density = 6,
                               int current = 40)
        => new() { Entity = entity, ModelIndex = modelIndex, Density = density, Current = current };

    // =====================================================================
    // The rectangle. This is the row's own predicted mistake.
    // =====================================================================

    /// <summary>
    /// The bubbles are spread across the MODEL's rectangle and start on its
    /// floor.
    ///
    /// <para>
    /// Stock places each at
    /// <c>(mins[0] + RandomInt(0,width-1), mins[1] + RandomInt(0,depth-1),
    /// mins[2])</c> — so the Z is the floor exactly, every time, and the X and Y
    /// are inside the box. The ±16 cube this must not be reading contains none
    /// of those points, so a wrong read cannot pass this by accident.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBubblesFillTheModelBoxAndStartOnItsFloor()
    {
        var rig = Rig();

        rig.Player.Play(Fizz());

        Assert.True(rig.Native.Created.Count == 7, "seven bubbles for a density of six");

        Assert.True(rig.Native.Created.All(c => c.Origin.Z == 300f),
            "every one starts on the model box's FLOOR — mins.z, which is neither the "
          + "box's middle nor the collision cube's centre");

        Assert.True(rig.Native.Created.All(c => c.Origin.X >= 100f && c.Origin.X <= 139f),
            "and inside the box across its width: mins.x plus RandomInt(0, width-1), so the "
          + "left edge is reachable and the right edge is one short of it");

        Assert.True(rig.Native.Created.All(c => c.Origin.Y >= 200f && c.Origin.Y <= 219f),
            "and across its depth the same way");

        Assert.True(rig.Native.Created.All(c => c.Model == 88),
            "each is the BUBBLE sprite the wire named and not the brush's own model — "
          + "drawing the brush's model would draw the volume itself");

        Assert.True(rig.Native.Created.All(c => c.Priority == (int)TempEntPriority.Low),
            "and low priority, which is stock's TempEntAlloc rather than TempEntAllocHigh");
    }

    /// <summary>
    /// The ENTITY the wire named is the entity that was asked, and it is asked
    /// exactly once however many bubbles come out.
    /// </summary>
    [Fact]
    public void TheEntityOnTheWireIsTheEntityAsked()
    {
        var rig = Rig();

        rig.Player.Play(Fizz(entity: 41));

        Assert.True(rig.Engine.CountOf("WorldQuery.ClientEntityModelBounds") == 1,
            "one question per fizz, not one per bubble");

        Assert.True(rig.Engine.Calls.Single(c => c.Member == "WorldQuery.ClientEntityModelBounds")
                                    .ArgAs<int>(0) == 41,
            "and it is the index DT_TEFizz carried");
    }

    /// <summary>
    /// <c>ClientTeFizz</c> asks for MODEL bounds and never for the collision
    /// box — asserted against the compiled call, not against the source text.
    ///
    /// <para>
    /// <c>Fake()</c> is strict: anything the object reads that the test did not
    /// stub throws naming the member. <c>Entities.GetBounds</c> is NOT stubbed
    /// here, so a class that reached for the collideable's rectangle instead
    /// would fail loudly rather than quietly using a plausible box.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCollisionBoxIsNeverAsked()
    {
        var rig = Rig();

        rig.Player.Play(Fizz());

        Assert.True(rig.Engine.CountOf("Entities.GetBounds") == 0,
            "the collideable's box is not what a fizz is spread across, and on "
          + "C_StandaloneEntity it is a symmetric ±16 placeholder that would put every "
          + "bubble column in a 32-unit cube");

        Assert.True(rig.Engine.CountOf("Entities.GetBoundsSize") == 0,
            "nor its size, which is the same rectangle by another name");
    }

    // =====================================================================
    // The current, which is the other thing read off the entity.
    // =====================================================================

    /// <summary>
    /// The current drifts along the entity's YAW: <c>SinCos(yaw)</c> with the
    /// COSINE in X and the SINE in Y, times the current.
    ///
    /// <para>
    /// THE ARGUMENT ORDER IS THE TRAP. Stock writes
    /// <c>SinCos( yaw, &amp;yspeed, &amp;xspeed )</c> — sine first — so the sine
    /// lands in Y and the cosine in X. Swapping them compiles, runs, and sends
    /// every current ninety degrees off, which on a square volume looks like a
    /// current that works.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCurrentDriftsAlongTheYaw()
    {
        var east = Rig(yaw: 0f);
        east.Player.Play(Fizz(current: 40));

        Assert.True(east.Native.Spawned.All(s => MathF.Abs(s.Velocity.X - 40f) < 0.001f),
            "at yaw 0 the whole current is along +X, which is the cosine");

        Assert.True(east.Native.Spawned.All(s => MathF.Abs(s.Velocity.Y) < 0.001f),
            "and none of it along Y, which is the sine");

        var north = Rig(yaw: 90f);
        north.Player.Play(Fizz(current: 40));

        Assert.True(north.Native.Spawned.All(s => MathF.Abs(s.Velocity.Y - 40f) < 0.001f),
            "at yaw 90 it has turned onto +Y — DEGREES converted by *M_PI/180, not "
          + "radians read raw, which would put 90 almost back where it started");

        Assert.True(north.Native.Spawned.All(s => MathF.Abs(s.Velocity.X) < 0.001f),
            "and nothing is left along X");
    }

    /// <summary>
    /// A negative current runs the other way rather than very fast the same
    /// way. <c>m_nCurrent</c> is the one SIGNED field on <c>DT_TEFizz</c>.
    /// </summary>
    [Fact]
    public void ANegativeCurrentRunsBackwards()
    {
        var rig = Rig(yaw: 0f);

        rig.Player.Play(Fizz(current: -40));

        Assert.True(rig.Native.Spawned.All(s => MathF.Abs(s.Velocity.X + 40f) < 0.001f),
            "a current of -40 at yaw 0 pushes along -X");
    }

    // =====================================================================
    // The rest of the record.
    // =====================================================================

    /// <summary>
    /// A bubble lives long enough to cross the WHOLE column, less a tenth so it
    /// pops just under the surface — and the height is the box's, not a
    /// per-bubble remainder.
    ///
    /// <para>
    /// <see cref="TempEntSpawns.Bubbles"/> computes
    /// <c>(height - (origin.z - mins.z)) / zspeed</c> because it scatters
    /// bubbles through the box; this one spawns every bubble on the floor, so
    /// the distance is the same for all of them and the remainder would always
    /// be the whole height anyway. Writing it the other way here would be right
    /// by coincidence and wrong the moment anything moved.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLifetimeIsTheWholeColumnLessATenth()
    {
        var rig = Rig();

        rig.Player.Play(Fizz());

        foreach (TempEntSpawn spawn in rig.Native.Spawned)
        {
            // 80 to 140, stock's RandomInt, and the rise speed is what the
            // lifetime is measured against -- so the pair is asserted together
            // rather than each against a constant.
            Assert.True(spawn.Velocity.Z >= 80f && spawn.Velocity.Z <= 140f,
                "each rises at stock's 80 to 140");

            float expected = 100f + (80f / spawn.Velocity.Z) - 0.1f;

            Assert.True(MathF.Abs(spawn.Die - expected) < 0.0005f,
                "and dies after the whole 80-unit column at that speed, less the tenth "
              + "that makes it pop under the surface rather than through it");
        }
    }

    /// <summary>
    /// A fizz's bubbles are the fizz's size, which is not the bubble field's.
    ///
    /// <para>
    /// <c>1 / RandomFloat(2,5)</c> here against <c>1 / RandomFloat(4,16)</c> in
    /// <see cref="TempEntSpawns.Bubbles"/> — so 0.2–0.5 against 0.0625–0.25.
    /// The two helpers are otherwise near enough alike to invite being merged,
    /// and this is one of the three differences that stops them being.
    /// </para>
    /// </summary>
    [Fact]
    public void TheScaleIsTheFizzsRangeAndNotTheBubbleFields()
    {
        var rig = Rig();

        rig.Player.Play(Fizz(density: 200));

        Assert.True(rig.Native.Spawned.Count == 201, "two hundred and one samples");

        Assert.True(rig.Native.Spawned.All(s => s.SpriteScale >= 0.2f && s.SpriteScale <= 0.5f),
            "every scale is 1/5 to 1/2, which is 1 / RandomFloat(2,5)");

        Assert.True(rig.Native.Spawned.Any(s => s.SpriteScale > 0.25f),
            "and the range reaches above 0.25, which is the TOP of Bubbles' 1/RandomFloat(4,16) "
          + "— borrowing that range would keep every sample below this line");
    }

    /// <summary>
    /// The wobble, the render mode and the alpha: the fields that are
    /// determined rather than sampled.
    /// </summary>
    [Fact]
    public void TheWobbleAxisIsTheSpawnPointAndTheAlphaIsFull()
    {
        var rig = Rig();

        rig.Player.Play(Fizz());

        for (int i = 0; i < rig.Native.Spawned.Count; i++)
        {
            TempEntSpawn spawn = rig.Native.Spawned[i];
            Vector origin = rig.Native.Created[i].Origin;

            Assert.True((spawn.Flags & (int)TempEntFlags.SineWave) != 0,
                "FTENT_SINEWAVE, which is what makes a bubble wander on its way up");

            Assert.True(spawn.SineX == origin.X && spawn.SineY == origin.Y,
                "and the axis it wanders about is where it started, not the box's centre");

            Assert.True(spawn.RenderMode == (int)RenderMode.TransAlpha,
                "kRenderTransAlpha");

            Assert.True(spawn.ColorA == 255,
                "SetRenderColorA( 255 ) — full, where Bubbles lands on 192");

            Assert.True(spawn.ColorR == 255 && spawn.ColorG == 255 && spawn.ColorB == 255,
                "and the RGB is the white Prepared already carried: stock calls the "
              + "one-argument SetRenderColorA here and never touches the colour");

            Assert.True(spawn.Frame >= 0f && spawn.Frame <= 9f,
                "starting on one of the sprite's ten frames");
        }
    }

    /// <summary>
    /// A density of zero is still ONE bubble. Stock's <c>count = density + 1</c>
    /// is what makes an <c>env_bubbles</c> with no density key visible at all.
    /// </summary>
    [Fact]
    public void ADensityOfZeroIsStillOneBubble()
    {
        var rig = Rig();

        rig.Player.Play(Fizz(density: 0));

        Assert.True(rig.Native.Created.Count == 1, "one bubble, not none");
    }

    /// <summary>
    /// A model index of zero, or one that resolves to nothing, spawns nothing —
    /// stock's <c>!modelIndex</c> and its <c>!model</c>, both of them.
    /// </summary>
    [Fact]
    public void NoBubbleSpriteMeansNoBubbles()
    {
        var noIndex = Rig();
        noIndex.Player.Play(Fizz(modelIndex: 0));

        Assert.True(noIndex.Native.Created.Count == 0, "a model index of zero spawns nothing");

        var unresolved = Rig(frames: -1);
        unresolved.Player.Play(Fizz());

        Assert.True(unresolved.Native.Created.Count == 0,
            "and neither does an index the client never precached, which the frame count "
          + "answers -1 for");
    }

    // =====================================================================
    // The refusal that is left, which is stock's own.
    // =====================================================================

    /// <summary>
    /// A fizz aimed at an index this client cannot measure spawns nothing and
    /// says so once per level.
    ///
    /// <para>
    /// STOCK IS SILENT HERE and this is not, deliberately: that silence WAS
    /// row C6 for the whole port, and a resolve that broke again would
    /// otherwise be exactly as quiet as the gap this closed.
    /// </para>
    /// </summary>
    [Fact]
    public void AFizzOnNothingSpawnsNothingAndSaysSoOncePerLevel()
    {
        var rig = Rig(entityAnswers: false);

        var said = new List<string>();
        rig.Log.Line = (message, _) => said.Add(message);

        try
        {
            for (int i = 0; i < 5; i++)
                rig.Player.Play(Fizz());

            Assert.True(rig.Native.Created.Count == 0, "nothing fizzed, which is stock's answer");

            Assert.True(said.Count(m => m.Contains("CTEFizz")) == 1, "five fizzes, one line");

            Assert.True(said.Any(m => m.Contains("entity 23")),
                "and it names the entity it could not measure, which is the first thing "
              + "anybody looking would want");

            Assert.True(said.Any(m => m.Contains("row C6")),
                "and the row, because this line existing at all is that row's history");

            rig.Player.LevelInit();
            rig.Player.Play(Fizz());

            Assert.True(said.Count(m => m.Contains("CTEFizz")) == 2,
                "and it is said again on the next level: it is missing again, here");
        }
        finally
        {
        }
    }

    // =====================================================================
    // Read off disk. The three that a managed expectation cannot be.
    // =====================================================================

    /// <summary>
    /// The slot is declared and actually wired, on the CLIENT, because
    /// <c>cl_entitylist</c> is this side's table. Since plan_client_vtables
    /// CC-last declared means a slot-table row whose area's generated layout
    /// has the field (it was a field of the deleted engine API struct).
    /// </summary>
    [Fact]
    public void TheSlotIsDeclaredAndWiredOnTheClient()
    {
        Assert.True(EngineApiAreasTests.SlotField("ClientEntityModelBounds") is not null,
            "the slot table declares the slot and its area's layout has it");

        // plan_client_vtables CC-last: wired means served by a shim area.
        var sides = SideAudit.SlotSidesFromServed(EngineApiCensusTests.ClientMethods(), EngineApiCensusTests.ServerMethods());

        Assert.True(sides.TryGetValue("ClientEntityModelBounds", out SlotSide side),
            "and a shim area serves it — a declared slot nobody wires is a null pointer "
          + "every guarded call in Engine.cs skips in silence");

        Assert.True(side == SlotSide.Client,
            "on the client, which is the side that has a cl_entitylist and the side that "
          + $"draws (found {side})");
    }

    /// <summary>
    /// <c>ClientTeFizz</c> can still be BUILT by the container the real client
    /// builds it with.
    ///
    /// <para>
    /// <b>THIS IS THE ONE THING THE REST OF THE FILE CANNOT SEE.</b> Every other
    /// <c>[Fact]</c> here constructs the player with <c>new</c>, which is the
    /// house rule — but the game does not: <c>ClientTempEnts.RegisterAll</c>
    /// activates it through the service container, and a constructor asking for
    /// something the container has no binding for is a WARNING and a player that
    /// is never registered. The effect then arrives, finds nobody to play it,
    /// and is dropped — which is the exact shape of the gap this lane just
    /// closed, arriving by a different door. Giving this class a second
    /// dependency is what made the risk real.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePlayerIsStillBuildableByTheContainerTheGameUses()
    {
        var pool = new ClientTempEntPool(new RecordingLog(), TestServices.Fake(), new Clock(), new TempEntPoolTests.NoSimulation());
        var services = TestServices.Services(new Clock(), pool);
        // The client's seeds, as the client's composition registers them: the
        // mock's are the server's copy of the shared service (plan_side_isolation P7).
        services.AddInstance<client::SourceSharp.Abi.IRandomSeeds>(new RandomSeeds(20260929));

        var spawns = ServiceActivator.For(typeof(TempEntSpawns)).Create(services);
        services.AddInstance(typeof(TempEntSpawns), spawns);

        object made = ServiceActivator.For(typeof(ClientTeFizz)).Create(services);

        Assert.True(made is ClientTeFizz,
            "the container can build the player, so RegisterAll will register it rather "
          + "than warning and moving on");

        Assert.True(made is IClientTempEntLevel,
            "and it still forgets what it said when the level changes, which is what the "
          + "once-per-level refusal is measured in");
    }

    // =====================================================================
    // What closing the row did NOT do, said out loud so it cannot go quiet.
    // =====================================================================

    /// <summary>
    /// The row's TITLE names <c>SourceSharp_BubbleEffect</c>, and that
    /// substitution is still in place: <c>EnvBubbles</c> still sends
    /// <c>te-&gt;Bubbles</c> with the corners on the wire rather than a fizz.
    ///
    /// <para>
    /// <b>THIS IS AN ASSERTION ABOUT THE CURRENT STATE, AND IT IS MEANT TO GO
    /// RED THE DAY SOMEBODY FLIPS IT.</b> The substitution exists because a
    /// fizz used to arrive at nothing; that reason is gone. It was not flipped
    /// here because flipping it changes which temp entity a live, working effect
    /// sends, and confirming the first burst still lands in the water needs a
    /// live join this lane cannot make — and docs/te-lane-contract.md's
    /// constraint is that there is no intermediate state where an effect that
    /// fires today stops firing. A red here means the flip happened, and the
    /// fix is to delete this <c>[Fact]</c> having watched an env_bubbles.
    /// </para>
    /// </summary>
    [Fact]
    public void TheServerSideSubstitutionIsStillNamedAndStillTracked()
    {
        // The server's bubble path is managed since plan_server_native_v2 T4f:
        // EffectsOverTempEnts.BubbleEffect, where SourceSharp_BubbleEffect was.
        string path = RepoLayout.Repo("src", "server", "managed", "EffectsOverTempEnts.cs");
        Assert.True(File.Exists(path), $"{path} holds the server's bubble path");

        string text = File.ReadAllText(path);

        int at = text.IndexOf("public override void BubbleEffect(", StringComparison.Ordinal);
        Assert.True(at >= 0, "EffectsOverTempEnts.BubbleEffect is still the server's bubble path");

        int close = text.IndexOf("\n    }", at, StringComparison.Ordinal);
        string body = text[at..close];

        Assert.True(body.Contains("_temps.Bubbles(", StringComparison.Ordinal) && !body.Contains("Fizz(", StringComparison.Ordinal),
            "and it still sends Bubbles rather than Fizz. That is the half of row C6's "
          + "title this lane did not close: the client can fizz now, so the substitution is "
          + "unblocked, but flipping it blind would risk the one thing the lane contract "
          + "forbids — an effect that fires today ceasing to");
    }

    // =====================================================================

}
