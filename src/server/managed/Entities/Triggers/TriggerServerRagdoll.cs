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
/// A volume that changes how whatever is inside it dies: as a ragdoll the
/// SERVER simulates, rather than one each client makes for itself.
///
/// A mapper draws one over the place where a body has to stay put and be
/// collided with — a pit, a conveyor, the thing a puzzle drops onto a button.
/// A client-side ragdoll is cheap and nobody else can see or touch it, which is
/// wrong everywhere that the corpse is part of the level.
///
/// <para>
/// The flag it sets does not live here and never did. It is
/// <c>m_bForceServerRagdoll</c> on CBaseCombatCharacter, and the only thing that
/// reads it is that class's own BecomeRagdoll. So the volume's whole job is to
/// turn it on for what walks in and off again for what walks out, and it reaches
/// it through <see cref="IEngineDamage.SetForceServerRagdoll"/> — an ABI call
/// that takes any entity and asks MyCombatCharacterPointer() on the far side,
/// because that is a question managed code cannot put.
/// </para>
///
/// <para>
/// WORTH KNOWING, and recorded in docs/divergences.md rather than papered over:
/// nothing in this build can currently answer that question. ai_basenpc.cpp left
/// the build, so CAI_BaseNPC does not exist; the player is the only
/// CBaseCombatCharacter left and this volume has always excluded players on
/// purpose. Every prop, crate and cannister that can walk into it lives on the
/// standalone hierarchy, which is not a CBaseEntity at all. The port is faithful
/// and the call is real — there is simply nothing here yet for it to land on.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.TriggerServerragdoll, ScriptedEntityKind.Brush)]
public sealed class TriggerServerRagdoll : TriggerVolume
{
    public TriggerServerRagdoll(IGameClock clock, IEngineDamage damage)
        : base(clock)
    {
        Damage = damage;
    }

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;
    /// <summary>
    /// The one thing it exists for is what is NOT a player, so a volume that
    /// only heard about players would be inert by construction.
    /// </summary>
    public override bool TouchedByAnything => true;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Stock is a bare CBaseTrigger and has no wait at all: StartTouch acts on
        // every arrival. The base class's fifth of a second would quietly drop
        // the second body of any pair that fell in together, which is exactly the
        // case a mapper draws one of these for.
        Vars.Wait = 0f;
    }

    protected override void Fire(EntityHandle toucher) => Mark(toucher, true);

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);
        Mark(toucher, false);
    }

    // Players are skipped on the way out as well as the way in, which stock also
    // does. It matters: a player's flag is set for a different reason —
    // ai_force_serverside_ragdoll, read once in the constructor — and a volume
    // clearing it on exit would be silently overriding a convar it was never
    // asked about.
    // No longer static: it asks the engine, and the engine now arrives on the
    // instance. Both callers were instance methods already.
    private void Mark(EntityHandle toucher, bool force)
    {
        if (Entities.IsPlayer(toucher))
            return;

        Damage.SetForceServerRagdoll(toucher, force);
    }
}
