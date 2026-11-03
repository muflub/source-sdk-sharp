//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;

using TF2.Shared;
using TF2.Shared.Ctf;

namespace TF2.Client.Entities;

/// <summary>
/// The flag's mirror: the paper trail half of <c>CCaptureFlag</c>
/// (shared/tf/entity_capture_flag.cpp, the client's <c>ManageTrailEffects</c>,
/// :2591-2645). While the wire says the flag is stolen and the map keyed an
/// effect switch that includes the paper, the particle system runs at
/// <c>PATTACH_ABSORIGIN_FOLLOW</c> — here the native shell feeds the mirror's
/// origin to the running system's control point 0, as every projectile trail
/// does — and stops when the flag is not stolen (or the switch excludes it).
///
/// <para>
/// Stock's gate on the carrier's own speed (<c>GetAbsVelocity().Length() >=
/// MaxSpeed() * 0.2f</c> per frame, :2611) is not ported: the mirror has no
/// carrier handle, only the flag's wire, and the paper running for the whole
/// carry differs only when the carrier stands still (docs/divergences.md).
/// </para>
/// </summary>
[ClientStandaloneEntity(TFFlagNetState.ClassName)]
public sealed class ClientTFFlag : ClientStandaloneEntity
{
    private readonly IEngineEffects Effects;
    private readonly GlowObjectManager Glow;

    /// <summary>The name of the system running, or null when none.</summary>
    private string? _running;

    /// <summary>The registry slot this flag holds; -1 until the first data change registers it.</summary>
    private int _glowSlot = -1;

    /// <summary>Point 0 is the only one a trail reads — the origin it follows.</summary>
    private const int FollowPoint = 0b1;

    public ClientTFFlag(IEngineEntityRender render, IEngineEffects effects, GlowObjectManager glow)
        : base(render)
    {
        Effects = effects;
        Glow = glow;
    }

    public override string ClassName => TFFlagNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFFlagNetState State = new();

    /// <summary>GetPaperEffect (entity_capture_flag.cpp:318-326): the map's, else stock's.</summary>
    public string PaperEffect => State.PaperEffect.Length > 0 ? State.PaperEffect : TFFlagTrail.PaperEffect;

    /// <summary>The switch includes the paper trail (entity_capture_flag.cpp:2600-2601): ALL and PAPERONLY.</summary>
    public bool WantsPaper => (TFFlagEffects)State.UseTrailEffect is TFFlagEffects.All or TFFlagEffects.PaperOnly;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        UpdateGlowEffect();

        bool stolen = (TFFlagStatus)State.FlagStatus == TFFlagStatus.Stolen && WantsPaper;

        if (stolen && _running is null)
        {
            if (!Effects.ParticleSystemStart(Handle, PaperEffect))
            {
                Log.Warning($"[TF2/Ctf] {PaperEffect} did not start on {ClassName}\n");
                return;
            }

            _running = PaperEffect;
            Effects.ParticleSystemFollowPoints(Handle, FollowPoint);
        }
        else if (!stolen && _running is not null)
        {
            Effects.ParticleSystemStop(Handle);
            _running = null;
        }
    }

    public override void OnRemoved()
    {
        if (_glowSlot >= 0)
        {
            Glow.UnregisterGlowObject(_glowSlot);
            _glowSlot = -1;
        }

        if (_running is null)
            return;

        Effects.ParticleSystemStop(Handle);
        _running = null;
    }

    /// <summary>
    /// CCaptureFlag::UpdateGlowEffect (entity_capture_flag.cpp:510-532): the
    /// slot is created once, greyed, then answered — the entity in or out with
    /// the hide gate, the colour the team's. Stock's owner arm of the change
    /// test (:460-464) re-colours on every wire change here; the owner is not
    /// on the wire. The powerup, robot-destruction and player-destruction
    /// hides, the stolen-vs-local-team arm and the disabled and no-draw halves
    /// of ShouldHideGlowEffect wait on their seams (docs/plans/tf2/deferred.tsv).
    /// </summary>
    private void UpdateGlowEffect()
    {
        if (_glowSlot < 0)
            _glowSlot = Glow.RegisterGlowObject(Handle, new Vector(0.76f, 0.76f, 0.76f), 1f, true, false);

        if (ShouldHideGlowEffect())
        {
            Glow.SetEntity(_glowSlot, EntityHandle.None);
            return;
        }

        Glow.SetEntity(_glowSlot, Handle);
        Glow.SetColor(_glowSlot, GetTeamGlowColor(State.Team));
    }

    /// <summary>ShouldHideGlowEffect's first arm (:539-542): off means hidden.</summary>
    private bool ShouldHideGlowEffect() => State.GlowEnabled == 0;

    /// <summary>
    /// TFTeamPlayRules::GetTeamGlowColor (tf_gamerules.cpp:17565-17584): the
    /// two team colours, grey for every other team.
    /// </summary>
    public static Vector GetTeamGlowColor(int team) => team switch
    {
        (int)TFTeam.Red => new Vector(0.74f, 0.23f, 0.23f),
        (int)TFTeam.Blue => new Vector(0.49f, 0.66f, 0.77f),
        _ => new Vector(0.76f, 0.76f, 0.76f),
    };
}
