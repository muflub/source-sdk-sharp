//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Buildings;
using TF2.Shared.Buildings;

namespace TF2.Client.Ui;

/// <summary>
/// The engineer's destroy menu in game (stock's
/// <c>CHudMenuEngyDestroy</c>, game/client/tf/tf_hud_menu_engy_destroy.cpp):
/// the show arm is the destroy PDA being the active weapon (the build
/// PDA's m_bBuilderEquipped pair, tf_weapon_pda.cpp:157-180, read here
/// as the arsenal's active classname), the Cancel sentence is the build
/// menu's token — stock's own CancelLabel names
/// <c>#Hud_Menu_Build_Cancel</c> — with <c>%lastinv%</c> resolved
/// through the engine's binding lookup (SetDialogVariable, :360-367),
/// and a choice is the slot's <c>destroy %d %d; lastinv</c> through the
/// console (:259-266, :264).
///
/// <para>
/// Whether a building stands is the building mirror's word (the
/// <c>tf_building_resource</c> the client decodes, stock's
/// <c>GetObjectOfType</c> read, :259): placed is the gate — stock's sapper
/// and plasma refinements ride the same rows' further bits and the
/// server's own destroy gate has the last word. Nothing arrived yet, no
/// slot is built, as the samples' empty world is.
/// </para>
///
/// <para>
/// The keys are the hud input layer's (<see cref="IHudMenuKeys"/>, the ask
/// stock's client mode makes of the element, tf_hud_menu_engy_destroy.cpp:
/// 158-275): while the menu draws, slot1..slot4 destroy their building —
/// or, nothing standing, stock's error sound (:269, :277-285) — and
/// slot10 is stock's KEY_0 cancel (:243-247); a bound slot5..slot9 passes,
/// stock's own answer for it (:206-207).
/// </para>
/// </summary>
// The hud walk (IHooks<IHudMenuKeys>) collects its menus by their concrete
// registrations (Services.ResolveAll); the contract itself is registered once,
// the eureka menu's As — a second claim is the container's refused duplicate.
[SideService(Side.Client, As = [typeof(ITFDestroyMenuSource), typeof(ITFDestroyMenuActions)])]
public sealed class TFDestroyMenuUi(ILocalArsenal arsenal, IEngineHud screen, IUiLocalisation localisation,
                                    IFwdEngineClient engine, TFClientBuildingResources buildings,
                                    IClientStandalones standalones, IEngineSound sound)
    : ITFDestroyMenuSource, ITFDestroyMenuActions, IHudMenuKeys, IOrderedHook
{
    /// <summary>The hud walk's place, past the build menu's +231.</summary>
    public const int Order = 1_000_000 - 1_000 + 232;

    /// <inheritdoc/>
    public int HookOrder => Order;

    /// <summary>Stock's denial (ErrorSound, :277-285).</summary>
    public const string DenySound = "Player.DenyWeaponSelection";

    private readonly TFClientBuildingResources _buildings = buildings;
    private readonly IClientStandalones _standalones = standalones;
    private readonly IEngineSound _sound = sound;

    /// <summary>Stock's destroy-PDA classname, the weapon whose
    /// activation the menu shows under.</summary>
    private const string DestroyPda = "tf_weapon_pda_engineer_destroy";

    public bool DestroyerEquipped
        => arsenal.TryGetActive(out string name, out _, out _) && name == DestroyPda;

    /// <summary>The build menu's token with its binding in it — stock's
    /// #Hud_Menu_Build_Cancel through SetDialogVariable("lastinv")
    /// (:360-367). Resolved through the UI's localiser (the mod's own
    /// file holds the token), the binding through the engine's lookup.</summary>
    public string CancelLabel => HudHints.Resolve("#TF2_BuildMenu_Cancel",
                                                  t => localisation.Find(t) ?? t, screen.LookupBinding);

    /// <summary>The mirror's placed word for the slot's building, stock's
    /// <c>GetObjectOfType</c> arm (:259): the local row carries the kind
    /// the pair names. Nothing arrived, nothing stands.</summary>
    public bool Built(int building, int mode)
    {
        if (_buildings.Live?.State is not { } state)
            return false;

        int local = screen.LocalPlayerIndex;
        if (local <= 0 || local >= TFBuildingResourceNetState.Slots)
            return false;

        int kind = building switch
        {
            2 => TFBuildingResourceNetState.Sentry,      // OBJ_SENTRYGUN
            0 => TFBuildingResourceNetState.Dispenser,   // OBJ_DISPENSER
            _ => mode == 0 ? TFBuildingResourceNetState.Entrance : TFBuildingResourceNetState.Exit,
        };

        return (state.Flags[local * TFBuildingResourceNetState.Kinds + kind]
                & TFBuildingResourceNetState.Present) != 0;
    }

    /// <summary>Stock's send arm: the OBJ_* pair out as
    /// <c>destroy %d %d; lastinv</c> (:259-266, :264). The screen only
    /// calls this for a built slot; the server's own gate has the last
    /// word.</summary>
    public void Destroy(int building, int mode)
        => engine.SubmitConsoleLine($"destroy {building} {mode}; lastinv");

    /// <summary>Stock's cancel: lastinv (the build menu's own keys).</summary>
    public void Cancel() => engine.SubmitConsoleLine("lastinv");

    /// <summary>Stock's refused choice, the error sound (:267-270).</summary>
    private void Deny()
    {
        if (_standalones.GetByIndex(screen.LocalPlayerIndex) is { } player)
            _sound.EntityEmitSound(player.Handle, DenySound);
    }

    /// <inheritdoc/>
    /// <summary>The menu's keys while it draws — the destroy PDA held.</summary>
    public bool Key(ClientKeyBinding binding)
    {
        if (!DestroyerEquipped)
            return false;

        switch (binding)
        {
            case ClientKeyBinding.Slot1:
            case ClientKeyBinding.Slot2:
            case ClientKeyBinding.Slot3:
            case ClientKeyBinding.Slot4:
                int slot = binding - ClientKeyBinding.Slot1 + 1;
                var (building, mode) = TFBuildMenuUi.SlotChoice(slot);
                if (Built(building, mode))
                    Destroy(building, mode);
                else
                    Deny();
                return true;
            case ClientKeyBinding.Slot10:
                Cancel();
                return true;
            default:
                return false;   // stock's slot5..9 answer: the engine runs the binding (:206-207)
        }
    }
}
