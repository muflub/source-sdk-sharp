using SourceSharp.Abi;
using SourceSharp.Client.Physics;
using SourceSharp.Client.TempEnts;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ClientContactTouches: stock's client CCollisionEvent::StartTouch/EndTouch
/// (physics.cpp), buffered through the step and dispatched at
/// UpdateTouchEvents through PhysicsMarkEntitiesAsTouchingEventDriven; the
/// world's side, a static object with no game data, is the world entity.
/// </summary>
public class ClientContactTouchesTests
{
    private const nint WorldObject = 0x10, Prop = 0x20, Ragdoll = 0x30, Loose = 0x40, Contact = 0x99;

    private static readonly EntityHandle PropOwner = new(2050), RagdollOwner = new(7);

    private sealed class World : IClientTouchWorld
    {
        public nint GameData(nint o) => o switch
        {
            Prop => PhysicsGameData.FromHandle(PropOwner),
            Ragdoll => PhysicsGameData.FromHandle(RagdollOwner),
            _ => 0,
        };

        public bool IsStatic(nint o) => o == WorldObject;

        public Vector ContactPoint(nint d) => new(1f, 2f, 3f);

        public Vector SurfaceNormal(nint d) => new(0f, 0f, 1f);
    }

    private sealed class Receiver : IClientTouchReceiver
    {
        public readonly List<(EntityHandle Entity, EntityHandle Other, Vector Point)> Starts = [];
        public readonly List<(EntityHandle Entity, EntityHandle Other)> Ends = [];
        public bool Alive = true;

        public bool Receives(EntityHandle entity) => Alive && entity == PropOwner;

        public void StartTouch(EntityHandle entity, EntityHandle other, Vector point, Vector normal)
            => Starts.Add((entity, other, point));

        public void EndTouch(EntityHandle entity, EntityHandle other) => Ends.Add((entity, other));
    }

    private readonly Receiver _receiver = new();

    private ClientContactTouches Touches() => new(new World(), _receiver);

    [Fact]
    public void A_static_object_with_no_game_data_is_the_world_entity()
    {
        var touches = Touches();

        Assert.Equal(new EntityHandle(0), touches.OwnerOf(WorldObject));
        Assert.Equal(PropOwner, touches.OwnerOf(Prop));
        Assert.Equal(EntityHandle.None, touches.OwnerOf(Loose));
    }

    [Fact]
    public void A_prop_on_the_world_is_touched_by_the_world_at_the_contact()
    {
        var touches = Touches();

        touches.StartTouch(Prop, WorldObject, Contact);
        touches.UpdateTouchEvents();

        var start = Assert.Single(_receiver.Starts);
        Assert.Equal(PropOwner, start.Entity);
        Assert.Equal(ClientContactTouches.World, start.Other);
        Assert.Equal(new Vector(1f, 2f, 3f), start.Point);
        Assert.Equal(1, touches.WorldStarts);
        Assert.Equal(1, touches.DispatchedByWorld);
    }

    [Fact]
    public void A_touch_waits_for_the_steps_end()
    {
        var touches = Touches();

        touches.StartTouch(WorldObject, Prop, Contact);

        Assert.Empty(_receiver.Starts);
        touches.UpdateTouchEvents();
        Assert.Single(_receiver.Starts);
    }

    [Fact]
    public void A_pair_with_an_ownerless_side_is_dropped()
    {
        var touches = Touches();

        touches.StartTouch(Prop, Loose, Contact);
        touches.UpdateTouchEvents();

        Assert.Empty(_receiver.Starts);
        Assert.Equal(0, touches.Starts);
    }

    [Fact]
    public void A_held_pair_touches_once_until_it_ends()
    {
        var touches = Touches();

        touches.StartTouch(Prop, WorldObject, Contact);
        touches.StartTouch(Prop, WorldObject, Contact);
        touches.UpdateTouchEvents();
        Assert.Single(_receiver.Starts);

        touches.EndTouch(Prop, WorldObject, Contact);
        touches.UpdateTouchEvents();
        Assert.Equal((PropOwner, ClientContactTouches.World), Assert.Single(_receiver.Ends));

        touches.StartTouch(Prop, WorldObject, Contact);
        touches.UpdateTouchEvents();
        Assert.Equal(2, _receiver.Starts.Count);
    }

    [Fact]
    public void An_entity_no_receiver_owns_takes_no_touch()
    {
        var touches = Touches();

        touches.StartTouch(Ragdoll, WorldObject, Contact);
        touches.UpdateTouchEvents();

        Assert.Empty(_receiver.Starts);
        Assert.Equal(1, touches.Starts);
    }

    [Fact]
    public void A_gone_props_links_go()
    {
        var touches = Touches();
        touches.StartTouch(Prop, WorldObject, Contact);
        touches.UpdateTouchEvents();
        Assert.Equal(2, touches.Held);

        _receiver.Alive = false;
        touches.UpdateTouchEvents();

        Assert.Equal(0, touches.Held);
    }

    // ---- PROPINTER_WORLD_BLOODSPLAT ------------------------------------------

    [Fact]
    public void A_model_splats_blood_only_with_prop_data_and_the_world_interaction()
    {
        Assert.False(ClientPropHitSurfaces.Parse("\"mdl\" { \"prop_data\" { \"base\" \"Wooden.Small\" } }"));
        Assert.False(ClientPropHitSurfaces.Parse(
            "\"mdl\" { \"world_interactions\" { \"onworldimpact\" \"bloodsplat\" } }"));
        Assert.True(ClientPropHitSurfaces.Parse(
            "\"mdl\" { \"prop_data\" { \"base\" \"Flesh.Small\" } \"world_interactions\" { \"onworldimpact\" \"bloodsplat\" } }"));
        Assert.False(ClientPropHitSurfaces.Parse(null));
    }
}
