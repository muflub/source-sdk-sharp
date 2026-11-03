using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// A player's one condition set: bound to the carrier's five words (what the
/// weapons and the movement read), and <see cref="TFPlayerConditions"/>'
/// AddCond/RemoveCond with stock's refusals and the arms after each change.
/// </summary>
public sealed class TFPlayerConditionsTests
{
    private sealed class RecordingHook : ITFConditionHook
    {
        public List<string> Heard { get; } = [];
        public void OnAdded(EntityHandle player, TFCond cond) => Heard.Add("+" + cond);
        public void OnRemoved(EntityHandle player, TFCond cond) => Heard.Add("-" + cond);
    }

    private static (PlayerRig Rig, TFPlayerConditions Conditions, RecordingHook Hook) Make()
    {
        var rig = new PlayerRig().Join();
        var hook = new RecordingHook();
        return (rig, new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(hook)), hook);
    }

    [Fact]
    public void An_added_condition_is_on_the_carriers_words()
    {
        var (rig, conditions, _) = Make();
        Assert.False(rig.State.InCond(TFCond.Burning));

        Assert.Equal(TFCondAdd.Added, conditions.Add(PlayerRig.Player, TFCond.Burning, 4f));

        Assert.True(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void A_write_to_the_carriers_words_is_what_the_set_reads_next()
    {
        var (rig, conditions, _) = Make();
        var set = conditions.Of(PlayerRig.Index)!;

        rig.State.SetConditions(TFCondMask.Of(TFCond.Aiming));

        Assert.True(set.Has(TFCond.Aiming));
    }

    [Fact]
    public void A_removed_condition_leaves_the_carriers_words()
    {
        var (rig, conditions, _) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Zoomed);

        Assert.True(conditions.Remove(PlayerRig.Player, TFCond.Zoomed));

        Assert.False(rig.State.InCond(TFCond.Zoomed));
    }

    [Fact]
    public void A_dead_player_takes_no_condition()
    {
        var (rig, conditions, hook) = Make();
        rig.Engine.Returns("Players.IsAlive", false);

        Assert.Equal(TFCondAdd.Refused, conditions.Add(PlayerRig.Player, TFCond.Burning, 4f));

        Assert.False(rig.State.InCond(TFCond.Burning));
        Assert.Empty(hook.Heard);
    }

    [Fact]
    public void The_competitive_conditions_are_refused_with_no_match_summary()
    {
        var (rig, conditions, _) = Make();

        Assert.Equal(TFCondAdd.Refused, conditions.Add(PlayerRig.Player, TFCond.CompetitiveWinner));
        Assert.Equal(TFCondAdd.Added, conditions.Add(PlayerRig.Player, TFCond.Taunting));

        Assert.False(rig.State.InCond(TFCond.CompetitiveWinner));
    }

    [Fact]
    public void A_player_with_no_carrier_takes_no_condition()
    {
        var rig = new PlayerRig();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);

        Assert.Equal(TFCondAdd.Refused, conditions.Add(PlayerRig.Player, TFCond.Burning, 4f));
        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Burning));
    }

    [Fact]
    public void Every_add_and_removal_tells_the_hooks()
    {
        var (_, conditions, hook) = Make();

        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);
        conditions.Add(PlayerRig.Player, TFCond.Burning, 6f);
        conditions.Remove(PlayerRig.Player, TFCond.Burning);

        Assert.Equal(["+Burning", "+Burning", "-Burning"], hook.Heard);
    }

    /// <summary>An arm's removals reach the hooks: uber ends the afterburn (OnAddInvulnerable).</summary>
    [Fact]
    public void What_an_arm_ends_is_told_as_a_removal()
    {
        var (rig, conditions, hook) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);
        hook.Heard.Clear();

        conditions.Add(PlayerRig.Player, TFCond.Invulnerable);

        Assert.Equal(["+Invulnerable", "-Burning"], hook.Heard);
        Assert.False(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void Remove_all_tells_each_condition_that_was_active()
    {
        var (rig, conditions, hook) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);
        conditions.Add(PlayerRig.Player, TFCond.Zoomed);
        hook.Heard.Clear();

        var removed = conditions.RemoveAll(PlayerRig.Player);

        Assert.Equal(2, removed.Count);
        Assert.Equal(["-Zoomed", "-Burning"], hook.Heard);
        Assert.True(rig.State.Conditions().IsEmpty);
    }

    [Fact]
    public void An_expiry_runs_the_removal_arms()
    {
        var (rig, conditions, hook) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 0.5f);
        conditions.Add(PlayerRig.Player, TFCond.HealingDebuff, 10f);
        hook.Heard.Clear();

        var set = conditions.Of(PlayerRig.Index)!;
        var expired = set.Tick(1f, 0, default);
        conditions.Expire(PlayerRig.Player, in expired);

        // OnRemoveBurning's server half ends the heal debuff with the fire.
        Assert.Equal(["-Burning", "-HealingDebuff"], hook.Heard);
        Assert.False(rig.State.InCond(TFCond.HealingDebuff));
    }

    // ---- the damage_prevented event (tf_player_shared.cpp:1184-1198) --------

    /// <summary>The game events as the conditions service fires them: each fired event's keys as one line.</summary>
    private sealed class RecordingEvents : IEngineGameEvents
    {
        public readonly List<string> Fired = [];
        private readonly Dictionary<IntPtr, SortedDictionary<string, string>> values = new();
        private IntPtr next = new(1);

        public IntPtr EventCreate(string name)
        {
            IntPtr h = next;
            next = new IntPtr(next.ToInt64() + 1);
            values[h] = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["event"] = name };
            return h;
        }

        public bool EventFire(IntPtr handle, bool dontBroadcast = false)
        {
            Fired.Add(string.Join(" ", values[handle].Select(kv => $"{kv.Key}={kv.Value}")));
            return true;
        }

        public void EventSetInt(IntPtr handle, string key, int value) => values[handle][key] = value.ToString();

        // The conditions service asks nothing else of the seam.
        public void EventSetBool(IntPtr handle, string key, bool value) => throw new NotSupportedException();
        public void EventSetString(IntPtr handle, string key, string value) => throw new NotSupportedException();
        public bool EventGetBool(IntPtr handle, string key) => throw new NotSupportedException();
        public float EventGetFloat(IntPtr handle, string key) => throw new NotSupportedException();
        public int EventGetInt(IntPtr handle, string key) => throw new NotSupportedException();
        public string EventGetString(IntPtr handle, string key) => throw new NotSupportedException();
        public void ListenForGameEvent(string name) => throw new NotSupportedException();
    }

    private static TFPlayerConditions WithEvents(PlayerRig rig, RecordingEvents events)
        => new(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(new RecordingHook()), events);

    [Fact]
    public void A_condition_ending_fires_damage_prevented_with_its_total()
    {
        var rig = new PlayerRig().Join();
        var events = new RecordingEvents();
        var conditions = WithEvents(rig, events);
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);
        conditions.Of(PlayerRig.Index)!.AddPreventedDamage(TFCond.Burning, 40);

        conditions.Remove(PlayerRig.Player, TFCond.Burning);

        // No provider: stock names the player the preventor (tf_player_shared.cpp:1189).
        // The condition rides as its stock value (SetInt of the ETFCond).
        Assert.Equal(["amount=40 condition=22 event=damage_prevented preventor=3 victim=3"], events.Fired);
        Assert.Equal(0, conditions.Of(PlayerRig.Index)!.PreventedDamage(TFCond.Burning));
    }

    [Fact]
    public void The_preventor_is_the_conditions_provider()
    {
        var rig = new PlayerRig().Join();
        var events = new RecordingEvents();
        var conditions = WithEvents(rig, events);
        var provider = new EntityHandle(0x0001_0009u);
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f, provider);
        conditions.Of(PlayerRig.Index)!.AddPreventedDamage(TFCond.Burning, 12);

        conditions.Remove(PlayerRig.Player, TFCond.Burning);

        Assert.Equal(["amount=12 condition=22 event=damage_prevented preventor=9 victim=3"], events.Fired);
    }

    [Fact]
    public void A_condition_with_no_total_fires_nothing()
    {
        var rig = new PlayerRig().Join();
        var events = new RecordingEvents();
        var conditions = WithEvents(rig, events);
        conditions.Add(PlayerRig.Player, TFCond.Zoomed);

        conditions.Remove(PlayerRig.Player, TFCond.Zoomed);

        Assert.Empty(events.Fired);
    }

    [Fact]
    public void The_accumulator_sums_across_writes()
    {
        var (rig, conditions, _) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);

        var set = conditions.Of(PlayerRig.Index)!;
        set.AddPreventedDamage(TFCond.Burning, 30);
        set.AddPreventedDamage(TFCond.Burning, 12);

        Assert.Equal(42, set.PreventedDamage(TFCond.Burning));
        Assert.Equal(0, set.PreventedDamage(TFCond.Zoomed));
    }

    [Fact]
    public void Remove_all_fires_per_condition_carrying_a_total()
    {
        var rig = new PlayerRig().Join();
        var events = new RecordingEvents();
        var conditions = WithEvents(rig, events);
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);
        conditions.Add(PlayerRig.Player, TFCond.Zoomed);
        conditions.Of(PlayerRig.Index)!.AddPreventedDamage(TFCond.Burning, 7);
        conditions.Of(PlayerRig.Index)!.AddPreventedDamage(TFCond.Zoomed, 3);

        conditions.RemoveAll(PlayerRig.Player);

        // The mask's order, not the add's: stock's RemoveAllCond walks the words.
        Assert.Equal([
            $"amount=3 condition={(int)TFCond.Zoomed} event=damage_prevented preventor=3 victim=3",
            $"amount=7 condition={(int)TFCond.Burning} event=damage_prevented preventor=3 victim=3",
        ], events.Fired);
    }
}
