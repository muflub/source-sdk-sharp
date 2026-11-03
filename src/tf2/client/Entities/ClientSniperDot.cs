//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Shared.Weapons;

namespace TF2.Client.Entities;

/// <summary>
/// The client's half of env_sniperdot: stock's <c>CSniperDot</c> client arm
/// (<c>shared/tf/tf_weapon_sniperrifle.cpp:1490-1718</c>). Two sprites at
/// one point, the team's own texture (<c>OnDataChanged</c>'s create arm
/// :1701-1716), the outer at the size the position arm answers and the inner
/// smaller by the charge's strength (<c>DrawModel</c> :1587-1646), both in
/// the translucent pass.
///
/// <para>
/// THE POSITION (<c>GetRenderingPositions</c> :1492-1581). The local player's
/// own dot is re-traced every frame from the view being drawn along its
/// forward, MASK_SHOT, past the owner, capped 384 out, and backed four units
/// off the face — two units across, or four while aiming unzoomed (The
/// Classic's wide dot) scaled by distance past 200. Anybody else's dot sits
/// where the server put it.
/// </para>
///
/// <para>
/// THE STRENGTH (<c>DrawModel</c> :1610-1636). The owner's carried rifle
/// charging answers its charged damage over the maximum
/// (<c>GetHUDDamagePerc</c> :1309-1312), clamped into 0.1..1 as stock's assert
/// demands; anything else falls back to the dot's own
/// <c>m_flChargeStartTime</c> clock over the charge time — the rage buff's
/// 1.25x rate when the owner has it — remapped into the same band.
/// </para>
///
/// <para>
/// DIVERGENCES, the laser dot's recorded ones
/// (<see cref="SourceSharp.Client.Standalone.ClientLaserDot"/>): a player
/// other than the local one has no eye pitch on this client, so their dot
/// never re-traces and sits at the networked origin (stock's own fallback for
/// a dormant owner); the trace is the plain filtered one past the owner, not
/// the friendly-combat-item filter. The third-person arm of
/// <c>ShouldDraw</c> (:1651-1661) silences the whole draw while the camera is
/// out of the player's head.
/// </para>
/// </summary>
[ClientStandaloneEntity(SniperDotNetState.ClassName)]
public sealed class ClientSniperDot(IEngineEntityRender render, IFxRender fx, IPixelVisSlots view,
                                    IEngineWorldQuery world, IThirdPersonCamera camera, IGameClock clock,
                                    IClientStandalones standalones, WeaponCatalog weapons,
                                    IWeaponDrawStates states, ITFPlayerData players)
    : ClientStandaloneEntity(render)
{
    private readonly IFxRender _fx = fx;
    private readonly IPixelVisSlots _view = view;
    private readonly IEngineWorldQuery _world = world;
    private readonly IThirdPersonCamera _camera = camera;
    private readonly IGameClock _clock = clock;
    private readonly IClientStandalones _standalones = standalones;
    private readonly WeaponCatalog _weapons = weapons;
    private readonly IWeaponDrawStates _states = states;
    private readonly ITFPlayerData _players = players;

    /// <summary>SNIPER_DOT_SPRITE_RED and _BLUE (tf_weapon_sniperrifle.cpp:43-44).</summary>
    public const string SpriteRed = "effects/sniperdot_red.vmt";
    public const string SpriteBlue = "effects/sniperdot_blue.vmt";

    /// <summary>c_fMaxSizeDistVR: how far the local arm's trace reaches.</summary>
    public const float LocalTraceDist = 384f;

    /// <summary>c_flMaxSizeDistUnzoomed: past here the aiming dot grows with distance.</summary>
    public const float UnzoomedScaleDist = 200f;

    /// <summary>The sizes: the dormant arm's 6, the local 2, the aiming 4 (:1498, :1520, :1525).</summary>
    public const float DistantSize = 6f, LocalSize = 2f, AimingSize = 4f;

    /// <summary>The backing-off the hit plane, towards the source (:1553).</summary>
    public const float SurfaceBackoff = 4f;

    /// <summary>The strength band's floor, and the rage buff's rate (:1631, :1620).</summary>
    public const float MinimumStrength = 0.1f, RageRate = 1.25f;

    /// <summary>CTFSniperRifle's classname, the carried-weapon test's subject.</summary>
    public const string RifleName = "tf_weapon_sniperrifle";

    /// <summary>
    /// The renderer's cull box about the networked origin, the laser dot's
    /// shape: the drawn point is the local re-trace, which can lead the
    /// networked one across the room on a flick.
    /// </summary>
    private const float CullHalf = 64f;

    private int _material;

    public override string ClassName => SniperDotNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public SniperDotNetState State = new();

    /// <summary>
    /// OnDataChanged's create arm: the team picks the sprite once, and the
    /// custom draw and the translucent pass are asked for
    /// (<c>RENDER_GROUP_TRANSLUCENT_ENTITY</c>).
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_material == 0)
            _material = _fx.MaterialFind(State.Team == (byte)TFTeam.Blue ? SpriteBlue : SpriteRed);

        Render.SetCustomDraw(Handle, new Vector(-CullHalf, -CullHalf, -CullHalf),
                             new Vector(CullHalf, CullHalf, CullHalf));
        Render.SetBlending(Handle, translucent: true, ignoreZ: false, alpha: 255);
    }

    public override void OnRemoved()
    {
        if (_material != 0)
            _fx.MaterialRelease(_material);
        _material = 0;
    }

    /// <summary>DrawModel: the two sprites, outer then inner.</summary>
    public override int OnDraw()
    {
        // ShouldDraw's third-person arm: no dot while the camera is not the
        // player's own eyes (:1656-1658).
        if (_material == 0 || _camera.IsThirdPerson)
            return 0;

        CurrentViewState view = _view.View();
        int owner = State.OwnerIndex;
        Vector end;
        float size;

        if (owner > 0 && owner == _fx.LocalPlayer())
        {
            size = LocalSize;
            bool scale = false;

            // The Classic's wide dot: aiming but not zoomed (:1523-1527).
            TFCondMask cond = ConditionsOf(owner);
            if (cond.Has(TFCond.Aiming) && !cond.Has(TFCond.Zoomed))
            {
                size = AimingSize;
                scale = true;
            }

            ClientTraceResult tr = _world.ClientTraceLine(view.Origin, view.Origin + view.Forward * LocalTraceDist,
                                                          TraceMask.Shot, TraceFilter.Simple, owner);
            end = tr.End - view.Forward * SurfaceBackoff;

            if (scale)
            {
                float dist = (end - view.Origin).Length;
                if (dist > UnzoomedScaleDist)
                    size *= dist / UnzoomedScaleDist;
            }
        }
        else
        {
            // No other player's eye pitch (see the class doc): the networked
            // origin, stock's dormant arm (:1573-1578).
            end = Entities.GetOrigin(Handle);
            size = DistantSize;
        }

        float strength = Strength(owner);

        Span<FxVertex> quads = stackalloc FxVertex[8];
        EffectQuads.Sprite(quads[..4], end, size, size, 255, 255, 255, 128, view);
        float inner = size * strength;
        EffectQuads.Sprite(quads[4..], end, inner, inner, 255, 255, 255, 255, view);
        _fx.DrawQuads(_material, quads, byteColor: true);
        return 1;
    }

    /// <summary>
    /// The strength the inner sprite is: the carried rifle's charge over the
    /// maximum when there is one (<c>GetHUDDamagePerc</c>, the local player's
    /// own replayed prediction while it is fresh), else the dot's own clock
    /// over the charge time at the rage-adjusted rate
    /// (<c>RemapValClamped</c> :1635).
    /// </summary>
    private float Strength(int owner)
    {
        if (CarriedRifle(owner) is { } rifle)
        {
            WeaponNetState state = _states.PredictedStateOf(rifle) ?? rifle.State;
            return Math.Clamp(TFSniperRifleBehaviour.Charge(state, _clock.Now) / TFSniperRifleBehaviour.DamageMax,
                              MinimumStrength, 1f);
        }

        float rate = TFSniperRifleBehaviour.ChargePerSec;
        if (ConditionsOf(owner).Has(TFCond.SniperchargeRageBuff))
            rate *= RageRate;

        float life = _clock.Now - State.ChargeStartTime;
        return CritBucket.RemapValClamped(life, 0f, TFSniperRifleBehaviour.DamageMax / rate, MinimumStrength, 1f);
    }

    /// <summary>The owner's carried weapon, when it is a sniper rifle — stock's
    /// <c>GetActiveTFWeapon</c> then <c>dynamic_cast</c> (:1607-1608).</summary>
    private ClientSsWeapon? CarriedRifle(int owner)
        => _standalones.Live().OfType<ClientSsWeapon>()
            .FirstOrDefault(w => w.State.OwnerIndex == owner && w.State.WeaponTypeId == _weapons.IndexOf(RifleName));

    /// <summary>The owner's conditions: the working copy when he is predicted, else the decode.</summary>
    private TFCondMask ConditionsOf(int owner)
        => _players.Predicted(owner)?.Conditions() ?? _players.Networked(owner)?.Conditions() ?? default;
}
