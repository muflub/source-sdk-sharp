//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Physics;

/// <summary>What the client's contact touches read of an object and a contact.</summary>
public interface IClientTouchWorld
{
    nint GameData(nint physicsObject);

    bool IsStatic(nint physicsObject);

    Vector ContactPoint(nint touchData);

    Vector SurfaceNormal(nint touchData);
}

/// <summary>The real one: the objects' and the contact's forwarders.</summary>
public sealed class VPhysicsTouchWorld(IVPhysicsObjects objects) : IClientTouchWorld
{
    public nint GameData(nint physicsObject) => objects.GameData(physicsObject);

    public bool IsStatic(nint physicsObject) => objects.IsStatic(physicsObject);

    public Vector ContactPoint(nint touchData) => objects.ContactPoint(touchData);

    public Vector SurfaceNormal(nint touchData) => objects.SurfaceNormal(touchData);
}

/// <summary>An entity that answers a touch: stock's StartTouch and EndTouch on it.</summary>
public interface IClientTouchReceiver
{
    /// <summary>Whether <paramref name="entity"/> is one of this receiver's.</summary>
    bool Receives(EntityHandle entity);

    /// <summary><c>entity-&gt;StartTouch( other )</c>, with the touch trace's end and plane normal.</summary>
    void StartTouch(EntityHandle entity, EntityHandle other, Vector point, Vector normal);

    /// <summary><c>entity-&gt;EndTouch( other )</c>.</summary>
    void EndTouch(EntityHandle entity, EntityHandle other);
}

/// <summary>
/// The client's contact touches: stock's client <c>CCollisionEvent::StartTouch</c>
/// and <c>EndTouch</c> (game/client/physics.cpp), buffered through the step
/// as <c>PhysicsSimulate</c>'s <c>BufferTouchEvents( true )</c> did and
/// dispatched at <c>FrameUpdate</c>'s <c>UpdateTouchEvents</c>.
///
/// <para>
/// THE OWNERS. Stock read each object's game data as a C_BaseEntity and
/// dropped a pair with either side empty. The world's object and the static
/// props' carried the world (PhysCreateWorld_Shared's GetClientWorldEntity());
/// ours carry nothing, so a static object with no game data is the world
/// (<see cref="World"/>), as the server's contact touches rule it
/// (PhysicsWorldEntity). Any other object names its tagged owner, or nobody.
/// vphysics reports a contact with a static object only when the other side
/// carries CALLBACK_GLOBAL_TOUCH_STATIC: the clientside props do.
/// </para>
///
/// <para>
/// THE DISPATCH. DispatchStartTouch's PhysicsMarkEntitiesAsTouchingEventDriven
/// links the pair both ways and runs StartTouch on each side the first time
/// (a held link is only re-stamped); DispatchEndTouch's
/// PhysicsNotifyOtherOfUntouch frees the link and runs EndTouch on each. The
/// held pairs stand for the event-driven touchlinks; a side no receiver owns
/// (the world here) has an empty StartTouch, as C_World's is.
/// </para>
/// </summary>
public sealed class ClientContactTouches(IClientTouchWorld world, IClientTouchReceiver receiver) : IClientTouchSink
{
    /// <summary>The world's handle: entry 0, serial 0, named on purpose.</summary>
    public static EntityHandle World => new(0);

    private enum Kind { Start, End }

    private readonly record struct TouchEvent(Kind Kind, EntityHandle First, EntityHandle Second, Vector Point, Vector Normal);

    private readonly List<TouchEvent> _events = [];
    private readonly HashSet<(EntityHandle, EntityHandle)> _held = [];

    /// <summary>Touch starts vphysics reported with an owner on both sides.</summary>
    public long Starts { get; private set; }

    /// <summary>Of <see cref="Starts"/>, those whose one side was the world.</summary>
    public long WorldStarts { get; private set; }

    /// <summary>StartTouch calls made on a receiver's entity.</summary>
    public long Dispatched { get; private set; }

    /// <summary>Of <see cref="Dispatched"/>, those whose toucher was the world.</summary>
    public long DispatchedByWorld { get; private set; }

    /// <summary>Touch ends vphysics reported with an owner on both sides.</summary>
    public long Ends { get; private set; }

    /// <summary>Pairs touching now.</summary>
    public int Held => _held.Count;

    /// <summary>The owner an object names: its tagged owner, the world for a static ownerless one, or nobody.</summary>
    public EntityHandle OwnerOf(nint physicsObject)
    {
        if (physicsObject == 0)
            return EntityHandle.None;

        nint gameData = world.GameData(physicsObject);
        if (gameData == 0)
            return world.IsStatic(physicsObject) ? World : EntityHandle.None;

        return PhysicsGameData.Owner(gameData);
    }

    /// <summary>CCollisionEvent::StartTouch: buffered with the contact point and normal.</summary>
    public void StartTouch(nint object1, nint object2, nint touchData)
    {
        EntityHandle first = OwnerOf(object1);
        EntityHandle second = OwnerOf(object2);
        if (first == EntityHandle.None || second == EntityHandle.None)
            return;

        Starts++;
        if (first == World || second == World)
            WorldStarts++;

        Vector point = touchData == 0 ? Vector.Zero : world.ContactPoint(touchData);
        Vector normal = touchData == 0 ? Vector.Zero : world.SurfaceNormal(touchData);
        _events.Add(new TouchEvent(Kind.Start, first, second, point, normal));
    }

    /// <summary>CCollisionEvent::EndTouch: buffered.</summary>
    public void EndTouch(nint object1, nint object2, nint touchData)
    {
        EntityHandle first = OwnerOf(object1);
        EntityHandle second = OwnerOf(object2);
        if (first == EntityHandle.None || second == EntityHandle.None)
            return;

        Ends++;
        _events.Add(new TouchEvent(Kind.End, first, second, Vector.Zero, Vector.Zero));
    }

    /// <summary>CCollisionEvent::UpdateTouchEvents: every buffered event, in order, then none.</summary>
    public void UpdateTouchEvents()
    {
        // Events a dispatch raises join the end and are run in this walk, as
        // stock's index loop over a growing list ran them.
        for (int i = 0; i < _events.Count; i++)
        {
            TouchEvent e = _events[i];
            if (e.Kind == Kind.Start)
                DispatchStartTouch(e.First, e.Second, e.Point, e.Normal);
            else
                DispatchEndTouch(e.First, e.Second);
        }

        _events.Clear();

        // A link whose entity went (a freed prop names nobody now) goes too.
        _held.RemoveWhere(p => Gone(p.Item1) || Gone(p.Item2));
    }

    private bool Gone(EntityHandle entity) => entity != World && !receiver.Receives(entity);

    private void DispatchStartTouch(EntityHandle first, EntityHandle second, Vector point, Vector normal)
    {
        if (first == second)
            return;

        MarkAsTouched(first, second, point, normal);
        MarkAsTouched(second, first, point, normal);
    }

    // PhysicsMarkEntityAsTouched: a new link runs StartTouch; a held one does not.
    private void MarkAsTouched(EntityHandle entity, EntityHandle other, Vector point, Vector normal)
    {
        if (!_held.Add((entity, other)))
            return;

        if (!receiver.Receives(entity))
            return;

        Dispatched++;
        if (other == World)
            DispatchedByWorld++;
        receiver.StartTouch(entity, other, point, normal);
    }

    private void DispatchEndTouch(EntityHandle first, EntityHandle second)
    {
        Untouch(first, second);
        Untouch(second, first);
    }

    private void Untouch(EntityHandle entity, EntityHandle other)
    {
        if (!_held.Remove((entity, other)))
            return;

        if (receiver.Receives(entity))
            receiver.EndTouch(entity, other);
    }

    /// <summary>A level's end.</summary>
    public void Clear()
    {
        _events.Clear();
        _held.Clear();
    }
}
