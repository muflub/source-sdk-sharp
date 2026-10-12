//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Throwing one piece of debris — the port of <c>SourceSharp_ShootGib</c>,
/// which was the last consumer of <c>game/server/gib.cpp</c> on the server link
/// line. <c>plan_entity.md</c> step 4a.
///
/// <para>
/// WHAT THIS OWNS is the three-way choice and everything that has to happen
/// before the entity is spawned; what each gib then DOES is
/// <see cref="SsGib"/>'s. The split is the same one <see cref="Corpses"/> and
/// <see cref="SsRagdoll"/> have, and for the same reason: a creator reads the
/// world, and an entity reads only itself.
/// </para>
///
/// <para>
/// THE LOOP IS NOT HERE EITHER. How many gibs, how far apart and how widely
/// scattered is <see cref="EnvShooter"/>'s and always was; this is one gib.
/// </para>
/// </summary>
public sealed class Gibs
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public Gibs(ILog log, IEngineEntities entities, IEngineEntityRender render,
                IEnginePrecache precache, IEntityDirectory directory)
    {
        _log = log;
        _entities = entities;
        _render = render;
        _precache = precache;
        _directory = directory;
    }

    private readonly IEngineEntities _entities;
    private readonly IEngineEntityRender _render;
    private readonly IEnginePrecache _precache;
    private readonly IEntityDirectory _directory;

    /// <summary>
    /// Models already reported as sprites, so the warning below is said once per
    /// model rather than once per gib.
    ///
    /// <para>
    /// Six identical console lines a volley is how the engine error this
    /// replaces went unnoticed for as long as it did.
    /// </para>
    /// </summary>
    private readonly HashSet<string> _spritesSaid = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One piece of debris, thrown. Answers whether one was made.
    /// </summary>
    public bool Shoot(string model, in GibShot shot)
    {
        if (string.IsNullOrEmpty(model))
            return false;

        // PRECACHED HERE, and this is a crash fix rather than tidying, carried
        // over verbatim from the native function this replaces.
        //
        // Neither branch below precaches, and both end in a SetModel, which
        // answers a model index of -1 with Error() -- and Error() raised on a
        // CoreCLR thread, inside a reverse P/Invoke, does not end the process
        // the way it would on the game thread: it runs out of stack and the
        // managed side reports a StackOverflow three frames from anything that
        // looks wrong. The default human gibs are precached by something else
        // and so this went unnoticed; any env_shooter whose mapper spelled a
        // `shootmodel` killed the server the first time it fired.
        //
        // At the shot rather than at map parse, which is the same place the
        // physics shell precaches for the same reason.
        int index = _precache.PrecacheModel(model);

        if (index < 0)
        {
            // PrecacheModel says so on the console when the file is not there
            // and answers -1. Refusing here rather than going on is the point:
            // setting a model that will not load is the Error() this exists to
            // avoid.
            _log.Warning($"[SourceSharp] cannot shoot gibs: no model '{model}'\n");
            return false;
        }

        // A jointed body rather than a lump, and there is no gib involved at all
        // -- stock builds a CRagGib, which is its own entity and not a
        // configured CGib. Here it is the managed corpse entity, whose whole job
        // is already "a body of this model, at this place, thrown this way".
        if (shot.Simulation == (int)GibSimulation.Ragdoll)
            return ThrowRagdoll(model, shot);

        // A SPRITE `shootmodel` IS NOT A MODEL A GIB CAN WEAR, and handing one
        // to a gib is not merely useless -- it printed an engine error per gib.
        //
        // A mapper who spells a .vmt in env_shooter's `shootmodel` is asking for
        // a glowing chip rather than a chunk of meat, and stock builds that as
        // TWO entities: the gib is spawned with an empty model so it is
        // invisible, and a sprite is hung off it that is the thing you actually
        // see. Only the first half survives here.
        //
        // ASKED OF THE ENGINE rather than of the filename. Stock sniffs for
        // ".vmt" with Q_stristr under a "not pretty..." apology; what matters is
        // what the loaded model IS, which the model table knows for certain and
        // a filename only suggests.
        bool sprite = _precache.ModelTypeOf(index) == ModelType.Sprite;

        if (sprite && _spritesSaid.Add(model))
        {
            _log.Warning($"[SourceSharp] env_shooter shootmodel '{model}' is a sprite: the gib " +
                        "is thrown (invisible, as in stock) but no sprite is attached to it. " +
                        "See Gibs.Shoot.\n");
        }

        var gib = _entities.Create(EntityNames.SsGib);

        // COMPARED WITH None RATHER THAN ASKED IsValid: IsValid is a live engine
        // call that resolves the handle's serial, and this handle was made half
        // a line ago. None is what Create answers when the factory refused,
        // which is the only failure there is here.
        if (gib == EntityHandle.None)
        {
            _log.Warning($"[SourceSharp] could not create a gib for '{model}'\n");
            return false;
        }

        // Model, place and facing BEFORE the spawn: spawning is what reads what
        // was set beforehand, which is the whole reason creation and spawning
        // are two calls. The shell builds the collision model from the model set
        // here, and SsGib.Throw's InitPhysics needs it to exist.
        _render.SetModel(gib, model);
        _entities.SetOrigin(gib, shot.Origin);
        _entities.SetAngles(gib, shot.Angles);

        _entities.Spawn(gib);

        // AND EVERYTHING ELSE AFTER IT, because spawning is what binds the
        // managed SsGib instance -- before this call there is no instance to
        // hand the shot to.
        if (_directory.Get(gib) is not SsGib thrown)
        {
            _log.Warning($"[SourceSharp] gib '{model}' spawned with no managed instance behind it\n");
            return false;
        }

        thrown.Throw(shot, sprite);
        return true;
    }

    /// <summary>
    /// GIB_SIMULATE_RAGDOLL: a whole jointed body, built on each client from the
    /// model and one force.
    ///
    /// <para>
    /// The port of <c>CreateRagGib</c> plus <c>CRagGib::Spawn</c>, which between
    /// them were SetModel followed by <c>BecomeRagdollOnClient( vecForce )</c>.
    /// </para>
    /// </summary>
    private bool ThrowRagdoll(string model, in GibShot shot)
    {
        var body = _entities.Create(EntityNames.SsRagdoll);

        if (body == EntityHandle.None)
        {
            _log.Warning($"[SourceSharp] could not create a ragdoll gib for '{model}'\n");
            return false;
        }

        _render.SetModel(body, model);
        _entities.SetOrigin(body, shot.Origin);
        _entities.SetAngles(body, shot.Angles);

        _entities.Spawn(body);

        if (_directory.Get(body) is SsRagdoll ragdoll)
        {
            // THE 200 IS STOCK'S ASSUMED MASS, commented UNDONE there. Kept,
            // because a map's velocity numbers were tuned against it.
            ragdoll.State.Force = shot.Direction * shot.Speed * SsGib.AssumedGibMass;

            // CENTRE OF MASS, because a thrown gib has no struck bone: the shot
            // carries a direction and nothing that names a part of the body.
            // Naming one here would be inventing it -- the same reason
            // Corpses.Make gives.
            ragdoll.State.ForceBone = -1;
        }

        return true;
    }
}
