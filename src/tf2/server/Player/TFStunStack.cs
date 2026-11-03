//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Items;
using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// <c>stun_struct_t</c> (tf_player_shared.h:56): one stun on the stack — who
/// dealt it, how long it lasts and when it expires, when its fade anim may
/// start, how strong it is (0..255) and what it does. The three times and the
/// amount are the fields stock rewrites after the append
/// (<c>GetActiveStunInfo()-&gt;</c>, tf_player_shared.cpp:9857-9886).
/// </summary>
public sealed class TFStunEntry(EntityHandle attacker, float duration, float expireTime,
                                float startFadeTime, float amount, TFStunFlags flags)
{
    /// <summary><c>hPlayer</c>.</summary>
    public EntityHandle Attacker { get; } = attacker;

    /// <summary><c>flDuration</c>.</summary>
    public float Duration { get; } = duration;

    /// <summary><c>flExpireTime</c>; the controls stun grows it by the anim time.</summary>
    public float ExpireTime { get; set; } = expireTime;

    /// <summary><c>flStartFadeTime</c>.</summary>
    public float StartFadeTime { get; set; } = startFadeTime;

    /// <summary><c>flStunAmount</c>: 0..255, the size of the slow.</summary>
    public float Amount { get; set; } = amount;

    /// <summary><c>iStunFlags</c>.</summary>
    public TFStunFlags Flags { get; } = flags;
}

/// <summary>
/// The player's stun stack (Stage "later" of the triggers lane):
/// <c>CTFPlayerShared</c>'s server-side <c>m_PlayerStuns</c> and
/// <c>m_iStunIndex</c> (tf_player_shared.h:1138-1232) — the
/// <see cref="Stun"/> port of <c>StunPlayer</c> (tf_player_shared.cpp:9766-9933),
/// the expiry half of <c>ConditionThink</c> in <see cref="Tick"/> (:3172-3211),
/// and the <c>OnRemoveStunned</c> stack wipe (:7384-7404, arriving one tick
/// late — every reader of the stack checks <c>TF_COND_STUNNED</c> first and
/// <see cref="Stun"/>'s own entry refuses a stunned-without-condition player,
/// as stock's <c>GetActiveStunInfo</c> arms at :9816-9826 do).
///
/// <para>
/// The condition's duration is the stack's, not the countdown's: the stun is
/// added permanent (<c>-1</c>, :9932) and the <see cref="Tick"/> expiry is what
/// ends it, promoting the next-strongest queued stun (:3187-3204) or removing
/// the condition when the stack runs out (:3205-3208).
/// </para>
///
/// <para>
/// The legacy mirror (<see cref="MirrorToCarrier"/>: the four carrier fields
/// stock's <c>UpdateLegacyStunSystem</c> copies at :7441-7465, after the
/// append :9889 and after the expiry arm :3211, and <c>m_iStunIndex</c>'s
/// own clearings at :3185, :7389, :9824 and :1025) rides the player's
/// <c>tf_player_data</c> carrier rather than stock's net fields — that is
/// how this build sends the same words — and it is what
/// <c>TFMovementRules.MovementStun</c> (tf_gamemovement.cpp:537-627) decides
/// from on both sides.
/// </para>
///
/// <para>
/// What stock wraps around the stack that this build has no half of, and so
/// leaves out (docs/divergences.md): the
/// taunt camera, <c>SpeakConceptIfAllowed</c>, <c>StunSound</c> and
/// <c>StopTaunt</c>/<c>ClearExpression</c> (:9891-9928, :9924-9929), the
/// <c>player_stunned</c> event (its <c>victim_capping</c> needs
/// <c>IsCapturingPoint()</c>, which the flag lane does not publish), and the
/// movement parity bump (:3201). The truce refusal (:9784-9789) needs an
/// attacker and a truce, neither of which this build has. Stock's
/// <c>IsControlStunned</c>/<c>IsLoserStateStunned</c> read the legacy
/// <c>m_iStunFlags</c>, which the legacy system only ever copies from the
/// active entry — read from the entry itself, they say the same thing.
/// </para>
/// </summary>
public sealed class TFStunStack(TFPlayerCarriers carriers, ITFMapPlayers players, IGameClock clock) : IPlayerComponent
{
    /// <summary><c>CONTROL_STUN_ANIM_TIME</c> (tf_player_shared.h:203).</summary>
    public const float ControlStunAnimTime = 1.5f;

    /// <summary>The <c>Count() + 1 &gt;= 250</c> insanity cap (:9770).</summary>
    public const int StunStackCap = 250;

    private readonly List<TFStunEntry> _stack = new();
    private int _activeIndex = -1;
    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;

    /// <summary>After the condition countdown (20): <c>ConditionThink</c> runs the expiry after the countdown thought (:3170 before :3172).</summary>
    public int Order => 30;

    /// <summary><c>GetActiveStunInfo()</c>: the active entry, or null.</summary>
    public TFStunEntry? ActiveStun => _activeIndex >= 0 && _activeIndex < _stack.Count ? _stack[_activeIndex] : null;

    /// <summary><c>m_PlayerStuns</c>, the queued ones included.</summary>
    public IReadOnlyList<TFStunEntry> Stuns => _stack;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _activeIndex = -1;
        _stack.Clear();
        carriers.EnterPart(_index, this);
    }

    /// <summary>
    /// <c>ConditionThink</c>'s stun arms (:3172-3217): the expiry of the active
    /// stun and the promotion of the next-strongest queued one; and the
    /// <c>OnRemoveStunned</c> wipe (:7384-7404) once the condition is gone by
    /// any route — death's <c>RemoveAllCond</c>, a removal, an expiry. The
    /// control-stun fade (:3213-3217) is the client's ending animation.
    /// </summary>
    public void Tick(float now)
    {
        if (_player == EntityHandle.None)
            return;

        if (!players.InCond(_player, TFCond.Stunned))
        {
            _stack.Clear();
            _activeIndex = -1;
            MirrorToCarrier(); // the OnRemoveStunned index clear, the condition gone by any route (:7389)
            return;
        }

        if (ActiveStun is not { } active || now <= active.ExpireTime)
            return;

        _stack.RemoveAt(_activeIndex);
        _activeIndex = -1;

        if (_stack.Count > 0)
        {
            // Apply our next stun (:3187-3204): the strongest of the rest, first tie-winner.
            int strongest = 0;
            for (int i = 1; i < _stack.Count; i++)
            {
                if (_stack[i].Amount > _stack[strongest].Amount)
                    strongest = i;
            }

            _activeIndex = strongest;   // m_iStunIndex = iStrongestIdx (:3198)
            players.AddCond(_player, TFCond.Stunned, TFConditions.Permanent); // :3200 (the entry keeps its attacker; the seam carries no provider)
        }
        else
        {
            players.RemoveCond(_player, TFCond.Stunned); // :3207
        }

        MirrorToCarrier(); // the legacy copy, after the expiry arm (:3211)
    }

    public void Detach()
    {
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
        _player = EntityHandle.None;
        _stack.Clear();
        _activeIndex = -1;
    }

    /// <summary>
    /// <c>CTFPlayerShared::Spawn</c>'s stun reset (tf_player_shared.cpp:1016-1025),
    /// which <c>CTFPlayer::Spawn</c> runs: the stack and the active index go
    /// with the rest of the old life.
    /// </summary>
    public void ClearOnSpawn()
    {
        _stack.Clear();
        _activeIndex = -1;
        MirrorToCarrier(); // Spawn clears the index (:1025), not the value words
    }

    /// <summary>
    /// <c>CTFPlayerShared::StunPlayer</c> (tf_player_shared.cpp:9766-9933): the
    /// refusals, the remap of the reduction to 0..255, the stomp or queue of the
    /// new stun against the active one, the controls stun's extra anim time,
    /// and the permanent <c>TF_COND_STUNNED</c>.
    /// </summary>
    public void Stun(EntityHandle player, float time, float reductionAmount, TFStunFlags flags, EntityHandle attacker)
    {
        float now = clock.Now;

        // Insanity prevention (:9769-9771).
        if (_stack.Count + 1 >= StunStackCap)
            return;

        if (players.InCond(player, TFCond.Phase) || players.InCond(player, TFCond.PasstimeInterception))
            return;
        if (players.InCond(player, TFCond.Megaheal))
            return;
        if (players.InCond(player, TFCond.InvulnerableHideUnlessDamaged)
            && !players.InCond(player, TFCond.MvmBotStunRadiowave))
        {
            return;
        }

        // RemapValClamped( flReductionAmount, 0.0, 1.0, 0, 255 ) (:9791).
        float remapAmount = 255f * Math.Clamp(reductionAmount, 0f, 1f);

        bool stomp = false;
        if (players.InCond(player, TFCond.Stunned))
        {
            if (ActiveStun is { } active)
            {
                // Is it stronger than the active? (:9803-9807)
                if (remapAmount > active.Amount || (flags & TFStunFlags.Controls) != 0 || (flags & TFStunFlags.LoserState) != 0)
                {
                    stomp = true;
                }
                // It's weaker. Would it expire before the active? (:9808-9813)
                else if (now + time < active.ExpireTime)
                {
                    return;
                }
            }
        }
        else if (_activeIndex >= 0)
        {
            // Something yanked TF_COND_STUNNED out unexpectedly (:9816-9826).
            _stack.Clear();
            _activeIndex = -1;
            MirrorToCarrier(); // this arm clears the index too (:9824)
            return;
        }

        var stun = new TFStunEntry(attacker, time, now + time, now + time, remapAmount, flags);

        // Should this become the active stun? (:9839-9874)
        if (stomp || ActiveStun is null)
        {
            // If stomping, see if the stun we're replacing has a stronger slow. (:9842-9860)
            float oldStun = ActiveStun?.Amount ?? 0f;

            _stack.Add(stun);
            _activeIndex = _stack.Count - 1; // m_iStunIndex = AddToTail

            if (oldStun > remapAmount)
                _stack[_activeIndex].Amount = oldStun;
        }
        else
        {
            _stack.Add(stun); // queued behind the active (:9862-9873)
            return;
        }

        // Add in extra time when TF_STUN_CONTROLS (:9877-9884); the Halloween-kart
        // exception has no kart in this build.
        if ((_stack[_activeIndex].Flags & TFStunFlags.Controls) != 0)
            _stack[_activeIndex].ExpireTime += ControlStunAnimTime;

        _stack[_activeIndex].StartFadeTime = now + _stack[_activeIndex].Duration; // :9886

        MirrorToCarrier(); // the legacy copy, after the append and its fix-ups (:9889)

        // The taunt camera, concepts, sounds and event around the append are
        // this build's out (docs/divergences.md).
        players.AddCond(player, TFCond.Stunned, TFConditions.Permanent); // AddCond( TF_COND_STUNNED, -1.f, pAttacker ) (:9932)
    }

    /// <summary>
    /// <c>UpdateLegacyStunSystem</c> (tf_player_shared.cpp:7441-7465) onto the
    /// player's carrier: while an entry is active the copy hands over its flags
    /// (:7453), its 0..255 slow (:7452) and its end (:7451); the index — stock
    /// sends <c>m_iStunIndex</c> itself (:597) and clears it at the expiry
    /// (:3185), at <c>OnRemoveStunned</c> (:7389), at the unexpected clear
    /// (:9824) and at Spawn (:1025) — follows the active row wherever it moves
    /// (:9847, :3198). Behind a cleared index the value words stay stale, as
    /// they do in stock: every reader gates on the index and the condition
    /// first (:9938-9975). Stock's parity bump (:7454) has no twin — the
    /// carrier's arrival is the client's word. The queue path of
    /// <see cref="Stun"/> never reaches a copy, as stock's returns before
    /// :9889 (:9866-9873).
    /// </summary>
    private void MirrorToCarrier()
    {
        if (carriers.Get(_index) is not { } state)
            return;

        state.StunIndex = _activeIndex;
        if (ActiveStun is { } active)
        {
            state.StunFlags = (int)active.Flags;              // :7453
            state.MovementStunAmount = active.Amount;         // :7452
            state.StunExpireTime = active.ExpireTime;         // :7451
        }
    }

    /// <summary><c>GetAmountStunned</c> (:9938-9947): the active stun's 0..1 slow for a matching flag set.</summary>
    public float AmountStunned(TFStunFlags flags)
    {
        if (ActiveStun is { } active && players.InCond(_player, TFCond.Stunned)
            && (flags & active.Flags) != 0 && active.ExpireTime > clock.Now)
        {
            return Math.Clamp(active.Amount, 0f, 255f) * (1f / 255f);
        }

        return 0f;
    }

    /// <summary><c>IsControlStunned</c> (:9952-9961), read through the active entry.</summary>
    public bool IsControlStunned()
        => ActiveStun is { } active && players.InCond(_player, TFCond.Stunned)
           && (active.Flags & TFStunFlags.Controls) != 0;

    /// <summary><c>IsLoserStateStunned</c> (:9966-9975), read through the active entry.</summary>
    public bool IsLoserStateStunned()
        => ActiveStun is { } active && players.InCond(_player, TFCond.Stunned)
           && (active.Flags & TFStunFlags.LoserState) != 0;

    /// <summary><c>IsSnared</c> (:9980-9986): slowed, but the controls are free.</summary>
    public bool IsSnared() => players.InCond(_player, TFCond.Stunned) && !IsControlStunned();
}
