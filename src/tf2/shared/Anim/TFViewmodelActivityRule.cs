//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared.Anim;

/// <summary>
/// Stock's <c>CTFWeaponBase::TranslateViewmodelHandActivityInternal</c>
/// (tf_weaponbase.cpp:4513) as a <see cref="IViewmodelActivityRule"/>: the
/// machine's base viewmodel activity rewritten to the weapon role's own
/// before the model is asked.
///
/// <para>
/// Stock's FIRST arm is the equipped item's own rename
/// (<c>GetActivityOverride</c> over the schema's
/// <c>animation_replacement</c> rows, :4515-4521, econ_item_schema.cpp:3600):
/// the heavy's fists rename the idle to <c>ACT_FISTS_VM_IDLE</c> before any
/// role table, and the arms model tags no <c>ACT_MELEE_*</c> at all. It
/// answers through the base item the weapon name wears
/// (<see cref="TFWeaponTypes.BaseItemFor"/>, the same read the hands rule
/// makes); a weapon with no item, or one without a row for the asked name,
/// falls to the role table — stock's early-return arm.
/// </para>
///
/// <para>
/// The role is <c>GetViewModelWeaponRole</c>'s base arm
/// (<c>GetTFWpnData().m_iWeaponType</c>, tf_weaponbase.cpp:4409) with the
/// econ item's <c>GetAnimationSlot</c> row over it — the seam's role is
/// <c>GetActivityWeaponRole</c> (tf_weaponbase.cpp:4185-4205, via
/// <see cref="TFWeaponTypes.ActivityWeaponRole"/>), whose answer is the one
/// this arm's own re-derivation (:4523-4531) lands on. Stock's remaining
/// arm, the per-class overrides (the sword, the buff item), is not here yet.
/// A player with no weapon the scripts name answers no role, and the base
/// name stands — stock's table-miss arm (<c>return actBase</c>, :4541).
/// </para>
///
/// <para>
/// The rewrite itself is <c>s_viewmodelacttable</c>
/// (tf_weaponbase.cpp:4292-4508), one row per (base activity, weapon role),
/// in <see cref="TFViewmodelActTables"/>. The rows the table keeps
/// identical (the scout's special-state and the spy's backstab sets) and the
/// player-body <c>ACT_MP_*</c> rows (the anim layer's tables, not viewmodel
/// asks) are left out: an identity row and no opinion are the same answer.
/// </para>
///
/// <para>
/// Registered on BOTH sides: the sequence number is predicted state, and
/// both sides must rewrite the same ask to the same name.
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFViewmodelActivityRule(ITFAnimWeapons? weapons = null, TFWeaponTypes? types = null)
    : IViewmodelActivityRule, IOrderedHook
{
    /// <summary>The rule's order: the weapons band, beside the weapon hooks (see the lane notes' bands).</summary>
    public const int Order = 1_000_000 - 1_000 + 220;

    private readonly ITFAnimWeapons _weapons = weapons ?? new NoTFAnimWeapons();
    private readonly TFWeaponTypes? _types = types;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public string Translate(int playerIndex, WeaponAnim anim, string activity)
    {
        if (_weapons.ActiveWeapon(playerIndex) is not { } weapon)
            return activity;

        // Stock's item arm first: the schema's animation_replacement, which
        // is an answer, not a fall-through (tf_weaponbase.cpp:4518-4520).
        if (_types?.BaseItemFor(TFWeaponNames.Name(weapon.Id).ToLowerInvariant())
                   ?.ActivityOverride(activity) is { Length: > 0 } replacement)
            return replacement;

        return TFViewmodelActTables.Translate(activity, weapon.Role) ?? activity;
    }
}

/// <summary>Stock's no-weapon arm: no role, no rewrite.</summary>
internal sealed class NoTFAnimWeapons : ITFAnimWeapons
{
    public TFAnimWeapon? ActiveWeapon(int playerIndex) => null;
    public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
}

/// <summary>
/// The viewmodel act table, <c>s_viewmodelacttable</c>
/// (tf_weaponbase.cpp:4292-4508): which per-role activity name a base one
/// becomes. Null for a pair the table does not name — the base stands.
/// </summary>
public static class TFViewmodelActTables
{
    /// <summary>
    /// The table's flat scan, as stock's loop is: one row per (base, role),
    /// first match wins. The names are stock's, spelled as the models tag
    /// them.
    /// </summary>
    public static string? Translate(string activity, TFWeaponRole role) => (role, activity) switch
    {
        //-----------------------------------------------------------------
        // TF_WPN_TYPE_PRIMARY (tf_weaponbase.cpp:4294-4306).
        //-----------------------------------------------------------------
        (TFWeaponRole.Primary, "ACT_VM_DRAW") => "ACT_PRIMARY_VM_DRAW",
        (TFWeaponRole.Primary, "ACT_VM_HOLSTER") => "ACT_PRIMARY_VM_HOLSTER",
        (TFWeaponRole.Primary, "ACT_VM_IDLE") => "ACT_PRIMARY_VM_IDLE",
        (TFWeaponRole.Primary, "ACT_VM_PULLBACK") => "ACT_PRIMARY_VM_PULLBACK",
        (TFWeaponRole.Primary, "ACT_VM_PRIMARYATTACK") => "ACT_PRIMARY_VM_PRIMARYATTACK",
        (TFWeaponRole.Primary, "ACT_VM_SECONDARYATTACK") => "ACT_PRIMARY_VM_SECONDARYATTACK",
        (TFWeaponRole.Primary, "ACT_VM_RELOAD") => "ACT_PRIMARY_VM_RELOAD",
        (TFWeaponRole.Primary, "ACT_RELOAD_START") => "ACT_PRIMARY_RELOAD_START",
        (TFWeaponRole.Primary, "ACT_RELOAD_FINISH") => "ACT_PRIMARY_RELOAD_FINISH",
        (TFWeaponRole.Primary, "ACT_VM_DRYFIRE") => "ACT_PRIMARY_VM_DRYFIRE",
        (TFWeaponRole.Primary, "ACT_VM_IDLE_TO_LOWERED") => "ACT_PRIMARY_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Primary, "ACT_VM_IDLE_LOWERED") => "ACT_PRIMARY_VM_IDLE_LOWERED",
        (TFWeaponRole.Primary, "ACT_VM_LOWERED_TO_IDLE") => "ACT_PRIMARY_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_STAND_PREFIRE") => "ACT_PRIMARY_ATTACK_STAND_PREFIRE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_STAND_POSTFIRE") => "ACT_PRIMARY_ATTACK_STAND_POSTFIRE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_STAND_STARTFIRE") => "ACT_PRIMARY_ATTACK_STAND_STARTFIRE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_CROUCH_PREFIRE") => "ACT_PRIMARY_ATTACK_CROUCH_PREFIRE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_CROUCH_POSTFIRE") => "ACT_PRIMARY_ATTACK_CROUCH_POSTFIRE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_SWIM_PREFIRE") => "ACT_PRIMARY_ATTACK_SWIM_PREFIRE",
        (TFWeaponRole.Primary, "ACT_MP_ATTACK_SWIM_POSTFIRE") => "ACT_PRIMARY_ATTACK_SWIM_POSTFIRE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_SECONDARY (:4315-4327).
        //-----------------------------------------------------------------
        (TFWeaponRole.Secondary, "ACT_VM_DRAW") => "ACT_SECONDARY_VM_DRAW",
        (TFWeaponRole.Secondary, "ACT_VM_HOLSTER") => "ACT_SECONDARY_VM_HOLSTER",
        (TFWeaponRole.Secondary, "ACT_VM_IDLE") => "ACT_SECONDARY_VM_IDLE",
        (TFWeaponRole.Secondary, "ACT_VM_PULLBACK") => "ACT_SECONDARY_VM_PULLBACK",
        (TFWeaponRole.Secondary, "ACT_VM_PRIMARYATTACK") => "ACT_SECONDARY_VM_PRIMARYATTACK",
        (TFWeaponRole.Secondary, "ACT_VM_SECONDARYATTACK") => "ACT_SECONDARY_VM_SECONDARYATTACK",
        (TFWeaponRole.Secondary, "ACT_VM_RELOAD") => "ACT_SECONDARY_VM_RELOAD",
        (TFWeaponRole.Secondary, "ACT_RELOAD_START") => "ACT_SECONDARY_RELOAD_START",
        (TFWeaponRole.Secondary, "ACT_RELOAD_FINISH") => "ACT_SECONDARY_RELOAD_FINISH",
        (TFWeaponRole.Secondary, "ACT_VM_DRYFIRE") => "ACT_SECONDARY_VM_DRYFIRE",
        (TFWeaponRole.Secondary, "ACT_VM_IDLE_TO_LOWERED") => "ACT_SECONDARY_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Secondary, "ACT_VM_IDLE_LOWERED") => "ACT_SECONDARY_VM_IDLE_LOWERED",
        (TFWeaponRole.Secondary, "ACT_VM_LOWERED_TO_IDLE") => "ACT_SECONDARY_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_STAND_PREFIRE") => "ACT_SECONDARY_ATTACK_STAND_PREFIRE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_STAND_POSTFIRE") => "ACT_SECONDARY_ATTACK_STAND_POSTFIRE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_STAND_STARTFIRE") => "ACT_SECONDARY_ATTACK_STAND_STARTFIRE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_CROUCH_PREFIRE") => "ACT_SECONDARY_ATTACK_CROUCH_PREFIRE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_CROUCH_POSTFIRE") => "ACT_SECONDARY_ATTACK_CROUCH_POSTFIRE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_SWIM_PREFIRE") => "ACT_SECONDARY_ATTACK_SWIM_PREFIRE",
        (TFWeaponRole.Secondary, "ACT_MP_ATTACK_SWIM_POSTFIRE") => "ACT_SECONDARY_ATTACK_SWIM_POSTFIRE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_MELEE (:4336-4348).
        //-----------------------------------------------------------------
        (TFWeaponRole.Melee, "ACT_VM_DRAW") => "ACT_MELEE_VM_DRAW",
        (TFWeaponRole.Melee, "ACT_VM_HOLSTER") => "ACT_MELEE_VM_HOLSTER",
        (TFWeaponRole.Melee, "ACT_VM_IDLE") => "ACT_MELEE_VM_IDLE",
        (TFWeaponRole.Melee, "ACT_VM_PULLBACK") => "ACT_MELEE_VM_PULLBACK",
        (TFWeaponRole.Melee, "ACT_VM_PRIMARYATTACK") => "ACT_MELEE_VM_PRIMARYATTACK",
        (TFWeaponRole.Melee, "ACT_VM_SECONDARYATTACK") => "ACT_MELEE_VM_SECONDARYATTACK",
        (TFWeaponRole.Melee, "ACT_VM_RELOAD") => "ACT_MELEE_VM_RELOAD",
        (TFWeaponRole.Melee, "ACT_VM_DRYFIRE") => "ACT_MELEE_VM_DRYFIRE",
        (TFWeaponRole.Melee, "ACT_VM_IDLE_TO_LOWERED") => "ACT_MELEE_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Melee, "ACT_VM_IDLE_LOWERED") => "ACT_MELEE_VM_IDLE_LOWERED",
        (TFWeaponRole.Melee, "ACT_VM_LOWERED_TO_IDLE") => "ACT_MELEE_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.Melee, "ACT_VM_HITCENTER") => "ACT_MELEE_VM_HITCENTER",
        (TFWeaponRole.Melee, "ACT_VM_SWINGHARD") => "ACT_MELEE_VM_SWINGHARD",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_STAND_PREFIRE") => "ACT_MELEE_ATTACK_STAND_PREFIRE",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_STAND_POSTFIRE") => "ACT_MELEE_ATTACK_STAND_POSTFIRE",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_STAND_STARTFIRE") => "ACT_MELEE_ATTACK_STAND_STARTFIRE",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_CROUCH_PREFIRE") => "ACT_MELEE_ATTACK_CROUCH_PREFIRE",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_CROUCH_POSTFIRE") => "ACT_MELEE_ATTACK_CROUCH_POSTFIRE",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_SWIM_PREFIRE") => "ACT_MELEE_ATTACK_SWIM_PREFIRE",
        (TFWeaponRole.Melee, "ACT_MP_ATTACK_SWIM_POSTFIRE") => "ACT_MELEE_ATTACK_SWIM_POSTFIRE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_PDA (:4375-4385).
        //-----------------------------------------------------------------
        (TFWeaponRole.Pda, "ACT_VM_DRAW") => "ACT_PDA_VM_DRAW",
        (TFWeaponRole.Pda, "ACT_VM_HOLSTER") => "ACT_PDA_VM_HOLSTER",
        (TFWeaponRole.Pda, "ACT_VM_IDLE") => "ACT_PDA_VM_IDLE",
        (TFWeaponRole.Pda, "ACT_VM_PULLBACK") => "ACT_PDA_VM_PULLBACK",
        (TFWeaponRole.Pda, "ACT_VM_PRIMARYATTACK") => "ACT_PDA_VM_PRIMARYATTACK",
        (TFWeaponRole.Pda, "ACT_VM_SECONDARYATTACK") => "ACT_PDA_VM_SECONDARYATTACK",
        (TFWeaponRole.Pda, "ACT_VM_RELOAD") => "ACT_PDA_VM_RELOAD",
        (TFWeaponRole.Pda, "ACT_VM_DRYFIRE") => "ACT_PDA_VM_DRYFIRE",
        (TFWeaponRole.Pda, "ACT_VM_IDLE_TO_LOWERED") => "ACT_PDA_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Pda, "ACT_VM_IDLE_LOWERED") => "ACT_PDA_VM_IDLE_LOWERED",
        (TFWeaponRole.Pda, "ACT_VM_LOWERED_TO_IDLE") => "ACT_PDA_VM_LOWERED_TO_IDLE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_ITEM1 (:4388-4400).
        //-----------------------------------------------------------------
        (TFWeaponRole.Item1, "ACT_VM_DRAW") => "ACT_ITEM1_VM_DRAW",
        (TFWeaponRole.Item1, "ACT_VM_HOLSTER") => "ACT_ITEM1_VM_HOLSTER",
        (TFWeaponRole.Item1, "ACT_VM_IDLE") => "ACT_ITEM1_VM_IDLE",
        (TFWeaponRole.Item1, "ACT_VM_PULLBACK") => "ACT_ITEM1_VM_PULLBACK",
        (TFWeaponRole.Item1, "ACT_VM_PRIMARYATTACK") => "ACT_ITEM1_VM_PRIMARYATTACK",
        (TFWeaponRole.Item1, "ACT_VM_SECONDARYATTACK") => "ACT_ITEM1_VM_SECONDARYATTACK",
        (TFWeaponRole.Item1, "ACT_VM_RELOAD") => "ACT_ITEM1_VM_RELOAD",
        (TFWeaponRole.Item1, "ACT_RELOAD_START") => "ACT_ITEM1_RELOAD_START",
        (TFWeaponRole.Item1, "ACT_RELOAD_FINISH") => "ACT_ITEM1_RELOAD_FINISH",
        (TFWeaponRole.Item1, "ACT_VM_DRYFIRE") => "ACT_ITEM1_VM_DRYFIRE",
        (TFWeaponRole.Item1, "ACT_VM_IDLE_TO_LOWERED") => "ACT_ITEM1_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Item1, "ACT_VM_IDLE_LOWERED") => "ACT_ITEM1_VM_IDLE_LOWERED",
        (TFWeaponRole.Item1, "ACT_VM_LOWERED_TO_IDLE") => "ACT_ITEM1_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_STAND_PREFIRE") => "ACT_ITEM1_ATTACK_STAND_PREFIRE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_STAND_POSTFIRE") => "ACT_ITEM1_ATTACK_STAND_POSTFIRE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_STAND_STARTFIRE") => "ACT_ITEM1_ATTACK_STAND_STARTFIRE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_CROUCH_PREFIRE") => "ACT_ITEM1_ATTACK_CROUCH_PREFIRE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_CROUCH_POSTFIRE") => "ACT_ITEM1_ATTACK_CROUCH_POSTFIRE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_SWIM_PREFIRE") => "ACT_ITEM1_ATTACK_SWIM_PREFIRE",
        (TFWeaponRole.Item1, "ACT_MP_ATTACK_SWIM_POSTFIRE") => "ACT_ITEM1_ATTACK_SWIM_POSTFIRE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_ITEM2 (:4410-4422).
        //-----------------------------------------------------------------
        (TFWeaponRole.Item2, "ACT_VM_DRAW") => "ACT_ITEM2_VM_DRAW",
        (TFWeaponRole.Item2, "ACT_VM_HOLSTER") => "ACT_ITEM2_VM_HOLSTER",
        (TFWeaponRole.Item2, "ACT_VM_IDLE") => "ACT_ITEM2_VM_IDLE",
        (TFWeaponRole.Item2, "ACT_VM_PULLBACK") => "ACT_ITEM2_VM_PULLBACK",
        (TFWeaponRole.Item2, "ACT_VM_PRIMARYATTACK") => "ACT_ITEM2_VM_PRIMARYATTACK",
        (TFWeaponRole.Item2, "ACT_VM_SECONDARYATTACK") => "ACT_ITEM2_VM_SECONDARYATTACK",
        (TFWeaponRole.Item2, "ACT_VM_RELOAD") => "ACT_ITEM2_VM_RELOAD",
        (TFWeaponRole.Item2, "ACT_RELOAD_START") => "ACT_ITEM2_RELOAD_START",
        (TFWeaponRole.Item2, "ACT_RELOAD_FINISH") => "ACT_ITEM2_RELOAD_FINISH",
        (TFWeaponRole.Item2, "ACT_VM_DRYFIRE") => "ACT_ITEM2_VM_DRYFIRE",
        (TFWeaponRole.Item2, "ACT_VM_IDLE_TO_LOWERED") => "ACT_ITEM2_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Item2, "ACT_VM_IDLE_LOWERED") => "ACT_ITEM2_VM_IDLE_LOWERED",
        (TFWeaponRole.Item2, "ACT_VM_LOWERED_TO_IDLE") => "ACT_ITEM2_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_STAND_PREFIRE") => "ACT_ITEM2_ATTACK_STAND_PREFIRE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_STAND_POSTFIRE") => "ACT_ITEM2_ATTACK_STAND_POSTFIRE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_STAND_STARTFIRE") => "ACT_ITEM2_ATTACK_STAND_STARTFIRE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_CROUCH_PREFIRE") => "ACT_ITEM2_ATTACK_CROUCH_PREFIRE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_CROUCH_POSTFIRE") => "ACT_ITEM2_ATTACK_CROUCH_POSTFIRE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_SWIM_PREFIRE") => "ACT_ITEM2_ATTACK_SWIM_PREFIRE",
        (TFWeaponRole.Item2, "ACT_MP_ATTACK_SWIM_POSTFIRE") => "ACT_ITEM2_ATTACK_SWIM_POSTFIRE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_ITEM3 (:4430-4443).
        //-----------------------------------------------------------------
        (TFWeaponRole.Item3, "ACT_VM_DRAW") => "ACT_ITEM3_VM_DRAW",
        (TFWeaponRole.Item3, "ACT_VM_HOLSTER") => "ACT_ITEM3_VM_HOLSTER",
        (TFWeaponRole.Item3, "ACT_VM_IDLE") => "ACT_ITEM3_VM_IDLE",
        (TFWeaponRole.Item3, "ACT_VM_PULLBACK") => "ACT_ITEM3_VM_PULLBACK",
        (TFWeaponRole.Item3, "ACT_VM_PRIMARYATTACK") => "ACT_ITEM3_VM_PRIMARYATTACK",
        (TFWeaponRole.Item3, "ACT_VM_SECONDARYATTACK") => "ACT_ITEM3_VM_SECONDARYATTACK",
        (TFWeaponRole.Item3, "ACT_VM_RELOAD") => "ACT_ITEM3_VM_RELOAD",
        (TFWeaponRole.Item3, "ACT_RELOAD_START") => "ACT_ITEM3_RELOAD_START",
        (TFWeaponRole.Item3, "ACT_RELOAD_FINISH") => "ACT_ITEM3_RELOAD_FINISH",
        (TFWeaponRole.Item3, "ACT_VM_DRYFIRE") => "ACT_ITEM3_VM_DRYFIRE",
        (TFWeaponRole.Item3, "ACT_VM_IDLE_TO_LOWERED") => "ACT_ITEM3_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Item3, "ACT_VM_IDLE_LOWERED") => "ACT_ITEM3_VM_IDLE_LOWERED",
        (TFWeaponRole.Item3, "ACT_VM_LOWERED_TO_IDLE") => "ACT_ITEM3_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_STAND_PREFIRE") => "ACT_ITEM3_ATTACK_STAND_PREFIRE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_STAND_POSTFIRE") => "ACT_ITEM3_ATTACK_STAND_POSTFIRE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_STAND_STARTFIRE") => "ACT_ITEM3_ATTACK_STAND_STARTFIRE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_CROUCH_PREFIRE") => "ACT_ITEM3_ATTACK_CROUCH_PREFIRE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_CROUCH_POSTFIRE") => "ACT_ITEM3_ATTACK_CROUCH_POSTFIRE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_SWIM_PREFIRE") => "ACT_ITEM3_ATTACK_SWIM_PREFIRE",
        (TFWeaponRole.Item3, "ACT_MP_ATTACK_SWIM_POSTFIRE") => "ACT_ITEM3_ATTACK_SWIM_POSTFIRE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_ITEM4 (:4450-4462).
        //-----------------------------------------------------------------
        (TFWeaponRole.Item4, "ACT_VM_DRAW") => "ACT_ITEM4_VM_DRAW",
        (TFWeaponRole.Item4, "ACT_VM_HOLSTER") => "ACT_ITEM4_VM_HOLSTER",
        (TFWeaponRole.Item4, "ACT_VM_IDLE") => "ACT_ITEM4_VM_IDLE",
        (TFWeaponRole.Item4, "ACT_VM_PULLBACK") => "ACT_ITEM4_VM_PULLBACK",
        (TFWeaponRole.Item4, "ACT_VM_PRIMARYATTACK") => "ACT_ITEM4_VM_PRIMARYATTACK",
        (TFWeaponRole.Item4, "ACT_VM_SECONDARYATTACK") => "ACT_ITEM4_VM_SECONDARYATTACK",
        (TFWeaponRole.Item4, "ACT_VM_RELOAD") => "ACT_ITEM4_VM_RELOAD",
        (TFWeaponRole.Item4, "ACT_RELOAD_START") => "ACT_ITEM4_RELOAD_START",
        (TFWeaponRole.Item4, "ACT_RELOAD_FINISH") => "ACT_ITEM4_RELOAD_FINISH",
        (TFWeaponRole.Item4, "ACT_VM_DRYFIRE") => "ACT_ITEM4_VM_DRYFIRE",
        (TFWeaponRole.Item4, "ACT_VM_IDLE_TO_LOWERED") => "ACT_ITEM4_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Item4, "ACT_VM_IDLE_LOWERED") => "ACT_ITEM4_VM_IDLE_LOWERED",
        (TFWeaponRole.Item4, "ACT_VM_LOWERED_TO_IDLE") => "ACT_ITEM4_VM_LOWERED_TO_IDLE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_MELEE_ALLCLASS (:4469-4481).
        //-----------------------------------------------------------------
        (TFWeaponRole.MeleeAllclass, "ACT_VM_DRAW") => "ACT_MELEE_ALLCLASS_VM_DRAW",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_HOLSTER") => "ACT_MELEE_ALLCLASS_VM_HOLSTER",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_IDLE") => "ACT_MELEE_ALLCLASS_VM_IDLE",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_PULLBACK") => "ACT_MELEE_ALLCLASS_VM_PULLBACK",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_PRIMARYATTACK") => "ACT_MELEE_ALLCLASS_VM_PRIMARYATTACK",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_SECONDARYATTACK") => "ACT_MELEE_ALLCLASS_VM_SECONDARYATTACK",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_RELOAD") => "ACT_MELEE_ALLCLASS_VM_RELOAD",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_DRYFIRE") => "ACT_MELEE_ALLCLASS_VM_DRYFIRE",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_IDLE_TO_LOWERED") => "ACT_MELEE_ALLCLASS_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_IDLE_LOWERED") => "ACT_MELEE_ALLCLASS_VM_IDLE_LOWERED",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_LOWERED_TO_IDLE") => "ACT_MELEE_ALLCLASS_VM_LOWERED_TO_IDLE",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_HITCENTER") => "ACT_MELEE_ALLCLASS_VM_HITCENTER",
        (TFWeaponRole.MeleeAllclass, "ACT_VM_SWINGHARD") => "ACT_MELEE_ALLCLASS_VM_SWINGHARD",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_SECONDARY2 (:4483-4494): no SECONDARYATTACK row.
        //-----------------------------------------------------------------
        (TFWeaponRole.Secondary2, "ACT_VM_DRAW") => "ACT_SECONDARY2_VM_DRAW",
        (TFWeaponRole.Secondary2, "ACT_VM_HOLSTER") => "ACT_SECONDARY2_VM_HOLSTER",
        (TFWeaponRole.Secondary2, "ACT_VM_IDLE") => "ACT_SECONDARY2_VM_IDLE",
        (TFWeaponRole.Secondary2, "ACT_VM_PULLBACK") => "ACT_SECONDARY2_VM_PULLBACK",
        (TFWeaponRole.Secondary2, "ACT_VM_PRIMARYATTACK") => "ACT_SECONDARY2_VM_PRIMARYATTACK",
        (TFWeaponRole.Secondary2, "ACT_VM_RELOAD") => "ACT_SECONDARY2_VM_RELOAD",
        (TFWeaponRole.Secondary2, "ACT_RELOAD_START") => "ACT_SECONDARY2_RELOAD_START",
        (TFWeaponRole.Secondary2, "ACT_RELOAD_FINISH") => "ACT_SECONDARY2_RELOAD_FINISH",
        (TFWeaponRole.Secondary2, "ACT_VM_DRYFIRE") => "ACT_SECONDARY2_VM_DRYFIRE",
        (TFWeaponRole.Secondary2, "ACT_VM_IDLE_TO_LOWERED") => "ACT_SECONDARY2_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Secondary2, "ACT_VM_IDLE_LOWERED") => "ACT_SECONDARY2_VM_IDLE_LOWERED",
        (TFWeaponRole.Secondary2, "ACT_VM_LOWERED_TO_IDLE") => "ACT_SECONDARY2_VM_LOWERED_TO_IDLE",

        //-----------------------------------------------------------------
        // TF_WPN_TYPE_PRIMARY2 (:4496-4507): the reload is the shotgun's
        // three-shell one, named _3.
        //-----------------------------------------------------------------
        (TFWeaponRole.Primary2, "ACT_VM_DRAW") => "ACT_PRIMARY_VM_DRAW",
        (TFWeaponRole.Primary2, "ACT_VM_HOLSTER") => "ACT_PRIMARY_VM_HOLSTER",
        (TFWeaponRole.Primary2, "ACT_VM_IDLE") => "ACT_PRIMARY_VM_IDLE",
        (TFWeaponRole.Primary2, "ACT_VM_PULLBACK") => "ACT_PRIMARY_VM_PULLBACK",
        (TFWeaponRole.Primary2, "ACT_VM_PRIMARYATTACK") => "ACT_PRIMARY_VM_PRIMARYATTACK",
        (TFWeaponRole.Primary2, "ACT_VM_RELOAD") => "ACT_PRIMARY_VM_RELOAD_3",
        (TFWeaponRole.Primary2, "ACT_RELOAD_START") => "ACT_PRIMARY_RELOAD_START_3",
        (TFWeaponRole.Primary2, "ACT_RELOAD_FINISH") => "ACT_PRIMARY_RELOAD_FINISH_3",
        (TFWeaponRole.Primary2, "ACT_VM_DRYFIRE") => "ACT_PRIMARY_VM_DRYFIRE",
        (TFWeaponRole.Primary2, "ACT_VM_IDLE_TO_LOWERED") => "ACT_PRIMARY_VM_IDLE_TO_LOWERED",
        (TFWeaponRole.Primary2, "ACT_VM_IDLE_LOWERED") => "ACT_PRIMARY_VM_IDLE_LOWERED",
        (TFWeaponRole.Primary2, "ACT_VM_LOWERED_TO_IDLE") => "ACT_PRIMARY_VM_LOWERED_TO_IDLE",

        _ => null,
    };
}
