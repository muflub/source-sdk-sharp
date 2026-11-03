//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Client.Ui;

/// <summary>
/// The crosshair's reads off the weapon in hand and the player's own
/// crosshair convars: <c>ShouldDrawCrosshair</c> (tf_weaponbase.cpp:3638-3648)
/// answers a custom <c>cl_crosshair_file</c> always draws and the empty-file
/// default follows the active weapon's script <c>DrawCrosshair</c> word
/// (tf_weapon_parse.cpp:78) — no weapon, no draw either way; the scale is the
/// weapon's <c>GetWeaponCrosshairScale</c> times the player's
/// <c>cl_crosshair_scale</c>/32 (Paint's <c>flWeaponScale × flPlayerScale</c>,
/// tf_hud_crosshair.cpp:189-216); the material is
/// Paint's <c>vgui/crosshairs/&lt;file&gt;</c> path (:150-154), empty for the
/// default crosshair; and the colour is the three <c>cl_crosshair_*</c>
/// ints (:200, and the base arm's same read at hud_crosshair.cpp:263).
///
/// <para>
/// The scale's weapon arm is <c>GetWeaponCrosshairScale</c>: the base's 1.0
/// for every weapon but the revolver, whose headshot mode
/// (<c>set_weapon_mode</c> 1 on its item, <c>CanHeadshot</c>,
/// <c>tf_weapon_revolver.h:49</c>) blooms the crosshair off the age of
/// <c>m_flLastAccuracyCheck</c> — the clock <c>PrimaryAttack</c> stamps at
/// each shot (tf_weapon_revolver.cpp:157), read
/// <c>RemapValClamped</c> from (1.0, 0.5) to (0.75, 2.5) (:207-223,
/// <see cref="TFRevolverBehaviour.CrosshairScale"/>). The clock rides the
/// carried weapon's generic kept-behaviour clock
/// (<see cref="WeaponNetState.BehaviourKeptTime"/>, named by the behaviour),
/// the replay's working copy while it is fresh — the same carried-state read
/// the scope's charge meter makes (tf_hud_scope.cpp:162-226). The clock's age
/// is taken at <see cref="IGameClock.Now"/>, the lane's stand-in for stock's
/// <c>GetFinalPredictedTime</c> plus the interpolation.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFCrosshair)])]
public sealed class TFHudCrosshair(ILocalArsenal arsenal, TFWeaponScripts scripts,
                                   IEngineHud screen, IClientStandalones standalones,
                                   IWeaponDrawStates states, IGameClock clock,
                                   ITFWeaponOwners owners, AttributeHooks attributes,
                                   ClCrosshairFileConVar file, ClCrosshairScaleConVar scale,
                                   ClCrosshairRedConVar red, ClCrosshairGreenConVar green,
                                   ClCrosshairBlueConVar blue) : ITFCrosshair
{
    // ShouldDrawCrosshair's two arms: a custom file draws whatever the script
    // says (tf_weaponbase.cpp:3646-3647); the empty-file default follows the
    // script's word (:3641-3644, the primary mode's — ShouldDrawCrosshair
    // reads m_WeaponData[TF_WEAPON_PRIMARY_MODE]). No weapon, no draw.
    public bool Draw => arsenal.TryGetActive(out string name, out _, out _)
                        && (file.Value.Length > 0
                            || (scripts.Get(name)?.Primary.DrawCrosshair ?? false));

    // GetWeaponCrosshairScale's arms — the base's 1.0 (basecombatweapon_shared.h:531), the
    // headshot revolver's bloom (tf_weapon_revolver.cpp:207-223) — times
    // flPlayerScale, Paint's cl_crosshair_scale / 32 (tf_hud_crosshair.cpp:201).
    public float Scale => WeaponScale * scale.Value / 32f;

    /// <summary>The hands' weapon's scale: the carried revolver's bloom off
    /// its accuracy clock — the replay's working copy while it is fresh, the
    /// decoded state otherwise — else the base's 1.0 (every other weapon, and
    /// the plain spy revolver whose item carries no headshot mode).</summary>
    private float WeaponScale
        => CarriedRevolver() is { } gun
           && (states.PredictedStateOf(gun) ?? gun.State) is { } state
            ? TFRevolverBehaviour.CrosshairScale(state, clock.Now, CanHeadshot())
            : 1f;

    /// <summary>Stock's <c>CanHeadshot</c> (<c>set_weapon_mode == 1</c> on the
    /// item, tf_weapon_revolver.h:49), answered off the local player's weapon
    /// item — the schema's statics by the carrier's defindex until the
    /// runtime list crosses (deferred.tsv's attributes row).</summary>
    private bool CanHeadshot()
        => arsenal.TryGetActive(out string name, out _, out _)
           && attributes.Int(0, "set_weapon_mode", owners.ItemOf(screen.LocalPlayerIndex, name)) == 1;

    /// <summary>The hands' weapon when it is a revolver: the carried entity is
    /// the local player's (the core holds one weapon per armed player), and
    /// the script's kind is what stock's <c>assert_cast</c> tests.</summary>
    private ClientSsWeapon? CarriedRevolver()
        => screen.LocalPlayerIndex is > 0
           && arsenal.TryGetActive(out string name, out _, out _)
           && scripts.Get(name) is { } script
           && TFWeaponKinds.Of(script) == TFWeaponKind.Revolver
            ? standalones.Live().OfType<ClientSsWeapon>()
                .FirstOrDefault(w => w.State.OwnerIndex == screen.LocalPlayerIndex)
            : null;

    // Paint's material path (tf_hud_crosshair.cpp:153-154), the empty file's
    // no-material arm standing for its BaseClass::Paint branch (:175-178).
    // Built here, not in the screen: the UI assemblies draw without allocating.
    public string Material => file.Value.Length > 0 ? "vgui/crosshairs/" + file.Value : "";

    // The empty-file arm's art (hud_crosshair.cpp:233-281): the held weapon's
    // script crosshair entry, stock's iconCrosshair handed over by
    // ItemPreFrame's SetCrosshair (c_basecombatweapon.cpp:280-284). The file
    // is the script's own path, DrawSetTextureFile's un-prefixed bind
    // (hud.cpp:723); no entry, no read (stock's null m_pCrosshair arm, :235-236).
    public string ScriptFile => ScriptIcon()?.File ?? "";

    public int ScriptX => ScriptIcon()?.X ?? 0;

    public int ScriptY => ScriptIcon()?.Y ?? 0;

    public int ScriptWide => ScriptIcon()?.Wide ?? 0;

    public int ScriptTall => ScriptIcon()?.Tall ?? 0;

    /// <summary>The hands' weapon's script crosshair entry: the active
    /// weapon's name through the script database, its <c>TextureData</c>
    /// <c>crosshair</c> block (<see cref="TFWeaponScript.Crosshair"/>). No
    /// hands, no script, no entry: nothing to draw.</summary>
    private TFHudIcon? ScriptIcon() =>
        arsenal.TryGetActive(out string name, out _, out _) ? scripts.Get(name)?.Crosshair : null;

    // Paint's colour, both arms (tf_hud_crosshair.cpp:200, hud_crosshair.cpp:263).
    public int Red => red.Value;

    public int Green => green.Value;

    public int Blue => blue.Value;
}
