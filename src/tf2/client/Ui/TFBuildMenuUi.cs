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

namespace TF2.Client.Ui;

/// <summary>
/// The engineer's build menu in game (stock's <c>CHudMenuEngyBuild</c>,
/// game/client/tf/tf_hud_menu_engy_build.cpp): the show arm is the build
/// PDA being the active weapon (stock's m_bBuilderEquipped set by the
/// PDA's OnDataChanged, tf_weapon_pda.cpp:157-180, read here as the
/// arsenal's active classname), the Cancel sentence is the menu token
/// with <c>%lastinv%</c> resolved through the engine's binding lookup
/// (stock's SetDialogVariable, :612-619), and a choice is the slot's
/// <c>build %d %d</c> through the console (:475-476) — the gate that
/// refuses built or unaffordable slots is the screen's, stock's own
/// (:472).
///
/// <para>
/// The keys are the hud input layer's (<see cref="IHudMenuKeys"/>, the ask
/// stock's client mode makes of the element, tf_hud_menu_engy_build.cpp:296-
/// 444): while the menu draws, slot1..slot4 send their building
/// (<c>SendBuildMessage</c>, :447-487) and slot10 is stock's KEY_0 cancel
/// (:425-430); a bound slot5..slot9 passes, stock's own answer for it
/// (:397-398) — only the bare keys are eaten, and a press carries the
/// binding's name. The sends ride the same gate the screen's clicks do: the
/// adapter's own answers (a full account, nothing built) leave every slot
/// available, so the deny arm is never reached and the server's gate has
/// the last word.
/// </para>
///
/// <para>
/// The metal account and the per-building reads are the two client
/// reads this port has not landed (no <c>TF_AMMO_METAL</c> seam, no
/// building mirror): the adapter answers a full account and
/// <c>CanBuild</c> yes — every slot draws available, and the reads are
/// rows in docs/plans/tf2/deferred.tsv.
/// </para>
/// </summary>
// The hud walk (IHooks<IHudMenuKeys>) collects its menus by their concrete
// registrations (Services.ResolveAll); the contract itself is registered once,
// the eureka menu's As — a second claim is the container's refused duplicate.
[SideService(Side.Client, As = [typeof(ITFBuildMenuSource), typeof(ITFBuildMenuActions)])]
public sealed class TFBuildMenuUi(ILocalArsenal arsenal, IEngineHud screen, IUiLocalisation localisation,
                                  IFwdEngineClient engine)
    : ITFBuildMenuSource, ITFBuildMenuActions, IHudMenuKeys, IOrderedHook
{
    /// <summary>The hud walk's place, past the eureka menu's +230: the
    /// menus are mutually exclusive, the order only names a tie.</summary>
    public const int Order = 1_000_000 - 1_000 + 231;

    /// <inheritdoc/>
    public int HookOrder => Order;

    /// <summary>Stock's build-PDA classname, the weapon whose activation
    /// the menu shows under.</summary>
    private const string BuildPda = "tf_weapon_pda_engineer_build";

    public bool BuilderEquipped
        => arsenal.TryGetActive(out string name, out _, out _) && name == BuildPda;

    /// <summary>A full account until the metal read lands (the deferred
    /// row): every price draws affordable, as the samples show it.</summary>
    public int Metal => int.MaxValue;

    /// <summary>The menu token with its binding in it — stock's
    /// #Hud_Menu_Build_Cancel through SetDialogVariable("lastinv")
    /// (:612-619). Resolved through the UI's localiser (the mod's own
    /// file holds the token; the engine's tables do not), the binding
    /// through the engine's lookup. Resolved outside the describe
    /// method: the binding changes without the menu rebuilding.</summary>
    public string CancelLabel => HudHints.Resolve("#TF2_BuildMenu_Cancel",
                                                  t => localisation.Find(t) ?? t, screen.LookupBinding);

    /// <summary>The reads the client cannot answer yet: the deferred rows
    /// leave every slot buildable, so stock's gate (:472) refuses
    /// nothing and the server's own does.</summary>
    public int Cost(int building, int mode) => building switch
    {
        2 => 125,   // OBJ_SENTRYGUN
        0 => 100,   // OBJ_DISPENSER
        _ => 50,    // OBJ_TELEPORTER, either end
    };

    public bool CanBuild(int building, int mode) => true;

    public bool Built(int building, int mode) => false;

    /// <summary>Stock's <c>SendBuildMessage</c> send arm: the OBJ_* pair
    /// out as <c>build %d %d</c> (:474-476). The screen only calls this
    /// for an available slot; the server's own gate has the last word.</summary>
    public void Build(int building, int mode)
        => engine.SubmitConsoleLine($"build {building} {mode}");

    /// <summary>Stock's cancel keys: lastinv (:425-430, :389-394).</summary>
    public void Cancel() => engine.SubmitConsoleLine("lastinv");

    /// <inheritdoc/>
    /// <summary>The menu's keys while it draws — the build PDA held. The
    /// screen's availability gate is the adapter's own answers, all
    /// affirmative, so the send is stock's unconditional
    /// <c>SendBuildMessage</c> arm (:437-441).</summary>
    public bool Key(ClientKeyBinding binding)
    {
        if (!BuilderEquipped)
            return false;

        switch (binding)
        {
            case ClientKeyBinding.Slot1:
            case ClientKeyBinding.Slot2:
            case ClientKeyBinding.Slot3:
            case ClientKeyBinding.Slot4:
                SendSlot(binding - ClientKeyBinding.Slot1 + 1);
                return true;
            case ClientKeyBinding.Slot10:
                Cancel();
                return true;
            default:
                return false;   // stock's slot5..9 answer: the engine runs the binding (:397-398)
        }
    }

    private void SendSlot(int slot)
    {
        var (building, mode) = SlotChoice(slot);
        Build(building, mode);
    }

    /// <summary>Stock's <c>g_kEngyBuildings</c> (tf_hud_menu_engy_build.cpp:35-84):
    /// the OBJ_*/mode pair each 1-based slot sends — the screen's own table,
    /// which lives across the UI assembly line (<see cref="TFBuildMenu"/> keeps
    /// the drawn twin).</summary>
    internal static (int Building, int Mode) SlotChoice(int slot) => slot switch
    {
        1 => (2, 0),  // OBJ_SENTRYGUN, no mode — tf_shareddefs.h:1368-1370
        2 => (0, 0),  // OBJ_DISPENSER
        3 => (1, 0),  // OBJ_TELEPORTER, MODE_TELEPORTER_ENTRANCE — tf_shareddefs.h:1389-1390
        _ => (1, 1),  // OBJ_TELEPORTER, MODE_TELEPORTER_EXIT
    };
}
