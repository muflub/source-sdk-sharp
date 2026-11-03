//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

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
public readonly record struct TFMoveInputs(
    TFClass Class,
    TFCondMask Conditions,
    TFClass DisguiseClass = TFClass.Undefined,
    TFWeaponId ActiveWeapon = TFWeaponId.None,
    float WeaponSpeedMod = 1f,
    TFStunFlags StunFlags = TFStunFlags.None,
    bool CarryingObject = false,
    int HealTargetIndex = -1)
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
               HealTargetIndex: state.HealTargetIndex);
}

/// <summary>
/// A second player's whole speed environment, what the medic's match needs to
/// run the patient through the same walk (<c>pHealTarget-&gt;
/// TeamFortress_CalculateMaxSpeed</c>, tf_player_shared.cpp:11043): the
/// inputs its carrier holds and the attribute roots its side would have
/// rooted the hooks with.
/// </summary>
/// <param name="Index">The entity index the inputs belong to — the walk's re-entry check is keyed by it.</param>
public sealed record TFPeerInput(int Index, TFMoveInputs Inputs,
                                 AttributeHooks? Attribs = null, IAttributeProvider? Manager = null);

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
/// mini-boss exception, <c>TF_COND_SPEED_BOOST</c> (GAME_DLL only in stock, so
/// stock's own client mispredicts it), the team-win bonus, the sword/shield
/// charge, the Quick-Fix's charge arm, the
/// bonesaw heads, heavy's <c>TF_COND_ENERGY_BUFF</c>, the hype meter and the
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

    /// <summary><c>mult_player_aiming_movespeed</c>, the aiming cap's hook (:10913).</summary>
    public const string AimSpeedAttrib = "mult_player_aiming_movespeed";

    /// <summary><c>mult_player_movespeed</c>, the final speed's hook (:11003).</summary>
    public const string MoveSpeedAttrib = "mult_player_movespeed";

    /// <summary>The player's maximum speed.</summary>
    /// <param name="spyMaxCloakedSpeed"><c>tf_spy_max_cloaked_speed</c>.</param>
    /// <param name="attribs">The side's attribute walk, with the player's own
    /// attribute manager as its root (<c>CALL_ATTRIB_HOOK_FLOAT</c> on the
    /// player); null for none, both arms then the value unchanged.</param>
    /// <param name="playerAttributes"><c>m_AttributeManager</c>.</param>
    /// <param name="peers">The other players' speed environments, for the
    /// medic's match; null for none, the arm then stands down.</param>
    /// <param name="playerIndex">The caller's entity index, the re-entry
    /// check's key; -1 for a caller with none, the match then unable to run.</param>
    public static float Calculate(TFClassTable classes, in TFMoveInputs inputs, float spyMaxCloakedSpeed,
                                  AttributeHooks? attribs = null, IAttributeProvider? playerAttributes = null,
                                  Func<int, TFPeerInput?>? peers = null, int playerIndex = -1)
        => Walk(classes, inputs, spyMaxCloakedSpeed, attribs, playerAttributes, peers, playerIndex, null);

    private static float Walk(TFClassTable classes, in TFMoveInputs inputs, float spyMaxCloakedSpeed,
                              AttributeHooks? attribs, IAttributeProvider? playerAttributes,
                              Func<int, TFPeerInput?>? peers, int playerIndex, HashSet<int>? inFlight)
    {
        TFClass playerClass = inputs.Class;
        if (playerClass == TFClass.Undefined)
            return 1.0f;

        float defaultSpeed = classes[playerClass].MaxSpeed;
        float maxfbspeed = defaultSpeed;

        // Re-entry ends at the default (tf_player_shared.cpp:10879-10888):
        // two characters matching each other's speed, the walk's own flag
        // its only guard. Stock scopes the flag to the one call; the walk
        // here keeps the whole visit's set, which the same chain meets the
        // same way — one heal target each, nobody visited twice.
        if (inFlight is not null && playerIndex >= 0 && inFlight.Contains(playerIndex))
            return defaultSpeed;
        if (peers is not null && playerIndex >= 0)
            (inFlight ??= []).Add(playerIndex);

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
            aimMax = attribs?.Float(aimMax, AimSpeedAttrib, playerAttributes) ?? aimMax;
            maxfbspeed = MathF.Min(maxfbspeed, aimMax);
        }

        if (inputs.Conditions.Has(TFCond.Stealthed) && maxfbspeed > spyMaxCloakedSpeed)
            maxfbspeed = spyMaxCloakedSpeed;

        if (inputs.ActiveWeapon != TFWeaponId.None)
            maxfbspeed *= inputs.WeaponSpeedMod;

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
        maxfbspeed = attribs?.Float(maxfbspeed, MoveSpeedAttrib, playerAttributes) ?? maxfbspeed;

        // The medic matches a faster patient (tf_player_shared.cpp:11029-11045,
        // after the hook as stock's order runs): the patient runs the same
        // walk, arms and all. Only the medigun publishes a target, so the
        // index stands for stock's active-medigun cast; the Quick-Fix's
        // charge arm is the convar row's.
        if (playerClass == TFClass.Medic && inputs.HealTargetIndex >= 0 && peers is not null)
        {
            if (peers(inputs.HealTargetIndex) is { } target)
            {
                maxfbspeed = MathF.Max(maxfbspeed, Walk(classes, target.Inputs, spyMaxCloakedSpeed,
                                                        target.Attribs, target.Manager, peers,
                                                        target.Index, inFlight));
            }
        }

        return maxfbspeed;
    }

    /// <summary><c>CTFPlayerShared::IsStealthed</c> (tf_player_shared.cpp:8112).</summary>
    public static bool IsStealthed(in TFCondMask conditions)
        => conditions.Has(TFCond.Stealthed) || conditions.Has(TFCond.StealthedUserBuff)
           || conditions.Has(TFCond.StealthedUserBuffFading);
}
