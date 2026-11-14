//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// <c>CTFPlayerShared</c>'s damage-event list and the crit multiplier it earns
/// (<c>m_DamageEvents</c>, <c>m_iCritMult</c>, tf_player_shared.cpp:9520-9697):
/// every hit the player deals lands as one event on the list, every half-second
/// think drops what <c>tf_damage_events_track_for</c> says is too old and
/// remaps what is left onto the network byte of
/// <see cref="TFPlayerNetState.CritMult"/>, and the fists' radial buff pays a
/// temporary bonus onto the same list. The list itself stays server-side as
/// stock's does (<c>m_iCritMult</c> alone networks, tf_player_shared.cpp:539).
/// </summary>
public sealed class TFCritMultTicker(TFPlayerCarriers carriers,
                                     TFDamageEventsTrackForConVar trackFor) : IPlayerComponent
{
    /// <summary><c>TF_DAMAGE_CRITMOD_MAXTIME</c> (<c>tf_shareddefs.h:151</c>): the oldest event still summed.</summary>
    public const float MaxTime = 20f;

    /// <summary><c>TF_DAMAGE_CRITMOD_MINTIME</c> (:152): how old an event must be before it counts — stock's window for the event to reach the client first (:9546-9547).</summary>
    public const float MinTime = 2f;

    /// <summary><c>TF_DAMAGE_CRITMOD_DAMAGE</c> (:153): the summed damage that tops the multiplier out.</summary>
    public const float DamageCeiling = 800f;

    /// <summary><c>TF_DAMAGE_CRITMOD_MAXMULT</c> (:154): the multiplier ceiling the byte remaps across.</summary>
    public const float MaxMult = 6f;

    /// <summary><c>MAX_DAMAGE_EVENTS</c> (<c>tf_player_shared.cpp:236</c>).</summary>
    public const int MaxDamageEvents = 128;

    /// <summary>The think's half second (<c>ConditionGameRulesThink</c>, :2343).</summary>
    public const float UpdateSeconds = 0.5f;

    /// <summary><c>CRIT_DAMAGE_TIME</c> (:9569): how fresh a blast must be to fold into the blast before it.</summary>
    public const float BlastFoldWindow = 0.1f;

    private readonly List<(float Damage, float Scale, DamageType Bits, float Time)> _events = [];
    private int _index = -1;
    private float _nextUpdate;

    /// <summary>After the donk list (33), before the respawn override (40): the meter reads the hits this frame's damage booked.</summary>
    public int Order => 34;

    /// <summary>The byte the last think computed, beside the carrier word it wrote.</summary>
    public int CritMultByte { get; private set; }

    public void Attach(EntityHandle player)
    {
        _index = player.EntryIndex;
        carriers.EnterPart(_index, this);
    }

    public void Detach()
    {
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
        _events.Clear();
    }

    /// <summary>
    /// Stock's think gate (<c>ConditionGameRulesThink</c>, :2340-2344): once
    /// the half second is past, strict, the update runs and the next half
    /// starts from the frame's time. The first think runs at once behind the
    /// zeroed member, as stock's does (:2340).
    /// </summary>
    public void Tick(float now)
    {
        if (_nextUpdate < now)
        {
            UpdateCritMult(now);
            _nextUpdate = now + UpdateSeconds;
        }
    }

    /// <summary>
    /// <c>RecordDamageEvent</c> (:9574-9660) on the hit's final numbers: the
    /// crit bonus portion counts for nothing (:9583), bits carrying
    /// <c>DMG_DONT_COUNT_DAMAGE_TOWARDS_CRIT_RATE</c> — stock's own alias of
    /// <see cref="DamageType.Dissolve"/> (<c>tf_shareddefs.h:1171</c>) — scale
    /// to zero (:9585-9587), the killing blow is capped at the victim's health
    /// before the damage (:9590-9593), and a blast inside a tenth of a second
    /// of the blast before it folds that one to the larger damage instead of
    /// stacking (:9597-9649). The kills tally and achievement arms of the fold
    /// are stock's MvM halves and stay out here. Stock trims the full list by
    /// its LAST index and lets the tail add refill it (:9576-9580); held.
    /// </summary>
    public void RecordDamageEvent(TFHitResult result, bool kill, float prevHealth, float now)
    {
        if (_events.Count >= MaxDamageEvents)
            _events.RemoveAt(_events.Count - 1);

        float damage = result.Damage - result.Bonus;                                    // :9583
        float scale = (result.Bits & DamageType.Dissolve) != 0 ? 0f : 1f;               // :9585-9587
        if (kill && damage > prevHealth)                                                // :9590-9593
            damage = prevHealth;

        bool bOverride = false;
        if ((result.Bits & DamageType.Blast) != 0)                                      // :9597
        {
            for (int i = 0; i < _events.Count; i++)
            {
                var earlier = _events[i];
                if ((earlier.Bits & DamageType.Blast) == 0)                             // :9603 stock asks its stored type
                    continue;
                if (now - earlier.Time >= BlastFoldWindow)                              // :9606 strict under
                    continue;

                // Take the max damage done in the time frame (:9630-9639).
                if (damage > earlier.Damage)
                    _events[i] = (damage, scale, result.Bits, now);
                bOverride = true;
            }
        }

        if (bOverride)                                                                  // :9648-9649
            return;

        _events.Add((damage, scale, result.Bits, now));                                 // :9651-9655
    }

    /// <summary>
    /// <c>AddTempCritBonus</c> (:9683-9696): the 0..1 amount on the 800-point
    /// scale, divided across the multiplier range (:9692), a generic hit of its
    /// own time at scale one (:9693-9695). The fists' radial buff pays 0.5
    /// through it (tf_player.cpp:18599). The full-list trim is the same last
    /// index the record uses (:9685-9689).
    /// </summary>
    public void AddTempCritBonus(float amount, float now)
    {
        if (_events.Count >= MaxDamageEvents)
            _events.RemoveAt(_events.Count - 1);

        _events.Add((CritBucket.RemapValClamped(amount, 0f, 1f, 0f, DamageCeiling) / (MaxMult - 1f),
                     1f, DamageType.Generic, now));
    }

    /// <summary>
    /// <c>UpdateCritMult</c> (:9520-9567): an empty list hands the byte its
    /// floor through stock's own remap of the minimum multiplier onto 1..4
    /// (:9525-9529 — the upper bound there is stock's 4, not the ceiling of 6);
    /// otherwise the walk runs backwards over the list (:9536), dropping what
    /// <c>tf_damage_events_track_for</c> says is too old (:9539-9544), skipping
    /// what is younger than <see cref="MinTime"/> or older than
    /// <see cref="MaxTime"/> (:9548-9555), and summing damage times scale
    /// (:9559). The total maps to the multiplier and the multiplier onto the
    /// byte, truncated (:9562-9566); the word lands on the carrier's sent
    /// <see cref="TFPlayerNetState.CritMult"/> (:539).
    /// </summary>
    private void UpdateCritMult(float now)
    {
        int critMultByte;
        if (_events.Count == 0)
        {
            critMultByte = (int)CritBucket.RemapValClamped(1f, 1f, 4f, 0f, 255f);       // :9527
        }
        else
        {
            float totalDamage = 0f;
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                var damageEvent = _events[i];
                float delta = now - damageEvent.Time;
                if (delta > trackFor.Value)                                             // :9539-9543 expired, off the list
                {
                    _events.RemoveAt(i);
                    continue;
                }
                if (delta < MinTime)                                                    // :9548-9551 too fresh for the client
                    continue;
                if (delta > MaxTime)                                                    // :9554-9555 kept, but counted nowhere
                    continue;

                totalDamage += damageEvent.Damage * damageEvent.Scale;                  // :9559
            }

            float mult = CritBucket.RemapValClamped(totalDamage, 0f, DamageCeiling, 1f, MaxMult);   // :9562
            critMultByte = (int)CritBucket.RemapValClamped(mult, 1f, MaxMult, 0f, 255f);            // :9566 the truncation is stock's
        }

        CritMultByte = critMultByte;
        if (carriers.Get(_index) is { } state)
            state.CritMult = critMultByte;
    }
}
