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

using TF2.Shared;

namespace TF2.Client.Ui;

/// <summary>
/// The spy's disguise menu in game (stock's CHudMenuSpyDisguise,
/// game/client/tf/tf_hud_menu_spy_disguise.cpp): the show arm is the
/// disguise kit being the active weapon (the build PDA's
/// m_bBuilderEquipped pair, tf_weapon_pda.cpp:157-180, read here as the
/// arsenal's active classname), the shown team arms from the local
/// player's team (the spy_pda_reset event's arm, :607-629, read here
/// off the player manager's row), the hint sentences are the menu
/// tokens with their bindings resolved through the engine's lookup
/// (FindToggleBinding, :453-476), and a choice is the class and the
/// shown team out as <c>disguise &lt;class&gt; &lt;team&gt;</c> through
/// the console (stock's SelectDisguise retokenised, :543-564 — the
/// interception's -1/-2 naming TFSpyDisguiseCommand documents).
///
/// <para>
/// The keys are the hud input layer's (<see cref="IHudMenuKeys"/>, the
/// ask stock's client mode makes of the element, :184-436): while the
/// menu draws, slot1..slot9 disguise as the tile's class
/// (<c>iRemapKeyToClass</c>, :197-208 — the menu's order is not the
/// defines') and slot10 is stock's lastinv cancel (:294-298); the
/// disguiseteam binding toggles the shown team (:308-312, over
/// ToggleDisguiseTeam :569-582). A redeploy re-arms the enemy team, the
/// spy_pda_reset arm (:613-624), here the think's equipment edge.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFDisguiseMenuSource), typeof(ITFDisguiseMenuActions)])]
public sealed class TFDisguiseMenuUi(ILocalArsenal arsenal, IEngineHud screen, IUiLocalisation localisation,
                                     IFwdEngineClient engine, ITFLocalPlayer player)
    : ITFDisguiseMenuSource, ITFDisguiseMenuActions, IHudMenuKeys, IClientFrameThink, IOrderedHook
{
    /// <summary>The hud walk's place, past the destroy menu's +232: the
    /// menus are mutually exclusive, the order only names a tie.</summary>
    public const int Order = 1_000_000 - 1_000 + 233;

    /// <inheritdoc/>
    public int HookOrder => Order;

    // Stock's m_iShowingTeam as the toggle: false shows the enemy team,
    // true the player's own (ToggleDisguiseTeam, :569-582). One word,
    // the draw and the key arm read it both.
    private bool _toggled;

    private bool _wasEquipped;

    /// <inheritdoc/>
    public bool Toggled => _toggled;

    /// <summary>The team the tiles show, the player's own once toggled
    /// (:569-582); no team yet and stock's constructor default stands
    /// (the screen's rule, :73).</summary>
    private int ShownTeam
    {
        get
        {
            int team = Team is 2 or 3 ? Team : 2;
            return _toggled ? team : (team == 2 ? 3 : 2);
        }
    }

    /// <summary>Stock's disguise-kit classname, the weapon whose
    /// activation the menu shows under.</summary>
    private const string DisguiseKit = "tf_weapon_pda_spy";

    public bool DisguiseKitEquipped
        => arsenal.TryGetActive(out string name, out _, out _) && name == DisguiseKit;

    public int Team => player.Team;

    /// <summary>The toggle sentence — the token with both bindings in it
    /// (stock's SetDialogVariable("disguiseteam")/("reload"),
    /// :463-475). Resolved through the UI's localiser (the mod's own
    /// file holds the token), the bindings through the engine's
    /// lookup.</summary>
    public string ToggleLabel => HudHints.Resolve("#TF2_DisguiseMenu_Toggle",
                                                  t => localisation.Find(t) ?? t, screen.LookupBinding);

    /// <summary>The build menu's token with its binding in it — stock's
    /// CancelLabel names #Hud_Menu_Build_Cancel
    /// (hudmenuspydisguise.res:122).</summary>
    public string CancelLabel => HudHints.Resolve("#TF2_BuildMenu_Cancel",
                                                  t => localisation.Find(t) ?? t, screen.LookupBinding);

    /// <summary>A redeploy re-arms the enemy team (the spy_pda_reset
    /// event, :613-624): the toggle lives for one kit deployment, the
    /// equipment edge its arm.</summary>
    public void ClientThink(float frameTime)
    {
        bool equipped = DisguiseKitEquipped;
        if (equipped && !_wasEquipped)
            _toggled = false;
        _wasEquipped = equipped;
    }

    /// <summary>The toggle arm (stock's disguiseteam key, :308-312, over
    /// ToggleDisguiseTeam :569-582): the shown team flips, and the
    /// draw follows the same word.</summary>
    public void ToggleTeam() => _toggled = !_toggled;

    /// <summary>Stock's SelectDisguise send arm: the class and the shown
    /// team out as <c>disguise &lt;class&gt; &lt;team&gt;</c> — the
    /// screen passes the interception's -1/-2 naming
    /// (:543-564, c_tf_player.cpp:9637-9679).</summary>
    public void Disguise(int classNumber, int team)
        => engine.SubmitConsoleLine($"disguise {classNumber} {team}");

    /// <summary>Stock's cancel: lastinv (:294-298, :395-398).</summary>
    public void Cancel() => engine.SubmitConsoleLine("lastinv");

    /// <inheritdoc/>
    /// <summary>The menu's keys while it draws — the disguise kit held
    /// (:184-436): a slot disguises its tile's class as shown, the
    /// disguise-team binding toggles, the tenth cancels, anything else
    /// is the engine's.</summary>
    public bool Key(ClientKeyBinding binding)
    {
        if (!DisguiseKitEquipped)
            return false;

        switch (binding)
        {
            case ClientKeyBinding.Slot1:
            case ClientKeyBinding.Slot2:
            case ClientKeyBinding.Slot3:
            case ClientKeyBinding.Slot4:
            case ClientKeyBinding.Slot5:
            case ClientKeyBinding.Slot6:
            case ClientKeyBinding.Slot7:
            case ClientKeyBinding.Slot8:
            case ClientKeyBinding.Slot9:
                int slot = binding - ClientKeyBinding.Slot1;   // the table's 0-based index
                Disguise(RemapClass(slot), ShownTeam == Team ? -2 : -1);
                return true;
            case ClientKeyBinding.DisguiseTeam:
                ToggleTeam();
                return true;
            case ClientKeyBinding.Slot10:
                Cancel();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Stock's <c>iRemapKeyToClass</c> (tf_hud_menu_spy_disguise.cpp:197-208):
    /// the TF_CLASS number each 0-based menu position disguises as —
    /// the menu's order is not the defines' (the table's own remark).
    /// The screen's click path carries the same table, spelled there
    /// because that assembly holds no TF2.Shared reference.</summary>
    internal static int RemapClass(int slot) => slot switch
    {
        0 => (int)TFClass.Scout,
        1 => (int)TFClass.Soldier,
        2 => (int)TFClass.Pyro,
        3 => (int)TFClass.Demoman,
        4 => (int)TFClass.HeavyWeapons,
        5 => (int)TFClass.Engineer,
        6 => (int)TFClass.Medic,
        7 => (int)TFClass.Sniper,
        _ => (int)TFClass.Spy,
    };
}
