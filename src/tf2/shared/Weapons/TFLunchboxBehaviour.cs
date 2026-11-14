//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The lunchbox family's behaviour (<c>CTFLunchBox</c>,
/// shared/tf/tf_weapon_lunchbox.cpp): the primary taunt-eats and the
/// secondary throws the plate. The bite itself — the heal, the modes' buffs —
/// is the server's (<c>ApplyBiteEffects</c>, :381-432, asked per bite by the
/// heavy's taunt attack, tf_player.cpp:18555-18584); this side decides when
/// the eat and the throw may run and what either costs, on both sides, as
/// every predicted state here is.
/// </summary>
public class TFLunchboxBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>
    /// <c>lunchbox_weapontypes_t</c> (tf_weapon_lunchbox.h:20-30), what the
    /// item's <c>set_weapon_mode</c> names: the Dalokohs bar and the
    /// fishcake buff the health, the Buffalo Sandwich is the minicrits arm
    ///<c>LUNCHBOX_ADDS_MINICRITS</c>, the robo and festive plates eat as the
    /// plain sandvich.
    /// </summary>
    public const int ModeStandard = 0;
    public const int ModeChocolateBar = 1;
    public const int ModeAddsMinicrits = 2;
    public const int ModeStandardRobo = 3;
    public const int ModeStandardFestive = 4;
    public const int ModeAddsAmmo = 5;
    public const int ModeBanana = 6;
    public const int ModeFishcake = 7;

    /// <summary>The item attribute that names the mode (tf_weapon_lunchbox.h:63's <c>CALL_ATTRIB_HOOK_INT</c>).</summary>
    public const string ModeAttribute = "set_weapon_mode";

    /// <summary>Stock's tail past the taunt, the server's (:186 <c>GetTauntRemoveTime() + 0.1</c>).</summary>
    public const float EatTail = 0.1f;

    /// <summary>Stock's client arm (:188's <c>+2.0f // corrected by the game server</c>), the eat's repeat when no condition stands.</summary>
    public const float ClientEatRepeat = 2.0f;

    /// <summary>
    /// The eat (<c>CTFLunchBox::PrimaryAttack</c>, :175-190): the whole shot
    /// is the taunt, so the behaviour takes it before the machine spends or
    /// animates anything — the same before-the-shot veto the bow's draw and
    /// the launchers run.
    /// </summary>
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        // The base's gate, in its order: the phase refuses the eat the way it
        // refuses every attack (tf_player_shared.cpp:12246-12250).
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context))
            return false;

        // Stock's head (:177-180): no sandvich, no eat — the machine has
        // already routed the empty press to its own click and reload.
        var weapon = attack.Weapon;
        if (weapon.Reserve <= 0)
            return false;

        // The eat is the taunt (:182's pOwner->Taunt(), the server's arm): the
        // scene, the condition and the heavy's bite clock arm there.
        if (attack.EffectsLive)
            Parts.Effects.LunchboxEat(in attack, script);

        // The next bite waits for the taunt's end on the side that decides it
        // (:186's <c>GAME_DLL</c> arm, the live pass — the server's run and the
        // press's own prediction, whose own side has the condition the press
        // just stamped); stock's client arm (:188) answers its constant to
        // every other pass and lets the game server correct it, so the replay
        // writes the constant too and every replay of the command lands on one
        // stamp — a remainder read through the arriving words ages between the
        // passes and would judge a different byte each time.
        float taunt = attack.EffectsLive && attack.Command.OwnerIndex > 0
            ? Parts.Owners.ConditionsOf(attack.Command.OwnerIndex)?.Duration(TFCond.Taunting) ?? 0f
            : 0f;
        weapon.NextPrimaryAttack = attack.Now + (taunt > 0f ? taunt + EatTail : ClientEatRepeat);

        // The sandvich spent and the meter at nought — stock's DrainAmmo
        // (:300-343) less its full-health-plain no-spend arm (:317-320): the
        // eaten sandvich comes back by the meter (OnResourceMeterFilled,
        // :437-444, the frame tail's refill), so the cooldown starts here for
        // the plain eat too.
        weapon.Reserve -= 1;
        weapon.ItemMeter = 0f;

        return false;
    }

    /// <summary>
    /// The throw (<c>CTFLunchBox::SecondaryAttack</c>, :195-295): the plate
    /// spawns by the server's arm, the sandvich and the meter go with it, and
    /// the hands take the next best weapon (:292) — the exhaust switch's
    /// live-only shape, the server's switch the one the update carries.
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        // DropAllowed (:160-170): no throw mid-taunt.
        if (attack.Command.OwnerIndex > 0
            && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } taunting
            && taunting.Has(TFCond.Taunting))
            return;

        // :197-198: nothing to throw.
        if (attack.Weapon.Reserve <= 0)
            return;

        if (ScriptOf(attack.Type) is not { } script)
            return;

        if (attack.EffectsLive)
            Parts.Effects.LunchboxDrop(in attack, script, ModeOf(in attack, script));

        // The plate's cost (:291) and the meter with it (:294).
        attack.Weapon.Reserve -= 1;
        attack.Weapon.ItemMeter = 0f;

        if (attack.EffectsLive)
            attack.Arms.SwitchToNextBest();
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        float before = attack.Weapon.ItemMeter;
        base.ItemPostFrame(attack);

        // OnResourceMeterFilled (:437-444): the filled meter hands the
        // sandvich back — stock's GiveAmmo(1, primary). Spent is the only way
        // the count falls, so it cannot pass what the carrier handed out.
        if (before < 1f && attack.Weapon.ItemMeter >= 1f && attack.Weapon.Reserve <= 0)
            attack.Weapon.Reserve += 1;
    }

    /// <summary>The held item's mode, stock's <c>GetLunchboxType</c> (tf_weapon_lunchbox.h:63).</summary>
    private int ModeOf(in WeaponAttackContext attack, TFWeaponScript script) =>
        (int)_attributes.Float(0f, ModeAttribute, Context(in attack, script).Item);
}
