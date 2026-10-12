//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>What an entity's physics init reads of the entity: its engine block's fields and its classname.</summary>
public readonly record struct EntityPhysicsFacts(EntityHandle Handle, int CollisionModelIndex, Vector Origin,
                                                 QAngle Angles, SolidFlags SolidFlags, string ClassName);

/// <summary>
/// What an entity's physics init asks of the entity's shell and its managed
/// class (plan_physics_bones_native_zero, InitPhysics to managed). Reads and
/// carries out only: the decisions are <see cref="EntityPhysicsCreation"/>'s.
/// </summary>
public interface IEntityPhysicsHost
{
    /// <summary>The entity's facts; false when the handle names no shell.</summary>
    bool TryFacts(EntityHandle entity, out EntityPhysicsFacts facts);

    /// <summary>The entity's model name, for a refusal that names it.</summary>
    string ModelName(EntityHandle entity);

    /// <summary>The object the shell holds, or 0.</summary>
    nint PhysicsObject(EntityHandle entity);

    /// <summary>The shell's <c>SetSolidity</c>: the block's solid type and flags, and the filter re-asked for an object it holds.</summary>
    void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags);

    /// <summary>
    /// Stock's <c>VPhysicsSwapObject</c>: the shell holds <paramref name="physicsObject"/>
    /// (0 for none) and answers the object it held before, which it does not
    /// destroy. False when the handle names no shell.
    /// </summary>
    bool Swap(EntityHandle entity, nint physicsObject, out nint previous);

    /// <summary>The shell's <c>DestroyPhysics</c>: the held object's owner cleared, the object destroyed, the hold emptied.</summary>
    void DestroyPhysics(EntityHandle entity);

    /// <summary>
    /// The entity's movetype word (the engine block's <c>MoveType</c>), which
    /// native reports for a non-player holding an object (standalone_movetype.h).
    /// </summary>
    void SetMoveType(EntityHandle entity, MoveType moveType);

    /// <summary>The managed class is told its object exists and has not been simulated (EntityOp.PhysicsCreated).</summary>
    void Created(EntityHandle entity);

    /// <summary><c>sourcesharp_solidity_before_physics</c>: stock's order (true) or the port's old one.</summary>
    bool SolidityBeforePhysics { get; }

    /// <summary>Where a refusal is said (native's Warning).</summary>
    void Warning(string text);
}

/// <summary>
/// Makes an entity physically simulated: the port of native's
/// <c>CStandaloneEntity::InitPhysics</c> (shell_standalone_base.cpp at
/// 4602cf071), which is stock's <c>CBaseEntity::VPhysicsInitNormal</c> /
/// <c>VPhysicsInitStatic</c> over <c>PhysModelCreate</c> /
/// <c>PhysModelCreateUnmoveable</c> (baseentity_shared.cpp, physics_shared.cpp):
/// which solid of the model, which surface, which params, static or
/// moveable, asleep or awake, debris or not, and when the solidity is set.
///
/// <para>
/// Native keeps only the pointer the shell holds (<see cref="IEntityPhysicsHost.Swap"/>),
/// which the engine and the shell read every frame.
/// </para>
/// </summary>
public sealed class EntityPhysicsCreation : IEntityPhysicsRoute
{
    /// <summary>FVPHYSICS_NO_PLAYER_PICKUP: what native's debris arm set (ss::kPhysFlagNoPlayerPickup).</summary>
    public const ushort NoPlayerPickup = 0x0080;

    private readonly IPhysicsWorldVPhysics _world;
    private readonly IVPhysicsEntityCalls _v;
    private readonly IEntityPhysicsHost _host;

    public EntityPhysicsCreation(IPhysicsWorldVPhysics world, IVPhysicsEntityCalls v, IEntityPhysicsHost host)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _v = v ?? throw new ArgumentNullException(nameof(v));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Objects created since the module started.</summary>
    public int Created { get; private set; }

    /// <summary>
    /// The entity simulated, or (<paramref name="isStatic"/>) solid and never
    /// simulated. True when it has an object afterwards, including when it
    /// already had one: a second object would orphan the first, and the
    /// solver would step it forever.
    /// </summary>
    public bool Create(EntityHandle entity, bool isStatic, bool asleep, bool debris)
    {
        if (_host.PhysicsObject(entity) != 0)
            return true;

        if (!_host.TryFacts(entity, out EntityPhysicsFacts f))
            return false;

        nint environment = _world.Environment();
        if (environment == 0 || f.CollisionModelIndex <= 0)
        {
            _host.Warning($"[SourceSharp] cannot simulate '{f.ClassName}': it has no model yet\n");
            return false;
        }

        // A model with no compiled collision data: common, and not an error.
        VCollideView collide = _world.Collide(f.CollisionModelIndex);
        if (collide.Pointer == 0 || collide.SolidCount == 0)
        {
            _host.Warning($"[SourceSharp] cannot simulate '{f.ClassName}': model '{_host.ModelName(entity)}' has no collision data\n");
            return false;
        }

        // The solid block the model was compiled with: stock's
        // PhysModelParseSolidByIndex( solid, pEntity, modelIndex, -1 ).
        nint scratch = _v.ScratchSolid;
        if (!ModelSolidParse.Parse(_world, f.CollisionModelIndex, -1, scratch))
            return false;

        EntitySolid solid = _v.ReadSolid(scratch);
        int surface = solid.SurfaceProp.Length > 0 ? _world.SurfaceIndex(solid.SurfaceProp) : -1;

        // An entity with no handle stores no owner, and every callback about
        // its object finds nobody: said, not assumed.
        if (f.Handle == EntityHandle.None)
        {
            _host.Warning($"[SourceSharp] '{f.ClassName}' is being simulated before it has a handle: " +
                          "its physics object will have no owner\n");
        }

        // GAME DATA IS THE HANDLE (physics_gamedata.h), and the name a string
        // that outlives the object (stock's pooled classname, never the
        // entity's own buffer).
        ObjectParams p = solid.Params;
        p.GameData = PhysicsGameData.FromHandle(f.Handle);
        p.Name = _world.NativeText(f.ClassName);

        // Stock's order: the solidity BEFORE the create, because the create
        // calls back into ShouldCollide, which reads it. The flags are read
        // once, so both arms write the same value.
        SolidFlags flags = f.SolidFlags & ~SolidFlags.NotSolid;
        bool solidityFirst = _host.SolidityBeforePhysics;
        if (solidityFirst)
            _host.SetSolidity(entity, SolidType.VPhysics, flags);

        nint made = _v.CreatePolyObject(environment, _world.CollideSolid(collide, solid.Index), surface,
                                        f.Origin, f.Angles, in p, isStatic);
        if (made == 0)
            return false;

        if (!_host.Swap(entity, made, out nint previous))
        {
            _v.DestroyObject(environment, made);
            return false;
        }

        // Nothing held one a moment ago; an object that appeared in between
        // would be orphaned by the swap, so it is destroyed rather than lost.
        if (previous != 0)
            _v.DestroyObject(environment, previous);

        // Debris passes through the player and other debris.
        if (debris)
            _v.SetGameFlags(made, (ushort)(_v.GameFlags(made) | NoPlayerPickup));

        if (asleep)
            _v.Sleep(made);
        else
            _v.Wake(made);

        if (!solidityFirst)
            _host.SetSolidity(entity, SolidType.VPhysics, flags);

        // Stock's movetypes: VPhysicsInitNormal sets MOVETYPE_VPHYSICS, while
        // VPhysicsInitStatic leaves the entity's own (a prop_dynamic's NONE).
        // Native reports this word for an entity holding an object, so a solid
        // prop_dynamic no longer reads as simulated.
        _host.SetMoveType(entity, isStatic ? MoveType.None : MoveType.VPhysics);

        Created++;

        // The object exists and the solver has not run it: the managed class
        // makes stock's creation-time choices now.
        _host.Created(entity);
        return true;
    }

    /// <summary>
    /// Static means "solid and never simulated", which is a different KIND of
    /// object: the held one is destroyed and the entity made again as asked.
    /// </summary>
    public bool SetStatic(EntityHandle entity, bool isStatic)
    {
        _host.DestroyPhysics(entity);
        return Create(entity, isStatic, asleep: false, debris: false);
    }

    /// <summary>
    /// After the managed spawn: a class whose kind the solver moves
    /// (<see cref="ScriptedEntityKind.Prop"/>, SOLID_VPHYSICS) gets an object,
    /// unless its spawn already made one or it has no model. Answers whether one
    /// was asked for.
    /// </summary>
    public bool Spawned(EntityHandle entity, ScriptedEntityKind kind)
    {
        if (kind != ScriptedEntityKind.Prop || _host.PhysicsObject(entity) != 0)
            return false;

        if (!_host.TryFacts(entity, out EntityPhysicsFacts f) || f.CollisionModelIndex <= 0)
            return false;

        Create(entity, isStatic: false, asleep: false, debris: false);
        return true;
    }
}
