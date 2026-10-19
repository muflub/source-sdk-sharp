using SourceSharp.Abi;
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

        public int Spawn(Vector origin, int modelIndex, in TempEntSpawn spawn)
        {
            if (Refuse)
                return -1;
            int slot = Made.Count + 3;
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
}
