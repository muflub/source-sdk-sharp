using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;
using SourceSharp.Tests.Ragdolls;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ClientPhysicsProps: CTEPhysicsProp's clientside physics prop, as stock's
/// CTempEnts::PhysicsProp built a C_PhysPropClientside (physpropclientside.cpp)
/// and broke it through props_shared.cpp's PropBreakableCreateAll.
/// </summary>
public class ClientPhysicsPropsTests
{
    private const int Crate = 40, Chunk = 41, ServerChunk = 42;
    private const string CrateModel = "models/props_junk/wood_crate001a.mdl";
    private const string ChunkModel = "models/props_junk/wood_crate001a_chunk01.mdl";
    private const string ServerChunkModel = "models/props_junk/wood_crate001a_chunk02.mdl";

    private sealed class Slots : IPhysicsPropSlots
    {
        public readonly List<(int Slot, Vector Origin, int Model, TempEntSpawn Spawn)> Made = [];
        public bool Refuse;

        public readonly HashSet<int> Adopted = [];
        public bool RefuseAdopt;

        public bool Adopt(int slot) => !RefuseAdopt && Adopted.Add(slot);

        public int Spawn(Vector origin, int modelIndex, in TempEntSpawn spawn)
        {
            if (Refuse)
                return -1;
            int slot = Made.Count + 3 + Adopted.Count * 100;
            Made.Add((slot, origin, modelIndex, spawn));
            return slot;
        }
    }

    private sealed class Names : IClientModelNames
    {
        public string? NameOf(int modelIndex) => modelIndex switch
        {
            Crate => CrateModel,
            Chunk => ChunkModel,
            ServerChunk => ServerChunkModel,
            _ => null,
        };

        public int IndexOf(string name) => name switch
        {
            CrateModel => Crate,
            ChunkModel => Chunk,
            ServerChunkModel => ServerChunk,
            _ => -1,
        };
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 50f;
    }

    private sealed class Seeds : IRandomSeeds
    {
        public int Next() => 7;
    }

    private readonly FakeRagdollVPhysics _vphysics = new()
    {
        Blocks =
        [
            new FakeRagdollVPhysics.SolidBlock(0, "first", "wood"),
            new FakeRagdollVPhysics.SolidBlock(1, "last", "wood_crate"),
        ],
    };

    private readonly Slots _slots = new();
    private readonly Clock _clock = new();
    private readonly RecordingLog _log = new();
    private int _maxClients = 4;

    // The crate's break list: one client piece and one the server makes.
    private const string CrateBreaks =
        "break { \"model\" \"props_junk/wood_crate001a_chunk01\" \"offset\" \"10 0 0\" \"fadetime\" \"8\" }\n"
      + "break { \"model\" \"props_junk/wood_crate001a_chunk02\" \"multiplayer_break\" \"server\" }";

    private ClientPhysicsProps Props()
    {
        var descriptors = new ModelDescriptors(_ => 0, _ => CrateModel, _log);
        var breaks = new BreakModels(new Names().IndexOf, null, index => index == Crate ? CrateBreaks : null, descriptors, _log);
        _vphysics.Surfaces["wood_crate"] = 9;
        return new ClientPhysicsProps(_vphysics, _slots, new Names(), breaks, _clock, () => _maxClients, new Seeds(), _log);
    }

    [Fact]
    public void A_prop_is_the_models_last_solid_placed_woken_and_given_its_velocity()
    {
        var props = Props();

        props.PhysicsProp(Crate, 2, new Vector(100f, 200f, 300f), QAngle.Zero, new Vector(0f, 0f, 250f), 0, 0);

        var obj = Assert.Single(_vphysics.Live);
        Assert.Equal(0x5000 + 1, obj.Collide);
        Assert.Equal("last", obj.SolidName);
        Assert.Equal(9, obj.SurfaceIndex);
        Assert.Equal(new Vector(100f, 200f, 300f), obj.Position);
        Assert.True(obj.LastTeleport);
        Assert.False(obj.Asleep);
        Assert.True(obj.CollisionsEnabled);
        Assert.Equal(new Vector(0f, 0f, 250f), obj.AddedVelocity);
        Assert.Equal(ClientPhysicsProps.DefaultRotInertiaLimit, obj.Overrides.RotInertiaLimit);
    }

    [Fact]
    public void The_prop_is_drawn_by_a_persistent_slot_of_its_model_and_skin_that_never_dies()
    {
        var props = Props();

        props.PhysicsProp(Crate, 2, new Vector(100f, 200f, 300f), new QAngle(0f, 90f, 0f), Vector.Zero, 0, 0);

        var made = Assert.Single(_slots.Made);
        Assert.Equal(Crate, made.Model);
        Assert.Equal(new Vector(100f, 200f, 300f), made.Origin);
        Assert.Equal(2, made.Spawn.Skin);
        Assert.Equal(new Vector(0f, 90f, 0f), made.Spawn.Angles);
        Assert.Equal((int)TempEntFlags.Persist, made.Spawn.Flags);
        Assert.Equal(float.MaxValue, made.Spawn.Die);
        Assert.Equal(1, props.Count);
    }

    [Fact]
    public void Each_frame_the_slot_is_where_its_object_is()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, new Vector(1f, 2f, 3f), QAngle.Zero, Vector.Zero, 0, 0);
        int slot = _slots.Made[0].Slot;
        var obj = _vphysics.Live.Single();
        var moved = Float3x4.Identity;
        moved[0, 3] = 40f;
        moved[2, 3] = -5f;
        obj.Matrix = moved;

        var transform = new TempEntTransform { Velocity = new Vector(9f, 9f, 9f) };
        Assert.True(props.Drive(slot, ref transform));

        Assert.Equal(new Vector(40f, 0f, -5f), transform.Origin);
        Assert.Equal(Vector.Zero, transform.Velocity);
        Assert.False(props.Drive(slot + 100, ref transform));
    }

    [Fact]
    public void A_freed_slot_destroys_its_object()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        props.Forget(_slots.Made[0].Slot);

        Assert.Empty(_vphysics.Live);
        Assert.Equal(0, props.Count);
    }

    [Fact]
    public void A_slot_freed_after_its_level_went_does_not_destroy_into_the_next()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        _vphysics.Env = 0x99;

        props.Forget(_slots.Made[0].Slot);

        Assert.Single(_vphysics.Live);
        Assert.Equal(0, props.Count);
    }

    [Fact]
    public void An_unknown_model_index_makes_nothing_and_says_so()
    {
        var props = Props();
        var said = new List<string>();
        _log.Line = (m, _) => said.Add(m);

        props.PhysicsProp(77, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        Assert.Empty(_vphysics.Objects);
        Assert.Empty(_slots.Made);
        Assert.Contains(said, m => m.Contains("model index 77 not found"));
    }

    [Fact]
    public void No_slot_means_no_object()
    {
        var props = Props();
        _slots.Refuse = true;

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        Assert.Empty(_vphysics.Live);
        Assert.Equal(0, props.Count);
    }

    [Fact]
    public void The_cap_is_cl_phys_props_max()
    {
        var props = Props();
        for (int i = 0; i < ClientPhysicsProps.MaxProps + 5; i++)
            props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        Assert.Equal(ClientPhysicsProps.MaxProps, props.Count);
        Assert.Equal(ClientPhysicsProps.MaxProps, _vphysics.Live.Count());
    }

    [Fact]
    public void A_break_model_makes_its_client_pieces_and_no_prop()
    {
        var props = Props();

        props.PhysicsProp(Crate, 1, new Vector(100f, 0f, 0f), QAngle.Zero, new Vector(0f, 0f, 40f), 1, 0);

        // The server's piece is skipped in multiplayer; the client's is made.
        var made = Assert.Single(_slots.Made);
        Assert.Equal(Chunk, made.Model);
        Assert.Equal(new Vector(110f, 0f, 0f), made.Origin);
        Assert.Equal(1, made.Spawn.Skin);
        Assert.Single(_vphysics.Live);
    }

    [Fact]
    public void A_piece_fades_out_over_FADEOUT_TIME_after_its_fade_time()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 1, 0);

        var spawn = _slots.Made.Single().Spawn;
        Assert.Equal((int)(TempEntFlags.Persist | TempEntFlags.FadeOut), spawn.Flags);
        Assert.Equal(_clock.Now + 8f, spawn.Die);
        Assert.Equal(1f / ClientPhysicsProps.FadeOutTime, spawn.FadeSpeed);
        Assert.Equal(255, spawn.RenderAmt);
    }

    [Fact]
    public void A_piece_is_thrown_at_the_props_velocity_within_five_percent_plus_its_burst()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, new Vector(100f, 0f, 0f), QAngle.Zero, new Vector(0f, 0f, 40f), 1, 0);

        var obj = _vphysics.Live.Single();
        // The burst: 100 (stock's defBurstScale) along the piece's offset from the break.
        Assert.Equal(100f, obj.AddedVelocity.X, 3);
        Assert.InRange(obj.AddedVelocity.Z, 40f * 0.975f, 40f * 1.025f);
    }

    [Fact]
    public void In_single_player_the_servers_pieces_are_the_clients_too()
    {
        _maxClients = 1;
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 1, 0);

        Assert.Equal([Chunk, ServerChunk], _slots.Made.Select(m => m.Model));
    }

    // ---- the owner: game data and collision group ---------------------------

    [Fact]
    public void A_props_object_carries_the_props_own_handle_as_its_game_data()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        EntityHandle owner = PhysicsGameData.Owner(_vphysics.Live.Single().Overrides.GameData);
        Assert.NotEqual(EntityHandle.None, owner);
        Assert.True(owner.IsInvisibleRange);
        Assert.Equal(props.HandleOf(_slots.Made[0].Slot), owner);
        Assert.True(props.IsProp(owner));
    }

    [Fact]
    public void Two_props_are_two_owners()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        var owners = _vphysics.Live.Select(o => PhysicsGameData.Owner(o.Overrides.GameData)).Distinct().ToList();
        Assert.Equal(2, owners.Count);
    }

    [Fact]
    public void A_freed_props_handle_names_nobody_even_when_its_entry_is_reused()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        EntityHandle first = props.HandleOf(_slots.Made[0].Slot);

        props.Forget(_slots.Made[0].Slot);
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        EntityHandle second = props.HandleOf(_slots.Made[1].Slot);

        Assert.False(props.IsProp(first));
        Assert.True(props.IsProp(second));
        Assert.Equal(first.EntryIndex, second.EntryIndex);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_prop_is_in_COLLISION_GROUP_PUSHAWAY()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        Assert.True(props.TryGroup(props.HandleOf(_slots.Made[0].Slot), out EngineCollisionGroup group));
        Assert.Equal(EngineCollisionGroup.PushAway, group);
    }

    [Fact]
    public void A_piece_with_health_stays_PUSHAWAY_and_one_with_none_is_COLLISION_GROUP_NONE()
    {
        var props = Props();

        // The crate's client piece writes no health, so it has the parser's default of 1.
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 1, 0);

        Assert.True(props.TryGroup(props.HandleOf(_slots.Made[0].Slot), out EngineCollisionGroup group));
        Assert.Equal(EngineCollisionGroup.PushAway, group);
        Assert.Equal(EngineCollisionGroup.None, ClientPhysicsProps.GroupFor(piece: true, health: 0f));
        Assert.Equal(EngineCollisionGroup.PushAway, ClientPhysicsProps.GroupFor(piece: false, health: 0f));
    }

    [Fact]
    public void The_solver_reads_a_props_group_and_a_ragdolls_as_DEBRIS()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        var facts = new SourceSharp.Client.Physics.ClientCollisionFacts(null!, null!, props);

        Assert.Equal(EngineCollisionGroup.PushAway, facts.GroupOf(props.HandleOf(_slots.Made[0].Slot)));
        Assert.Equal(EngineCollisionGroup.Debris, facts.GroupOf(new EntityHandle(5)));
    }

    [Fact]
    public void A_props_splash_is_placed_at_its_object()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, new Vector(10f, 20f, 30f), QAngle.Zero, Vector.Zero, 0, 0);
        var world = new SourceSharp.Client.Physics.ClientFluidWorld(null!, null!, null!, null!, null!, props);

        (Vector origin, _) = world.OwnerTransform(props.HandleOf(_slots.Made[0].Slot));

        Assert.Equal(new Vector(10f, 20f, 30f), origin);
    }

    // ---- the scrape ---------------------------------------------------------

    [Fact]
    public void A_props_scrape_is_the_worlds_to_play_and_anyone_elses_their_own()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        EntityHandle prop = props.HandleOf(_slots.Made[0].Slot);

        Assert.Equal(new EntityHandle(0), props.SoundSource(prop));
        Assert.Equal(new EntityHandle(77), props.SoundSource(new EntityHandle(77)));
    }

    // ---- the effects --------------------------------------------------------

    private const int EfNoShadow = 0x010, EfNoDraw = 0x020;

    [Fact]
    public void A_prop_with_EF_NODRAW_is_drawn_by_nobody()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, EfNoDraw);

        Assert.Equal(ClientPhysicsProps.RenderNone, _slots.Made.Single().Spawn.RenderMode);
    }

    [Fact]
    public void A_prop_without_EF_NODRAW_keeps_the_prepared_render_mode()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, EfNoShadow);

        Assert.Equal(TempEntSpawn.Prepared(0f).RenderMode, _slots.Made.Single().Spawn.RenderMode);
    }

    [Fact]
    public void A_broken_props_pieces_inherit_its_effects()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 1, EfNoShadow | EfNoDraw);

        Assert.Equal(ClientPhysicsProps.RenderNone, _slots.Made.Single().Spawn.RenderMode);
    }

    // ---- the piece limits ---------------------------------------------------

    private sealed class Limits : IPropBreakLimits
    {
        public int MaxPieces { get; set; } = -1;
        public int MaxPiecesPerFrame { get; set; } = -1;
        public int FrameCount { get; set; } = 1;
    }

    // Four client pieces, each its own block.
    private const string FourPieces =
        "break { \"model\" \"props_junk/wood_crate001a_chunk01\" }\n"
      + "break { \"model\" \"props_junk/wood_crate001a_chunk01\" }\n"
      + "break { \"model\" \"props_junk/wood_crate001a_chunk01\" }\n"
      + "break { \"model\" \"props_junk/wood_crate001a_chunk01\" }";

    private ClientPhysicsProps Limited(Limits limits)
    {
        var descriptors = new ModelDescriptors(_ => 0, _ => CrateModel, _log);
        var breaks = new BreakModels(new Names().IndexOf, null, index => index == Crate ? FourPieces : null, descriptors, _log);
        return new ClientPhysicsProps(_vphysics, _slots, new Names(), breaks, _clock, () => _maxClients, new Seeds(), _log,
                                      limits);
    }

    [Fact]
    public void Without_limits_every_piece_is_made()
    {
        var props = Limited(new Limits());

        Assert.Equal(4, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));
    }

    [Fact]
    public void props_break_max_pieces_stops_the_walk_at_its_index()
    {
        var props = Limited(new Limits { MaxPieces = 2 });

        Assert.Equal(2, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));
    }

    [Fact]
    public void The_per_frame_limit_lets_one_past_it_through_then_stops_the_frame()
    {
        var limits = new Limits { MaxPiecesPerFrame = 2 };
        var props = Limited(limits);

        // Stock breaks once the count is PAST the limit, so three are made.
        Assert.Equal(3, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));
        // The frame's count is spent: a second break makes nothing.
        Assert.Equal(0, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));

        // A new frame resets it.
        limits.FrameCount = 2;
        Assert.Equal(3, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));
    }

    [Fact]
    public void A_frame_that_made_exactly_its_limit_makes_no_more()
    {
        var props = Limited(new Limits { MaxPiecesPerFrame = 4 });

        Assert.Equal(4, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));
        // The frame's count equals the limit: the break returns before its walk.
        Assert.Equal(0, props.Break(CrateModel, 0, Vector.Zero, QAngle.Zero, Vector.Zero, Vector.Zero));
    }

    // ---- the reload ---------------------------------------------------------

    private static ReloadReader Carry(IReloadParticipant from)
    {
        var writer = new ReloadWriter();
        from.Save(writer);
        return new ReloadReader(writer.ToArray());
    }

    [Fact]
    public void A_prop_survives_a_reload_with_its_object_slot_handle_and_group()
    {
        var before = Props();
        before.PhysicsProp(Crate, 0, new Vector(5f, 6f, 7f), QAngle.Zero, Vector.Zero, 0, 0);
        int slot = _slots.Made[0].Slot;
        EntityHandle handle = before.HandleOf(slot);
        nint obj = before.ObjectOf(slot);

        var after = Props();
        var reader = Carry(before);
        after.Restore(reader);

        Assert.True(reader.AtEnd);
        Assert.Equal(1, after.Count);
        Assert.Equal(obj, after.ObjectOf(slot));
        Assert.Equal(handle, after.HandleOf(slot));
        Assert.True(after.IsProp(handle));
        Assert.True(after.TryGroup(handle, out EngineCollisionGroup group));
        Assert.Equal(EngineCollisionGroup.PushAway, group);
        Assert.Contains(slot, _slots.Adopted);

        var transform = new TempEntTransform();
        Assert.True(after.Drive(slot, ref transform));
        Assert.Equal(new Vector(5f, 6f, 7f), transform.Origin);
    }

    [Fact]
    public void A_prop_made_after_a_reload_names_a_different_handle()
    {
        var before = Props();
        before.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        EntityHandle carried = before.HandleOf(_slots.Made[0].Slot);

        var after = Props();
        after.Restore(Carry(before));
        after.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        Assert.NotEqual(carried, after.HandleOf(_slots.Made[1].Slot));
        Assert.Equal(2, after.Count);
    }

    [Fact]
    public void A_carried_prop_whose_slot_cannot_be_taken_back_is_destroyed()
    {
        var before = Props();
        before.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        _slots.RefuseAdopt = true;

        var after = Props();
        after.Restore(Carry(before));

        Assert.Equal(0, after.Count);
        Assert.Empty(_vphysics.Live);
    }

    // ---- the touch ----------------------------------------------------------

    [Fact]
    public void A_props_object_asks_for_touches_against_static_objects_keeping_its_other_flags()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        // The fake's objects start with GLOBAL_COLLISION | GLOBAL_FRICTION.
        Assert.Equal(0x0003 | ClientPhysicsProps.CallbackGlobalTouchStatic, _vphysics.Live.Single().CallbackFlags);
    }

    [Fact]
    public void A_broken_props_pieces_ask_for_static_touches_too()
    {
        var props = Props();

        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 1, 0);

        Assert.Equal(0x0008, _vphysics.Live.Single().CallbackFlags & 0x0008);
    }

    // ---- StartTouch and HitSurface ------------------------------------------

    private sealed class Hits : IClientPropHitSurfaces
    {
        public bool Splats;
        public readonly List<(EntityHandle Other, Vector Start, Vector End)> Decals = [];

        public bool BloodSplats(string model) => Splats;

        public void BloodDecal(EntityHandle other, Vector start, Vector end) => Decals.Add((other, start, end));
    }

    [Fact]
    public void A_props_world_touch_splats_blood_where_its_model_asks_at_most_every_tenth_of_a_second()
    {
        var props = Props();
        var hits = new Hits { Splats = true };
        props.HitSurfaces = hits;
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        EntityHandle prop = props.HandleOf(_slots.Made[0].Slot);
        var at = new Vector(4f, 5f, 6f);

        props.StartTouch(prop, ClientPhysicsProps.World, at, Vector.Zero);
        _clock.Now += 0.05f;
        props.StartTouch(prop, ClientPhysicsProps.World, at, Vector.Zero);
        _clock.Now += 0.1f;
        props.StartTouch(prop, ClientPhysicsProps.World, at, Vector.Zero);

        Assert.Equal(2, hits.Decals.Count);
        Assert.Equal((ClientPhysicsProps.World, Vector.Zero, at), hits.Decals[0]);
        Assert.Equal(3, props.Touches);
        Assert.Equal(3, props.WorldTouches);
        Assert.Equal(2, props.SurfaceHits);
    }

    [Fact]
    public void A_props_touch_splats_nothing_where_its_model_does_not_ask()
    {
        var props = Props();
        var hits = new Hits();
        props.HitSurfaces = hits;
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);

        props.StartTouch(props.HandleOf(_slots.Made[0].Slot), ClientPhysicsProps.World, Vector.Zero, Vector.Zero);

        Assert.Empty(hits.Decals);
        Assert.Equal(1, props.SurfaceHits);
    }

    [Fact]
    public void Only_a_live_prop_receives_touches()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        EntityHandle prop = props.HandleOf(_slots.Made[0].Slot);

        Assert.True(props.Receives(prop));
        props.Forget(_slots.Made[0].Slot);
        Assert.False(props.Receives(prop));
        Assert.False(props.Receives(ClientPhysicsProps.World));
    }

    [Fact]
    public void A_prop_resting_on_the_world_is_touched_by_the_world_end_to_end()
    {
        var props = Props();
        props.PhysicsProp(Crate, 0, Vector.Zero, QAngle.Zero, Vector.Zero, 0, 0);
        nint obj = _vphysics.Live.Single().Id;
        var touches = new SourceSharp.Client.Physics.ClientContactTouches(new VPhysicsWorld(_vphysics), props);

        touches.StartTouch(obj, 0x7770, 0x1);
        touches.UpdateTouchEvents();

        Assert.Equal(1, props.WorldTouches);
        Assert.Equal(1, touches.DispatchedByWorld);
    }

    private sealed class VPhysicsWorld(FakeRagdollVPhysics vphysics) : SourceSharp.Client.Physics.IClientTouchWorld
    {
        public nint GameData(nint o) => vphysics.Objects.ContainsKey(o) ? vphysics.GameData(o) : 0;

        public bool IsStatic(nint o) => o == 0x7770;

        public Vector ContactPoint(nint d) => Vector.Zero;

        public Vector SurfaceNormal(nint d) => Vector.Zero;
    }
}
