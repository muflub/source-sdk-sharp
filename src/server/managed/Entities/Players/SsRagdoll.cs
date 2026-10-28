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
/// The body a player leaves behind — stock's <c>hl2mp_ragdoll</c>, as a managed
/// entity.
///
/// <para>
/// IT IS ITS OWN ENTITY AND NOT THE DEAD PLAYER, which is the one structural
/// decision in this file. Respawning reuses the player's handle —
/// <c>Players.Think</c> calls <c>Respawn</c> on the same entity — so a player
/// turned limp in place would stand up and run off as their own corpse three
/// seconds later. Stock draws the same line for the same reason:
/// <c>CHL2MP_Player::CreateRagdollEntity</c> spawns a separate
/// <c>hl2mp_ragdoll</c> and the player entity is never the thing that goes
/// slack.
/// </para>
///
/// <para>
/// <see cref="ScriptedEntityKind.Item"/> because a corpse is a model in the
/// world, and Item is the kind that has one and networks it — the model index,
/// the origin and the angles the client's solve starts from all ride the
/// standalone shell for free. What Item also brings is collision, which a
/// corpse must not have (see <see cref="OnSpawn"/>); that is turned off rather
/// than worked around, because the alternative kinds are worse fits: an
/// <see cref="ScriptedEntityKind.Effect"/> networks no model at all, and a
/// <see cref="ScriptedEntityKind.Prop"/> would put a second, server-side
/// physics simulation underneath a body the client is already simulating.
/// </para>
///
/// <para>
/// NOTHING HERE MAKES A RAGDOLL. The server's whole job is to say "a body of
/// this model, at this place, thrown this way" and let each client build one
/// locally; <c>ClientSsRagdoll</c> is where a ragdoll actually appears. That
/// split is not an artefact of this port — it is what
/// <c>BecomeRagdollOnClient</c> means, and it is why a corpse costs the wire a
/// model index and two vectors rather than twenty-four bone transforms a tick.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.Hl2mpRagdoll, ScriptedEntityKind.Item)]
public sealed class SsRagdoll : AnimatingEntity
{
    /// <summary>
    /// How long a body lasts before it is removed, in seconds.
    ///
    /// <para>
    /// FINITE BECAUSE NOTHING ELSE WILL CLEAN THESE UP. Stock leans on
    /// <c>g_RagdollLVManager</c> and a round restart to bound how many corpses
    /// exist; this mod has neither, so a deathmatch with no limit accumulates
    /// one edict and one client-side ragdoll solve per death for the length of
    /// the map. The number is a pacing decision rather than an engine
    /// constraint.
    /// </para>
    ///
    /// <para>
    /// A CONSTANT. It was a settable static property "so it could be tuned",
    /// and nothing ever set it; a mutable static is process-wide state every
    /// test shares, and the owner's rule (2026-09-29) is that managed state
    /// comes from the container. If it ever needs tuning at runtime, the place
    /// for that is a convar the ragdoll takes through its constructor.
    /// </para>
    /// </summary>
    public const float Lifetime = 15f;

    /// <summary>
    /// Solidity is not <c>Entities</c>' area, so it arrives by constructor —
    /// the base class's rule for every area whose subject is not always this
    /// entity.
    /// </summary>
    public SsRagdoll(IEngineEntityPhysics physics) => _physics = physics;

    private readonly IEngineEntityPhysics _physics;

    /// <summary>The impulse and the struck bone. See <see cref="RagdollNetState"/>.</summary>
    [NetState] public RagdollNetState State = new();

    /// <summary>
    /// What the spawn armed the removal for, or -1 if nothing did.
    ///
    /// <para>
    /// Exposed for tests for the reason <c>PropPhysics.ThinkArmed</c> gives:
    /// <c>SetNextThink</c> is a <see cref="ScriptedEntity"/> convenience that
    /// goes through the STATIC engine, so a test's double never sees it — and
    /// a body that never armed its removal looks exactly like one that did.
    /// </para>
    /// <para>
    /// <c>[SaveField]</c>: the value is written ONCE in <see cref="OnSpawn"/>,
    /// which a hot reload does not run again, and the removal it records is
    /// booked natively -- a rebuilt body that forgot it would read as one that
    /// never armed its removal (OnSpawnFieldReloadTests).
    /// </para>
    /// </summary>
    [SaveField] internal float ThinkArmed { get; private set; } = -1f;

    /// <summary>
    /// Whether this body has taken itself out of the world. Exposed for the
    /// same reason <see cref="ThinkArmed"/> is — <c>Remove</c> goes through the
    /// static engine too, and <c>PropPhysics.Gone</c> exists for this exact
    /// wall.
    /// </summary>
    internal bool Gone { get; private set; }

    public override void OnSpawn()
    {
        // NOT SOLID, and this is the line that keeps a corpse from being a
        // wall. An Item is solid so it can be walked into and collected, which
        // is exactly wrong here: a body left in a doorway would block it, and
        // a player who died on a stairway would leave an invisible step. The
        // client's ragdoll does its own collision against the world.
        _physics.SetNotSolid(Handle, true);

        ThinkArmed = Lifetime;
        SetNextThink(Lifetime);
    }

    /// <summary>
    /// The body's time is up.
    ///
    /// <para>
    /// Removing the entity is the whole of it: the client half's
    /// <c>OnRemoved</c> is what tears the ragdoll down on each viewer, so
    /// there is no second message to send and no per-client bookkeeping here.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        if (Kept)
            return;

        Gone = true;
        Remove();
    }

    /// <summary>
    /// Whether this body stays until the level ends (<see cref="Corpses.Keep"/>).
    /// The lifetime's think still fires and does nothing. <c>[SaveField]</c>:
    /// the think is booked natively and survives a hot reload, so a rebuilt
    /// body that forgot it was kept would remove itself.
    /// </summary>
    [SaveField] internal bool Kept;
}
