//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The local player's HUD row (what stock's <c>CTFPlayerStatusHUD</c>
/// paints, tf_hud_playerstatus.cpp): the engine's health for the local
/// player, and the local row of the <c>tf_player_manager</c> columns.
/// The cross's <c>m_iMaxHealth</c> and <c>m_iMaxBuffedHealth</c> are
/// what the server wrote from <c>GetMaxHealth</c> and
/// <c>GetMaxHealthForBuffing</c> (src/tf2/server/Player/TFPlayerResource.cs)
/// — read here rather than recomputed: the boost belongs to
/// <c>tf_max_health_boost</c> and to the server that applies it.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFLocalPlayer)])]
public sealed class TFHudLocalPlayer(IEngineHud screen, TFClientPlayerResources resources,
                                     ILocalArsenal arsenal, TFWeaponScripts scripts,
                                     TFClassTable classes) : ITFLocalPlayer
{
    public string ClassName
    {
        get
        {
            if (Row is not ({ } live, var index))
                return "";
            TFClass tfClass = (TFClass)live.State.PlayerClass[index];
            return tfClass == TFClass.Undefined ? "" : TFClassNames.NonLocalized(tfClass);
        }
    }

    public int Health => screen.LocalPlayerHealth;

    public int MaxHealth => Row is ({ } live, var index) ? live.State.MaxHealth[index] : 0;

    public int MaxBuffedHealth => Row is ({ } live, var index) ? live.State.MaxBuffedHealth[index] : 0;

    public int Overheal => System.Math.Max(0, Health - MaxHealth);

    public bool ShowsAmmo
    {
        get
        {
            if (!arsenal.TryGetActive(out string name, out _, out _))
                return false;                   // empty hands: stock's no-player/no-weapon arms (:125-135)
            if (scripts.Get(name) is not { } script)
                return false;                   // no script: stock's empty gNullWeaponInfo, which spends nothing
            if (TFWeaponKinds.Of(script) == TFWeaponKind.Medigun)
                return false;                   // the medigun (:137-140; its charge is the :150 test's subject too)
            return script.Base.Ammo1.Length > 0 // UsesPrimaryAmmo (:143-144): the scripts spell "None" empty
                && script.Base.Ammo1 != "TF_AMMO_METAL";   // metal is the engineer's build pool, not a readout (:147-148)
        }
    }

    public int Clip => arsenal.TryGetActive(out _, out int clip, out _) ? clip : -1;

    public int Reserve => arsenal.TryGetActive(out _, out _, out int reserve) ? reserve : 0;

    public int Team => Row is ({ } live, var index) ? live.State.Team[index] : 0;

    /// <summary>Stock's <c>nMaxTotalAmmo</c> (tf_hud_ammostatus.cpp:313-319):
    /// the class's <c>m_aAmmoMax</c> for the weapon's primary ammo — zero
    /// before a class is chosen, as stock's undefined row is — with the
    /// script's <c>MaxClip1</c> joined when the weapon has one.</summary>
    public int MaxTotalAmmo
    {
        get
        {
            if (!arsenal.TryGetActive(out string name, out _, out _)
                || scripts.Get(name) is not { } script)
                return 0;

            int max = 0;
            if (TFAmmoTypes.Of(script.Base.Ammo1) is { } ammo && Row is ({ } live, var index))
            {
                var tfClass = (TFClass)live.State.PlayerClass[index];
                max = tfClass > TFClass.Undefined && tfClass < TFClass.CountAll
                    ? (ammo == TFAmmo.Grenades3 ? 1 : classes[tfClass].MaxAmmo(ammo))
                    : 0;
            }

            if (script.Base.MaxClip1 > 0)
                max += script.Base.MaxClip1;          // the clip joins the pool (:315-319)
            return max;
        }
    }

    /// <summary>The local player's row of the cross, when the cross has
    /// arrived and the local index is a player's slot (0 is the world).
    /// The pair, so each member reads the columns once.</summary>
    private (ClientTFPlayerResource? Live, int Index) Row =>
        resources.Live is { } live && screen.LocalPlayerIndex is > 0 and < TFPlayerResourceNetState.Slots
            ? (live, screen.LocalPlayerIndex)
            : (null, 0);
}
