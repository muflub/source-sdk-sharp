//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Client.Buildings;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Proxies;

/// <summary>
/// TF2's own material proxies, answered by name beside the core's table: the
/// proxies stock's client.dll compiled in (<c>c_tf_player.cpp</c> and friends)
/// that the base engine never had. Registered as a mod provider, asked by
/// <see cref="MaterialProxyFactory"/> after its own names.
/// </summary>
[SideService(Side.Client)]
public sealed class TFMaterialProxies(ITFPlayerData players, IClientStandalones standalones, IEngineHud screen,
                                      TFTeammateMaxInvisConVar maxInvis, IRenderPolicyWishes policy,
                                      WeaponCatalog catalog, IWeaponDrawStates states)
    : IMaterialProxyProvider
{
    private readonly WeaponCatalog _catalog = catalog;
    private readonly IWeaponDrawStates _states = states;
    /// <summary>The names this mod answers. <c>MaterialProxyFactoryTests</c> holds native's list equal to the core's plus these.</summary>
    public static IReadOnlyCollection<string> ProxyNames =>
    [
        "ModelGlowColor", "spy_invis", "weapon_invis", "vm_invis", "invis",
        "InvulnLevel", "BurnLevel", "YellowLevel", "HeartbeatScale", "ShieldFalloff", "WheatlyEyeGlow",
        "CommunityWeapon", "BenefactorLevel", "ItemTintColor", "WeaponSkin", "ObjectPower",
        "StatTrakIllum", "StatTrakDigit", "StatTrakIcon", "AnimatedWeaponSheen",
        "TeamTexture", "building_invis", "BuildingRescueLevel", "SniperRifleCharge", "StickybombGlowColor",
    ];

    public IReadOnlyCollection<string> Names => ProxyNames;

    private TFProxyPlayers ProxyPlayers() => new(players, standalones, screen, maxInvis, policy);

    public MaterialProxy? Create(string name)
    {
        MaterialProxy? made = name switch
        {
        "ModelGlowColor" => new TFProxyModelGlowColor(ProxyPlayers()),
        "spy_invis" => new TFProxySpyInvis(ProxyPlayers()),
        "weapon_invis" => new TFProxyWeaponInvis(ProxyPlayers()),
        "vm_invis" => new TFProxyVmInvis(ProxyPlayers()),
        "invis" => new TFProxyInvisGeneric(ProxyPlayers()),
        "InvulnLevel" => new TFProxyInvulnLevel(ProxyPlayers()),
        "BurnLevel" => new TFProxyBurnLevel(ProxyPlayers()),
        "YellowLevel" => new TFProxyYellowLevel(ProxyPlayers()),
        "HeartbeatScale" => new TFProxyHeartbeatScale(),
        "ShieldFalloff" => new TFProxyShieldFalloff(),
        "WheatlyEyeGlow" => new TFProxyWheatlyEyeGlow(),
        "CommunityWeapon" => new TFProxyCommunityWeapon(),
        "BenefactorLevel" => new TFProxyBenefactorLevel(),
        "ItemTintColor" => new TFProxyItemTintColor(),
        "WeaponSkin" => new TFProxyWeaponSkin(),
        "ObjectPower" => new TFProxyObjectPower(ProxyPlayers()),
        "StatTrakIllum" => new TFProxyStatTrakIllum(),
        "StatTrakDigit" => new TFProxyStatTrakDigit(),
        "StatTrakIcon" => new TFProxyStatTrakIcon(),
        "AnimatedWeaponSheen" => new TFProxyAnimatedWeaponSheen(),
        "TeamTexture" => new TFProxyTeamTexture(ProxyPlayers()),
        "building_invis" => new TFProxyBuildingInvis(ProxyPlayers()),
        "BuildingRescueLevel" => new TFProxyBuildingRescueLevel(),
        "SniperRifleCharge" => new TFProxySniperRifleCharge(ProxyPlayers(), _catalog, _states),
        "StickybombGlowColor" => new TFProxyStickybombGlowColor(),
        _ => null,
        };
        return made;
    }
}

/// <summary>
/// How a TF2 proxy reaches the player whose model it is colouring: the owner
/// chain stock's <c>IHasOwner</c> walk answers (the viewmodel's and the
/// weapon's owner), the team the player resource rows by entity index, and
/// whether the player is the local one.
/// </summary>
internal sealed class TFProxyPlayers(ITFPlayerData players, IClientStandalones standalones, IEngineHud screen,
                                     TFTeammateMaxInvisConVar maxInvis, IRenderPolicyWishes policy)
{
    private const int Slots = PlayerResourceNetState.Slots;

    private ClientPlayerResource? _core;

    /// <summary>
    /// The cloaked body's render-group wish — stock's
    /// <c>C_TFPlayer::IsTransparent</c> (c_tf_player.h:208,
    /// <c>GetPercentInvisible() &gt; 0</c>) reaching
    /// <see cref="IRenderPolicyWishes.WantTransparent"/> from the cloak
    /// proxies' bind: the level a body material was just written with is the
    /// same word stock re-leafs the body by, into the translucent pass whose
    /// frame-buffer refresh the cloak shader samples.
    /// </summary>
    public void WishTransparent(int entityIndex, bool transparent)
    {
        if (standalones.GetByIndex(entityIndex) is ClientSsPlayer player)
            policy.WantTransparent(player.Handle, transparent);
    }

    /// <summary><c>tf_teammate_max_invis</c> (c_tf_player.cpp:1702): the cap
    /// the cloak proxies' effective level applies to a non-enemy view.</summary>
    public float MaxInvis => maxInvis.Value;

    /// <summary>The player behind the bound entity, or null when the model answers to nobody.</summary>
    public TFProxyPlayer? OfEntity(int entityIndex)
    {
        int player = entityIndex switch
        {
            _ when standalones.GetByIndex(entityIndex) is ClientSsViewModel viewmodel => viewmodel.State.OwnerIndex,
            // The viewmodel attachment's move parent is the viewmodel it merges onto, whose
            // owner is the player — stock's walk through the non-player move parent's owner
            // entity (tf_weaponbase.cpp:5019-5024, tf_viewmodel.cpp:454-467).
            _ when standalones.GetByIndex(entityIndex) is ClientSsViewmodelAttachment attachment
                   && standalones.GetByIndex(attachment.State.ViewModelIndex) is ClientSsViewModel viaViewmodel
                => viaViewmodel.State.OwnerIndex,
            _ when standalones.GetByIndex(entityIndex) is ClientSsWeapon weapon => weapon.State.OwnerIndex,
            _ when standalones.GetByIndex(entityIndex) is ClientSsPlayer => entityIndex,
            _ => -1,
        };
        if (player <= 0)
            return null;

        TFPlayerNetState? state = players.Predicted(player) ?? players.Networked(player);
        if (state is null)
            return null;

        int team = Core is { } resource && player < Slots ? resource.State.Team[player] : 0;
        return new TFProxyPlayer(state, team, screen.LocalPlayerIndex == player);
    }

    /// <summary>Whether the bound entity is a viewmodel, or rides one as its move parent
    /// (the viewmodel attachment): <c>vm_invis</c>'s remapped arm is theirs
    /// (tf_viewmodel.cpp:437-467, where the attachment's move parent names the viewmodel).</summary>
    public bool IsViewModel(int entityIndex) => standalones.GetByIndex(entityIndex)
        is ClientSsViewModel or ClientSsViewmodelAttachment;

    /// <summary>Stock's <c>IsBaseObject()</c>: the bound entity is one of the building
    /// mirrors (c_baseobject.cpp:1172, :1228).</summary>
    public bool IsBuilding(int entityIndex) => standalones.GetByIndex(entityIndex) is TFClientBuilding;

    /// <summary>The ragdoll arm's read (c_tf_player.cpp:1860-1864): the bound
    /// corpse's own burn start — the one it stamped on arrival
    /// (c_tf_player.cpp:928-932) — or 0 when the bind is no ragdoll or
    /// arrived cold.</summary>
    public float RagdollBurnStart(int entityIndex) => standalones.GetByIndex(entityIndex)
        is ClientSsRagdoll body ? body.BurnStartTime : 0f;

    /// <summary>Who the charge proxy reads, stock's
    /// <c>CProxySniperRifleCharge::OnBind</c> walk (tf_weapon_sniperrifle.cpp:
    /// 1327-1332): the local player, or the spectator's in-eye target while one
    /// is set — <c>GetSpectatorTarget() != 0 && GetSpectatorMode() ==
    /// OBS_MODE_IN_EYE</c>, ours the carrier's ObserverMode and ObserverTarget
    /// (the handle's entry, the low 16 bits).</summary>
    public int ViewPlayer
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            if (standalones.GetByIndex(local) is ClientSsPlayer { State.ObserverMode: GameMovement.ObsModeInEye }
                localPlayer)
            {
                int target = (int)(localPlayer.State.ObserverTarget & ClientPlayerPrediction.HandleEntryMask);
                if (target > 0)
                    return target;
            }
            return local;
        }
    }

    /// <summary>The entity index's carried weapon — the one entity the core
    /// holds per armed player, whose kind moves on a switch (stock's
    /// <c>GetActiveTFWeapon</c>).</summary>
    public ClientSsWeapon? WeaponOf(int entityIndex)
        => standalones.Live().OfType<ClientSsWeapon>()
            .FirstOrDefault(w => w.State.OwnerIndex == entityIndex);

    /// <summary><c>GetLocalPlayerTeam</c>: the resource row for the local player.</summary>
    public int LocalTeam => Core is { } resource && screen.LocalPlayerIndex < Slots
        ? resource.State.Team[screen.LocalPlayerIndex] : 0;

    private ClientPlayerResource? Core
    {
        get
        {
            if (_core is null || !standalones.Live().Contains(_core))
                _core = standalones.OfClass(EntityNames.PlayerManager)
                                   .OfType<ClientPlayerResource>().FirstOrDefault();
            return _core;
        }
    }
}

/// <param name="State">The carrier: predicted for the local player, decoded for anyone else.</param>
/// <param name="Team">The row the player resource holds for them (<c>GetTeamNumber</c>).</param>
/// <param name="IsLocal">Stock's <c>IsLocalPlayer</c>.</param>
internal readonly record struct TFProxyPlayer(TFPlayerNetState State, int Team, bool IsLocal);

/// <summary>
/// <c>CProxyModelGlowColor</c> ("ModelGlowColor",
/// game/client/tf/c_tf_player.cpp:1985-2121): the colour the weapon's result
/// var takes while its owner fights — stock's crit-boost arm (never on a
/// disguised spy, unless the bind is the local player's own view), the soda
/// popper's hype arm (<c>IsHypeBuffed</c> is
/// <c>TF_COND_SODAPOPPER_HYPE</c>, tf_player_shared.h:527), and the
/// offence/energy-buff arm. No player behind the model, or none of the
/// conditions: stock's default [1 1 1].
///
/// <para>
/// The demoman charge-glow arms (:2063-2104) are deferred: the charge meter,
/// the next-melee-crit and the glow cooldown clock are on no carrier
/// (docs/plans/tf2/deferred.tsv), so their arm answers the default until one
/// is networked.
/// </para>
/// </summary>
internal sealed class TFProxyModelGlowColor(TFProxyPlayers players) : ResultProxy
{
    private readonly TFConditionEffects _effects = new();

    public override void Bind(IProxyBind bind)
    {
        // The no-entity arm and the default: [1 1 1] (:1996, :2015, :2107).
        float r = 1f, g = 1f, b = 1f;

        if (bind.Arg != 0)
        {
            ProxyEntity entity = bind.Entity();
            if (entity.IsEntity && players.OfEntity(entity.EntIndex) is { } player)
            {
                TFPlayerNetState state = player.State;
                if (_effects.IsCritBoosted(state.Conditions(), clientSide: true))
                {
                    // Never show crit-boosted on a disguised spy, unless it is me (:2029-2040).
                    if (!state.InCond(TFCond.Disguised) || player.IsLocal)
                    {
                        if (player.Team == (int)TFTeam.Red)
                            (r, g, b) = (80f, 8f, 5f);
                        else
                            (r, g, b) = (5f, 20f, 80f);
                    }
                }
                else if (state.InCond(TFCond.SodapopperHype))
                {
                    (r, g, b) = (50f, 2f, 48f);
                }
                else if (state.InCond(TFCond.Offensebuff) || state.InCond(TFCond.EnergyBuff))
                {
                    // Temporarily hijacking this proxy for buff FX (:2048-2060).
                    if (player.Team == (int)TFTeam.Red)
                        (r, g, b) = (226f, 150f, 62f);
                    else
                        (r, g, b) = (29f, 202f, 135f);
                }
            }
        }

        Span<float> colour = [r, g, b];
        bind.SetVec(Result, colour, 3);
    }
}

/// <summary>
/// The cloak proxies' common base, stock's <c>CBaseInvisMaterialProxy</c>
/// (game/client/tf/tf_proxyentity.cpp:12-45): Init finds <c>$cloakfactor</c>
/// and refuses without it, and every arm answers through
/// <see cref="SetLevel"/>.
/// </summary>
internal abstract class TFProxyInvis(TFProxyPlayers players) : MaterialProxy
{
    /// <summary>tf_viewmodel.cpp:488-492 and :587-591: the blink overwrite.</summary>
    protected const float BlinkInvis = 0.3f;

    /// <summary>TF_VM_MIN_INVIS / TF_VM_MAX_INVIS (tf_viewmodel.cpp:425-426).</summary>
    protected const float VmMinInvis = 0.22f, VmMaxInvis = 0.5f;

    /// <summary>The owner chain and the resources the arms read.</summary>
    protected TFProxyPlayers Players => players;

    private int _cloakFactor = -1;

    public override bool Init(IProxyInit init)
    {
        _cloakFactor = init.FindVar("$cloakfactor", true, out bool found);
        return found;
    }

    /// <summary>The player behind the bound model, or null when stock's walk finds none.</summary>
    protected TFProxyPlayer? Player(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return null;
        ProxyEntity entity = bind.Entity();
        return entity.IsEntity ? players.OfEntity(entity.EntIndex) : null;
    }

    /// <summary><c>GetPercentInvisible</c>: the carrier and the bind's clock
    /// give the level (<see cref="TFInvisibility"/>).</summary>
    protected static float Percent(TFProxyPlayer player, IProxyBind bind)
        => TFInvisibility.Percent(player.State, bind.CurTime);

    /// <summary>
    /// <c>GetEffectiveInvisibilityLevel</c> (c_tf_player.cpp:6844-6898): the
    /// level capped at <c>tf_teammate_max_invis</c> for anyone the local view
    /// does not see as an enemy (<c>IsEnemyPlayer</c> :5384-5412 — the local
    /// team's opposite number, the coaching arm aside). The deathcam/freezecam
    /// and taunt-invisibility arms are the viewer's and the econ's
    /// (docs/plans/tf2/deferred.tsv).
    /// </summary>
    protected float EffectiveLevel(TFProxyPlayer player, IProxyBind bind)
    {
        float percent = Percent(player, bind);
        if (!IsEnemyPlayer(player) && percent > Players.MaxInvis)
            percent = Players.MaxInvis;
        return percent;
    }

    private bool IsEnemyPlayer(TFProxyPlayer player) =>
        (Players.LocalTeam == (int)TFTeam.Red && player.Team == (int)TFTeam.Blue)
        || (Players.LocalTeam == (int)TFTeam.Blue && player.Team == (int)TFTeam.Red);

    /// <summary>The viewmodel arm's remap, 0.22..0.5 but dropped below a
    /// hundredth (tf_viewmodel.cpp:483-486, :582-585); stock's unclamped
    /// <c>RemapVal</c>.</summary>
    protected static float VmRemap(float percent) => percent < 0.01f
        ? 0f
        : VmMinInvis + percent * (VmMaxInvis - VmMinInvis);

    protected void SetLevel(IProxyBind bind, float level)
    {
        bind.SetFloat(_cloakFactor, level);

        // The body's render group rides the same word (see WishTransparent).
        // A bind with no entity behind it names no renderable to wish for, and
        // the arms that answer 0 for one — the ragdoll, the mimic, the
        // building — land on that entity's handle, which is no player's.
        if (bind.Arg != 0 && bind.Entity().IsEntity)
            players.WishTransparent(bind.Entity().EntIndex, level > 0f);
    }
}

/// <summary>
/// <c>CSpyInvisProxy</c> ("spy_invis", game/client/tf/c_tf_player.cpp:1667-1765):
/// the player arm writes <c>$cloakColorTint</c> by the carrier's team
/// (:1739-1747) and the effective invisibility level into <c>$cloakfactor</c>
/// (:1735); the not-entity arm writes both to rest (:1755-1763), an entity
/// that walks to no player only the level — stock's ragdoll arm
/// (:1721-1725) has no C_TFRagdoll mirror to walk to, so it answers 0.
/// </summary>
internal sealed class TFProxySpyInvis(TFProxyPlayers players) : TFProxyInvis(players)
{
    private int _tint = -1;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        _tint = init.FindVar("$cloakColorTint", true, out bool found);
        return found;
    }

    public override void Bind(IProxyBind bind)
    {
        if (Player(bind) is { } player)
        {
            // The player arm (:1737-1749): the tint by the carrier's team.
            if (player.Team == (int)TFTeam.Red)
                bind.SetVec3(_tint, 1.0f, 0.5f, 0.4f);
            else
                bind.SetVec3(_tint, 0.4f, 0.5f, 1.0f);

            SetLevel(bind, EffectiveLevel(player, bind));
            return;
        }

        if (bind.Arg == 0)
        {
            // OnBindNotEntity (:1755-1763): the tint to white.
            bind.SetVec3(_tint, 1f, 1f, 1f);
        }

        // An entity that walks to no player (the ragdoll or mimic arm) leaves
        // the tint as the last bind left it (:1721-1731).
        SetLevel(bind, 0f);
    }
}

/// <summary>
/// <c>CWeaponInvisProxy</c> ("weapon_invis", game/shared/tf/tf_weaponbase.cpp:
/// 4993-5041): the bound weapon's player's effective invisibility level
/// (:5023-5038), or 0 when the bind walks to none (:5013, :5028).
/// </summary>
internal sealed class TFProxyWeaponInvis(TFProxyPlayers players) : TFProxyInvis(players)
{
    public override void Bind(IProxyBind bind)
    {
        SetLevel(bind, Player(bind) is { } player ? EffectiveLevel(player, bind) : 0f);
    }
}

/// <summary>
/// <c>CViewModelInvisProxy</c> ("vm_invis", game/shared/tf/tf_viewmodel.cpp:
/// 415-505): a viewmodel's level remapped into 0.22..0.5 (:425-426, :483-486)
/// with the blink overwrite (:488-492); a weapon bound with it takes the plain
/// level (:477-478). The motion-cloak overwrite (:494-499) is the Dead Ringer
/// lane's; no player behind the model is 0 (:471-475).
/// </summary>
internal sealed class TFProxyVmInvis(TFProxyPlayers players) : TFProxyInvis(players)
{
    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0 || !bind.Entity().IsEntity || Player(bind) is not { } player)
        {
            SetLevel(bind, 0f);
            return;
        }

        if (Players.IsViewModel(bind.Entity().EntIndex))
        {
            float weaponInvis = VmRemap(Percent(player, bind));
            if (player.State.Conditions().Has(TFCond.StealthedBlink))
                weaponInvis = BlinkInvis;
            SetLevel(bind, weaponInvis);
            return;
        }

        SetLevel(bind, Percent(player, bind));
    }
}

/// <summary>
/// <c>CInvisProxy</c> ("invis", game/shared/tf/tf_viewmodel.cpp:508-610): the
/// local player's own view takes the vm arm (:576-600), anyone else the
/// effective level (:604). No player behind the model — stock's ragdoll arm
/// included (:565-573) — is 0.
/// </summary>
internal sealed class TFProxyInvisGeneric(TFProxyPlayers players) : TFProxyInvis(players)
{
    public override void Bind(IProxyBind bind)
    {
        if (Player(bind) is not { } player)
        {
            SetLevel(bind, 0f);
            return;
        }

        if (player.IsLocal)
        {
            float weaponInvis = VmRemap(Percent(player, bind));
            if (player.State.Conditions().Has(TFCond.StealthedBlink))
                weaponInvis = BlinkInvis;
            SetLevel(bind, weaponInvis);
            return;
        }

        SetLevel(bind, EffectiveLevel(player, bind));
    }
}

/// <summary>
/// <c>CProxyInvulnLevel</c> ("InvulnLevel", game/client/tf/c_tf_player.cpp:
/// 1771-1819): 1.0 unless the model's player is invulnerable while the
/// wearing-off condition runs (:1805-1808); a bind with no player behind it is
/// stock's 1.0 (:1778-1789). The preview-effect arm is the tools'.
/// </summary>
internal sealed class TFProxyInvulnLevel(TFProxyPlayers players) : ResultProxy
{
    private readonly TFConditionEffects _effects = new();

    public override void Bind(IProxyBind bind)
    {
        float result = 1f;
        if (bind.Arg != 0)
        {
            ProxyEntity entity = bind.Entity();
            if (entity.IsEntity && players.OfEntity(entity.EntIndex) is { } player
                && _effects.IsInvulnerable(player.State.Conditions())
                && player.State.InCond(TFCond.InvulnerableWearingoff))
            {
                result = 0f;
            }
        }

        SetFloatResult(bind, result);
    }
}

/// <summary>
/// <c>CProxyBurnLevel</c> ("BurnLevel", game/client/tf/c_tf_player.cpp:1825-1896):
/// a burning player's skin ramp — 0→1 over the first 0.3 s of the burn
/// (:1872-1876), then 1→0 until <c>TF_BURNING_FLAME_LIFE</c> is out (:1877-1881),
/// folded back into the material's band by the <c>1 - |temp - 1|</c> the result
/// wants (:1884). The start is the carrier's <c>BurnEffectStartTime</c> — stock's
/// client sets it as <c>TF_COND_BURNING</c> arrives (c_tf_player.cpp:7306); the
/// server writes it at ignition here. The ragdoll arm (:1857-1865) reads the
/// bound corpse's own clock, which the corpse stamped on arrival
/// (<see cref="SourceSharp.Client.Standalone.ClientSsRagdoll.BurnStartTime"/>). The
/// preview arm is the tools'.
/// </summary>
internal sealed class TFProxyBurnLevel(TFProxyPlayers players) : ResultProxy
{
    /// <summary>Stock's literal peak (c_tf_player.cpp:1870).</summary>
    private const float PeakDelay = 0.3f;

    public override void Bind(IProxyBind bind)
    {
        float result = 0f;
        float start = 0f;
        if (bind.Arg != 0)
        {
            ProxyEntity entity = bind.Entity();
            if (entity.IsEntity)
            {
                // The player arm (:1848-1855): a burning player's own clock —
                // the owner walk answers players, and a corpse walks to nobody.
                if (players.OfEntity(entity.EntIndex) is { } player)
                {
                    if (player.State.InCond(TFCond.Burning) && player.State.BurnEffectStartTime > 0f)
                        start = player.State.BurnEffectStartTime;
                }
                else
                {
                    // The ragdoll arm (:1857-1865).
                    start = players.RagdollBurnStart(entity.EntIndex);
                }
            }
        }

        if (start > 0f)
        {
            // :1869-1885 — the ramp over the burn's clock.
            float peak = start + PeakDelay;
            float temp = bind.CurTime < peak
                ? CritBucket.RemapValClamped(bind.CurTime, start, peak, 0f, 1f)
                : CritBucket.RemapValClamped(bind.CurTime, peak, start + TFCombatDefs.BurningFlameLife, 1f, 0f);
            result = 1f - MathF.Abs(temp - 1f);
        }

        SetFloatResult(bind, result);
    }
}

/// <summary>
/// <c>CProxyUrineLevel</c> ("YellowLevel", game/client/tf/c_tf_player.cpp:
/// 1903-1978): jarate colours the model by the team the local view sees — a
/// disguised spy's disguise team, unless the spy is the local player or already
/// shows the local team (:1946-1953) — RED [6 9 2], else [7 5 1] (:1918-1925,
/// :1954-1961). No urine, or no player behind the model: [1 1 1] (:1911).
/// </summary>
internal sealed class TFProxyYellowLevel(TFProxyPlayers players) : ResultProxy
{
    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg != 0)
        {
            ProxyEntity entity = bind.Entity();
            if (entity.IsEntity && players.OfEntity(entity.EntIndex) is { } player
                && player.State.InCond(TFCond.Urine))
            {
                int visibleTeam = player.Team;
                if (player.State.InCond(TFCond.Disguised)
                    && !player.IsLocal && visibleTeam != players.LocalTeam)
                {
                    visibleTeam = player.State.DisguiseTeam;
                }

                if (visibleTeam == (int)TFTeam.Red)
                    bind.SetVec3(Result, 6f, 9f, 2f);
                else
                    bind.SetVec3(Result, 7f, 5f, 1f);
                return;
            }
        }

        bind.SetVec3(Result, 1f, 1f, 1f);
    }
}

/// <summary>
/// <c>CProxyHeartbeatScale</c> ("HeartbeatScale", game/client/tf/c_tf_player.cpp:
/// 2168-2211): the heart texture's pulse, pure clock arithmetic — the stronger
/// of two beats a quarter cycle apart, scaled into 0.6..0.48.
/// </summary>
internal sealed class TFProxyHeartbeatScale : ResultProxy
{
    private const float TwoPi = 2f * 3.141592f;

    public override void Bind(IProxyBind bind)
    {
        float beat = MathF.Max(Beat(bind.CurTime), Beat(bind.CurTime + 0.25f));

        const float Scale = 0.6f;
        float scaledBeat = 1.0f * Scale + (0.8f * Scale - 1.0f * Scale) * beat;
        SetFloatResult(bind, scaledBeat);
    }

    private static float Beat(float time)
    {
        float s = MathF.Sin(time * TwoPi);
        s = Math.Clamp(s, 0f, 1f);
        s *= s;
        s *= s;
        s = Math.Clamp(s, 0.5f, 1f);
        s -= 0.5f;
        return s * 2f;
    }
}

/// <summary>
/// <c>CProxyResistShield</c> ("ShieldFalloff", game/client/tf/c_tf_player.cpp:
/// 2389-2421): the Vaccinator shield's pulse reads the owner's
/// <c>GetLastResistTime</c>, which is on no carrier
/// (docs/plans/tf2/deferred.tsv), so stock's rest value [1 1 1 1].
/// </summary>
internal sealed class TFProxyShieldFalloff : ResultProxy
{
    public override void Bind(IProxyBind bind)
    {
        Span<float> rest = [1f, 1f, 1f, 1f];
        bind.SetVec(Result, rest, 4);
    }
}

/// <summary>
/// <c>CProxyWheatlyEyeGlow</c> ("WheatlyEyeGlow", game/client/tf/c_tf_player.cpp:
/// 2426-2522): the sapper's eye glow. The talking arm asks
/// <c>IsWheatleyTalking</c> and the eyelids pose parameter, on no carrier
/// (docs/plans/tf2/deferred.tsv), so the instance holds stock's fresh statics
/// (<c>s_flCurr</c> 0.2, :2443-2453) and the ramp never runs.
/// </summary>
internal sealed class TFProxyWheatlyEyeGlow : ResultProxy
{
    private float _curr = 0.2f;

    public override void Bind(IProxyBind bind) => SetFloatResult(bind, _curr);
}

/// <summary>
/// <c>CProxyCommunityWeapon</c> ("CommunityWeapon", game/client/tf/c_tf_player.cpp:
/// 2126-2162): 1 when the bound item's quality is AE_COMMUNITY (:2143-2147).
/// The econ item view reaches no client mirror (docs/plans/tf2/deferred.tsv),
/// so the no-item arm answers 0 (:2153).
/// </summary>
internal sealed class TFProxyCommunityWeapon : ResultProxy
{
    public override void Bind(IProxyBind bind) => bind.SetInt(Result, 0);
}

/// <summary>
/// <c>CProxyBenefactorLevel</c> ("BenefactorLevel", game/client/tf/c_tf_player.cpp:
/// 2222-2292): the benefactor heart's texture scale from the item's kill-eater
/// count. The attribute reaches no client (docs/plans/tf2/deferred.tsv), so
/// stock's return-without-write arm (:2248-2249) is every bind.
/// </summary>
internal sealed class TFProxyBenefactorLevel : ResultProxy
{
    public override void Bind(IProxyBind bind) { }
}

/// <summary>
/// <c>CProxyItemTintColor</c> ("ItemTintColor", game/shared/econ/econ_wearable.cpp:
/// 465-543): the wearable's paint. The modified RGB asks the econ item view
/// (docs/plans/tf2/deferred.tsv), so stock's default <c>Vector(0,0,0)</c> is
/// the only answer (:471, :540).
/// </summary>
internal sealed class TFProxyItemTintColor : ResultProxy
{
    public override void Bind(IProxyBind bind) => bind.SetVec3(Result, 0f, 0f, 0f);
}

/// <summary>
/// <c>CWeaponSkinProxy</c> ("WeaponSkin", game/client/tf/c_tf_player.cpp:
/// 3396-3707): swaps <c>$basetexture</c> for the owner's weapon-skin composite.
/// No skin exists on the client (docs/plans/tf2/deferred.tsv), so every bind
/// is stock's no-item exit (:3484-3486) — the original texture stays — and the
/// proxy keeps to materials holding <c>$basetexture</c> (:3454-3457; the DX8
/// refusal at :3447-3449 is gone with the DX8 path itself).
/// </summary>
internal sealed class TFProxyWeaponSkin : MaterialProxy
{
    public override bool Init(IProxyInit init)
    {
        init.FindVar("$basetexture", true, out bool found);
        return found;
    }

    public override void Bind(IProxyBind bind) { }
}

/// <summary>
/// <c>CObjectPowerProxy</c> ("ObjectPower", game/client/tf/c_baseobject.cpp:
/// 1146-1184): the bound building's sapper power. Stock casts the bind to
/// <c>C_BaseObject</c> and returns without writing when the cast fails
/// (:1172-1174); a building takes <c>m_Factor.GetFloat()</c> (:1176) — the
/// "scale" keyvalue, default 1 (:1161). The power itself is on no carrier:
/// stock writes the factor unchanged, never the sapper's charge.
/// </summary>
internal sealed class TFProxyObjectPower(TFProxyPlayers players) : ResultProxy
{
    private ProxyFloatInput _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        return _factor.Init(init, "scale", 1f);
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0 || !bind.Entity().IsEntity)
            return;
        if (!players.IsBuilding(bind.Entity().EntIndex))
            return;

        SetFloatResult(bind, _factor.Get(bind));
    }
}

/// <summary>
/// <c>CStatTrakIllumProxy</c> ("StatTrakIllum", game/client/tf/c_tf_player.cpp:
/// 3063-3110): the stat clock's illumination, written only when the bound
/// module's move parent is a viewmodel (:3099-3105). No client mirror answers
/// a module's move parent (docs/plans/tf2/deferred.tsv), so stock's fall-through
/// leave-it arm (:3092, :3106-3108) is every bind.
/// </summary>
internal sealed class TFProxyStatTrakIllum : ResultProxy
{
    public override void Bind(IProxyBind bind) { }
}

/// <summary>
/// <c>CStatTrakDigitProxy</c> ("StatTrakDigit", game/client/tf/c_tf_player.cpp:
/// 3114-3271): one digit of the kill odometer. The kill-eater score reaches no
/// client (docs/plans/tf2/deferred.tsv), so stock's no-score arm answers 0
/// (:3240-3245).
/// </summary>
internal sealed class TFProxyStatTrakDigit : ResultProxy
{
    public override void Bind(IProxyBind bind) => SetFloatResult(bind, 0f);
}

/// <summary>
/// <c>CStatTrakIconProxy</c> ("StatTrakIcon", game/client/tf/c_tf_player.cpp:
/// 3276-3370): the stat clock icon's texture transform — stock's identity, the
/// dev-only offset convars at their 0 defaults (:3302-3304, :3326-3361).
/// </summary>
internal sealed class TFProxyStatTrakIcon : ResultProxy
{
    public override void Bind(IProxyBind bind)
    {
        VMatrix identity = VMatrixMath.Identity();
        bind.SetMatrix(Result, in identity);
    }
}

/// <summary>
/// <c>CProxyAnimatedWeaponSheen</c> ("AnimatedWeaponSheen", game/client/tf/
/// c_tf_player.cpp:2595-3056): the killstreak sheen. Init holds stock's
/// nine-var refusal (:2619-2655, :2657); the ramp asks the killstreak
/// idleeffect attribute, which reaches no client (docs/plans/tf2/deferred.tsv),
/// so every bind past the animated-texture checks is stock's RunNoProxy
/// (:2831-2835, :3026-3031).
/// </summary>
internal sealed class TFProxyAnimatedWeaponSheen : AnimatedTextureProxyBase
{
    private int _tint = -1;
    private int _sheenIndex = -1;

    public override bool Init(IProxyInit init)
    {
        _tint = init.FindVar("$sheenmaptint", true, out bool found);
        if (!found)
            return false;
        _sheenIndex = init.FindVar("$sheenindex", true, out found);
        if (!found)
            return false;
        foreach (string name in new[]
                 {
                     "$sheenmapmaskscaleX", "$sheenmapmaskscaleY",
                     "$sheenmapmaskoffsetX", "$sheenmapmaskoffsetY",
                     "$sheenmapmaskdirection", "$sheenmap", "$sheenmapmask",
                 })
        {
            init.FindVar(name, true, out found);
            if (!found)
                return false;
        }

        return base.Init(init);
    }

    /// <summary>The ramp arm is the deferred killstreak attribute's; it never runs.</summary>
    protected override float StartTime(IProxyBind bind) => 0f;

    public override void Bind(IProxyBind bind)
    {
        if (bind.Var(TextureVar).Type != (int)MaterialVarType.Texture)
            return;

        if (bind.Texture(TextureVar, out _) <= 0)
            return;

        bind.SetVec3(_tint, 0f, 0f, 0f);
        bind.SetInt(FrameNumVar, 0);
        bind.SetInt(_sheenIndex, 0);
    }
}

/// <summary>
/// <c>CTeamMaterialProxy</c> ("TeamTexture", game/client/tf/teammaterialproxy.cpp:
/// 20-87): the model's animated-texture frame is its team - 2 (:72-76). Init
/// finds <c>$frame</c> without complaining and always answers true (:53-62);
/// without the var, or with no player behind the model, nothing is written
/// (:69-70).
/// </summary>
internal sealed class TFProxyTeamTexture(TFProxyPlayers players) : MaterialProxy
{
    private int _frame = -1;

    public override bool Init(IProxyInit init)
    {
        _frame = init.FindVar("$frame", false, out bool found);
        if (!found)
            _frame = -1;
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (_frame < 0 || bind.Arg == 0)
            return;

        ProxyEntity entity = bind.Entity();
        if (entity.IsEntity && players.OfEntity(entity.EntIndex) is { } player)
            bind.SetInt(_frame, player.Team - 2);
    }
}

/// <summary>
/// <c>CBuildingInvisProxy</c> ("building_invis", game/client/tf/c_baseobject.cpp:
/// 1213-1245): the building's sapper fade. Stock returns without writing when
/// the bind is not a base object (:1228-1229); a building takes the owner-less
/// 0 (:1237-1239) or GetInvisibilityLevel (:1242) — a field no code writes and
/// no recv table carries (c_baseobject.cpp:1089-1111), so both arms answer 0.
/// </summary>
internal sealed class TFProxyBuildingInvis(TFProxyPlayers players) : TFProxyInvis(players)
{
    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0 || !bind.Entity().IsEntity || !Players.IsBuilding(bind.Entity().EntIndex))
            return;

        SetLevel(bind, 0f);
    }
}

/// <summary>
/// <c>CProxyBuildingRescueLevel</c> ("BuildingRescueLevel", game/client/tf/
/// c_tf_player.cpp:2300-2383): the Rescue Ranger's oscilloscope. Every arm
/// that writes asks the local player's building_teleporting_pickup attribute,
/// their active weapon and their metal ammo — none reach the client
/// (docs/plans/tf2/deferred.tsv) — so stock's early returns (:2324-2335) are
/// every bind.
/// </summary>
internal sealed class TFProxyBuildingRescueLevel : ResultProxy
{
    public override void Bind(IProxyBind bind) { }
}

/// <summary>
/// <c>CProxySniperRifleCharge</c> ("SniperRifleCharge", game/shared/tf/
/// tf_weapon_sniperrifle.cpp:1317-1373): the charge meter's texture transform.
/// Stock's read is the viewer's active TF weapon — the local player, or the
/// spectator's in-eye target while one is set (:1327-1332) — and the charged
/// damage over the maximum (<c>GetHUDDamagePerc</c>, :1311) folded into the
/// centre-anchored scale and the y translation ((1-perc)*0.8+0.6, :1339-1363).
/// The charge is the carrier's begin-time clock
/// (<see cref="TFSniperRifleBehaviour.Charge"/>), the local player's own
/// replayed prediction while it is fresh. A viewer with no rifle — or a
/// carried weapon that is not one — writes nothing (:1334-1365).
/// </summary>
internal sealed class TFProxySniperRifleCharge(TFProxyPlayers players, WeaponCatalog catalog,
                                               IWeaponDrawStates states) : ResultProxy
{
    /// <summary>Stock's <c>CTFSniperRifle</c>'s classname.</summary>
    private const string RifleName = "tf_weapon_sniperrifle";

    public override void Bind(IProxyBind bind)
    {
        int viewer = players.ViewPlayer;
        if (viewer <= 0 || players.WeaponOf(viewer) is not { } rifle
            || rifle.State.WeaponTypeId != catalog.IndexOf(RifleName))
        {
            return;
        }

        WeaponNetState state = states.PredictedStateOf(rifle) ?? rifle.State;
        float charge = TFSniperRifleBehaviour.Charge(state, bind.CurTime);
        float chargeValue = (1f - charge / TFSniperRifleBehaviour.DamageMax) * 0.8f + 0.6f;

        VMatrix mat = VMatrixMath.Translation(-0.5f, -0.5f, 0f);
        mat = VMatrixMath.Multiply(VMatrixMath.Scale(1f, 0.25f, 1f), mat);
        mat = VMatrixMath.Multiply(VMatrixMath.Translation(0.5f, 0.5f, 0f), mat);
        mat = VMatrixMath.Multiply(VMatrixMath.Translation(0f, chargeValue, 0f), mat);
        bind.SetMatrix(Result, in mat);
    }
}

/// <summary>
/// <c>CProxyStickybombGlowColor</c> ("StickybombGlowColor", game/shared/tf/
/// tf_weapon_grenade_pipebomb.cpp:1276-1345): the pipebomb's highlight. The
/// highlight state reaches no client (docs/plans/tf2/deferred.tsv), so the
/// not-a-pipebomb fall-through answers [1 1 1] (:1298, :1342).
/// </summary>
internal sealed class TFProxyStickybombGlowColor : ResultProxy
{
    public override void Bind(IProxyBind bind) => bind.SetVec3(Result, 1f, 1f, 1f);
}
