//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// CBaseVPhysicsTrigger (triggers.cpp): a volume the solver reports objects
/// entering, rather than one the engine's trigger lists pair. SOLID_VPHYSICS
/// and passable, its model a static object made a trigger (a shadow when
/// SF_VPHYSICS_MOTION_MOVEABLE asks it to follow its parent), and switched on
/// and off by its object's collisions.
///
/// <para>
/// Nothing on this mod's hierarchy has an edict, so the engine never pairs a
/// crate with a brush trigger: a volume that acts on physics objects has to be
/// one of these to hear about them at all.
/// </para>
/// </summary>
public abstract class VPhysicsTriggerVolume : TriggerVolume
{
    public VPhysicsTriggerVolume(IGameClock clock, IEngineNetworking net)
        : base(clock)
    {
        Net = net;
    }

    /// <summary>putting this entity's state on the wire, and reading a toucher's.</summary>
    protected readonly IEngineNetworking Net;

    /// <summary>SF_VPHYSICS_MOTION_MOVEABLE: a shadow that follows the volume, not a static object.</summary>
    public const int SpawnFlagMoveable = 0x1000;

    protected override SolidType BrushSolidType => SolidType.VPhysics;

    // A crate is what these are written for: the registry's players-only
    // default would drop it before the class saw it.
    public override bool TouchedByAnything => true;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // NOTE: Don't make yourself FSOLID_TRIGGER here or you'll get game
        // collisions AND vphysics collisions.
        if ((SpawnFlags & SpawnFlagMoveable) == 0)
            MakePhysicsStatic();
        else
            InitShadowPhysics();

        SetSolidity(BrushSolidity.None);
        BecomePhysicsTrigger(true);
    }

    /// <summary>InputEnable and InputDisable: the object's collisions follow the volume.</summary>
    protected override void OnDisabledChanged() => EnablePhysicsCollisions(!Disabled);

    /// <summary>PassesTriggerFilters: physics objects and players, then the spawnflags and the filter.</summary>
    protected override bool Passes(EntityHandle toucher)
    {
        var state = Net.EntityGetState(toucher, out MoveType moveType);

        if (moveType != MoveType.VPhysics && (state & EntityState.Player) == 0)
            return false;

        return base.Passes(toucher);
    }
}
