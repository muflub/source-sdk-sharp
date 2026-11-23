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
    /// Solidity is not <c>Entities</c>' area, so it arrives by constructor —
    /// the base class's rule for every area whose subject is not always this
    /// entity.
    /// </summary>
    public SsRagdoll(IEngineEntityPhysics physics) => _physics = physics;

    private readonly IEngineEntityPhysics _physics;

    /// <summary>The impulse and the struck bone. See <see cref="RagdollNetState"/>.</summary>
    [NetState] public RagdollNetState State = new();

    /// <summary>
    /// NOT SOLID, and this is the line that keeps a corpse from being a
    /// wall. An Item is solid so it can be walked into and collected, which
    /// is exactly wrong here: a body left in a doorway would block it, and
    /// a player who died on a stairway would leave an invisible step. The
    /// client's ragdoll does its own collision against the world.
    /// </summary>
    public override void OnSpawn() => _physics.SetNotSolid(Handle, true);

    /// <summary>
    /// THE DISSOLVE'S END, or a left body's lifetime (<see cref="Expire"/>).
    /// A player's body books only its dissolve's (<see cref="Dissolve"/>): stock's server
    /// ragdoll has no lifetime of its own — it goes when the player's next
    /// death replaces it (<c>CTFPlayer::CreateRagdollEntity</c>'s m_hRagdoll
    /// destroy, tf_player.cpp:15659-15666) or a round reset sweeps it. The
    /// visible bound is the client's fade (<c>cl_ragdoll_fade_time</c>), the
    /// client half's to carry.
    /// </summary>
    public override void OnThink() => Remove();

    /// <summary>
    /// A body with a lifetime of its own: gone <paramref name="seconds"/> from
    /// now (the same think its dissolve books). For a body no player's next
    /// death will replace (<see cref="Corpses.Leave"/>); a dissolve booked
    /// later takes the think over, and ends the body sooner.
    /// </summary>
    internal void Expire(float seconds)
    {
        if (!Dissolving && seconds > 0f)
            SetNextThink(seconds);
    }

    /// <summary>Whether this body is dissolving: stock's GetEffectEntity, for what sweeps bodies.</summary>
    internal bool Dissolving => State.DissolveStart > 0f;

    /// <summary>
    /// CBaseAnimating::Dissolve( ENTITY_DISSOLVE_NORMAL ): from now it fades on every
    /// client, and it is gone when that is done — the one think this body books.
    /// </summary>
    internal void Dissolve()
    {
        if (Dissolving)
            return;

        State.DissolveStart = GameTime;
        SetNextThink(RagdollDissolve.Seconds);
    }
}
