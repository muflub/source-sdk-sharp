//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Conditions;

/// <summary>How <see cref="TFConditions.Add"/> took a condition.</summary>
public enum TFCondAdd
{
    /// <summary>
    /// The condition was not active and now is. Stock runs
    /// <c>OnConditionAdded</c> (<c>CTFCondition_CritBoost::OnAdded</c> for
    /// <c>Critboosted</c>).
    /// </summary>
    Added,

    /// <summary>
    /// The condition was active; its duration and provider were updated. Stock
    /// runs <c>OnConditionAdded</c> again (<c>AddCond</c> calls it on every
    /// add, <c>tf_player_shared.cpp:1159</c>).
    /// </summary>
    Refreshed,

    /// <summary>
    /// <c>Critboosted</c> was active and the new duration was merged into its
    /// min/max (<c>CTFCondition::Add</c>, <c>tf_condition.cpp:315-340</c>). Stock
    /// runs no arm.
    /// </summary>
    Merged,

    /// <summary>
    /// A client asked for <c>Critboosted</c>: stock's client leaves it to the
    /// server, which networks it (<c>CTFConditionList::Add</c>,
    /// <c>tf_condition.cpp:48-54</c>).
    /// </summary>
    Ignored,

    /// <summary>
    /// The player refused it: stock's <c>AddCond</c> returns before the set
    /// changes for a dead player, and for the competitive winner/loser
    /// conditions outside a match summary (<c>tf_player_shared.cpp:1110-1127</c>).
    /// Answered by the player's caller, never by the set itself.
    /// </summary>
    Refused,
}

/// <summary>
/// Where a player's five condition words are kept when a <see cref="TFConditions"/>
/// does not keep them itself: the <c>tf_player_data</c> carrier's
/// <c>m_nPlayerCond</c>..<c>m_nPlayerCondEx4</c>, the words the movement rules
/// and the client read.
/// </summary>
public interface ITFCondWords
{
    /// <summary>The active set as the words hold it.</summary>
    TFCondMask Read();

    /// <summary>Stores <paramref name="bits"/> as the active set.</summary>
    void Write(in TFCondMask bits);
}

/// <summary>What a network update changed: the conditions it set and the ones it cleared.</summary>
public readonly record struct TFCondDelta(TFCondMask Added, TFCondMask Removed);

/// <summary>
/// One player's conditions: <c>CTFPlayerShared</c>'s <c>AddCond</c>,
/// <c>RemoveCond</c>, <c>InCond</c>, <c>RemoveAllCond</c>,
/// <c>GetConditionDuration</c>, <c>GetConditionProvider</c> and the expiry
/// half of <c>ConditionGameRulesThink</c> (<c>tf_player_shared.cpp:1105-1286,
/// 1545-1564, 2334-2375</c>), with <c>CTFConditionList</c>'s one condition,
/// the crit boost (<c>tf_condition.cpp</c>), folded in.
///
/// <para>
/// Pure: no engine, no entity, no effects. What stock does around a change
/// (<c>OnConditionAdded</c>/<c>OnConditionRemoved</c>, sounds, particles,
/// the <c>damage_prevented</c> event) is the caller's, told by the return
/// values: <see cref="Add"/>'s <see cref="TFCondAdd"/>, <see cref="Remove"/>'s
/// bool, and the sets <see cref="Tick"/> and <see cref="RemoveAll"/> return.
/// The arms that only move other conditions are <see cref="TFConditionArms"/>.
/// The caller also owns stock's refusals that need the player:
/// <c>AddCond</c> on a dead player, and the competitive winner/loser gate
/// (:1110-1127).
/// </para>
///
/// <para>
/// All state is <see cref="State"/>, a plain value (<see cref="TFConditionState"/>),
/// so a predicted netstate can carry it and <see cref="Restore"/> can put a
/// copy back.
/// </para>
/// </summary>
public sealed class TFConditions
{
    /// <summary><c>PERMANENT_CONDITION</c> (<c>tf_player_shared.h:68</c>): a duration that never runs out.</summary>
    public const float Permanent = -1f;

    /// <summary>How many conditions there are: <c>TF_COND_LAST</c>.</summary>
    public const int Count = (int)TFCond.Last;

    private TFConditionState _state;
    private readonly ITFCondWords? _words;

    /// <summary>
    /// A player's empty condition set. <paramref name="authoritative"/> is the
    /// server: stock's <c>GAME_DLL</c> arms (the crit list's add and remove,
    /// the countdown) run only there.
    ///
    /// <para>
    /// <paramref name="words"/>, when given, is where the active set LIVES:
    /// the player's carrier (<c>m_nPlayerCond</c> and the four <c>Ex</c>
    /// words, networked and predicted). Every call reads the set from there
    /// first and writes it back after, so a write anyone made to the words
    /// (a prediction reset, a network update) is what the next call sees, and
    /// a change made here is on the carrier before the call returns. The
    /// durations, providers and the crit boost's min/max stay in
    /// <see cref="State"/>, which nothing networks (stock's
    /// <c>m_ConditionData</c> is not sent either).
    /// </para>
    /// </summary>
    public TFConditions(bool authoritative, ITFCondWords? words = null)
    {
        Authoritative = authoritative;
        _words = words;
        Pull();
    }

    /// <summary>Whether this is the server's copy (<c>GAME_DLL</c>).</summary>
    public bool Authoritative { get; }

    /// <summary>The whole state, by reference, for a netstate or a prediction ring to copy.</summary>
    public ref readonly TFConditionState State
    {
        get
        {
            Pull();
            return ref _state;
        }
    }

    /// <summary>The active conditions.</summary>
    public TFCondMask Bits
    {
        get
        {
            Pull();
            return _state.Bits;
        }
    }

    /// <summary>Replaces the whole state with <paramref name="state"/> (a prediction restore).</summary>
    public void Restore(in TFConditionState state)
    {
        _state = state;
        Push();
    }

    /// <summary>The active set from the bound words, when there are any.</summary>
    private void Pull()
    {
        if (_words is not null)
            _state.Bits = _words.Read();
    }

    /// <summary>The active set back to the bound words, when there are any and it changed.</summary>
    private void Push()
    {
        if (_words is not null && _words.Read() != _state.Bits)
            _words.Write(in _state.Bits);
    }

    /// <summary>
    /// Takes the networked words (a client's <c>OnDataChanged</c>), leaving
    /// the rest of the state as it was, and returns what changed: the client
    /// runs <c>OnConditionAdded</c> for each of <see cref="TFCondDelta.Added"/>
    /// and <c>OnConditionRemoved</c> for each of <see cref="TFCondDelta.Removed"/>
    /// (<c>SyncConditions</c>, :1504-1538).
    /// </summary>
    public TFCondDelta ReceiveBits(in TFCondMask bits)
    {
        Pull();
        TFCondMask before = _state.Bits;
        _state.Bits = bits;
        Push();
        return new TFCondDelta(TFCondMask.Added(before, bits), TFCondMask.Removed(before, bits));
    }

    /// <summary><c>InCond</c>: whether <paramref name="cond"/> is active.</summary>
    public bool Has(TFCond cond)
    {
        Pull();
        return _state.Bits.Has(cond);
    }

    /// <summary><c>m_ConditionData[cond].m_bPrevActive</c>: at the last add, the condition still had time on it.</summary>
    public bool WasActiveAtAdd(TFCond cond) => _state.PrevActive.Has(cond);

    /// <summary>
    /// <c>GetConditionDuration</c> (:1251-1262): the seconds left, or
    /// <see cref="Permanent"/>, of an active condition; 0 when inactive. For
    /// <c>Critboosted</c> it reads the slot stock never writes, so 0: the
    /// boost's own durations are in <see cref="State"/>.
    /// </summary>
    public float Duration(TFCond cond) => Has(cond) ? _state.ExpireTimes[TFCondMask.Check(cond)] : 0f;

    /// <summary><c>SetConditionDuration</c> (<c>tf_player_shared.h:314-318</c>): overwrites the seconds left.</summary>
    public void SetDuration(TFCond cond, float duration) => _state.ExpireTimes[TFCondMask.Check(cond)] = duration;

    /// <summary><c>GetConditionProvider</c> (:1267-1286): who applied an active condition, else none.</summary>
    public EntityHandle Provider(TFCond cond) =>
        Has(cond) ? _state.Providers[TFCondMask.Check(cond)] : EntityHandle.None;

    /// <summary>
    /// The damage this condition has prevented so far: stock's
    /// <c>m_nPreventedDamageFromCondition</c>, read when the condition ends
    /// (tf_player_shared.cpp:1184-1198). Stock's only writer is the phase
    /// dodge's damage rules (tf_player.cpp:9330-9361), which are not ported.
    /// </summary>
    public int PreventedDamage(TFCond cond) => _state.PreventedDamage[TFCondMask.Check(cond)];

    /// <summary>Accumulates the condition's prevented damage (stock's <c>m_nPreventedDamageFromCondition +=</c>).</summary>
    public void AddPreventedDamage(TFCond cond, int amount) => _state.PreventedDamage[TFCondMask.Check(cond)] += amount;

    /// <summary>Stock's post-event zero (:1197).</summary>
    public void ClearPreventedDamage(TFCond cond) => _state.PreventedDamage[TFCondMask.Check(cond)] = 0;

    /// <summary>
    /// <c>AddCond</c> (:1105-1161). A finite <paramref name="duration"/> never
    /// shortens what is left and never ends a permanent condition: stock keeps
    /// the current time when it is permanent or longer (:1144-1153). A
    /// permanent add makes it permanent. <c>Critboosted</c> follows the
    /// condition list's min/max rule instead (<c>tf_condition.cpp:57-93,
    /// 315-340</c>).
    /// </summary>
    public TFCondAdd Add(TFCond cond, float duration = Permanent, EntityHandle provider = default)
    {
        Pull();
        var added = AddCore(cond, duration, provider);
        Push();
        return added;
    }

    private TFCondAdd AddCore(TFCond cond, float duration, EntityHandle provider)
    {
        int index = TFCondMask.Check(cond);
        if (cond == TFCond.Critboosted)
        {
            return AddCritBoost(duration, provider);
        }

        bool wasActive = _state.Bits.Has(cond);
        _state.Bits.Set(cond);

        if (_state.ExpireTimes[index] != 0f)
        {
            _state.PrevActive.Set(cond);
        }
        else
        {
            _state.PrevActive.Clear(cond);
        }

        if (duration != Permanent)
        {
            float current = _state.ExpireTimes[index];
            if (current == Permanent || duration < current)
            {
                duration = current;
            }
        }

        _state.ExpireTimes[index] = duration;
        _state.Providers[index] = provider;
        return wasActive ? TFCondAdd.Refreshed : TFCondAdd.Added;
    }

    /// <summary>
    /// <c>RemoveCond</c> (:1166-1204): true when <paramref name="cond"/> was
    /// active and now is not (stock then runs <c>OnConditionRemoved</c>).
    /// <c>Critboosted</c> is removed only on the server, and not while its
    /// min duration is left unless <paramref name="ignoreDuration"/>: then its
    /// max drops to the min and it stays (<c>tf_condition.cpp:113-139</c>).
    /// </summary>
    public bool Remove(TFCond cond, bool ignoreDuration = false)
    {
        Pull();
        bool removed = RemoveCore(cond, ignoreDuration);
        Push();
        return removed;
    }

    private bool RemoveCore(TFCond cond, bool ignoreDuration)
    {
        TFCondMask.Check(cond);
        if (!_state.Bits.Has(cond))
        {
            return false;
        }

        if (cond == TFCond.Critboosted)
        {
            if (!Authoritative)
            {
                return false;
            }

            if (!ignoreDuration && _state.CritBoostMinDuration > 0f)
            {
                _state.CritBoostMaxDuration = _state.CritBoostMinDuration;
                return false;
            }

            ClearCritBoost();
            return true;
        }

        Clear(cond);
        return true;
    }

    /// <summary>
    /// <c>RemoveAllCond</c> (:1545-1564): every condition goes, the crit boost
    /// whatever its min duration (<c>CTFConditionList::RemoveAll</c>). Returns
    /// the set that was active.
    /// </summary>
    public TFCondMask RemoveAll()
    {
        Pull();
        TFCondMask removed = RemoveAllCore();
        Push();
        return removed;
    }

    private TFCondMask RemoveAllCore()
    {
        TFCondMask removed = _state.Bits;
        if (removed.Has(TFCond.Critboosted))
        {
            ClearCritBoost();
        }

        foreach (TFCond cond in _state.Bits)
        {
            Clear(cond);
        }

        _state.Bits = default;
        return removed;
    }

    /// <summary>
    /// The countdown of <c>ConditionGameRulesThink</c> (:2338, :2346-2375),
    /// server only: every active, non-permanent condition loses
    /// <paramref name="frametime"/> seconds and is removed when it reaches 0.
    /// A condition in <paramref name="expiresFast"/> loses
    /// <c>healers × frametime × 4</c> more while <paramref name="healers"/> is
    /// above 0, <c>Urine</c> <c>healers × frametime</c> (:2357-2367). The crit
    /// boost counts its min and max down first and never faster
    /// (<c>CTFConditionList::ServerThink</c>, <c>tf_condition.cpp:196-242</c>).
    /// Returns the conditions that expired; no allocation.
    /// </summary>
    public TFCondMask Tick(float frametime, int healers, in TFCondMask expiresFast)
    {
        if (!Authoritative)
        {
            throw new InvalidOperationException(
                "condition expiry is server-only (ConditionGameRulesThink is GAME_DLL)");
        }

        Pull();
        TFCondMask expired = TickCore(frametime, healers, in expiresFast);
        Push();
        return expired;
    }

    private TFCondMask TickCore(float frametime, int healers, in TFCondMask expiresFast)
    {

        var expired = default(TFCondMask);
        if (_state.Bits.Has(TFCond.Critboosted) && TickCritBoost(frametime))
        {
            expired.Set(TFCond.Critboosted);
        }

        TFCondMask active = _state.Bits;
        foreach (TFCond cond in active)
        {
            int index = (int)cond;
            if (cond == TFCond.Critboosted || _state.ExpireTimes[index] == Permanent)
            {
                continue;
            }

            float reduction = frametime;
            if (healers > 0 && expiresFast.Has(cond))
            {
                reduction += cond == TFCond.Urine ? healers * reduction : healers * reduction * 4;
            }

            float left = MathF.Max(_state.ExpireTimes[index] - reduction, 0f);
            _state.ExpireTimes[index] = left;
            if (left == 0f)
            {
                Clear(cond);
                expired.Set(cond);
            }
        }

        return expired;
    }

    private TFCondAdd AddCritBoost(float duration, EntityHandle provider)
    {
        if (!Authoritative)
        {
            return TFCondAdd.Ignored;
        }

        const int index = (int)TFCond.Critboosted;
        if (!_state.Bits.Has(TFCond.Critboosted))
        {
            _state.Bits.Set(TFCond.Critboosted);
            _state.CritBoostMaxDuration = duration;
            _state.CritBoostMinDuration = 0f;
            _state.Providers[index] = provider;
            return TFCondAdd.Added;
        }

        MergeCritBoost(duration);
        _state.Providers[index] = provider;
        return TFCondAdd.Merged;
    }

    /// <summary><c>CTFCondition::Add</c> (<c>tf_condition.cpp:315-340</c>).</summary>
    private void MergeCritBoost(float duration)
    {
        if (duration != Permanent)
        {
            if (_state.CritBoostMaxDuration == Permanent || duration < _state.CritBoostMaxDuration)
            {
                if (duration > _state.CritBoostMinDuration)
                {
                    _state.CritBoostMinDuration = duration;
                }

                return;
            }
        }
        else if (_state.CritBoostMaxDuration != Permanent)
        {
            _state.CritBoostMinDuration = _state.CritBoostMaxDuration;
        }

        _state.CritBoostMaxDuration = duration;
    }

    /// <summary>The crit boost's arm of <c>CTFConditionList::ServerThink</c>; true when it expired.</summary>
    private bool TickCritBoost(float frametime)
    {
        if (_state.CritBoostMaxDuration <= Permanent && _state.CritBoostMinDuration <= Permanent)
        {
            return false;
        }

        float reduction = frametime;
        if (_state.CritBoostMinDuration > Permanent)
        {
            _state.CritBoostMinDuration = MathF.Max(_state.CritBoostMinDuration - reduction, 0f);
        }

        if (_state.CritBoostMaxDuration > Permanent)
        {
            _state.CritBoostMaxDuration = MathF.Max(_state.CritBoostMaxDuration - reduction, 0f);
            if (_state.CritBoostMaxDuration < _state.CritBoostMinDuration)
            {
                _state.CritBoostMaxDuration = _state.CritBoostMinDuration;
            }
        }

        if (_state.CritBoostMaxDuration == 0f)
        {
            return RemoveCore(TFCond.Critboosted, ignoreDuration: false);
        }

        return false;
    }

    /// <summary>
    /// The crit boost's removal: the list deletes its object, so the bit, the
    /// durations and the provider go; <c>RemoveCond</c>'s bookkeeping of
    /// <c>m_ConditionData</c> never runs for it (it returns at :1178-1179).
    /// </summary>
    private void ClearCritBoost()
    {
        _state.Bits.Clear(TFCond.Critboosted);
        _state.CritBoostMaxDuration = 0f;
        _state.CritBoostMinDuration = 0f;
        _state.Providers[(int)TFCond.Critboosted] = EntityHandle.None;
    }

    /// <summary>The bookkeeping half of <c>RemoveCond</c> (:1181, :1200-1202).</summary>
    private void Clear(TFCond cond)
    {
        int index = (int)cond;
        _state.Bits.Clear(cond);
        _state.PrevActive.Clear(cond);
        _state.ExpireTimes[index] = 0f;
        _state.Providers[index] = EntityHandle.None;
    }
}
