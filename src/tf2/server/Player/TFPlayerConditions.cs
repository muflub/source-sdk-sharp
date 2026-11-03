//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// The server's <see cref="ITFPlayerConditions"/>: <c>CTFPlayerShared</c>'s
/// <c>AddCond</c>/<c>RemoveCond</c>/<c>RemoveAllCond</c> over the carrier's
/// condition set (<see cref="TFPlayerCarrier.Conditions"/>), with the player
/// half stock keeps around the set: the refusals of <c>AddCond</c>
/// (tf_player_shared.cpp:1110-1127), and the arms after every change —
/// <see cref="TFConditionArms"/> first (the conditions an arm ends), then the
/// <see cref="ITFConditionHook"/> chain for the condition and for each one an
/// arm or <see cref="RemoveAll"/> ended.
///
/// <para>
/// <see cref="Expire"/> is the countdown's half (<c>ConditionGameRulesThink</c>
/// removes through <c>RemoveCond</c>, :2369, so the arms run for an expiry
/// as for a removal).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerConditions)])]
public sealed class TFPlayerConditions(TFPlayerCarriers carriers, IEnginePlayers players,
                                       IHooks<ITFConditionHook>? hooks = null,
                                       IEngineGameEvents? events = null) : ITFPlayerConditions
{
    private readonly TFConditionArms _arms = new();

    public TFConditions? Of(int playerIndex) => carriers.ConditionsOf(playerIndex);

    public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent,
                         EntityHandle provider = default)
    {
        if (!players.IsAlive(player) || cond is TFCond.CompetitiveWinner or TFCond.CompetitiveLoser
            || Of(player.EntryIndex) is not { } conditions)
        {
            return TFCondAdd.Refused;
        }

        // Merged is the crit list's merge into a live boost, which runs no arm
        // (CTFConditionList::Add); a crit boost's first add is its object's
        // OnAdded, an Added here like any other.
        var added = conditions.Add(cond, duration, provider);
        if (added is TFCondAdd.Added or TFCondAdd.Refreshed)
        {
            TFCondMask ended = _arms.OnAdded(conditions, cond);
            Added(player, cond);
            Removed(player, in ended);
        }

        return added;
    }

    public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false)
    {
        if (Of(player.EntryIndex) is not { } conditions)
            return false;

        // Stock reads the accumulator and the provider while the condition is
        // still set (RemoveCond clears them after the event,
        // tf_player_shared.cpp:1184-1198), so the snapshot comes first.
        int prevented = conditions.PreventedDamage(cond);
        EntityHandle preventor = Preventor(conditions, cond, player);
        if (!conditions.Remove(cond, ignoreDuration))
            return false;

        Ended(player, conditions, cond);
        conditions.ClearPreventedDamage(cond);
        FirePrevented(player, cond, prevented, preventor);
        return true;
    }

    public TFCondMask RemoveAll(EntityHandle player)
    {
        if (Of(player.EntryIndex) is not { } conditions)
            return default;

        // Stock's RemoveAllCond walks RemoveCond per condition, so every
        // prevented total is read while its condition is still set.
        List<(TFCond Cond, int Amount, EntityHandle Preventor)>? prevented = null;
        foreach (TFCond cond in conditions.State.Bits)
        {
            if (conditions.PreventedDamage(cond) > 0)
                (prevented ??= []).Add((cond, conditions.PreventedDamage(cond), Preventor(conditions, cond, player)));
        }

        TFCondMask removed = conditions.RemoveAll();
        Removed(player, in removed);
        if (prevented is not null)
        {
            foreach (var (cond, amount, preventor) in prevented)
            {
                conditions.ClearPreventedDamage(cond);
                FirePrevented(player, cond, amount, preventor);
            }
        }
        return removed;
    }

    public bool InCond(EntityHandle player, TFCond cond) => Of(player.EntryIndex)?.Has(cond) ?? false;

    /// <summary>
    /// The countdown's removals: every condition <see cref="TFConditions.Tick"/>
    /// expired runs its removal arms, as <c>RemoveCond</c> would.
    /// </summary>
    public void Expire(EntityHandle player, in TFCondMask expired)
    {
        if (Of(player.EntryIndex) is not { } conditions)
            return;

        foreach (TFCond cond in expired)
            Ended(player, conditions, cond);
    }

    private void Ended(EntityHandle player, TFConditions conditions, TFCond cond)
    {
        TFCondMask ended = _arms.OnRemoved(conditions, cond);
        Removed(player, cond);
        Removed(player, in ended);
    }

    private void Added(EntityHandle player, TFCond cond)
        => hooks?.Each(nameof(ITFConditionHook.OnAdded), h => h.OnAdded(player, cond));

    private void Removed(EntityHandle player, TFCond cond)
        => hooks?.Each(nameof(ITFConditionHook.OnRemoved), h => h.OnRemoved(player, cond));

    private void Removed(EntityHandle player, in TFCondMask conds)
    {
        foreach (TFCond cond in conds)
            Removed(player, cond);
    }

    /// <summary>Stock's preventor: the condition's provider, else the player (tf_player_shared.cpp:1189).</summary>
    private static EntityHandle Preventor(TFConditions conditions, TFCond cond, EntityHandle player)
        => conditions.Provider(cond) is var provider && provider != EntityHandle.None ? provider : player;

    /// <summary>
    /// The <c>damage_prevented</c> event (tf_player_shared.cpp:1186-1194): the
    /// preventor, the victim, the total and the condition. Stock fires it
    /// without broadcasting. Only the server fires: the accumulator lives on
    /// the authoritative set.
    /// </summary>
    private void FirePrevented(EntityHandle player, TFCond cond, int amount, EntityHandle preventor)
    {
        if (events is null || amount <= 0)
            return;

        IntPtr e = events.EventCreate("damage_prevented");
        if (e == IntPtr.Zero)
            return;

        events.EventSetInt(e, "preventor", preventor.EntryIndex);
        events.EventSetInt(e, "victim", player.EntryIndex);
        events.EventSetInt(e, "amount", amount);
        events.EventSetInt(e, "condition", (int)cond);
        events.EventFire(e);
    }
}
