using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics stock parity B7: CBasePlayer::VPhysicsShadowUpdate, once per
/// physics frame after the step. The shadow's solved position and achieved
/// velocity are compared with the player's; past 2 units or 10 in/s (a quarter
/// of each on rideable ground) a player touching physics -- or standing on it --
/// is given the shadow's velocity less their own along their direction of
/// travel, and moved to the shadow if they fit there; a frozen controller is
/// recovered toward the game position; and a player found in solid is put back
/// where they last were valid.
/// </summary>
public class PlayerShadowRecoveryTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly Vector Mins = new(-16f, -16f, 0f);
    private static readonly Vector Maxs = new(16f, 16f, 72f);
    private const int OnGround = 1;

    private static PlayerShadowState State(Vector shadow, Vector shadowVelocity = default, Vector velocity = default,
                                           int contact = 0, bool updated = true, bool frozen = false,
                                           bool penetrating = false, float groundMass = 0f, int flags = OnGround,
                                           int moveType = 2)
        => new()
        {
            HasShadow = 1,
            Origin = Vector.Zero,
            Velocity = velocity,
            HasShadowPosition = 1,
            ShadowOrigin = shadow,
            ShadowVelocity = shadowVelocity,
            ContactBits = contact,
            PhysicsUpdated = updated ? 1 : 0,
            WasFrozen = frozen ? 1 : 0,
            PenetratingMover = penetrating ? 1 : 0,
            GroundPhysicsMass = groundMass,
            ShadowMass = 85f,
            Flags = flags,
            MoveType = moveType,
        };

    /// <summary>
    /// The service over the double. <paramref name="solidAt"/> says which
    /// points a stationary hull trace finds solid; a sweep stops at
    /// <paramref name="sweepEnd"/>.
    /// </summary>
    private static (PlayerShadowRecovery Recovery, RecordingEngine Engine) World(
        PlayerShadowState state, Func<Vector, bool>? solidAt = null, Vector? sweepEnd = null, bool alive = true,
        SsPlayer? player = null, PlayerShadowTrace? trace = null, float physTimescale = 1f,
        Func<EntityHandle, bool>? penetratedByMover = null, PlayerMoveMemory? memory = null)
    {
        var engine = Fake();
        var directory = new EntityDirectory(new RecordingLog());
        if (player is not null)
            directory.Bind(Player, player);
        engine.Returns("Shadows.Probe", (PlayerShadowState?)state)
              .Returns("Players.IsAlive", alive)
              .Returns("Entities.GetBounds", (Mins: Mins, Maxs: Maxs))
              .Ignores("Entities.SetOrigin", "Entities.ApplyVelocityImpulse");
        engine.ReturnsEachCall("WorldQuery.EntityTraceHull", () =>
        {
            var call = engine.Calls.Last(c => c.Member == "WorldQuery.EntityTraceHull");
            Vector start = call.ArgAs<Vector>(1), end = call.ArgAs<Vector>(2);
            bool solid = solidAt?.Invoke(start) ?? false;
            Vector stop = start.Equals(end) ? end : (sweepEnd ?? end);
            return new EntityHullTrace(stop, solid, solid && start.Equals(end));
        });
        return (new PlayerShadowRecovery(engine, engine, engine, new RecordingPlayerShadows(engine), directory, trace,
                                         () => physTimescale,
                                         penetratedByMover, memory), engine);
    }

    private static List<Vector> Moves(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Entities.SetOrigin").Select(c => c.ArgAs<Vector>(1)).ToList();

    private static List<Vector> Impulses(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Entities.ApplyVelocityImpulse").Select(c => c.ArgAs<Vector>(1)).ToList();

    private const int Touched = PlayerShadow.ContactTouched;

    [Fact]
    public void Without_a_solved_position_nothing_happens()
    {
        var (recovery, engine) = World(State(new Vector(5f, 0f, 0f), contact: Touched, updated: false));

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
        Assert.Empty(Impulses(engine));
    }

    [Fact]
    public void A_shadow_two_units_off_brings_a_touching_player_to_it()
    {
        var (recovery, engine) = World(State(new Vector(3f, 0f, 0f), contact: Touched));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(3f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void A_frozen_simulation_moves_no_one_toward_their_shadow()
    {
        // phys_timescale 0: physicsUpdated = false (player.cpp:8349-8353). The
        // same shadow three units off moves the player at timescale 1 (above).
        var (recovery, engine) = World(State(new Vector(3f, 0f, 0f), contact: Touched), physTimescale: 0f);

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
        Assert.Empty(Impulses(engine));
    }

    [Fact]
    public void A_frozen_player_whose_trace_did_not_start_in_solid_stays_put()
    {
        // player.cpp:8389-8390: the frozen self-check is startsolid ONLY. An
        // all-solid trace that did not start in solid returns before the
        // back-sweep; requiring both relocated the player.
        var (recovery, engine) = World(State(new Vector(5f, 0f, 0f), frozen: true), sweepEnd: new Vector(2f, 0f, 0f));
        engine.ReturnsEachCall("WorldQuery.EntityTraceHull", () =>
        {
            var call = engine.Calls.Last(c => c.Member == "WorldQuery.EntityTraceHull");
            Vector start = call.ArgAs<Vector>(1), end = call.ArgAs<Vector>(2);
            return start.Equals(end)
                ? new EntityHullTrace(end, StartSolid: false, AllSolid: true)
                : new EntityHullTrace(new Vector(2f, 0f, 0f), StartSolid: false, AllSolid: false);
        });

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
    }

    [Fact]
    public void A_shadow_within_two_units_leaves_the_player_alone()
    {
        var (recovery, engine) = World(State(new Vector(1f, 0f, 0f), contact: Touched));

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
        Assert.Empty(Impulses(engine));
    }

    [Fact]
    public void An_untouched_player_far_from_their_shadow_is_only_checked_for_being_stuck()
    {
        // Out of tolerance, not touching, no physics ground: bCheckStuck, and
        // the player fits where they are.
        var (recovery, engine) = World(State(new Vector(5f, 0f, 0f)));

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
        Assert.Contains(engine.Calls, c => c.Member == "WorldQuery.EntityTraceHull");
    }

    [Fact]
    public void An_untouched_player_found_in_solid_is_put_back_where_the_shadow_was()
    {
        // bCheckStuck with lastValidPosition still the solved position.
        var (recovery, engine) = World(State(new Vector(5f, 0f, 0f)), solidAt: p => p.Equals(Vector.Zero));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(5f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void A_velocity_error_gives_the_shadows_velocity_less_the_players_own()
    {
        // deltaV 50^2 >= 10^2. Along the player's travel (+X) the shadow's 100
        // is the player's 100 and is removed; the 50 across it is the impulse.
        var (recovery, engine) = World(State(Vector.Zero, shadowVelocity: new Vector(100f, 50f, 0f),
                                             velocity: new Vector(100f, 0f, 0f), contact: Touched));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(0f, 50f, 0f) }, Impulses(engine));
    }

    //-------------------------------------------------------------------------
    // cs_sv_playershadow_trace (A39): the push jam's suspects are per-frame
    // values, so the recovery says which arm ran and what impulse it gave.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_trace_names_the_impulse_arm_and_the_impulse()
    {
        var log = new RecordingLog();
        var (recovery, _) = World(State(Vector.Zero, shadowVelocity: new Vector(40f, 0f, 0f),
                                        velocity: new Vector(100f, 0f, 0f), contact: Touched),
                                  trace: new PlayerShadowTrace(log, () => true));

        recovery.Update(Player);

        // Along +X the shadow's 40 is within the player's 100, so all of it is
        // removed and the impulse is zero along the walk: no pull back.
        Assert.Contains("recover:", log.Text);
        Assert.Contains("arm impulse+move impulse (0, 0, 0) along 0", log.Text);
    }

    [Fact]
    public void The_trace_off_says_nothing()
    {
        var log = new RecordingLog();
        var (recovery, engine) = World(State(Vector.Zero, shadowVelocity: new Vector(100f, 50f, 0f),
                                             velocity: new Vector(100f, 0f, 0f), contact: Touched),
                                       trace: new PlayerShadowTrace(log, () => false));

        recovery.Update(Player);

        Assert.NotEmpty(Impulses(engine));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void The_plan_line_says_how_far_ahead_the_shadow_is_aimed_and_when_it_arrives()
    {
        var log = new RecordingLog();
        var state = State(Vector.Zero, contact: Touched);
        state.OutWishVel = new Vector(190f, 0f, 0f);
        var plan = new PlayerShadowPlan { Valid = 1, Position = new Vector(3f, 4f, 0f), Arrival = 0.03f };

        new PlayerShadowTrace(log, () => true).Planned(Player, in state, in plan);

        Assert.Contains("wish (190, 0, 0)", log.Text);
        Assert.Contains("leaning 1 ground 1 groundmass 0 stepheight 0 ", log.Text);
        Assert.Contains("-> aim (3, 4, 0) lead 5 ", log.Text);
        Assert.Contains("arrive 0.03", log.Text);
    }

    [Fact]
    public void A_pull_back_against_the_walk_reads_negative_along_it()
        => Assert.Equal(-30f, PlayerShadowTrace.Along(new Vector(-30f, 5f, 0f), new Vector(100f, 0f, 0f)));

    [Fact]
    public void A_frozen_controller_where_the_player_fits_changes_nothing()
    {
        var (recovery, engine) = World(State(new Vector(5f, 0f, 0f), contact: Touched, frozen: true));

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
        Assert.Empty(Impulses(engine));
    }

    [Fact]
    public void A_frozen_controller_in_solid_moves_the_player_toward_the_shadow()
    {
        // In solid at the game position: sweep from the shadow back toward it
        // and take where the sweep stopped.
        var (recovery, engine) = World(State(new Vector(5f, 0f, 0f), contact: Touched, frozen: true),
                                       solidAt: p => p.Equals(Vector.Zero), sweepEnd: new Vector(4f, 0f, 0f));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(4f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void Rideable_ground_tightens_the_tolerance_to_a_quarter()
    {
        // 1.5 units: inside 2 (4.0 squared), outside a quarter of it (1.0).
        // Ground of 500 kg is rideable for an 85 kg shadow.
        var (ridden, rideEngine) = World(State(new Vector(1.5f, 0f, 0f), contact: Touched, groundMass: 500f));
        ridden.Update(Player);
        var (flat, flatEngine) = World(State(new Vector(1.5f, 0f, 0f), contact: Touched));
        flat.Update(Player);

        Assert.Equal(new[] { new Vector(1.5f, 0f, 0f) }, Moves(rideEngine));
        Assert.Empty(Moves(flatEngine));
    }

    [Fact]
    public void Standing_on_physics_reconciles_even_untouched()
    {
        // (pPhysGround && !m_touchedPhysObject) opens the error arm by itself.
        var (recovery, engine) = World(State(new Vector(1f, 0f, 0f), groundMass: 50f));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(1f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void A_penetrating_mover_counts_as_touching()
    {
        var (recovery, engine) = World(State(new Vector(3f, 0f, 0f), penetrating: true));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(3f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void A_mover_in_the_managed_penetration_ledger_counts_as_touching()
    {
        // 2.1: the ledger is managed's, so PhysGetListOfPenetratingEntities'
        // answer comes from the solver, not the native snapshot field.
        var asked = new List<EntityHandle>();
        var (recovery, engine) = World(State(new Vector(3f, 0f, 0f)),
                                       penetratedByMover: h => { asked.Add(h); return true; });

        recovery.Update(Player);

        Assert.Equal(new[] { Player }, asked);
        Assert.Equal(new[] { new Vector(3f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void Without_a_penetrating_mover_or_a_contact_the_player_stays()
    {
        var (recovery, engine) = World(State(new Vector(3f, 0f, 0f)), penetratedByMover: _ => false);

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
    }

    [Fact]
    public void A_dead_or_noclipping_player_is_left_alone()
    {
        var (dead, deadEngine) = World(State(new Vector(3f, 0f, 0f), contact: Touched), alive: false);
        dead.Update(Player);
        var (noclip, noclipEngine) = World(State(new Vector(3f, 0f, 0f), contact: Touched, moveType: 8));
        noclip.Update(Player);

        Assert.Empty(Moves(deadEngine));
        Assert.Empty(Moves(noclipEngine));
    }

    //-------------------------------------------------------------------------
    // PFLAG_GAMEPHYSICS_ROTPUSH (player.cpp:8320): a rotating pusher moved the
    // player, so this update checks whether that left them stuck.
    //-------------------------------------------------------------------------

    private static SsPlayer RotPushed()
    {
        var engine = Fake();
        return new SsPlayer(engine, engine, Impacts(engine), Fluid())
        {
            PhysicsFlags = SsPlayer.PhysicsFlagGamePhysicsRotPush,
        };
    }

    [Fact]
    public void A_rotating_push_has_a_player_found_in_solid_put_back_where_the_shadow_was()
    {
        // Within tolerance and untouched: without the flag nothing is checked.
        // With it, bCheckStuck, and lastValidPosition is the solved position.
        var (recovery, engine) = World(State(new Vector(1f, 0f, 0f)), solidAt: p => p.Equals(Vector.Zero),
                                       player: RotPushed());

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(1f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void Without_a_rotating_push_the_same_player_is_left_alone()
    {
        var (recovery, engine) = World(State(new Vector(1f, 0f, 0f)), solidAt: p => p.Equals(Vector.Zero),
                                       player: new SsPlayer(Fake(), Fake(), Impacts(Fake()), Fluid()));

        recovery.Update(Player);

        Assert.Empty(Moves(engine));
    }

    [Fact]
    public void The_rotating_push_flag_is_consumed_by_the_update()
    {
        // Stock clears it before the noclip and not-updated returns, so it is
        // consumed even by an update that does nothing else.
        SsPlayer player = RotPushed();
        var (recovery, _) = World(State(new Vector(1f, 0f, 0f), updated: false), player: player);

        recovery.Update(Player);

        Assert.Equal(0, player.PhysicsFlags & SsPlayer.PhysicsFlagGamePhysicsRotPush);
    }

    //-------------------------------------------------------------------------
    // PFLAG_VPHYSICS_MOTIONCONTROLLER (player.cpp:8326, :8425): a player in a
    // trigger_vphysics_motion counts as touching, and the velocity impulse is
    // eased back toward the player's own travel by Lerp( 0.1, len, dot ).
    //-------------------------------------------------------------------------

    private static SsPlayer OnMotionController()
    {
        var engine = Fake();
        return new SsPlayer(engine, engine, Impacts(engine), Fluid())
        {
            PhysicsFlags = SsPlayer.PhysicsFlagVPhysicsMotionController,
        };
    }

    [Fact]
    public void A_player_on_the_motion_controller_counts_as_touching()
    {
        // Untouched and three units off: stock's touched arm moves them to the
        // shadow only because the flag says touched.
        var (recovery, engine) = World(State(new Vector(3f, 0f, 0f)), player: OnMotionController());

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(3f, 0f, 0f) }, Moves(engine));
    }

    [Fact]
    public void The_motion_controller_eases_the_impulse_toward_the_players_travel()
    {
        // dir +X, len 100, dot 40: newVelocity = (40,50,0) - 40 dir = (0,50,0);
        // val = Lerp( 0.1, 100, 40 ) = 94; newVelocity += (94 - 100) dir.
        var (recovery, engine) = World(State(Vector.Zero, shadowVelocity: new Vector(40f, 50f, 0f),
                                             velocity: new Vector(100f, 0f, 0f), contact: Touched),
                                       player: OnMotionController());

        recovery.Update(Player);

        var impulse = Assert.Single(Impulses(engine));
        Assert.Equal(-6f, impulse.X, 3);
        Assert.Equal(50f, impulse.Y, 3);
    }

    [Fact]
    public void Without_the_motion_controller_the_same_impulse_is_not_eased()
    {
        var (recovery, engine) = World(State(Vector.Zero, shadowVelocity: new Vector(40f, 50f, 0f),
                                             velocity: new Vector(100f, 0f, 0f), contact: Touched));

        recovery.Update(Player);

        Assert.Equal(new[] { new Vector(0f, 50f, 0f) }, Impulses(engine));
    }

    //-------------------------------------------------------------------------
    // ONE m_oldOrigin (player.cpp:4851, :8338, :8487): the command and the
    // shadow update write the same member and read the same member.
    //-------------------------------------------------------------------------

    [Fact]
    public void An_overlapped_stuck_player_goes_back_to_where_the_last_command_left_them()
    {
        Vector valid = new(100f, 40f, 8f);
        var memory = new PlayerMoveMemory();
        memory.SetOldOrigin(Player, valid); // PostThinkVPhysics, at the end of the last command

        Vector shadow = valid + new Vector(1f, 0f, 0f);
        var (recovery, engine) = World(State(shadow, contact: Touched) with { Origin = valid }, solidAt: _ => true,
                                       memory: memory);

        recovery.Update(Player);

        Assert.Equal(new[] { shadow, valid }, Moves(engine));
    }

    [Fact]
    public void The_shadow_update_writes_the_old_origin_the_next_command_reads()
    {
        // VPhysicsShadowUpdate's last line: m_oldOrigin = GetAbsOrigin().
        var memory = new PlayerMoveMemory();
        Vector at = new(3f, 0f, 0f);
        var (recovery, _) = World(State(at, contact: Touched), memory: memory);

        recovery.Update(Player);

        Assert.Equal(at, memory.OldOrigin(Player));
    }

    //-------------------------------------------------------------------------
    // Across a reload (player.cpp:311): m_oldOrigin is DATADESC, and the
    // overlapped arm's stuck check puts the player back there.
    //-------------------------------------------------------------------------

    [Fact]
    public void After_a_reload_an_overlapped_stuck_player_goes_back_to_their_last_valid_origin()
    {
        Vector valid = new(100f, 40f, 8f);

        // One quiet update at the valid spot: m_oldOrigin = valid.
        var (before, _) = World(State(valid) with { Origin = valid });
        before.Update(Player);

        var record = new ReloadRecord("SourceSharp.Server.PlayerShadowRecovery");
        Assert.IsAssignableFrom<ISaveCustom>(before).OnSave(new SaveRecordAccess(record, record.Owner, new RecordingLog()));

        // Within tolerance and touched, but in solid where they are and at the
        // shadow: moved to the shadow, still stuck, so back to m_oldOrigin.
        Vector shadow = valid + new Vector(1f, 0f, 0f);
        var (after, engine) = World(State(shadow, contact: Touched) with { Origin = valid }, solidAt: _ => true);
        Assert.IsAssignableFrom<ISaveCustom>(after).OnRestore(new SaveRecordAccess(record, record.Owner, new RecordingLog(),
                                                                                 new SavedHandles(engine, engine)));

        after.Update(Player);

        Assert.Equal(new[] { shadow, valid }, Moves(engine));
    }
}
