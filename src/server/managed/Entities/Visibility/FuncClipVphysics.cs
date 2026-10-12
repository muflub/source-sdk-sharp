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
/// func_clip_vphysics: an invisible brush that stops physics objects and
/// nothing else.
///
/// <para>
/// A player walks straight through it, a bullet goes through it, a trace does
/// not see it — and a crate rolling towards it stops dead. That is the whole
/// entity, and it is a real one rather than a curiosity: dm_lockdown places
/// THREE, and they are the last stock C++ classname in either shipping map
/// that C# did not answer.
/// </para>
///
/// <para>
/// HOW IT DOES IT, and this is the part worth reading, because nothing in the
/// three lines below looks like clipping.
/// </para>
///
/// <para>
/// Stock spells the brush as SOLID_VPHYSICS with FSOLID_NOT_SOLID on top, and
/// then has to UNDO the not-solid rule for the one case it wants:
/// <c>CCollisionEvent::ShouldCollide</c> rejects any pair where either side is
/// FSOLID_NOT_SOLID, so <c>CFuncVPhysicsClip</c> overrides
/// <c>ForceVPhysicsCollide</c> and <c>PhysicsOwner_t::ForcesCollisionWith</c>
/// asks it before that rule is reached. That override is the entity: the class
/// exists to answer one virtual.
/// </para>
///
/// <para>
/// On this hierarchy it is the same shape (physics stock parity A5/A6). The
/// solver sees the brush's real FSOLID_NOT_SOLID, as stock's does, and the
/// brush marks its own object with SourceSharp's force-collide-moveable game
/// flag. The collision rule table answers that flag with
/// CFuncVPhysicsClip::EntityPassesFilter's no-filter rule: collide with a
/// moveable MOVETYPE_VPHYSICS object, ahead of the solid rules. A crate stops;
/// a player's shadow (MOVETYPE_WALK) walks through. It used to work only
/// because PhysicsOwner_t answered a neutral 0 for every managed entity's
/// solid flags, which also hid every other entity's NOT_SOLID from the solver.
/// </para>
///
/// <para>
/// THE ORDER OF THE THREE CALLS IS LOAD-BEARING.
/// <c>CStandaloneEntity::InitPhysics</c> ends with
/// <c>SetSolidity( SOLID_VPHYSICS, m_nSolidFlags &amp; ~FSOLID_NOT_SOLID )</c> —
/// it CLEARS not-solid, because everything else that asks for a physics object
/// wants a real wall. So the flag has to go back on AFTER the object is made.
/// Reversed, this is an invisible wall a player cannot walk through, in a map
/// where three of them stand in the open. <c>FuncClipVphysicsTests</c> asserts
/// the order rather than the comment.
/// </para>
///
/// <para>
/// WHAT IS NOT HERE, and both are stated rather than omitted — see
/// docs/divergences.md:
/// </para>
///
/// <para>
/// The <c>filtername</c> key. Stock consults a filter entity before deciding to
/// clip, which lets a mapper build a brush that stops crates and passes barrels.
/// Neither shipping map uses it — dm_lockdown's three brushes carry no keys at
/// all beyond their model — and honouring it would mean asking a managed filter
/// per collision PAIR, from inside the solver's ShouldCollide, which is the one
/// place in this build where a managed transition per call would be felt.
/// </para>
///
/// <para>
/// The Enable and Disable inputs. Stock toggles the solver object's own
/// <c>EnableCollisions</c>, and nothing on this hierarchy can reach that yet.
/// It is unreachable in both shipping maps for a structural reason rather than
/// by luck: <c>m_bDisabled</c> is a DEFINE_FIELD and not a DEFINE_KEYFIELD, so
/// stock has no "start disabled" key either, and an input needs a targetname —
/// which none of dm_lockdown's three brushes has. Same shape as
/// <see cref="FuncVehicleClip"/>'s stated half.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncClipVphysics, ScriptedEntityKind.Brush)]
public sealed class FuncClipVphysics : ScriptedEntity
{
    public FuncClipVphysics(IEngineEntityPhysics physics)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>ss::kPhysFlagForceCollideMoveable (ss_physics_shared.h).</summary>
    private const uint ForceCollideMoveable = 0x1000;

    public override void OnSpawn()
    {
        // The shape first: the compiled brush, presented to the world. Not
        // drawn — stock is EF_NODRAW and a mapper never sees one either.
        SetSolidity(BrushSolidity.Solid);
        SetVisible(false);

        // Into the solver as a static object, which is CFuncVPhysicsClip::
        // CreateVPhysics. This is the half that does the clipping.
        Physics.SetPhysicsStatic(Handle, true);

        // CFuncVPhysicsClip::ForceVPhysicsCollide with no filter: the object
        // forces collision with any moveable MOVETYPE_VPHYSICS object, ahead of
        // the solid rules that its FSOLID_NOT_SOLID would otherwise lose to.
        // A player's shadow is MOVETYPE_WALK and walks through, as in stock.
        Physics.AddGameFlags(Handle, ForceCollideMoveable);

        // ...and back out of the trace world. AFTER the object, never before:
        // InitPhysics clears this flag on its way out. See the class comment.
        Physics.SetNotSolid(Handle, true);
    }
}
