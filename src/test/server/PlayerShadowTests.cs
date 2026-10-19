using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The player's VPhysics shadow — <c>plan_entity.md</c> step 3f.
///
/// <para>
/// What it is FOR is one sentence: without it a player walks into a crate, is
/// stopped dead by it, and the crate moves 0.000 units. The live gate's
/// <c>push</c> step was a recorded skip for exactly that, measured per tick
/// before this step changed anything, and this file is what keeps the fix from
/// being quietly undone.
/// </para>
///
/// <para>
/// <b>THE LIVE HALF CANNOT BE TESTED HERE AND IS NOT PRETENDED TO BE.</b>
/// Whether a crate actually slides is a question about VPhysics on a loaded
/// map; <c>tools/3f-shadow-ab</c> is the instrument for it and the live gate is
/// the gate. What these facts pin is everything on THIS side of that: that the
/// spawn asks at all, with which numbers, that the numbers are the movement
/// loop's own hull rather than a second opinion about how big a player is, and
/// that the native half still has the four pieces stock's has.
/// </para>
/// </summary>
public unsafe class PlayerShadowTests
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    private static string PlayerCpp =>
        Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server",
                     "shell_standalone_player.cpp");

    /// <summary>
    /// The native source with its comments removed.
    ///
    /// <para>
    /// EVERY needle below goes through this. 67 of the 75 test files that read
    /// a native source in this tree match raw text, and step 3g's own
    /// replacement comment put two deleted cast sites straight back into the
    /// census that way. A file whose CODE lost a call but whose COMMENT still
    /// describes it would keep every one of these facts green.
    /// </para>
    /// </summary>
    private static string PlayerCode => AbiParityTests.CodeOnly(File.ReadAllText(PlayerCpp));

    private static RecordingEngine Rig()
    {
        var engine = TestServices.Fake();
        engine.Ignores("Entities.GetOrigin", "Entities.GetHealth");
        engine.Returns("Shadows.Init", true);
        return engine;
    }

    // -----------------------------------------------------------------------
    // What the spawn asks for
    // -----------------------------------------------------------------------

    /// <summary>
    /// A player who leaves hands their command queue back. Step 3d left the
    /// table bounded by a staleness sweep every 256 batches because nothing told
    /// it about a disconnect; this is the telling.
    /// </summary>
    [Fact]
    public unsafe void LeavingForgetsThePlayersCommandQueue()
    {
        var engine = Rig();
        var handle = new EntityHandle(0x0F0002);
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(), commands: _queue,
                                  shadows: new RecordingPlayerShadows(engine)) { Handle = handle };
        player.BindAreas(TestServices.Areas(engine));

        var cmds = new[] { new UserCmd { StructSize = (uint)sizeof(int), CommandNumber = 1 },
                           new UserCmd { StructSize = (uint)sizeof(int), CommandNumber = 2 } };
        int[] order = new int[8];

        fixed (UserCmd* pCmds = cmds)
        fixed (int* pOrder = order)
            _queue.Queue(handle, pCmds, cmds.Length, cmds.Length, 0, pOrder, order.Length);

        Assert.True(_queue.Depth(handle) > 0,
            "the queue has to have something in it, or the forget below would be "
          + "green against an empty table");

        player.OnRemoved();

        Assert.Equal(0, _queue.Depth(handle));
    }

    [Fact]
    public void SpawningAsksForAPhysicsShadow()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                shadows: new RecordingPlayerShadows(engine));
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();

        Assert.Equal(1, engine.CountOf("Shadows.Init"));
    }

    /// <summary>
    /// A RESPAWN ASKS AGAIN, and it has to: the native side teleports the
    /// existing shadow to wherever the new spawn put the player. A shadow left
    /// at the place they died spends the next second dragging itself across the
    /// level, shoving every crate on the way.
    /// </summary>
    [Fact]
    public void RespawningAsksAgain()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                shadows: new RecordingPlayerShadows(engine));
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();
        player.OnKilled(EntityHandle.None);
        player.OnSpawn();

        Assert.Equal(2, engine.CountOf("Shadows.Init"));
    }

    /// <summary>
    /// A22: CBasePlayer::Event_Killed's AddSolidFlags( FSOLID_NOT_SOLID )
    /// (player.cpp:1723). The collision rules read the owner's solid flags, so a
    /// dying player's shadow stops blocking and pushing props at once, not a
    /// think later when observer mode comes.
    /// </summary>
    [Fact]
    public void ADyingPlayerIsNotSolidAtOnce()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                shadows: new RecordingPlayerShadows(engine));
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();
        player.OnSpawn();

        player.OnKilled(EntityHandle.None);

        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.SetNotSolid" && Equals(c.Args[1], true));
    }

    [Fact]
    public void TheShadowIsAskedForWithThePlayersOwnHandle()
    {
        var engine = Rig();
        var handle = new EntityHandle((uint)((7 << 16) | 3));
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(), commands: _queue,
                                  shadows: new RecordingPlayerShadows(engine)) { Handle = handle };
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();

        Assert.Equal(handle, engine.Last("Shadows.Init")!.Value.ArgAs<EntityHandle>(0));
    }

    // -----------------------------------------------------------------------
    // The numbers, which are the game's and not the engine's
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>THE SHADOW IS THE SIZE THE MOVEMENT LOOP TRACES WITH.</b>
    ///
    /// <para>
    /// A shadow of a different size from the body is a player who is stopped by
    /// a crate their shadow has not reached, or who shoves one they are not
    /// touching. Restating the hull here would be a second opinion about how big
    /// a player is, so <see cref="SsPlayer.ShadowHulls"/> takes
    /// <see cref="MoveConstantDefaults.Stock"/>'s and this says so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShadowHullIsTheMovementHull()
    {
        var (standMin, standMax, crouchMin, crouchMax) = SsPlayer.ShadowHulls;
        var c = MoveConstantDefaults.Stock;

        Assert.Equal(c.HullMin, standMin);
        Assert.Equal(c.HullMax, standMax);
        Assert.Equal(c.DuckHullMin, crouchMin);
        Assert.Equal(c.DuckHullMax, crouchMax);
    }

    [Fact]
    public void TheHullsReachTheEngineInStandThenCrouchOrder()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                shadows: new RecordingPlayerShadows(engine));
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();

        var call = engine.Last("Shadows.Init")!.Value;
        var c = MoveConstantDefaults.Stock;

        Assert.Equal(c.HullMin, call.ArgAs<Vector>(1));
        Assert.Equal(c.HullMax, call.ArgAs<Vector>(2));
        Assert.Equal(c.DuckHullMin, call.ArgAs<Vector>(3));
        Assert.Equal(c.DuckHullMax, call.ArgAs<Vector>(4));
    }

    /// <summary>
    /// The crouch hull is SHORTER than the standing one, which is the whole
    /// reason there are two of them. Stated as its own fact because the pair
    /// above would be just as green if both entries named the same hull.
    /// </summary>
    [Fact]
    public void TheCrouchHullIsShorterThanTheStandingOne()
    {
        var (_, standMax, _, crouchMax) = SsPlayer.ShadowHulls;

        Assert.True(crouchMax.Z < standMax.Z);
    }

    [Fact]
    public void TheShadowWeighsWhatStocksDoes()
        => Assert.Equal(85f, SsPlayer.ShadowMass);

    [Fact]
    public void ThePushMassLimitIsStocks()
        => Assert.Equal(350f, SsPlayer.PushMassLimit);

    [Fact]
    public void ThePushSpeedLimitIsStocks()
        => Assert.Equal(50f, SsPlayer.PushSpeedLimit);

    /// <summary>
    /// A player may not shove something faster than they can walk. Not a
    /// restatement of the constant: it is the relation that makes a shove a
    /// shove — a crate that outruns the player pushing it is what the live gate
    /// measured on this step's first landing, and it read as the player being
    /// stopped by nothing.
    /// </summary>
    [Fact]
    public void APlayerCannotShoveSomethingFasterThanTheyWalk()
        => Assert.True(SsPlayer.PushSpeedLimit < MoveConstantDefaults.Stock.MaxSpeed);

    [Fact]
    public void TheThreeNumbersReachTheEngine()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                shadows: new RecordingPlayerShadows(engine));
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();

        var call = engine.Last("Shadows.Init")!.Value;

        Assert.Equal(SsPlayer.ShadowMass, call.ArgAs<float>(5));
        Assert.Equal(SsPlayer.PushMassLimit, call.ArgAs<float>(6));
        Assert.Equal(SsPlayer.PushSpeedLimit, call.ArgAs<float>(7));
    }

    // -----------------------------------------------------------------------
    // The vphysics half, read as CODE
    // -----------------------------------------------------------------------

    /// <summary>
    /// The pieces stock's shadow is made of, and none of them is optional: the
    /// box collide, the poly object built from it, the player controller and
    /// its two push limits. Managed's since plan_physics_bones_native_zero 2.5,
    /// called through vphysics' own tables (VPhysicsShadowCalls); and native
    /// makes none of the calls any more.
    /// </summary>
    [Theory]
    [InlineData("BBoxToCollide")]
    [InlineData("CreatePolyObject")]
    [InlineData("CreatePlayerController")]
    [InlineData("SetPushMassLimit")]
    [InlineData("SetPushSpeedLimit")]
    public void TheShadowStillHasItsPieceAndItIsManagedsCall(string needle)
    {
        Assert.Contains(needle, ShadowCallsCode);
        Assert.DoesNotContain(needle, PlayerCode);
    }

    private static string ShadowCallsCode => string.Concat(
        new[] { "VPhysicsShadowCalls.cs", "PhysicsCollision.cs" }.Select(f => AbiParityTests.CodeOnly(File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed", "SourceSharp.Server.Interop", f)))));

    private static string BodiesCode => AbiParityTests.CodeOnly(File.ReadAllText(
        Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed", "SourceSharp.Server.Interop",
                     "PlayerShadowBodies.cs")));

    /// <summary>
    /// <b>THE SHADOW IS DRIVEN FROM THE COMMAND LOOP, AND SO STILL COSTS NO
    /// COUNTED CROSSING.</b>
    ///
    /// <para>
    /// It used to be the last line of native stage 2, with the whole state
    /// machine behind it. The policy-player lane moved the decisions to
    /// <see cref="PlayerShadow.Plan"/> and the drive to
    /// <c>_queue.Run</c>, immediately after that same stage — the
    /// point in the sequence where stock runs <c>UpdateVPhysicsPosition</c>
    /// (player.cpp:3395), and the only place that knows WHICH command just ran.
    /// </para>
    ///
    /// <para>
    /// The crossing argument survives the move because BOTH of the slots it
    /// reaches are outbound, and the counters count native→managed thunk
    /// entries. This fact pins the position in the loop; the two below pin what
    /// the plan decides.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShadowStepRunsImmediatelyAfterTheCommandsOwnStageTwo()
    {
        string loop = AbiParityTests.CodeOnly(File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                         "SourceSharp.Server.Interop", "PlayerCommandQueue.cs")));

        int stage2 = loop.IndexOf("runMove(player, i, 2);", StringComparison.Ordinal);
        int step = loop.IndexOf("Shadow.Step(side, player, i, shadowEnabled);", StringComparison.Ordinal);

        Assert.True(stage2 >= 0, "stage 2 is still driven from the managed loop");
        Assert.True(step > stage2, "and the shadow step follows it");

        // AND NATIVE NO LONGER DRIVES IT. Without this the fact above would be
        // green on a build that did BOTH, which would update the shadow twice a
        // command.
        Assert.DoesNotContain("UpdateVCollision(", PlayerCode);
    }

    /// <summary>
    /// AND IT IS TOLD WHICH COMMAND IT IS. Stock ramps the shadow's arrival time
    /// by a tick per command in a batch (player.cpp:3368, :3399) — "if
    /// simulating at 2 * TICK_INTERVAL, add an extra TICK_INTERVAL to position
    /// arrival computation". A constant tick tells the controller to cover a
    /// whole packet's worth of travel in the time one command takes, which is
    /// how a shove becomes a kick.
    /// </summary>
    [Fact]
    public void TheArrivalTimeRampsWithTheCommandIndex()
    {
        PlayerShadowState s = LiveShadow();
        s.TickInterval = 0.015f;

        s.CommandIndex = 0;
        Assert.Equal(0.015f, PlayerShadow.Plan(in s).Arrival, 6);

        s.CommandIndex = 3;
        Assert.Equal(0.060f, PlayerShadow.Plan(in s).Arrival, 6);
    }

    //-------------------------------------------------------------------------
    // Stock's `!pPhysGround` (player.cpp:4791, :4811, :4813): a player standing
    // on a moveable vphysics object is riding it, not leaning on it. The aim is
    // not softened, and the wish velocity is not replaced by the max speed.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_player_on_a_moveable_physics_object_is_not_leaning()
    {
        var s = LiveShadow();
        s.ContactBits = PlayerShadow.ContactTouched;
        Assert.True(PlayerShadow.IsLeaning(in s), "the control: touching, on the ground, not stepping");

        s.GroundPhysicsMass = 30f;
        Assert.False(PlayerShadow.IsLeaning(in s));
    }

    [Fact]
    public void Riding_a_physics_object_aims_the_shadow_at_the_player()
    {
        var s = LiveShadow();
        s.ContactBits = PlayerShadow.ContactTouched;
        s.OutWishVel = new Vector(200f, 0f, 0f);
        s.Origin = new Vector(102f, 0f, 0f);
        Assert.NotEqual(s.Origin, PlayerShadow.Plan(in s).Position);   // the control: softened

        s.GroundPhysicsMass = 30f;
        Assert.Equal(s.Origin, PlayerShadow.Plan(in s).Position);
    }

    [Fact]
    public void Riding_a_physics_object_keeps_the_wish_velocity()
    {
        var s = LiveShadow();
        s.OutWishVel = new Vector(200f, 0f, 0f);
        Assert.Equal(new Vector(320f, 320f, 320f), PlayerShadow.Plan(in s).WishVelocity);   // the control

        s.GroundPhysicsMass = 30f;
        Assert.Equal(new Vector(200f, 0f, 0f), PlayerShadow.Plan(in s).WishVelocity);
    }

    /// <summary>
    /// A shadow this side of the boundary starts from a player who has one, is
    /// switched on, is standing still on the ground and is touching nothing.
    /// Each fact below moves exactly the field it is about.
    /// </summary>
    private static PlayerShadowState LiveShadow() => new()
    {
        StructSize = (uint)sizeof(PlayerShadowState),
        HasShadow = 1,
        Enabled = 1,
        Origin = new Vector(100f, 0f, 0f),
        OldOrigin = new Vector(100f, 0f, 0f),
        Velocity = Vector.Zero,
        OutWishVel = Vector.Zero,
        OutStepHeight = 0f,
        MaxSpeed = 320f,
        Flags = GameMovement.FlOnGround,
        MoveType = GameMovement.MoveTypeWalk,
        State = PlayerShadow.StateWalk,
        HasShadowPosition = 1,
        ShadowOrigin = new Vector(100f, 0f, 0f),
        ContactBits = 0,
        FrameTime = 0.015f,
        TickInterval = 0.015f,
        CommandIndex = 0,
    };

    /// <summary>
    /// A ducked player puts the SHORT hull in the solver, and the change is
    /// announced so native places it before anything else happens.
    /// </summary>
    [Fact]
    public void ADuckedPlayerPutsTheCrouchHullInTheSolver()
    {
        PlayerShadowState s = LiveShadow();
        s.Flags |= GameMovement.FlDucking;

        PlayerShadowPlan plan = PlayerShadow.Plan(in s);

        Assert.Equal(PlayerShadow.StateCrouch, plan.State);
        Assert.Equal(1, plan.StateChanged);
    }

    /// <summary>
    /// A NOCLIP player has no hull in the solver at all — walking through a
    /// wall must not shove what is on the other side of it.
    /// </summary>
    [Fact]
    public void ANoClipPlayerTakesTheirHullOutOfTheSolver()
    {
        PlayerShadowState s = LiveShadow();
        s.MoveType = GameMovement.MoveTypeNoClip;

        Assert.Equal(PlayerShadow.StateNoClip, PlayerShadow.Plan(in s).State);
    }

    /// <summary>An observer likewise: they are watching, not standing.</summary>
    [Fact]
    public void AnObserverTakesTheirHullOutOfTheSolver()
    {
        PlayerShadowState s = LiveShadow();
        s.MoveType = GameMovement.MoveTypeObserver;

        Assert.Equal(PlayerShadow.StateNoClip, PlayerShadow.Plan(in s).State);
    }

    /// <summary>
    /// A hull that has not changed is NOT re-placed. Announcing it every command
    /// would teleport the shadow to the player every tick, which is a shadow
    /// that can never be leaning on anything.
    /// </summary>
    [Fact]
    public void AnUnchangedHullIsNotAnnounced()
    {
        PlayerShadowState s = LiveShadow();

        Assert.Equal(0, PlayerShadow.Plan(in s).StateChanged);
    }

    /// <summary>
    /// <b>NOTHING IN THE WAY MEANS FULL SPEED.</b> player.cpp:4834 — an
    /// unresisted shadow may travel as fast as it likes to keep up, and stock
    /// says so by handing the controller the player's own speed cap as the wish
    /// velocity in all three axes.
    /// </summary>
    [Fact]
    public void AnUnresistedShadowIsGivenThePlayersOwnSpeedCap()
    {
        PlayerShadowState s = LiveShadow();
        s.OutWishVel = new Vector(10f, 0f, 0f);

        PlayerShadowPlan plan = PlayerShadow.Plan(in s);

        Assert.Equal(320f, plan.WishVelocity.X);
        Assert.Equal(320f, plan.WishVelocity.Y);
        Assert.Equal(320f, plan.WishVelocity.Z);
    }

    /// <summary>
    /// <b>AND TOUCHING SOMETHING MEANS THE MOVE'S OWN WISH.</b> The other half
    /// of the same rule, and the half step 3f measured: without it 94% of a push
    /// ran the full-force arm and the gate's crate outran the player pushing it,
    /// 119 units of crate against 74 of player.
    /// </summary>
    [Fact]
    public void ALeaningShadowKeepsTheMovesOwnWishVelocity()
    {
        PlayerShadowState s = LiveShadow();
        s.OutWishVel = new Vector(10f, 0f, 0f);
        s.ContactBits = PlayerShadow.ContactTouched;

        Assert.Equal(10f, PlayerShadow.Plan(in s).WishVelocity.X);
    }

    /// <summary>
    /// ONLY THE MOVE'S TOUCH SOFTENS IT (plan_physics_managed A16). Stock's
    /// PlayerRunCommand clears m_touchedPhysObject (player.cpp:3782) before the
    /// move's Touch sets it (:4774) and PostThinkVPhysics reads it (:4793,
    /// :4814); the controller's IsInContact is written in VPhysicsShadowUpdate
    /// (:8328), after the commands, and cleared before the next one reads it.
    /// So contact the solver found alone neither softens the wish nor leans the
    /// aim: the shadow is sent at max speed to where the player is.
    /// </summary>
    [Fact]
    public void TheControllersContactAloneDoesNotSoftenTheShove()
    {
        PlayerShadowState s = LiveShadow();
        s.OutWishVel = new Vector(10f, 0f, 0f);
        s.OldOrigin = new Vector(90f, 0f, 0f);
        s.ContactBits = PlayerShadow.ContactController;

        PlayerShadowPlan plan = PlayerShadow.Plan(in s);

        Assert.Equal(new Vector(320f, 320f, 320f), plan.WishVelocity);
        Assert.Equal(s.Origin, plan.Position);
        Assert.False(PlayerShadow.IsLeaning(in s));
    }

    /// <summary>
    /// The halfway aim, player.cpp:4816: a player leaning on something has the
    /// shadow aimed between where the move put them and where their wish would
    /// carry them, so it does not arrive at full speed inside the thing.
    /// </summary>
    [Fact]
    public void ALeaningShadowIsAimedHalfwayToTheWish()
    {
        PlayerShadowState s = LiveShadow();
        s.Origin = new Vector(100f, 0f, 0f);
        s.OldOrigin = new Vector(90f, 0f, 0f);
        s.OutWishVel = new Vector(200f, 0f, 0f);   // 200 * 0.015 = 3 units
        s.ContactBits = PlayerShadow.ContactTouched;

        // (100 + (90 + 3)) / 2
        Assert.Equal(96.5f, PlayerShadow.Plan(in s).Position.X, 4);
    }

    /// <summary>
    /// And a player who is NOT leaning is aimed at where the move actually put
    /// them. Without this the fact above would pass on a build that halved
    /// every position.
    /// </summary>
    [Fact]
    public void AnUnresistedShadowIsAimedAtThePlayer()
    {
        PlayerShadowState s = LiveShadow();
        s.Origin = new Vector(100f, 0f, 0f);
        s.OldOrigin = new Vector(90f, 0f, 0f);
        s.OutWishVel = new Vector(200f, 0f, 0f);

        Assert.Equal(100f, PlayerShadow.Plan(in s).Position.X, 4);
    }

    /// <summary>
    /// A small step is climbed; player.cpp:4857's four units is the line between
    /// a climb the controller can cover in a tick and a placement it cannot.
    /// </summary>
    [Fact]
    public void AShortStepIsClimbed()
    {
        PlayerShadowState s = LiveShadow();
        s.OutStepHeight = 3f;

        PlayerShadowPlan plan = PlayerShadow.Plan(in s);

        Assert.Equal(PlayerShadow.StepClimb, plan.StepUp);
        Assert.Equal(3f, plan.StepHeight);
    }

    /// <summary>A tall one is a placement, not a climb.</summary>
    [Fact]
    public void ATallStepTeleportsTheHull()
    {
        PlayerShadowState s = LiveShadow();
        s.OutStepHeight = 12f;

        Assert.Equal(PlayerShadow.StepTeleport, PlayerShadow.Plan(in s).StepUp);
    }

    /// <summary>
    /// And a step under a tenth of a unit is not a step at all. Telling the
    /// controller to climb one every tick jitters the shadow on a flat floor.
    /// </summary>
    [Fact]
    public void AFlatFloorIsNotAStep()
    {
        PlayerShadowState s = LiveShadow();
        s.OutStepHeight = 0.05f;

        Assert.Equal(PlayerShadow.StepNone, PlayerShadow.Plan(in s).StepUp);
    }

    /// <summary>
    /// <b>A TELEPORT IS NOT SNAPPED BY THE PLAN</b> (plan_physics_managed A18).
    /// Stock has no snap. The managed one assumed a shadow left far behind by
    /// setpos is dragged along the line, shouldering whatever is on it; measured
    /// live on physics-managed (2026-10-05, dm_lockdown, snap disabled): two
    /// 400-unit setpos jumps straight through the concrete_chunk field moved no
    /// chunk, snaps stayed 0, and the shadow stood at the destination -- vphysics
    /// teleports a shadow more than 24 units from its target. The plan aims at
    /// the player and leaves the rest to the controller.
    /// </summary>
    [Fact]
    public void AShadowLeftFarBehindIsAimedAtThePlayerAndNotSnapped()
    {
        PlayerShadowState s = LiveShadow();
        s.Origin = new Vector(500f, 0f, 0f);
        s.ShadowOrigin = new Vector(100f, 0f, 0f);

        PlayerShadowPlan plan = PlayerShadow.Plan(in s);

        Assert.Equal(0, plan.Snap);
        Assert.Equal(500f, plan.Position.X);
    }

    /// <summary>
    /// A player with no shadow produces no plan at all — distinct from a plan
    /// of zeroes, which would be a real instruction to aim at the world origin.
    /// </summary>
    [Fact]
    public void APlayerWithNoShadowProducesNoPlan()
    {
        PlayerShadowState s = LiveShadow();
        s.HasShadow = 0;

        Assert.Equal(0, PlayerShadow.Plan(in s).Valid);
    }

    /// <summary>
    /// THE BOTH-SIDES SIZE CHECK. Two hand-written declarations of one layout
    /// is the drift the ABI's fixed-width types exist to prevent, and a struct
    /// that disagreed by a field would read garbage rather than fail.
    /// <c>shell_standalone_player.cpp</c> carries the matching
    /// <c>COMPILE_TIME_ASSERT</c>s.
    /// </summary>
    [Fact]
    public void TheShadowFactsAreTheSizeTheNativeSideAsserts()
    {
        // plan_physics_bones_native_zero 2.5: the state and the plan are
        // managed-only now; the one struct that crosses is the facts.
        Assert.Equal(PlayerShadowFacts.SizeInBytes, sizeof(PlayerShadowFacts));
        Assert.Contains("COMPILE_TIME_ASSERT( sizeof( SourceSharpPlayerShadowFacts ) == 192 )", PlayerCode);
        Assert.DoesNotContain("SourceSharpPlayerShadowState", PlayerCode);
        Assert.DoesNotContain("SourceSharpPlayerShadowPlan", PlayerCode);
    }

    /// <summary>
    /// STOCK'S RIDEABLE GROUND (player.cpp:4603-4612, :4854-4867): ground
    /// physics heavier than twice the shadow is handed to the controller as
    /// its ground, so it solves in that object's frame. The shadow is 85 kg
    /// (SsPlayer.ShadowMass), so the line is 170.
    /// </summary>
    [Theory]
    [InlineData(171f, 1)]
    [InlineData(170f, 0)]   // stock's test is strictly greater
    [InlineData(40f, 0)]
    [InlineData(0f, 0)]     // no physics ground
    public void GroundHeavierThanTwiceTheShadowIsRideable(float groundMass, int rideable)
    {
        PlayerShadowState s = LiveShadow();
        s.ShadowMass = 85f;
        s.GroundPhysicsMass = groundMass;

        Assert.Equal(rideable, PlayerShadow.Plan(in s).RideableGround);
    }

    /// <summary>
    /// ...and the decision is carried out: the ground's object, looked up only
    /// when the plan says rideable, is Update's fifth argument. Managed's apply
    /// since plan_physics_bones_native_zero 2.5 (PlayerShadowBodiesTests'
    /// A_rideable_ground_is_handed_to_the_controller holds the behaviour).
    /// </summary>
    [Fact]
    public void TheApplyHandsTheControllerTheRideableGround()
    {
        Assert.Contains("nint ground = plan.RideableGround != 0 ? GroundObject(in facts) : 0;", BodiesCode);
        Assert.Matches(@"_v\.Update\(facts\.Controller, plan\.Position, plan\.WishVelocity, plan\.Arrival, plan\.OnGround != 0, ground\);",
                       BodiesCode);
    }

    /// <summary>
    /// <b>STOCK'S m_touchedPhysObject IS PORTED, AND IT IS NOT A DETAIL.</b>
    ///
    /// <para>
    /// Both arms of <c>PostThinkVPhysics</c> that soften the shove are gated on
    /// it. Measured with the controller's own <c>IsInContact()</c> standing in
    /// for it, it was true on 359 of 5760 updates, so 94% of a push ran the
    /// full-force arm and the gate's crate outran the player pushing it — 119
    /// units of crate against 74 of player. With the flag ported, the same four
    /// rounds read 66-68 units of crate every time.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTouchListIsClearedAtTheStartOfEveryCommand()
    {
        var clear = PlayerCode.IndexOf("m_nMoveTouched = 0;", StringComparison.Ordinal);

        Assert.True(clear >= 0, "the per-command clear is still there");
        Assert.True(clear < PlayerCode.IndexOf("m_pCurrentCommand = ucmd;", StringComparison.Ordinal),
            "and it happens BEFORE the command is adopted, which is where stock "
          + "clears m_touchedPhysObject (player.cpp:3801, the first line of PlayerRunCommand)");
    }

    /// <summary>
    /// The move helper's touch list is RECORDED natively and judged in managed
    /// (PlayerShadowBodiesTests hold the judgement, stock's four tests and the
    /// ground). Native asking IsMoveable() again would be the decision back.
    /// </summary>
    [Fact]
    public void NativeRecordsTheMovesTouchesAndDecidesNothing()
    {
        var addBody = Regex.Match(PlayerCode,
            @"virtual bool AddToTouched\( const CGameTrace &tr, const Vector &impactvelocity \) OVERRIDE\s*\{(.*?)\n\t\}",
            RegexOptions.Singleline);

        Assert.True(addBody.Success, "the move helper still notes what it ran into");
        Assert.Contains("RecordMoveTouch(", addBody.Groups[1].Value);
        Assert.DoesNotContain("IsMoveable", PlayerCode);
        Assert.DoesNotContain("NoteTouchedPhysics", PlayerCode);
    }

    /// <summary>
    /// The shadow is torn down when the player goes, <b>and the destructor is
    /// the ONLY thing that does it</b>. A managed second owner for the same
    /// teardown was tried and reverted — a live-gate run with a
    /// <c>DestroyVCollision</c> in <c>SsPlayer.OnRemoved</c> came back with
    /// every physics prop in the SECOND map falling five thousand units through
    /// the floor. <see cref="NothingButTheDestructorTakesTheShadowAway"/> is the
    /// other half of this fact.
    /// </summary>
    [Fact]
    public void TheDestructorTakesTheShadowWithIt()
    {
        var dtor = Regex.Match(PlayerCode,
            @"CStandalonePlayer::~CStandalonePlayer\(\)\s*\{(.*?)\n\}",
            RegexOptions.Singleline);

        Assert.True(dtor.Success, "the destructor is still recognisable");
        Assert.Contains("DestroyVCollision()", dtor.Groups[1].Value);
    }

    /// <summary>
    /// <b>THE SHELL'S OWN PHYSICS POINTER IS NOT AIMED AT THE SHADOW.</b>
    ///
    /// <para>
    /// Stock's <c>SetVCollisionState</c> calls <c>VPhysicsSwapObject</c>, which
    /// aliases the entity's physics object onto whichever hull is active. Here
    /// that member is what <c>CStandaloneEntity::UpdatePhysics</c> copies onto
    /// the entity every frame, so pointing it at the shadow would let the
    /// solver's answer overwrite the position the movement loop just computed.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShadowIsNotInstalledAsTheShellsPhysicsObject()
        => Assert.DoesNotContain("VPhysicsSwapObject", PlayerCode);

    /// <summary>
    /// <b>ONE OWNER FOR THE TEARDOWN.</b> There is no <c>PlayerDestroyVCollision</c>
    /// slot and there must not be one: the shadow's lifetime is the player's and
    /// the player's is the engine's edict, both native. Written as an absence of
    /// the ABI name rather than as a call count, because the failure this pins is
    /// somebody adding the slot back and calling it from a managed teardown that
    /// runs at a different point of a level's life than the destructor does.
    /// </summary>
    [Fact]
    public void TheAbiHasNoWayToTakeAShadowAway()
    {
        // The server's slots are its shim areas' since plan_engineapi_vtables C6c.
        var areas = File.ReadAllText(RepoLayout.Repo("src", "shared", "native", "ishim_areas.h"));
        var abi = File.ReadAllText(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp",
                                                "host", "sourcesharp.h"));

        // plan_physics_bones_native_zero 2.5: the body is managed's to build
        // (PlayerShadowBodies), so the slots are native's half of it -- and
        // PlayerShadowRelease asks for native's teardown, DestroyVCollision,
        // which is still the one implementation of it.
        Assert.Contains("PlayerShadowAdopt", areas);
        Assert.Contains("PlayerShadowRelease", areas);
        Assert.DoesNotContain("PlayerDestroyVCollision", areas);
        Assert.DoesNotContain("PlayerDestroyVCollision", abi);

        // ASKED FOR AT NATIVE'S TWO POINTS AND NO OTHER: the body Init drops
        // because its environment has gone, and the plan's Destroy when the
        // switch is turned off live -- the two places native's InitVCollision
        // and ShadowApply called DestroyVCollision. Never at a removal, which
        // is the second owner that was tried and reverted.
        var bodies = AbiParityTests.CodeOnly(File.ReadAllText(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp",
            "managed", "SourceSharp.Server.Interop", "PlayerShadowBodies.cs")));
        Assert.Equal(2, Regex.Matches(bodies, @"_native\.Release\(player\);").Count);
        Assert.Contains("if (plan.Destroy != 0)", bodies);
        Assert.Contains("if (f.HasShadow != 0 && f.BodyCurrent == 0)", bodies);
    }

    /// <summary>
    /// THE PLAYER'S STEP IS MANAGED'S (plan_physics_bones_native_zero 2.3):
    /// which hull is awake after the step, its stress and its shadow update
    /// are PhysicsFrame's over PlayerShadowBodies.AwakeShadow and
    /// PlayerShadowStress. Native keeps none of the walk, and object_stress.h
    /// is gone with its caller; the presence half names where each went.
    /// </summary>
    [Fact]
    public void The_players_step_and_stress_are_managed()
    {
        string code = PlayerCode;
        Assert.DoesNotContain("AwakeShadow(", code);
        Assert.DoesNotContain("ShadowStress(", code);
        Assert.DoesNotContain("StandalonePlayer_AwakeShadows", code);
        Assert.DoesNotContain("StandalonePlayer_RecordShadowStress", code);
        Assert.DoesNotContain("SourceSharp_ObjectStress", code);
        Assert.False(File.Exists(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server", "object_stress.h")),
                     "object_stress.h is back: the stress sum is PhysicsObjectStress's");

        string managed = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed");
        string bodies = AbiParityTests.CodeOnly(File.ReadAllText(Path.Combine(managed, "SourceSharp.Server.Interop", "PlayerShadowBodies.cs")));
        Assert.Contains("public nint AwakeShadow(EntityHandle player, out PlayerShadowFacts facts)", bodies);
        string stress = AbiParityTests.CodeOnly(File.ReadAllText(Path.Combine(managed, "SourceSharp.Server", "PlayerShadowStress.cs")));
        Assert.Contains("PhysicsObjectStress.Compute(in subject, _contacts.AsSpan(0, n))", stress);
    }

    /// <summary>
    /// AND NO MANAGED CODE ASKS FOR ONE. The other half: an absent slot is only
    /// half the guard, because the next person to want a managed teardown would
    /// add both. This is a sweep of the whole managed tree, so a new caller in a
    /// file this test has never heard of is red.
    /// </summary>
    [Fact]
    public void NoManagedCodeTakesAShadowAway()
    {
        var managed = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed");
        // THIS FILE IS EXCLUDED and CodeOnly is why it has to be: the needle
        // below is a STRING LITERAL here, and CodeOnly copies literals whole
        // (deliberately -- a "//" inside one is not a comment). So the sweep
        // would always find itself. Excluded by full path, not by name.
        var self = Path.Combine(managed, "SourceSharp.Server.Tests", "PlayerShadowTests.cs");

        var offenders = Directory.EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories)
                                 .Where(f => !Path.GetFullPath(f).Equals(Path.GetFullPath(self)))
                                 // CODE ONLY. SsPlayer's own doc comment explains
                                 // why the call is not there, and a raw read
                                 // would count the explanation as the offence --
                                 // which is step 3g's census mistake in a new
                                 // place.
                                 .Where(f => AbiParityTests.CodeOnly(File.ReadAllText(f))
                                                            .Contains("DestroyVCollision"))
                                 .Select(f => Path.GetFileName(f))
                                 .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// <b>A SHADOW IS ONLY FREED THROUGH THE ENVIRONMENT THAT BUILT IT.</b>
    ///
    /// <para>
    /// <c>physenv</c> is a global destroyed and recreated at every level change
    /// (<c>CPhysicsHook::LevelShutdownPostEntity</c>). An object outlives its
    /// environment only as a pointer, and handing a stale one to the NEW
    /// environment's <c>DestroyObject</c> is a free of whatever that environment
    /// put at the address — whose visible symptom is the next map's props
    /// falling through its floor.
    /// </para>
    /// </summary>
    [Fact]
    public void AStaleShadowIsDroppedRatherThanFreed()
    {
        var destroy = Regex.Match(PlayerCode,
            @"void CStandalonePlayer::DestroyVCollision\(\)\s*\{(.*?)\n\}",
            RegexOptions.Singleline);

        Assert.True(destroy.Success);
        Assert.Contains("physenv == m_pShadowEnv", destroy.Groups[1].Value);
        Assert.DoesNotContain("physenv->DestroyObject", destroy.Groups[1].Value);
        Assert.DoesNotContain("physenv->DestroyPlayerController", destroy.Groups[1].Value);
    }

    /// <summary>
    /// AND A STALE ONE IS NOT REUSED EITHER. The respawn arm teleports the
    /// shadow it already has; doing that to three pointers into a freed
    /// environment is a write through them.
    /// </summary>
    /// <summary>
    /// The three containment convars of lane physics-defects stay gone, from
    /// the native file and from the managed body that replaced its build
    /// (shadow_priming_pin_test's last case, carried at 2.5).
    /// </summary>
    [Theory]
    [InlineData("ShadowSwitchPrimingOn")]
    [InlineData("ShadowSwitchDamping")]
    [InlineData("ShadowSwitchNow")]
    [InlineData("sourcesharp_shadow_stock_params")]
    public void TheContainmentConVarsAreGoneFromBothHalves(string needle)
    {
        Assert.DoesNotContain(needle, PlayerCode);
        Assert.DoesNotContain(needle, BodiesCode);
    }

    [Fact]
    public void AStaleShadowIsNotTeleportedOnRespawn()
    {
        // Native says whether the held body is the current environment's...
        Assert.Contains("pOut->bBodyCurrent = m_pShadowEnv && m_pShadowEnv == physenv ? 1 : 0;", PlayerCode);
        // ...and managed drops a stale one before the respawn arm could teleport it.
        Assert.Contains("if (f.HasShadow != 0 && f.BodyCurrent == 0)", BodiesCode);
    }

    /// <summary>
    /// The switch is honoured LIVE, which is what makes an A/B possible in one
    /// process — <c>tools/3f-shadow-ab</c> stands on it, and so does the control
    /// run that showed the first landing of this step was breaking
    /// <c>projectile</c>.
    /// </summary>
    [Fact]
    public void TheShadowCanBeTurnedOffWithoutARebuild()
    {
        // THE SWITCH IS DECLARED IN MANAGED CODE. It was `ConVar
        // sourcesharp_player_shadow` in the shim; no native declaration of it
        // may come back, or two variables would answer to one word.
        Assert.DoesNotContain("ConVar sourcesharp_player_shadow(", PlayerCode);
        Assert.Equal("sourcesharp_player_shadow",
                     typeof(PlayerShadowConVar).GetCustomAttributes(typeof(ConVarAttribute), false)
                         .Cast<ConVarAttribute>().Single().Name);

        // ...AND NATIVE READS IT NOWHERE: the body is managed's to build or
        // not (PlayerShadowBodies.Enabled), and the step writes it from the
        // side that owns it.
        Assert.DoesNotContain("sourcesharp_player_shadow", PlayerCode);

        // ...the DECISION it drives is managed...
        PlayerShadowState off = LiveShadow();
        off.Enabled = 0;

        PlayerShadowPlan plan = PlayerShadow.Plan(in off);

        Assert.Equal(1, plan.Valid);
        Assert.Equal(1, plan.Destroy);

        // ...and a switched-on player is not destroyed, or the fact above would
        // pass on a build that tore the shadow down every command.
        PlayerShadowState on = LiveShadow();
        Assert.Equal(0, PlayerShadow.Plan(in on).Destroy);

        // ...and it is carried out: the plan's Destroy asks native for its
        // teardown (PlayerShadowBodiesTests' A_destroy_plan_releases_the_body).
        Assert.Contains("void SourceSharp_PlayerShadowRelease( uint32_t hPlayer )", PlayerCode);
        Assert.Contains("pPlayer->DestroyVCollision();", PlayerCode);
    }

    //=========================================================================
    // THE JAM -- lane defect-shadow. Three facts, and each pins one link of a
    // mechanism that was measured live before it was written down here.
    //
    // The live gate's `push` step is bimodal over 89 measured holds at the
    // gate's own two-second hold, in ten processes: the player either covers
    // 199-286 units and shoves the crate the whole way (23 of 89), or covers
    // 18-109, meets the crate and STOPS, with neither moving again while
    // +forward is held (66 of 89). It is a deadlock and not a slow push -- at
    // two and a half times the hold the jam-mode walk is unchanged -- and
    // during a jam the shadow sits a median 2.15 units in FRONT of the player
    // against 0.62 on a free walk (n=15 vs 7, exact permutation p = 0.0043).
    //
    // These three facts are why 2.15, and why nothing here can undo it.
    //=========================================================================

    /// <summary>
    /// <b>A BLOCKED PLAYER'S SHADOW IS AIMED AT A FIXED POINT.</b>
    ///
    /// <para>
    /// The softened aim is <c>0.5 * Origin + 0.5 * (OldOrigin + wishVel * dt)</c>
    /// — stock's, at player.cpp:4816. When the crate has stopped the player, the
    /// move leaves <see cref="PlayerShadowState.Origin"/> where it was, so
    /// <c>OldOrigin == Origin</c> and the whole expression collapses to
    /// <c>Origin + 0.5 * wishVel * dt</c>: an ABSOLUTE point a fixed short
    /// distance ahead, recomputed to the same place every command.
    /// </para>
    ///
    /// <para>
    /// So the shadow reaches it, shoves the crate the few units that takes, and
    /// stops. Nothing here advances <c>Origin</c>, so nothing advances the
    /// target. Stock advances it from the other side, in
    /// <c>CBasePlayer::VPhysicsShadowUpdate</c> — <c>SetAbsOrigin( newPosition )</c>
    /// at player.cpp:8505 moves the PLAYER to where the shadow got to — and this
    /// port does not carry that path.
    /// </para>
    ///
    /// <para>
    /// 190 units a second at a 66 Hz tick is <c>0.5 * 190 * 0.015 = 1.425</c>,
    /// and the measured jam gap's median was 2.15 with frame times running long
    /// under the probe. The arithmetic and the measurement are the same number.
    /// </para>
    /// </summary>
    [Fact]
    public void ABlockedPlayersShadowIsAimedAtAFixedPointAhead()
    {
        PlayerShadowState s = LiveShadow();
        s.OutWishVel = new Vector(190f, 0f, 0f);
        s.ContactBits = PlayerShadow.ContactTouched;   // the move's touch leans it (A16)
        s.FrameTime = 0.015f;
        // BLOCKED: the move ran and left the player exactly where it found them.
        s.OldOrigin = s.Origin;

        PlayerShadowPlan first = PlayerShadow.Plan(in s);

        Assert.Equal(s.Origin.X + 0.5f * 190f * 0.015f, first.Position.X, 4);

        // AND THE NEXT COMMAND ASKS FOR THE SAME PLACE. That is the deadlock:
        // a target that does not move is a shove that happens once.
        s.CommandIndex = 1;
        Assert.Equal(first.Position.X, PlayerShadow.Plan(in s).Position.X, 4);
    }

    /// <summary>
    /// <b>THE OVERHANG IS HALF THE PLAYER'S SHORTFALL, AND THAT IS THE WHOLE
    /// RULE.</b>
    ///
    /// <para>
    /// Writing the fact above as an inequality got it wrong, and the failure is
    /// the better statement: a player moving at exactly their wish velocity has
    /// the shadow aimed at their origin EXACTLY, not behind it. The aim is
    /// <c>Origin + 0.5 * (wished - Origin)</c>, so the distance the shadow sits
    /// in front of the player is half of how far short of their wish the move
    /// fell — zero when nothing resisted them, <c>0.5 * wishVel * dt</c> when
    /// the crate stopped them dead.
    /// </para>
    ///
    /// <para>
    /// Measured live, both ends of exactly that: a median 0.62 units on a free
    /// walk against 2.15 on a jam. It is here so the fixed-point fact cannot be
    /// read as "the aim is always ahead" — the overhang is a consequence of the
    /// origin being STUCK, and a build that aimed both modes the same way would
    /// fail this one.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShadowSitsHalfThePlayersShortfallInFrontOfThem()
    {
        PlayerShadowState s = LiveShadow();
        s.OutWishVel = new Vector(190f, 0f, 0f);
        s.ContactBits = PlayerShadow.ContactTouched;   // the move's touch leans it (A16)
        s.FrameTime = 0.015f;

        // FREE: the last command carried them the whole 2.85 units their wish
        // asked for, so the shortfall is nothing and the aim is their origin.
        s.OldOrigin = new Vector(s.Origin.X - 2.85f, s.Origin.Y, s.Origin.Z);
        Assert.Equal(s.Origin.X, PlayerShadow.Plan(in s).Position.X, 4);

        // HALF BLOCKED: they got half of it, and the shadow sits half of what
        // is left in front of them.
        s.OldOrigin = new Vector(s.Origin.X - 1.425f, s.Origin.Y, s.Origin.Z);
        Assert.Equal(s.Origin.X + 0.7125f, PlayerShadow.Plan(in s).Position.X, 4);

        // FULLY BLOCKED: the fixed overhang the jam sits at.
        s.OldOrigin = s.Origin;
        Assert.Equal(s.Origin.X + 1.425f, PlayerShadow.Plan(in s).Position.X, 4);
    }


    // A19: stock clamps a climb of four units or less to what the shadow's hull
    // can actually rise (player.cpp:4828-4838).
    [Fact]
    public void A_climb_under_a_ceiling_is_clamped_to_the_room_the_hull_has()
    {
        var stopped = new EntityHullTrace(new Vector(0f, 0f, 101.5f), false, false);
        Assert.Equal(1.5f, PlayerShadow.ClampStep(3f, 100f, in stopped), 4);
    }

    [Fact]
    public void A_climb_with_room_keeps_its_height()
    {
        var clear = new EntityHullTrace(new Vector(0f, 0f, 103f), false, false);
        Assert.Equal(3f, PlayerShadow.ClampStep(3f, 100f, in clear), 4);
    }

    [Fact]
    public void A_shadow_already_in_solid_climbs_nothing()
    {
        var stuck = new EntityHullTrace(new Vector(0f, 0f, 100f), true, true);
        Assert.Equal(0f, PlayerShadow.ClampStep(3f, 100f, in stuck), 4);
    }
}
