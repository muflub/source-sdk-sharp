//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A crate a walking player shoves out of the way.
///
/// func_breakable's keys with a physics object underneath, and the mass is the
/// whole entity: stock pins it at thirty however big the mapper drew the brush,
/// because what makes this a pushable rather than a wall is that a player wins
/// against it. So the shoving needs no code here at all — vphysics already
/// resolves a player against a light object — and the decision left is whether
/// the thing can be hurt.
///
/// It cannot, unless the map asks. Spawnflag 128 is the mapper wanting
/// func_breakable's damage rules back; without it a pushable is furniture, and
/// shooting furniture should move it rather than destroy it.
/// </summary>
/// <summary>
/// CPushable (func_break.cpp): a brush the player shoves about, simulated
/// with stock's mass of 30 whatever its size. With SF_PUSH_BREAKABLE it is a
/// whole <see cref="BreakableBrush"/> and takes damage; without, it only
/// precaches and takes none.
/// </summary>
[ScriptedEntity(EntityNames.FuncPushable, ScriptedEntityKind.Brush)]
public sealed class FuncPushable(IServerPropData propData, IEngineSound sound, IEnginePrecache precache, ITempEnts temps,
                                 IEngineHost host, IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                                 IBlastSweep blast, IServerDecals decals, IEngineEntityPhysics physics,
                                 ConVarRegistry? convars = null)
    : BreakableBrush(propData, sound, precache, temps, host, damage, world, effects, blast, decals, physics, convars)
{
    /// <summary>
    /// Stock's SF_PUSH_BREAKABLE. High, because a pushable shares
    /// func_breakable's spawnflags and this one starts above them.
    /// </summary>
    private const int BreakableFlag = 0x0080;

    /// <summary>What stock gives every pushable, whatever its size (CPushable::CreateVPhysics).</summary>
    private const float PushableMass = 30f;

    /// <summary>Whether damage does anything to this.</summary>
    public static bool IsBreakable(int spawnFlags) => (spawnFlags & BreakableFlag) != 0;

    protected override bool DamageReaches => IsBreakable(SpawnFlags);

    protected override bool PushedByDamage => true;

    /// <summary>CPushable::Spawn: a breakable one is a CBreakable's spawn; any other only precaches.</summary>
    public override void OnSpawn()
    {
        if (IsBreakable(SpawnFlags))
        {
            base.OnSpawn();
            return;
        }

        PrecacheSounds();
        SetSolidity(BrushSolidity.Solid);
        CreatePhysics();
        SetVisible(true);
    }

    /// <summary>CPushable::CreateVPhysics: simulated, at a mass of 30.</summary>
    protected override void CreatePhysics()
    {
        // A brush has its model by now, so the physics object can be made here.
        // See FuncPhysbox for why a prop cannot.
        if (!Physics.InitPhysics(Handle))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be simulated\n");
            return;
        }

        Mass = PushableMass;
    }
}
