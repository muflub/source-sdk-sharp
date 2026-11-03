using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFConditions"/> against stock's <c>AddCond</c>/<c>RemoveCond</c>/
/// <c>ConditionGameRulesThink</c> (<c>tf_player_shared.cpp</c>) and
/// <c>CTFConditionList</c> (<c>tf_condition.cpp</c>), stage1 Phase 1.7. Ticks
/// use power-of-two frametimes so the countdown is exact in single precision.
/// </summary>
public sealed class TFConditionsTests
{
    private const float Tick = 0.25f;

    private static TFConditions Server() => new(authoritative: true);

    private static TFConditions Client() => new(authoritative: false);

    private static TFCondMask Tick1(TFConditions conditions) => conditions.Tick(Tick, 0, default);

    [Fact]
    public void Add_makes_the_condition_active()
    {
        TFConditions conditions = Server();
        Assert.False(conditions.Has(TFCond.Burning));

        Assert.Equal(TFCondAdd.Added, conditions.Add(TFCond.Burning, 4f));

        Assert.True(conditions.Has(TFCond.Burning));
        Assert.Equal(4f, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void Add_sets_the_stock_word_and_bit()
    {
        TFConditions conditions = Server();

        conditions.Add(TFCond.Aiming);
        conditions.Add(TFCond.SpeedBoost);
        conditions.Add(TFCond.ImmuneToPushback);

        TFCondMask bits = conditions.Bits;
        Assert.Equal(1 << 0, bits.Word0);
        Assert.Equal(1 << (32 - 32), bits.Word1);
        Assert.Equal(1 << (130 - 128), bits.Word4);
        Assert.Equal(0, bits.Word2 | bits.Word3);
    }

    [Fact]
    public void A_condition_expires_on_the_tick_its_time_runs_out()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Teleported, 1f);

        for (int tick = 1; tick <= 3; tick++)
        {
            Assert.True(Tick1(conditions).IsEmpty);
            Assert.True(conditions.Has(TFCond.Teleported));
        }

        TFCondMask expired = Tick1(conditions);

        Assert.Equal(TFCondMask.Of(TFCond.Teleported), expired);
        Assert.False(conditions.Has(TFCond.Teleported));
    }

    [Fact]
    public void The_countdown_is_the_frametime_not_the_clock()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Teleported, 1f);

        conditions.Tick(0.125f, 0, default);

        Assert.Equal(0.875f, conditions.Duration(TFCond.Teleported));
    }

    [Fact]
    public void A_zero_duration_lasts_until_the_next_tick()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Teleported, 0f);
        Assert.True(conditions.Has(TFCond.Teleported));

        Assert.True(Tick1(conditions).Has(TFCond.Teleported));
    }

    [Fact]
    public void A_permanent_condition_never_expires()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Aiming, TFConditions.Permanent);

        for (int tick = 0; tick < 10_000; tick++)
        {
            Assert.True(Tick1(conditions).IsEmpty);
        }

        Assert.True(conditions.Has(TFCond.Aiming));
        Assert.Equal(TFConditions.Permanent, conditions.Duration(TFCond.Aiming));
    }

    [Fact]
    public void Add_defaults_to_permanent()
    {
        TFConditions conditions = Server();

        conditions.Add(TFCond.Zoomed);

        Assert.Equal(TFConditions.Permanent, conditions.Duration(TFCond.Zoomed));
    }

    [Fact]
    public void Readding_longer_extends_the_time_left()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 2f);

        Assert.Equal(TFCondAdd.Refreshed, conditions.Add(TFCond.Burning, 5f));

        Assert.Equal(5f, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void Readding_shorter_keeps_the_time_left()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 5f);

        Assert.Equal(TFCondAdd.Refreshed, conditions.Add(TFCond.Burning, 2f));

        Assert.Equal(5f, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void Readding_finite_keeps_a_permanent_condition_permanent()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, TFConditions.Permanent);

        conditions.Add(TFCond.Burning, 3f);

        Assert.Equal(TFConditions.Permanent, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void Readding_permanent_makes_a_timed_condition_permanent()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 3f);

        conditions.Add(TFCond.Burning, TFConditions.Permanent);

        Assert.Equal(TFConditions.Permanent, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void Readding_replaces_the_provider()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 3f, new EntityHandle(5));

        conditions.Add(TFCond.Burning, 1f, new EntityHandle(7));

        Assert.Equal(new EntityHandle(7), conditions.Provider(TFCond.Burning));
    }

    [Fact]
    public void PrevActive_records_whether_time_was_left_at_the_add()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.MadMilk, 2f);
        Assert.False(conditions.WasActiveAtAdd(TFCond.MadMilk));

        conditions.Add(TFCond.MadMilk, 2f);

        Assert.True(conditions.WasActiveAtAdd(TFCond.MadMilk));
    }

    [Fact]
    public void Has_after_Remove_is_false()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Disguised);

        Assert.True(conditions.Remove(TFCond.Disguised));

        Assert.False(conditions.Has(TFCond.Disguised));
    }

    [Fact]
    public void Remove_clears_the_duration_and_provider()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 3f, new EntityHandle(9));

        conditions.Remove(TFCond.Burning);
        conditions.Add(TFCond.Burning, 1f);

        Assert.Equal(1f, conditions.Duration(TFCond.Burning));
        Assert.Equal(EntityHandle.None, conditions.Provider(TFCond.Burning));
    }

    [Fact]
    public void Remove_of_an_inactive_condition_reports_nothing()
    {
        TFConditions conditions = Server();

        Assert.False(conditions.Remove(TFCond.Disguised));
    }

    [Fact]
    public void Inactive_conditions_answer_no_duration_and_no_provider()
    {
        TFConditions conditions = Server();
        conditions.SetDuration(TFCond.Burning, 3f);

        Assert.Equal(0f, conditions.Duration(TFCond.Burning));
        Assert.Equal(EntityHandle.None, conditions.Provider(TFCond.Burning));
    }

    [Fact]
    public void An_expired_condition_is_cleared_like_a_removal()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Teleported, Tick, new EntityHandle(3));

        Tick1(conditions);

        Assert.Equal(EntityHandle.None, conditions.Provider(TFCond.Teleported));
        Assert.Equal(TFCondAdd.Added, conditions.Add(TFCond.Teleported, 1f));
    }

    [Fact]
    public void RemoveAll_returns_the_active_set_and_clears_it()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Aiming);
        conditions.Add(TFCond.Burning, 3f);
        conditions.Add(TFCond.Critboosted, 5f);
        conditions.Add(TFCond.Critboosted, 2f);

        TFCondMask removed = conditions.RemoveAll();

        Assert.Equal(TFCondMask.Of([TFCond.Aiming, TFCond.Burning, TFCond.Critboosted]), removed);
        Assert.True(conditions.Bits.IsEmpty);
    }

    [Fact]
    public void Healers_wear_off_an_expires_fast_condition_five_times_as_fast_per_healer()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Bleeding, 4f);

        conditions.Tick(Tick, healers: 1, TFCondMask.Of(TFCond.Bleeding));

        Assert.Equal(4f - (Tick * 5), conditions.Duration(TFCond.Bleeding));
    }

    [Fact]
    public void Healers_wear_off_jarate_twice_as_fast_per_healer()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Urine, 4f);

        conditions.Tick(Tick, healers: 1, TFCondMask.Of(TFCond.Urine));

        Assert.Equal(4f - (Tick * 2), conditions.Duration(TFCond.Urine));
    }

    [Fact]
    public void Healers_do_not_speed_up_a_condition_outside_the_expires_fast_set()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Bleeding, 4f);

        conditions.Tick(Tick, healers: 3, default);

        Assert.Equal(4f - Tick, conditions.Duration(TFCond.Bleeding));
    }

    [Fact]
    public void Healers_wear_off_each_of_stocks_five_expires_fast_conditions_quickly()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 4f);
        conditions.Add(TFCond.Urine, 4f);
        conditions.Add(TFCond.Bleeding, 4f);
        conditions.Add(TFCond.MadMilk, 4f);
        conditions.Add(TFCond.Gas, 4f);

        conditions.Tick(Tick, healers: 1, new TFConditionEffects().ExpiresFastUnderHealing);

        Assert.Equal(4f - Tick * 5, conditions.Duration(TFCond.Burning));
        Assert.Equal(4f - Tick * 2, conditions.Duration(TFCond.Urine));
        Assert.Equal(4f - Tick * 5, conditions.Duration(TFCond.Bleeding));
        Assert.Equal(4f - Tick * 5, conditions.Duration(TFCond.MadMilk));
        Assert.Equal(4f - Tick * 5, conditions.Duration(TFCond.Gas));
    }

    [Fact]
    public void A_condition_outside_the_set_wears_off_at_the_plain_rate_under_healers()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.SpeedBoost, 4f);

        conditions.Tick(Tick, healers: 3, new TFConditionEffects().ExpiresFastUnderHealing);

        Assert.Equal(4f - Tick, conditions.Duration(TFCond.SpeedBoost));
    }

    [Fact]
    public void Without_a_healer_no_condition_wears_off_faster()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 4f);

        conditions.Tick(Tick, healers: 0, new TFConditionEffects().ExpiresFastUnderHealing);

        Assert.Equal(4f - Tick, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void Tick_is_server_only()
    {
        TFConditions conditions = Client();

        Assert.Throws<InvalidOperationException>(() => conditions.Tick(Tick, 0, default));
    }

    [Fact]
    public void A_client_predicts_ordinary_conditions()
    {
        TFConditions conditions = Client();

        Assert.Equal(TFCondAdd.Added, conditions.Add(TFCond.Zoomed));
        Assert.True(conditions.Remove(TFCond.Zoomed));
    }

    [Fact]
    public void Tick_over_a_thousand_cycles_allocates_nothing()
    {
        TFConditions conditions = Server();
        TFCondMask fast = TFCondMask.Of(TFCond.Bleeding);
        int expired = 0;

        // Nothing in Cycle allocates — a concrete struct enumerator, inline
        // arrays, no interface dispatch, no capture — so a window's charge is
        // the runtime's one-off on the measuring thread: a tier-up or a
        // call-counting stub's install (SteadyAllocation's note; this fact
        // read 4,360 bytes in one loaded full run, 1,992 at 48a4e1ac2). Each
        // hot method installs once, so of 20 windows at least one is clean,
        // while a steady per-cycle allocation is in every window and the
        // fewest bytes are still in the thousands.
        long allocated = SteadyAllocation.LeastAllocated(() =>
        {
            expired = 0;
            for (int i = 0; i < 1_000; i++)
            {
                expired += Cycle(conditions, fast);
            }
        }, warm: 2, samples: 20);

        Assert.Equal(3_000, expired);
        Assert.Equal(0, allocated);
    }

    private static int Cycle(TFConditions conditions, TFCondMask fast)
    {
        conditions.Add(TFCond.Burning, Tick);
        conditions.Add(TFCond.Bleeding, Tick, new EntityHandle(4));
        conditions.Add(TFCond.Critboosted, Tick);
        conditions.Add(TFCond.Aiming);
        int count = 0;
        foreach (TFCond cond in conditions.Tick(Tick, 2, fast))
        {
            count += cond == TFCond.Aiming ? 0 : 1;
        }

        return count;
    }

    [Fact]
    public void Restore_puts_back_a_copied_state()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 2f, new EntityHandle(6));
        conditions.Add(TFCond.Critboosted, 3f);
        TFConditionState saved = conditions.State;

        conditions.RemoveAll();
        conditions.Restore(saved);

        Assert.True(conditions.Has(TFCond.Burning));
        Assert.Equal(2f, conditions.Duration(TFCond.Burning));
        Assert.Equal(new EntityHandle(6), conditions.Provider(TFCond.Burning));
        Assert.Equal(3f, conditions.State.CritBoostMaxDuration);
    }

    [Fact]
    public void The_state_holds_no_references_so_a_netstate_can_carry_it()
    {
        Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<TFConditionState>());
    }

    [Fact]
    public void A_copied_state_is_a_snapshot()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 2f);
        TFConditionState saved = conditions.State;

        Tick1(conditions);

        Assert.Equal(2f, saved.ExpireTimes[(int)TFCond.Burning]);
        Assert.Equal(1.75f, conditions.Duration(TFCond.Burning));
    }

    [Fact]
    public void ReceiveBits_reports_what_the_network_added_and_removed()
    {
        TFConditions conditions = Client();
        conditions.Add(TFCond.Zoomed);
        conditions.Add(TFCond.Aiming);

        TFCondDelta delta = conditions.ReceiveBits(TFCondMask.Of([TFCond.Aiming, TFCond.Critboosted]));

        Assert.Equal(TFCondMask.Of(TFCond.Critboosted), delta.Added);
        Assert.Equal(TFCondMask.Of(TFCond.Zoomed), delta.Removed);
        Assert.True(conditions.Has(TFCond.Critboosted));
    }

    [Fact]
    public void An_invalid_condition_throws()
    {
        TFConditions conditions = Server();

        Assert.Throws<ArgumentOutOfRangeException>(() => conditions.Add(TFCond.Invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => conditions.Has(TFCond.Last));
    }
}
