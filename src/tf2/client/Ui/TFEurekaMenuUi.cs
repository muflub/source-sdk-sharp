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
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Buildings;
using TF2.Shared;
using TF2.Shared.Buildings;
using TF2.Shared.Player;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Client.Ui;

/// <summary>
/// The Eureka Effect's teleport menu in game (stock's
/// CHudEurekaEffectTeleportMenu, game/client/tf/tf_hud_menu_eureka_teleport.cpp).
/// The arm is the wrench's own client ItemPostFrame
/// (tf_weapon_wrench.cpp:221-235): the +reload press while the held weapon is
    /// the wrench — read here off the arsenal's active state, the word the
/// prediction's ItemPostFrame wrote — with the eureka's
/// <c>alt_fire_teleport_to_spawn</c> on the active item, lays the want down
/// (<c>WantsToTeleport</c>, tf_hud_menu_eureka_teleport.cpp:351-354); the
/// release picks the edge's other half up (:236-239). The want dies with the
/// wrench leaving the hands, stock's ShouldDraw clear (:103), and with the
/// choice, stock's spend (:189).
///
/// <para>
/// A choice is stock's SendTeleportMessage (:186-209): the want spent first
/// (:189), then the send — <c>eureka_teleport %d</c> (:206-208) — unless the
/// chosen exit is not standing, which plays the deny sound and sends nothing
/// (:200-204). Stock's kart refusal (:200) is the halloween lane's, as the
/// command's own is (TFEurekaTeleport's note); the placing and upgrading
/// refusals wait with the building mirror's words of them. The cancel is
/// stock's KEY_0 / slot10 <c>lastinv</c> (:320-325, :289-294).
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFEurekaMenuSource), typeof(ITFEurekaMenuActions), typeof(IHudMenuKeys)])]
public sealed class TFEurekaMenuUi(ILocalArsenal arsenal, IEngineHud screen, IUiLocalisation localisation,
                                   IFwdEngineClient engine, ITFWeaponOwners owners, IClientStandalones standalones,
                                   IEngineSound sound, TFClientBuildingResources buildings,
                                   ITFPlayerData? players = null, AttributeHooks? hooks = null)
    : ITFEurekaMenuSource, ITFEurekaMenuActions, IClientFrameThink, IOrderedHook, IHudMenuKeys
{
    /// <summary>The frame think's place, past the viewmodel rules' +223 and
    /// clear of the round timer's +190: the chain the client module steps
    /// refuses two members on one order live.</summary>
    public const int Order = 1_000_000 - 1_000 + 230;

    /// <inheritdoc/>
    public int HookOrder => Order;

    /// <summary>Stock's wrench, the weapon whose press arms the menu
    /// (CTFWrench::ItemPostFrame's class).</summary>
    private const string Wrench = "tf_weapon_wrench";

    /// <summary>Stock's denial (SendTeleportMessage's refuse, :202).</summary>
    public const string DenySound = "Player.DenyWeaponSelection";

    private readonly ILocalArsenal _arsenal = arsenal;
    private readonly IEngineHud _screen = screen;
    private readonly IUiLocalisation _localisation = localisation;
    private readonly IFwdEngineClient _engine = engine;
    private readonly ITFWeaponOwners _owners = owners;
    private readonly IClientStandalones _standalones = standalones;
    private readonly IEngineSound _sound = sound;
    private readonly TFClientBuildingResources _buildings = buildings;
    private readonly ITFPlayerData? _players = players;
    private readonly AttributeHooks _hooks = hooks ?? new();

    /// <summary>Stock's m_bWantsToTeleport: laid by the press, spent by the
    /// choice, cleared with the wrench (:103, :189, :351-354).</summary>
    private bool _wants;

    /// <summary>Stock's m_bReloadDown, the press edge's held half (:221-239).</summary>
    private bool _reloadWasDown;

    public bool WantsTeleport => _wants;

    public bool WrenchActive
        => _arsenal.TryGetActive(out string name, out _, out _) && name == Wrench;

    public bool ExitStanding
    {
        get
        {
            if (_buildings.Live?.State is not { } state)
                return false;

            int local = _screen.LocalPlayerIndex;
            if (local <= 0 || local >= TFBuildingResourceNetState.Slots)
                return false;

            int row = local * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Exit;
            byte flags = state.Flags[row];
            return (flags & TFBuildingResourceNetState.Present) != 0
                   && (flags & TFBuildingResourceNetState.Built) != 0
                   && (flags & TFBuildingResourceNetState.Building) == 0
                   && (flags & TFBuildingResourceNetState.Carried) == 0;
        }
    }

    public string CancelLabel => HudHints.Resolve("#TF2_BuildMenu_Cancel",
                                                  t => _localisation.Find(t) ?? t, _screen.LookupBinding);

    /// <summary>The wrench's client ItemPostFrame press edge
    /// (tf_weapon_wrench.cpp:221-239): once a frame, the +reload press while
    /// the eureka is the held wrench and the player is not mid-taunt —
    /// stock's <c>CanAttack()</c> gate's taunting arm (:209) — arms the menu.
    /// The wrench leaving clears the want, stock's ShouldDraw (:101-104).</summary>
    public void ClientThink(float frameTime)
    {
        bool down = WrenchActive
                    && _arsenal.ActiveWeaponState is { } wrench
                    && (wrench.LastButtons & (int)InputButtons.Reload) != 0
                    && EurekaHeld()
                    && !Taunting();

        if (down && !_reloadWasDown)
            _wants = true;
        _reloadWasDown = down;

        // The menu served a wrench that has gone: stock's ShouldDraw clear.
        if (!WrenchActive)
            _wants = false;
    }

    public void Teleport(int target)
    {
        // The spend, first of all (m_bWantsToTeleport = false, :188-189): the
        // menu closes on a refused choice too, stock's own order.
        _wants = false;

        // The available panel's visibility is the send's gate (:200): the
        // exit not standing is the deny, the home always stands.
        if (target != TFEurekaTeleportTargets.Home && !ExitStanding)
        {
            if (_standalones.GetByIndex(_screen.LocalPlayerIndex) is { } player)
                _sound.EntityEmitSound(player.Handle, DenySound);
            return;
        }

        _engine.SubmitConsoleLine($"{TFEurekaTeleportTargets.Command} {target}");
    }

    public void Cancel() => _engine.SubmitConsoleLine(LastInv);

    /// <summary>The menu's own keys, the element half of the client mode's
    /// walk (tf_hud_menu_eureka_teleport.cpp:296-344): slot1 and slot2 send
    /// their choice, slot10 is stock's KEY_0 cancel (:320-325), the middle
    /// slots are eaten while the menu stands, anything else passes through.
    /// Only asked while pressed, and only while the menu draws — the
    /// ShouldDraw gate (:159-181) the visible ask is.</summary>
    public bool Key(ClientKeyBinding binding)
    {
        if (!( _wants && WrenchActive ))
            return false;

        switch (binding)
        {
            case ClientKeyBinding.Slot1:
                Teleport(TFEurekaTeleportTargets.Home);
                return true;
            case ClientKeyBinding.Slot2:
                Teleport(TFEurekaTeleportTargets.TeleporterExit);
                return true;
            case ClientKeyBinding.Slot10:
                Cancel();
                return true;
            case >= ClientKeyBinding.Slot3 and <= ClientKeyBinding.Slot9:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The eureka's attribute on the held item, the arm's
    /// <c>CALL_ATTRIB_HOOK_INT</c> (:224-225) — the active weapon's own view.</summary>
    private bool EurekaHeld()
        => _hooks.Int(0, TFEurekaTeleportTargets.Attribute, _owners.ActiveItemOf(_screen.LocalPlayerIndex)) != 0;

    /// <summary>The taunting arm of stock's CanAttack, off the local player's
    /// predicted words: the taunt is the teleport, its player is not asked.</summary>
    private bool Taunting()
        => _players?.Predicted(_screen.LocalPlayerIndex)?.InCond(TFCond.Taunting) ?? false;

    /// <summary>Stock's cancel and the hint's binding, the literal stock's
    /// own keys run (:292, :324).</summary>
    private const string LastInv = "lastinv";
}

/// <summary>
/// The eureka teleport's numbers, stock's own (<c>eEurekaTeleportTargets</c>,
/// tf_shareddefs.h:1843-1849, and the command they ride): the menu's choices
/// and the adapter's send agree on these.
/// </summary>
public static class TFEurekaTeleportTargets
{
    /// <summary><c>EUREKA_TELEPORT_HOME</c>: the spawn.</summary>
    public const int Home = 0;

    /// <summary><c>EUREKA_TELEPORT_TELEPORTER_EXIT</c>: the builder's exit.</summary>
    public const int TeleporterExit = 1;

    /// <summary>The command the choices send (<c>SendTeleportMessage</c>'s
    /// format, tf_hud_menu_eureka_teleport.cpp:207) — the server's own
    /// (<c>CTFPlayer::ClientCommand</c>, tf_player.cpp:7705).</summary>
    public const string Command = "eureka_teleport";

    /// <summary>The eureka's attribute (<c>alt_fire_teleport_to_spawn</c>,
    /// the arm's CALL_ATTRIB_HOOK_INT, tf_weapon_wrench.cpp:225).</summary>
    public const string Attribute = "alt_fire_teleport_to_spawn";
}
