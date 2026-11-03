//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Conditions;

/// <summary>
/// The arms of <c>OnConditionAdded</c>/<c>OnConditionRemoved</c>
/// (<c>tf_player_shared.cpp:1571-1886</c>, <c>:1892-2230</c>) that do
/// nothing but remove other conditions. Each returns every condition it
/// removed, its own arms' removals included, so the caller runs
/// <c>OnConditionRemoved</c>'s effect arms for exactly that set.
///
/// <para>
/// Stock removes inside the arm and runs the removed condition's arm before
/// returning; here the condition state ends the same, and the caller runs the
/// effect arms after (<c>docs/divergences.md</c>). Every other arm touches the
/// player, its weapons or effects, and belongs to the player components
/// (<c>docs/plans/tf2/conditions.tsv</c> names each arm's line).
/// </para>
/// </summary>
public sealed class TFConditionArms
{
    /// <summary>
    /// Runs the condition-only arms of <c>OnConditionAdded(cond)</c>. Call it
    /// after an <see cref="TFConditions.Add"/> that answered
    /// <see cref="TFCondAdd.Added"/> or <see cref="TFCondAdd.Refreshed"/>.
    /// </summary>
    public TFCondMask OnAdded(TFConditions conditions, TFCond cond)
    {
        var removed = default(TFCondMask);
        switch (cond)
        {
            // OnAddInvulnerable's server half (:3468-3499): uber ends the
            // persistent damaging conditions.
            case TFCond.Invulnerable:
            case TFCond.InvulnerableUserBuff:
            case TFCond.InvulnerableCardEffect:
                if (conditions.Authoritative)
                {
                    RemoveIfActive(conditions, TFCond.Burning, ref removed);
                    RemoveIfActive(conditions, TFCond.Urine, ref removed);
                    RemoveIfActive(conditions, TFCond.Bleeding, ref removed);
                    RemoveIfActive(conditions, TFCond.MadMilk, ref removed);
                    RemoveIfActive(conditions, TFCond.Gas, ref removed);
                    RemoveIfActive(conditions, TFCond.Plague, ref removed);
                }

                break;
        }

        return removed;
    }

    /// <summary>
    /// Runs the condition-only arms of <c>OnConditionRemoved(cond)</c>. Call
    /// it after a <see cref="TFConditions.Remove"/> that answered true and for
    /// each condition <see cref="TFConditions.Tick"/> expired.
    /// </summary>
    public TFCondMask OnRemoved(TFConditions conditions, TFCond cond)
    {
        var removed = default(TFCondMask);
        switch (cond)
        {
            // OnRemoveStealthed (:7108-7112, both sides): leaving stealth ends feign death.
            case TFCond.Stealthed:
            case TFCond.StealthedUserBuff:
                RemoveIfActive(conditions, TFCond.FeignDeath, ref removed);
                break;

            // OnRemoveBurning's server half (:6899-6902): the afterburn heal debuff ends with the fire.
            case TFCond.Burning:
                if (conditions.Authoritative)
                {
                    RemoveIfActive(conditions, TFCond.HealingDebuff, ref removed);
                }

                break;
        }

        return removed;
    }

    private void RemoveIfActive(TFConditions conditions, TFCond cond, ref TFCondMask removed)
    {
        if (conditions.Has(cond) && conditions.Remove(cond))
        {
            removed.Set(cond);
            removed |= OnRemoved(conditions, cond);
        }
    }
}
