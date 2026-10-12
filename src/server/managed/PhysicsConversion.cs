//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// What phys_convert's conversion reads and asks of the world, one engine
/// call each. Decides nothing: the decisions are <see cref="PhysicsConversion"/>'s.
/// </summary>
public interface IPhysicsConversionWorld
{
    MoveType MoveTypeOf(EntityHandle entity);

    string ClassName(EntityHandle entity);

    string Name(EntityHandle entity);

    string ModelName(EntityHandle entity);

    /// <summary>Whether the entity's model is a brush model (<c>mod_brush</c>).</summary>
    bool IsBrushModel(EntityHandle entity);

    Vector AbsOrigin(EntityHandle entity);

    Vector AbsAngles(EntityHandle entity);

    /// <summary>The entity unparented, keeping where it is (stock's <c>UnlinkFromParent</c>).</summary>
    void Unparent(EntityHandle entity);

    /// <summary>The entities parented to <paramref name="parent"/>, each with its attachment.</summary>
    IReadOnlyList<(EntityHandle Child, int Attachment)> Children(EntityHandle parent);

    /// <summary>The child parented to <paramref name="parent"/>, keeping where it is.</summary>
    void SetParent(EntityHandle child, EntityHandle parent, int attachment);

    void SetModel(EntityHandle entity, string model);

    EntityHandle Create(string className);

    void SetKeyValue(EntityHandle entity, string key, string value);

    void SetOrigin(EntityHandle entity, Vector origin);

    void SetAngles(EntityHandle entity, Vector angles);

    void Spawn(EntityHandle entity);

    nint PhysicsObject(EntityHandle entity);

    /// <summary>Stock's <c>VPhysicsSwapObject</c> (the shell's hold); false when the handle names no shell.</summary>
    bool Swap(EntityHandle entity, nint physicsObject, out nint previous);

    /// <summary>The shell's <c>DestroyPhysics</c>.</summary>
    void DestroyPhysics(EntityHandle entity);

    /// <summary>The entity's movetype word, as <see cref="IEntityPhysicsHost.SetMoveType"/>.</summary>
    void SetMoveType(EntityHandle entity, MoveType moveType);

    void SetMass(EntityHandle entity, float mass);

    void SetCollisionGroup(EntityHandle entity, CollisionGroup group);

    int PoseParameterCount(EntityHandle entity);

    float GetPoseParameter(EntityHandle entity, int parameter);

    void SetPoseParameter(EntityHandle entity, int parameter, float value);

    /// <summary><c>AddSolidFlags( FSOLID_NOT_SOLID )</c>.</summary>
    void SetNotSolid(EntityHandle entity);

    /// <summary><c>AddEffects( EF_NODRAW )</c>.</summary>
    void SetNoDraw(EntityHandle entity);

    void Remove(EntityHandle entity);

    /// <summary>Stock's <c>Msg</c>.</summary>
    void Message(string text);
}

/// <summary>phys_convert's conversion of one target, as the entity asks it.</summary>
public interface IPhysicsConversion
{
    /// <summary>The simple physics entity that replaces <paramref name="target"/>, or <see cref="EntityHandle.None"/>.</summary>
    EntityHandle Convert(EntityHandle target, EntityHandle swapModelFrom, float massOverride, bool asleep, bool debris);
}

/// <summary>
/// Turning scenery into something that falls over: stock's
/// <c>CPhysConvert::InputConvertTarget</c>'s per-entity body,
/// <c>CreateSimplePhysicsObject</c> and <c>TransferPhysicsObject</c>
/// (physobj.cpp), and <c>UTIL_TransferPoseParameters</c> / <c>TransferChildren</c>.
/// Native's SourceSharp_EntityConvertToPhysics answered 0 for every entity
/// (its SDK arm was always NULL); this is the stock arm, ported.
/// </summary>
public sealed class PhysicsConversion : IPhysicsConversion
{
    /// <summary>MAXSTUDIOPOSEPARAM: the pose parameters stock copies across.</summary>
    public const int MaxPoseParameters = 24;

    private readonly IPhysicsConversionWorld _world;
    private readonly IVPhysicsEntityCalls _v;
    private readonly EntityPhysicsCreation _create;

    public PhysicsConversion(IPhysicsConversionWorld world, IVPhysicsEntityCalls v, EntityPhysicsCreation create)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _v = v ?? throw new ArgumentNullException(nameof(v));
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    /// <summary>
    /// One target converted: the simple physics entity that replaces it, or
    /// <see cref="EntityHandle.None"/> when it is not converted (already
    /// physics, or nothing could be made). <paramref name="swapModelFrom"/>,
    /// when valid, lends its model to the target first.
    /// </summary>
    public EntityHandle Convert(EntityHandle target, EntityHandle swapModelFrom, float massOverride,
                                bool asleep, bool debris)
    {
        // don't convert something that is already physics based. A solid
        // prop_dynamic's static object leaves it MOVETYPE_NONE, as stock's
        // CDynamicProp is (EntityPhysicsCreation writes the movetype since
        // 2026-10-12), so it converts.
        if (_world.MoveTypeOf(target) == MoveType.VPhysics)
        {
            _world.Message($"ERROR phys_convert {_world.ClassName(target)} ! Already MOVETYPE_VPHYSICS\n");
            return EntityHandle.None;
        }

        _world.Unparent(target);

        if (swapModelFrom != EntityHandle.None)
        {
            // we can't reuse this physics object, so kill it
            _world.DestroyPhysics(target);
            _world.SetModel(target, _world.ModelName(swapModelFrom));
        }

        // created phys object, now move hierarchy over
        EntityHandle made = CreateSimplePhysicsObject(target, asleep, debris);
        if (made == EntityHandle.None)
            return EntityHandle.None;

        // Override the mass if specified
        if (massOverride > 0 && _world.PhysicsObject(made) != 0)
            _world.SetMass(made, massOverride);

        _world.SetKeyValue(made, "targetname", _world.Name(target));
        TransferPoseParameters(target, made);
        TransferChildren(target, made);
        _world.SetNotSolid(target);
        _world.SetNoDraw(target);
        _world.Remove(target);
        return made;
    }

    /// <summary>
    /// Stock's <c>CreateSimplePhysicsObject</c>: a <c>simple_physics_brush</c>
    /// for a brush model, else a <c>simple_physics_prop</c>, with the
    /// target's model and pose, spawned; the target's object moved over, or a
    /// new one made.
    /// </summary>
    private EntityHandle CreateSimplePhysicsObject(EntityHandle entity, bool createAsleep, bool createAsDebris)
    {
        string className = _world.IsBrushModel(entity) ? EntityNames.SimplePhysicsBrush : EntityNames.SimplePhysicsProp;

        EntityHandle physEntity = _world.Create(className);
        if (physEntity == EntityHandle.None)
            return EntityHandle.None;

        _world.SetKeyValue(physEntity, "model", _world.ModelName(entity));
        _world.SetOrigin(physEntity, _world.AbsOrigin(entity));
        _world.SetAngles(physEntity, _world.AbsAngles(entity));
        _world.Spawn(physEntity);

        if (!TransferPhysicsObject(entity, physEntity, !createAsleep))
        {
            // Stock's VPhysicsInitNormal( SOLID_VPHYSICS, 0, createAsleep ). The
            // simple classes have usually made their object in their own spawn
            // already (awake); asleep is then the one choice left to make.
            nint held = _world.PhysicsObject(physEntity);
            if (held == 0)
                _create.Create(physEntity, isStatic: false, createAsleep, debris: false);
            else if (createAsleep)
                _v.Sleep(held);

            if (createAsDebris)
                _world.SetCollisionGroup(physEntity, CollisionGroup.Debris);
        }

        return physEntity;
    }

    /// <summary>
    /// Stock's <c>TransferPhysicsObject</c>: the target's moveable object
    /// handed to the new entity, keeping its velocity, constraints and
    /// contacts. False (nothing moved) for no object or a static one.
    /// </summary>
    public bool TransferPhysicsObject(EntityHandle from, EntityHandle to, bool wakeUp)
    {
        nint vphysics = _world.PhysicsObject(from);
        if (vphysics == 0 || _v.IsStatic(vphysics))
            return false;

        // The new entity's spawn may have made its own: stock's had none to
        // replace, so it goes first rather than being orphaned by the swap.
        _world.DestroyPhysics(to);

        // clear out the pointer so it won't get deleted
        _world.Swap(from, 0, out _);
        // remove any AI behavior bound to it
        _v.RemoveShadowController(vphysics);
        // transfer to the new owner
        if (!_world.Swap(to, vphysics, out _))
        {
            _world.Swap(from, vphysics, out _);
            return false;
        }

        // The simple classes are MOVETYPE_VPHYSICS from their own Spawn in
        // stock; here the word follows the object, which the new entity now holds.
        _world.SetMoveType(to, MoveType.VPhysics);

        _v.SetGameData(vphysics, PhysicsGameData.FromHandle(to));
        // (Stock's pTo->VPhysicsUpdate( pVPhysics ) is not repeated here: the new
        // entity was spawned at the target's own pose, which is where the
        // object is, and the shell copies an awake object's pose every frame.)

        // may have been temporarily disabled by the old object
        _v.EnableMotion(vphysics, true);
        _v.EnableGravity(vphysics, true);

        // Update for the new entity solid type
        _v.RecheckCollisionFilter(vphysics);
        if (wakeUp)
            _v.Wake(vphysics);

        return true;
    }

    // UTIL_TransferPoseParameters: every pose parameter, by index.
    private void TransferPoseParameters(EntityHandle source, EntityHandle dest)
    {
        int count = Math.Min(MaxPoseParameters, _world.PoseParameterCount(dest));
        for (int pose = 0; pose < count; pose++)
            _world.SetPoseParameter(dest, pose, _world.GetPoseParameter(source, pose));
    }

    // TransferChildren (hierarchy.cpp): each child relinked to the new parent
    // where it stands, its attachment kept.
    private void TransferChildren(EntityHandle oldParent, EntityHandle newParent)
    {
        foreach (var (child, attachment) in _world.Children(oldParent))
            _world.SetParent(child, newParent, attachment);
    }
}
