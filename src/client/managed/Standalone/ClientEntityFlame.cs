//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of ss_entityflame: one particle system, started where the
/// flame is.
///
/// <para>
/// Stock's <c>C_EntityFlame</c> had four jobs and this has one of them,
/// because three of the four were about a handle this side no longer needs.
/// <c>CreateEffect</c> bound two control points to <c>m_hEntAttached</c>;
/// <c>StopEffect</c> cleared that entity's <c>FL_ONFIRE</c> and stopped its
/// two burning sounds; <c>ClientThink</c> released the client-only copy that
/// <c>FireEffect</c> made for a ragdoll. The first is answered by the flame
/// being PARENTED to what it burns — its origin is already the burning
/// entity's — and the other two are the server's, where the sounds are now
/// emitted from.
/// </para>
///
/// <para>
/// What remains of <c>Simulate</c> is the episodic dynamic light, which is
/// <c>#ifdef HL2_EPISODIC</c> and gated on <c>EF_BRIGHTLIGHT</c>/<c>EF_DIMLIGHT</c>
/// — effect bits that only <c>FireEffect</c> ever set, on the client-only flame
/// that has left the build. It is not carried across: there is no path in this
/// mod that could turn it on.
/// </para>
///
/// <para>
/// THE CONTROL POINT IS THE WHOLE OF THE CARE HERE. <c>burning_character</c>
/// reads control point 1 as well as 0 — its "Position on Model Random"
/// initializer names it, and four of its five child systems do too — and an
/// unfed control point is <c>(0, 0, 0)</c>. The engine's fallback for that
/// operator, when the point has no model bound (which is always, on this
/// hierarchy), is to emit every particle AT the control point. So a flame that
/// asked only for point 0 would burn at the centre of the map. See
/// <see cref="IEngineEffects.ParticleSystemFollowPoints"/>.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsEntityFlame)]
public sealed class ClientEntityFlame : ClientStandaloneEntity
{
    public ClientEntityFlame(IEngineEntityRender render, IEngineEffects effects)
        : base(render)
    {
        Effects = effects;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    public override string ClassName => EntityNames.SsEntityFlame;

    /// <summary>
    /// Which control points this flame's system is kept at. Bit 0 and bit 1,
    /// which is stock's <c>SetControlPointEntity( 0, pEntity )</c> and
    /// <c>( 1, pEntity )</c> — one entity, named twice, because the second
    /// point says WHAT is burning rather than where a second thing is.
    /// </summary>
    public const int FollowPoints = 0b11;

    /// <summary>Whether the system is running, so a PVS re-entry does not start a second.</summary>
    private bool _burning;

    /// <summary>
    /// Lights the flame.
    ///
    /// <para>
    /// On spawn rather than on a data change, because there is no data: this
    /// class networks nothing, so <c>OnDataChanged</c> would be called with an
    /// empty payload and would have nothing to read. An entity re-entering the
    /// PVS is spawned again, which is why the running flag exists at all —
    /// stock could not be reached twice because its only caller was
    /// <c>DATA_UPDATE_CREATED</c>.
    /// </para>
    /// </summary>
    public override void OnSpawn()
    {
        if (_burning)
            return;

        if (!Effects.ParticleSystemStart(Handle, FlameRules.ParticleSystem))
        {
            // A name no .pcf defines. Said once per flame, and worth saying:
            // the difference between "the fire did not start" and "the fire is
            // drawing somewhere you are not looking" is otherwise invisible.
            Log.Warning($"[SourceSharp] ss_entityflame: no particle system named '{FlameRules.ParticleSystem}'\n");
            _burning = true;
            return;
        }

        // AND THE SECOND CONTROL POINT, which is not optional — see the class
        // remark. Refused only if the system above did not start, which the
        // branch over this has already handled.
        bool fed = Effects.ParticleSystemFollowPoints(Handle, FollowPoints);

        // Said once per flame, and worth the line for the same reason
        // ClientFiresmoke says its own: whether this side started the system at
        // all is otherwise unobservable from outside, and "no warning" is not
        // the same evidence as "this one". The MASK is in it because a flame
        // drawing at the world origin and a flame drawing at what is burning
        // are the same log line without it.
        Log.Msg($"[SourceSharp/Client] ss_entityflame: {FlameRules.ParticleSystem}, "
              + $"control points 0x{FollowPoints:X} {(fed ? "fed" : "REFUSED")}\n");

        _burning = true;
    }

    /// <summary>
    /// The flame goes out with its entity.
    ///
    /// The particles already in the air finish rather than blinking away, which
    /// is the last second of a fire burning down and is what stock's
    /// <c>~C_EntityFlame</c> asks for through <c>StopEmission</c>.
    /// </summary>
    public override void OnRemoved()
    {
        Effects.ParticleSystemStop(Handle);
        _burning = false;
    }
}
