//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

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
/// </summary>
[SideService(Side.Client, As = [typeof(ITFDisguiseMenuSource), typeof(ITFDisguiseMenuActions)])]
public sealed class TFDisguiseMenuUi(ILocalArsenal arsenal, IEngineHud screen, IUiLocalisation localisation,
                                     IFwdEngineClient engine, ITFLocalPlayer player)
    : ITFDisguiseMenuSource, ITFDisguiseMenuActions
{
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

    /// <summary>Stock's SelectDisguise send arm: the class and the shown
    /// team out as <c>disguise &lt;class&gt; &lt;team&gt;</c> — the
    /// screen passes the interception's -1/-2 naming
    /// (:543-564, c_tf_player.cpp:9637-9679).</summary>
    public void Disguise(int classNumber, int team)
        => engine.SubmitConsoleLine($"disguise {classNumber} {team}");

    /// <summary>Stock's cancel: lastinv (:294-298, :395-398).</summary>
    public void Cancel() => engine.SubmitConsoleLine("lastinv");
}
