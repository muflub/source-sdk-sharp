//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Shared.Movement;

/// <summary>
/// What <see cref="TFMaxSpeed.Calculate"/> reads: the player's class and
/// condition words, the class a disguise shows, and the active weapon's two
/// inputs. Built from PREDICTED state only (<see cref="Of"/>), so the server's
/// run and every client replay of a command compute the same speed.
/// </summary>
/// <param name="ActiveWeapon">The active weapon's id, <see cref="TFWeaponId.None"/> for none (the bow arm reads it).</param>
/// <param name="WeaponSpeedMod"><c>CTFWeaponBase::GetSpeedMod()</c>; 1 for none.</param>
/// <param name="StunFlags">The legacy stun word <c>m_iStunFlags</c> — the
/// active stun's flags, zero with none (the stun arm reads it).</param>
/// <param name="CarryingObject"><c>m_bCarryingObject</c>: a building rides in the arms (the carry arm reads it).</param>
/// <param name="HealTargetIndex"><c>m_hHealTarget</c>: the medic's patient, as an entity index; -1 for none
/// (the medic's match reads it). Only the medigun publishes one, so a set
/// target is stock's active-medigun check.</param>
/// <param name="SwordSpeedMod">The demoman's owned sword's
/// <c>GetSwordSpeedMod</c> (tf_weapon_sword.cpp:295-309) — the heads' speed,
/// applied even out of hand; 1 without the sword or the heads.</param>
public readonly record struct TFMoveInputs(
    TFClass Class,
    TFCondMask Conditions,
    TFClass DisguiseClass = TFClass.Undefined,
    TFWeaponId ActiveWeapon = TFWeaponId.None,
    float WeaponSpeedMod = 1f,
    TFStunFlags StunFlags = TFStunFlags.None,
    bool CarryingObject = false,
    int HealTargetIndex = -1,
    float SwordSpeedMod = 1f,

    /// <summary>The demoman's shield is among the worn wearables — the speed
    /// walk's `IsShieldEquipped` gate (tf_player_shared.cpp:11005, the
    /// equip walk of :6117-6120 and :626-643).</summary>
    bool ShieldEquipped = false,

    /// <summary>The medic's owned medigun's ÜberCharge, 0..1 — stock's
    /// <c>Weapon_OwnsThisID(TF_WEAPON_MEDIGUN)-&gt;GetChargeLevel()</c>
    /// (:11050-11053), carried on the player since the one-weapon entity
    /// reuses its state on a switch. The resource-level speed arm reads it;
    /// the zero default is a full remap to one, no bonus, the same answer
    /// stock's empty meter gives.</summary>
    float MedicCharge = 0f,

    /// <summary>The scout's hype speed (<c>tf_player_shared.cpp:11085</c>):
    /// the meter remapped 0..100 onto 1..1.45 for a Baby Face's Blaster owner
    /// — the scale the movement rules compute (<see cref="TFHype.SpeedScale"/>),
    /// 1 without the item, the walk's no-op.</summary>
    float HypeSpeedScale = 1f,

    /// <summary>The player's observer mode, the engine's <c>m_iObserverMode</c>
    /// — the walk's head arm (tf_player_shared.cpp:10844-10851): the roaming
    /// classic observer runs at the scout's speed, any other observer stands.
    /// <see cref="ObserverMode.None"/> in play.</summary>
    ObserverMode Observer = ObserverMode.None,

    /// <summary>The team-win bonus's scale (tf_player_shared.cpp:10938-10954):
    /// 1.1 a winner and 0.9 a loser while the round is
    /// <c>GR_STATE_TEAM_WIN</c> and a winner stands — the scale
    /// <see cref="TFMaxSpeed.TeamWinScale"/> decides, 1 outside the win.</summary>
    float TeamWinScale = 1f)
{
    /// <summary>The inputs a carrier's state holds. The weapon inputs wait for the weapons lane (1.9).</summary>
    public static TFMoveInputs Of(TFPlayerNetState state)
        => new(state.Class(), state.Conditions(), state.DisguiseAs(),
               // IsLoserStateStunned's gate (tf_player_shared.cpp:9966-9975): the
               // flags count only behind the active entry and TF_COND_STUNNED,
               // since the carrier's copy goes stale behind a cleared index.
               StunFlags: state.StunIndex >= 0 && state.InCond(TFCond.Stunned)
                   ? (TFStunFlags)state.StunFlags
                   : TFStunFlags.None,
               CarryingObject: state.CarryingObject,
               HealTargetIndex: state.HealTargetIndex,
               MedicCharge: state.MedicCharge);
}

/// <summary>
/// A second player's whole speed environment, what the medic's match needs to
/// run the patient through the same walk (<c>pHealTarget-&gt;
/// TeamFortress_CalculateMaxSpeed</c>, tf_player_shared.cpp:11043): the
/// inputs its carrier holds and the attribute roots its side would have
/// rooted the hooks with.
/// </summary>
/// <param name="Index">The entity index the inputs belong to — the walk's re-entry check is keyed by it.</param>
public sealed record TFPeerInput(int Index, TFMoveInputs Inputs, AttributeHooks Attribs,
                                 IAttributeProvider? Manager, IAttributeProvider? ActiveItem);

/// <summary>
/// <c>CTFPlayer::TeamFortress_CalculateMaxSpeed</c>
/// (src/game/shared/tf/tf_player_shared.cpp:10837), the milestone's arms, as a
/// pure function. In stock order:
/// <list type="number">
/// <item>no class: 1 ("this can't return 0 because other parts of the code
/// interpret that as use default speed", :10855);</item>
/// <item>the class's <c>m_flMaxSpeed</c> (:10858);</item>
/// <item>disguised as a dispenser and not stealthed: 0; disguised and not
/// stealthed: no faster than the disguise class (:10877-10885);</item>
/// <item>aiming (<c>TF_COND_AIMING</c>): a flat cap of 110 for the heavy
/// ("allowed to move slightly faster than a sniper when spun-up"), 160 with
/// the compound bow, else 80 (:10889-10913);</item>
/// <item>stealthed: no faster than <c>tf_spy_max_cloaked_speed</c> (:10930-10936);</item>
/// <item>times the active weapon's speed mod (:10956-10960).</item>
/// </list>
/// Not ported (docs/plans/tf2/deferred.tsv): the observer arm, the MvM
/// mini-boss exception, the team-win bonus, the
/// bonesaw heads, the hype meter and the
/// runes.
/// </summary>
public static class TFMaxSpeed
{
    /// <summary>The heavy's aiming cap (tf_player_shared.cpp:10898).</summary>
    public const float HeavyAiming = 110f;

    /// <summary>The compound bow's aiming cap (:10905).</summary>
    public const float BowAiming = 160f;

    /// <summary>Everyone else's aiming cap, the sniper's scope (:10909).</summary>
    public const float Aiming = 80f;

    /// <summary><c>tf_whip_speed_increase</c>'s default, the speed boost's cap
    /// (<c>tf_player_shared.cpp:119</c>, read at :10925).</summary>
    public const float WhipSpeedIncrease = 105f;

    /// <summary><c>mult_player_aiming_movespeed</c>, the aiming cap's hook (:10913).</summary>
    public const string AimSpeedAttrib = "mult_player_aiming_movespeed";

    /// <summary><c>mult_player_movespeed</c>, the final speed's hook (:11003).</summary>
    public const string MoveSpeedAttrib = "mult_player_movespeed";

    /// <summary><c>mult_player_movespeed_active</c>, the held weapon item's hook on the active weapon (:11059).</summary>
    public const string ActiveSpeedAttrib = "mult_player_movespeed_active";

    /// <summary><c>mult_player_movespeed_resource_level</c>, the active item's hook whose scale is the ÜberCharge (:11044).</summary>
    public const string ResourceLevelSpeedAttrib = "mult_player_movespeed_resource_level";

    /// <summary><c>mult_player_movespeed_shieldrequired</c>, the shield wearer's hook (:11007).</summary>
    public const string ShieldRequiredSpeedAttrib = "mult_player_movespeed_shieldrequired";

    /// <summary><c>set_weapon_mode</c>, the medigun type's hook — the
    /// Quick-Fix arm's <c>GetMedigunType</c> (tf_weapon_medigun.cpp:781-785),
    /// read off the owner's manager as the medigun's own arms do.</summary>
    public const string MedigunModeAttrib = "set_weapon_mode";

    /// <summary><c>MEDIGUN_QUICKFIX</c> (tf_weapon_medigun.h:23-29).</summary>
    public const int QuickFixMedigunMode = 2;

    /// <summary><c>tf_max_charge_speed</c>'s default, the shield charge's speed (tf_gamemovement.cpp:52, read at tf_player_shared.cpp:10972).</summary>
    public const float MaxChargeSpeed = 750f;

    /// <summary>The player's maximum speed. Every dependency is handed in: both
    /// sides' registrations hold the walker and the peer lookup, so the call
    /// never answers itself a fallback.</summary>
    /// <param name="spyMaxCloakedSpeed"><c>tf_spy_max_cloaked_speed</c>.</param>
    /// <param name="attribs">The side's attribute walk, the one
    /// <c>CALL_ATTRIB_HOOK_FLOAT</c> takes on the player.</param>
    /// <param name="playerAttributes"><c>m_AttributeManager</c>, the hooks'
    /// root; null when the player has none, as stock's absent manager.</param>
    /// <param name="peers">The other players' speed environments, for the
    /// medic's match; answers null for an index the side never saw.</param>
    /// <param name="playerIndex">The caller's entity index, the re-entry
    /// check's key; -1 for a caller with none, the match then unable to run.</param>
    /// <param name="activeItem">The active weapon's item, the root of the
    /// <c>mult_player_movespeed_active</c> hook (stock's
    /// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> on the held weapon); null when
    /// nothing is held, stock's null <c>pTFActiveWeapon</c>.</param>
    public static float Calculate(TFClassTable classes, in TFMoveInputs inputs, float spyMaxCloakedSpeed,
                                  AttributeHooks attribs, IAttributeProvider? playerAttributes,
                                  Func<int, TFPeerInput?> peers, int playerIndex,
                                  IAttributeProvider? activeItem)
        => Walk(classes, inputs, spyMaxCloakedSpeed, attribs, playerAttributes, peers, playerIndex, [], activeItem);

    private static float Walk(TFClassTable classes, in TFMoveInputs inputs, float spyMaxCloakedSpeed,
                              AttributeHooks attribs, IAttributeProvider? playerAttributes,
                              Func<int, TFPeerInput?> peers, int playerIndex, HashSet<int> inFlight,
                              IAttributeProvider? activeItem)
    {
        TFClass playerClass = inputs.Class;

        // The observer's head arm (tf_player_shared.cpp:10844-10851), stock's
        // early returns before the class check: the roaming classic observer
        // moves at the scout's speed, and any other observer stands.
        if (inputs.Observer != ObserverMode.None)
            return inputs.Observer == ObserverMode.Roaming
                ? classes[TFClass.Scout].MaxSpeed
                : 0f;

        if (playerClass == TFClass.Undefined)
            return 1.0f;

        float defaultSpeed = classes[playerClass].MaxSpeed;
        float maxfbspeed = defaultSpeed;

        // Re-entry ends at the default (tf_player_shared.cpp:10879-10888):
        // two characters matching each other's speed, the walk's own flag
        // its only guard. Stock scopes the flag to the one call; the walk
        // here keeps the whole visit's set, which the same chain meets the
        // same way — one heal target each, nobody visited twice.
        if (playerIndex >= 0)
        {
            if (inFlight.Contains(playerIndex))
                return defaultSpeed;
            inFlight.Add(playerIndex);
        }

        TFCondMask conditions = inputs.Conditions;
        bool stealthed = IsStealthed(in conditions);
        if (inputs.Conditions.Has(TFCond.DisguisedAsDispenser) && !stealthed)
        {
            maxfbspeed = 0.0f;
        }
        else if (inputs.Conditions.Has(TFCond.Disguised) && !stealthed)
        {
            float maxDisguiseSpeed = classes[inputs.DisguiseClass].MaxSpeed;
            maxfbspeed = MathF.Min(maxDisguiseSpeed, maxfbspeed);
        }

        if (inputs.Conditions.Has(TFCond.Aiming))
        {
            float aimMax = playerClass == TFClass.HeavyWeapons
                ? HeavyAiming
                : inputs.ActiveWeapon == TFWeaponId.CompoundBow ? BowAiming : Aiming;
            aimMax = attribs.Float(aimMax, AimSpeedAttrib, playerAttributes);
            maxfbspeed = MathF.Min(maxfbspeed, aimMax);
        }

        // The carried speed boost (tf_player_shared.cpp:10919-10927): the
        // lesser of 40 percent of the speed and tf_whip_speed_increase, its
        // default 105 (:119) — only over a speed that moves at all, so a
        // stationed player keeps standing.
        if (conditions.Has(TFCond.SpeedBoost) && maxfbspeed > 0f)
            maxfbspeed += MathF.Min(maxfbspeed * 0.4f, WhipSpeedIncrease);

        if (inputs.Conditions.Has(TFCond.Stealthed) && maxfbspeed > spyMaxCloakedSpeed)
            maxfbspeed = spyMaxCloakedSpeed;

        // The bonus time (tf_player_shared.cpp:10938-10954): while the round
        // is GR_STATE_TEAM_WIN the winners run 110 and the losers 90 — the
        // scale the rules decided, 1 outside the win, before the weapon's own
        // mod as stock's order runs.
        maxfbspeed *= inputs.TeamWinScale;

        if (inputs.ActiveWeapon != TFWeaponId.None)
            maxfbspeed *= inputs.WeaponSpeedMod;

        // The demoman's heads (tf_player_shared.cpp:10962-10968): the owned
        // sword's GetSwordSpeedMod, +8% a head under the cap, out of hand too
        // — the input the movement rules compute, 1 without the sword.
        maxfbspeed *= inputs.SwordSpeedMod;

        // The charge (tf_player_shared.cpp:10970-10973): a charging demoman
        // moves at tf_max_charge_speed outright — the assignment stock makes
        // inside the demoman gate, before the carry toll and the item hooks.
        // Stock's bIgnoreSpecialAbility rider is the MvM giant's to lift.
        if (playerClass == TFClass.Demoman && conditions.Has(TFCond.ShieldCharge))
            maxfbspeed = MaxChargeSpeed;

        // Not a Halloween bomb head: stock's bAllowSlowing (:10875), the gate
        // the carried building and the stun share.
        bool allowSlowing = !conditions.Has(TFCond.HalloweenBombHead);

        // The carried building's toll (:10976-10987): a tenth off the top
        // while it rides. Stock's bCarryPenalty is MvM's to lift; this build
        // has none.
        if (inputs.CarryingObject && allowSlowing)
            maxfbspeed *= 0.90f;

        // The loser state's walk of shame (:10989-11000): the trigger-borne
        // stun shames less than the rest.
        if (allowSlowing && conditions.Has(TFCond.Stunned)
            && (inputs.StunFlags & TFStunFlags.LoserState) != 0)
        {
            maxfbspeed *= (inputs.StunFlags & TFStunFlags.ByTrigger) != 0 ? 0.75f : 0.5f;
        }

        // If we have an item with a move speed modification, apply it to the
        // final speed. (tf_player_shared.cpp:11002-11003, after the weapon's
        // own mod as stock's order runs.)
        maxfbspeed = attribs.Float(maxfbspeed, MoveSpeedAttrib, playerAttributes);

        // The shield wearer's toll (:11005-11008): while the demoman wears the
        // shield, the boots' mult_player_movespeed_shieldrequired — stock's
        // player-rooted hook, the same walk the line above takes.
        if (inputs.ShieldEquipped)
            maxfbspeed = attribs.Float(maxfbspeed, ShieldRequiredSpeedAttrib, playerAttributes);

        // The medic matches a faster patient (tf_player_shared.cpp:11029-11045,
        // after the hook as stock's order runs): the patient runs the same
        // walk, arms and all. Only the medigun publishes a target, so the
        // index stands for stock's active-medigun cast.
        if (playerClass == TFClass.Medic && inputs.HealTargetIndex >= 0)
        {
            if (peers(inputs.HealTargetIndex) is { } target)
            {
                // The Quick-Fix attaches to charging demos (:11021-11024): a
                // healing Quick-Fix (set_weapon_mode MEDIGUN_QUICKFIX, the
                // same owner-manager walk the medigun's own arms take) on a
                // patient in TF_COND_SHIELD_CHARGE answers
                // tf_max_charge_speed outright, the patient's walk unmatched.
                bool quickFix = attribs.Int(0, MedigunModeAttrib, playerAttributes) == QuickFixMedigunMode;
                float healTargetSpeed =
                    quickFix && target.Inputs.Conditions.Has(TFCond.ShieldCharge)
                        ? MaxChargeSpeed
                        : Walk(classes, target.Inputs, spyMaxCloakedSpeed, target.Attribs, target.Manager,
                               peers, target.Index, inFlight, target.ActiveItem);
                maxfbspeed = MathF.Max(maxfbspeed, healTargetSpeed);
            }
        }

        // The ÜberCharge's own speed (:11043-11056): the held weapon's
        // mult_player_movespeed_resource_level — the Overdose's — scaling by
        // the owned medigun's charge remapped 0..1 onto 1..mod. Stock asks the
        // medic's Weapon_OwnsThisID(TF_WEAPON_MEDIGUN); ours is the carrier's
        // mirror, and the class gate is stock's medic arm.
        if (playerClass == TFClass.Medic && activeItem is not null)
        {
            float levelMod = attribs.Float(1f, ResourceLevelSpeedAttrib, activeItem);
            if (levelMod != 1f)
                maxfbspeed *= CritBucket.RemapValClamped(inputs.MedicCharge, 0f, 1f, 1f, levelMod);
        }

        // mult_player_movespeed_active (:11058-11063): the held weapon's item,
        // hooked on the active weapon after the medic's match — the running
        // Gloves of Running Urgently. The walk starts the mod at one, as the
        // hook does; only a differing one scales the speed.
        if (activeItem is not null)
        {
            float itemSpeedMod = attribs.Float(1f, ActiveSpeedAttrib, activeItem);
            if (itemSpeedMod != 1f)
                maxfbspeed *= itemSpeedMod;
        }

        // The heavy's berzerker (tf_player_shared.cpp:11065-11078): the
        // Steak Sandvich's TF_COND_ENERGY_BUFF scales 1.3, held at 1.35 times
        // the class default so another modifier (the G.R.U.'s) cannot pass it.
        if (playerClass == TFClass.HeavyWeapons)
        {
            float heavyMaxSpeed = defaultSpeed * 1.35f;
            if (conditions.Has(TFCond.EnergyBuff))
            {
                maxfbspeed *= 1.3f;
                if (maxfbspeed > heavyMaxSpeed)
                    maxfbspeed = heavyMaxSpeed;
            }
        }

        // The scout's hype speed (tf_player_shared.cpp:11080-11086): a Baby
        // Face's Blaster owner runs at the meter's scale — stock's
        // Weapon_OwnsThisID(TF_WEAPON_PEP_BRAWLER_BLASTER) gate is the
        // movement rules' (a scout's class arm before it), the scale their
        // input carries.
        maxfbspeed *= inputs.HypeSpeedScale;

        return maxfbspeed;
    }

    /// <summary>The bonus arm's scale (tf_player_shared.cpp:10938-10954):
    /// 1.1 the winner's team and 0.9 the rest while the round is
    /// <c>GR_STATE_TEAM_WIN</c> and a winner stands; an unassigned winner is
    /// stock's TEAM_UNASSIGNED arm, no scale. The walk's
    /// <see cref="TFMoveInputs.TeamWinScale"/> input.</summary>
    public static float TeamWinScale(Rules.RoundState state, TFTeam winner, int playerTeam)
        => state == Rules.RoundState.TeamWin && winner is TFTeam.Red or TFTeam.Blue
            ? (playerTeam == (int)winner ? 1.1f : 0.9f)
            : 1f;

    /// <summary><c>CTFPlayerShared::IsStealthed</c> (tf_player_shared.cpp:8112).</summary>
    public static bool IsStealthed(in TFCondMask conditions)
        => conditions.Has(TFCond.Stealthed) || conditions.Has(TFCond.StealthedUserBuff)
           || conditions.Has(TFCond.StealthedUserBuffFading);
}
