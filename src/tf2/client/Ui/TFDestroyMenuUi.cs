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
/// Whether a building stands is a client read this port has not landed
/// (no building mirror): the adapter answers Built no — every slot
/// draws not-built, as the samples show it — and the read is a row in
/// docs/plans/tf2/deferred.tsv. Stock's sapper and plasma-disabled gate
/// rides the same mirror; the server's own destroy gate has the last
/// word meanwhile.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFDestroyMenuSource), typeof(ITFDestroyMenuActions)])]
public sealed class TFDestroyMenuUi(ILocalArsenal arsenal, IEngineHud screen, IUiLocalisation localisation,
                                    IFwdEngineClient engine) : ITFDestroyMenuSource, ITFDestroyMenuActions
{
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

    /// <summary>The read the client cannot answer yet: the deferred row
    /// leaves every slot not-built, so the screen's gate refuses every
    /// send and the menu only shows the state.</summary>
    public bool Built(int building, int mode) => false;

    /// <summary>Stock's send arm: the OBJ_* pair out as
    /// <c>destroy %d %d; lastinv</c> (:259-266, :264). The screen only
    /// calls this for a built slot; the server's own gate has the last
    /// word.</summary>
    public void Destroy(int building, int mode)
        => engine.SubmitConsoleLine($"destroy {building} {mode}; lastinv");

    /// <summary>Stock's cancel: lastinv (the build menu's own keys).</summary>
    public void Cancel() => engine.SubmitConsoleLine("lastinv");
}
