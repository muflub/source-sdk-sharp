using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Server.Entities.Triggers;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Tests.Entities.Items;
using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Entities.Triggers;

/// <summary>
/// trigger_stun and the player's stun stack (stage "later"): stock's
/// <c>CTriggerStun</c> (tf_triggers.cpp:25-167) and the <c>CTFPlayerShared</c>
/// stun stack it drives (<c>StunPlayer</c>, tf_player_shared.cpp:9766-9933,
/// and the <c>ConditionThink</c> expiry at :3172-3211).
/// </summary>
public sealed class TriggerStunTests
{
    private static readonly (string, string)[] StunKeys =
        [("stun_duration", "3"), ("move_speed_reduction", "0.4"), ("trigger_delay", "5")];

    private static TFStunStack Attach(MapItemRig rig, TFPlayerCarriers carriers, EntityHandle player)
    {
        var stack = new TFStunStack(carriers, rig.Players, rig.Clock);
        stack.Attach(player);
        return stack;
    }

    private static (MapItemRig Rig, TFPlayerCarriers Carriers, TriggerStun Trigger) Setup(params (string, string)[] keys)
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        rig.Services.AddInstance(carriers);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer",
            () => rig.Players.All.ContainsKey((EntityHandle)rig.Engine.Last("Entities.IsPlayer")!.Value.Arg(0)!));
        var trigger = rig.Spawn<TriggerStun>(TriggerStun.MapClass, keys);
        return (rig, carriers, trigger);
    }

    [Fact]
    public void The_first_touch_arms_the_stun_think_after_the_trigger_delay()
    {
        var (rig, carriers, trigger) = Setup(StunKeys);
        var (player, _) = rig.Players.Add();
        Attach(rig, carriers, player);

        rig.Touch(trigger, player);

        Assert.True(trigger.Stunning);
        Assert.Equal(5f, trigger.NextThinkDelay());
    }

    [Fact]
    public void The_think_stuns_everyone_inside_and_fires_the_output()
    {
        var (rig, carriers, trigger) = Setup(StunKeys);
        var (first, a) = rig.Players.Add();
        var (second, b) = rig.Players.Add();
        var stackA = Attach(rig, carriers, first);
        var stackB = Attach(rig, carriers, second);

        rig.Touch(trigger, first);
        rig.Touch(trigger, second);
        rig.Clock.Now = 5f;
        trigger.OnThink();

        Assert.Contains(TFCond.Stunned, a.Conditions);
        Assert.Contains(TFCond.Stunned, b.Conditions);
        Assert.Equal(2, trigger.OnStunPlayer.FireCount);
        Assert.Equal([first, second], trigger.StunnedEntities);
        Assert.Equal(TriggerStun.StunInterval, trigger.NextThinkDelay());

        // Movement | NoEffects | ByTrigger, the effects key off; reduction 0.4 -> 102.
        Assert.Equal(TFStunFlags.Movement | TFStunFlags.NoEffects | TFStunFlags.ByTrigger, stackA.ActiveStun!.Flags);
        Assert.Equal(102f, stackA.ActiveStun.Amount, 2);
        Assert.Equal(8f, stackA.ActiveStun.ExpireTime); // 5 + 3
    }

    [Fact]
    public void The_think_keeps_re_stunning_while_anyone_stays_inside()
    {
        var (rig, carriers, trigger) = Setup(StunKeys);
        var (player, _) = rig.Players.Add();
        Attach(rig, carriers, player);

        rig.Touch(trigger, player);
        rig.Clock.Now = 5f;
        trigger.OnThink();
        rig.Clock.Now = 5.5f;
        trigger.OnThink();

        Assert.True(trigger.Stunning);
        Assert.Equal(2, trigger.OnStunPlayer.FireCount);
    }

    [Fact]
    public void The_think_stops_when_the_volume_goes_empty()
    {
        var (rig, carriers, trigger) = Setup(StunKeys);
        var (player, _) = rig.Players.Add();
        Attach(rig, carriers, player);

        rig.Touch(trigger, player);
        rig.Clock.Now = 5f;
        trigger.OnThink();
        rig.Registry.EndTouch(trigger.Handle, player); // the way out never re-stuns: already tallied
        rig.Clock.Now = 5.5f;
        trigger.OnThink();

        Assert.False(trigger.Stunning);
        Assert.Equal(1, trigger.OnStunPlayer.FireCount);
    }

    [Fact]
    public void A_player_never_stunned_by_a_think_is_stunned_on_the_way_out()
    {
        var (rig, carriers, trigger) = Setup(StunKeys);
        var (player, p) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        rig.Touch(trigger, player); // the delay has not come due
        rig.Registry.EndTouch(trigger.Handle, player);

        Assert.Contains(TFCond.Stunned, p.Conditions);
        Assert.Equal(1, trigger.OnStunPlayer.FireCount);
        Assert.Single(stack.Stuns);
    }

    [Fact]
    public void Controls_and_effects_keys_change_the_flags_and_the_anim_time()
    {
        var (rig, carriers, trigger) = Setup(("stun_duration", "3"), ("trigger_delay", "0"), ("stun_type", "1"),
                                              ("stun_effects", "1"));
        var (player, _) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        rig.Touch(trigger, player);
        trigger.OnThink(); // stun_type 1, effects on

        Assert.Equal(TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.ByTrigger, stack.ActiveStun!.Flags);
        Assert.Equal(rig.Clock.Now + 3f + TFStunStack.ControlStunAnimTime, stack.ActiveStun.ExpireTime, 3);
        Assert.True(stack.IsControlStunned());
        Assert.False(stack.IsSnared());
    }

    [Fact]
    public void A_non_player_is_never_stunned_and_the_think_gives_up()
    {
        var (rig, _, trigger) = Setup(StunKeys);
        var prop = new EntityHandle(0x0070_0001u);

        rig.Touch(trigger, prop);
        rig.Clock.Now = 5f;
        trigger.OnThink();

        Assert.Empty(trigger.StunnedEntities);
        Assert.False(trigger.Stunning);
        Assert.Equal(0, trigger.OnStunPlayer.FireCount);
    }

    [Fact]
    public void A_disabled_trigger_arms_nothing()
    {
        var (rig, carriers, trigger) = Setup([.. StunKeys, ("StartDisabled", "1")]);
        var (player, p) = rig.Players.Add();
        Attach(rig, carriers, player);

        rig.Touch(trigger, player);

        Assert.False(trigger.Stunning);
        Assert.Empty(p.Conditions);
    }

    // ---- the stack itself ----

    [Fact]
    public void The_refusals_stun_nobody()
    {
        foreach (TFCond refusal in new[] { TFCond.Phase, TFCond.PasstimeInterception, TFCond.Megaheal,
                                           TFCond.InvulnerableHideUnlessDamaged })
        {
            var rig = new MapItemRig();
            var carriers = new TFPlayerCarriers();
            var (player, p) = rig.Players.Add();
            p.Conditions.Add(refusal);
            var stack = Attach(rig, carriers, player);

            stack.Stun(player, 3f, 1f, TFStunFlags.Movement, EntityHandle.None);

            Assert.Empty(stack.Stuns);
            Assert.DoesNotContain(TFCond.Stunned, p.Conditions);
        }
    }

    [Fact]
    public void A_radiowave_stuns_the_hidden_invulnerable_anyway()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, p) = rig.Players.Add();
        p.Conditions.Add(TFCond.InvulnerableHideUnlessDamaged);
        p.Conditions.Add(TFCond.MvmBotStunRadiowave);
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 3f, 1f, TFStunFlags.Movement, EntityHandle.None);

        Assert.Contains(TFCond.Stunned, p.Conditions);
    }

    [Fact]
    public void The_active_stun_refuses_a_weaker_one_that_expires_first()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, p) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 10f, 0.5f, TFStunFlags.Movement, EntityHandle.None);  // active, expires 0.5 s * 255
        stack.Stun(player, 3f, 0.2f, TFStunFlags.Movement, EntityHandle.None);   // weaker, expires sooner: ignored

        Assert.Single(stack.Stuns);
    }

    [Fact]
    public void A_queued_stun_waits_behind_the_active_and_then_the_stack_empties()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, p) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 4f, 0.4f, TFStunFlags.Movement, EntityHandle.None); // amount 102, expires 4
        stack.Stun(player, 8f, 0.2f, TFStunFlags.Movement, EntityHandle.None); // weaker but longer: queued

        Assert.Equal(2, stack.Stuns.Count);
        Assert.Equal(102f, stack.ActiveStun!.Amount, 2);

        rig.Clock.Now = 4.5f;
        stack.Tick(rig.Clock.Now);
        Assert.Contains(TFCond.Stunned, p.Conditions);
        Assert.Single(stack.Stuns);
        Assert.Equal(51f, stack.ActiveStun!.Amount, 2); // the queued one is active now

        rig.Clock.Now = 8.5f;
        stack.Tick(rig.Clock.Now);
        Assert.Empty(stack.Stuns);
        Assert.DoesNotContain(TFCond.Stunned, p.Conditions);
    }

    [Fact]
    public void A_stronger_or_controlling_stun_stomps_and_keeps_the_strongest_slow()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, _) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 5f, 0.8f, TFStunFlags.Movement, EntityHandle.None); // amount 204
        stack.Stun(player, 10f, 0.2f, TFStunFlags.Movement | TFStunFlags.Controls, EntityHandle.None);

        Assert.Equal(2, stack.Stuns.Count); // the old one stays queued behind
        var active = stack.ActiveStun!;
        Assert.Equal(204f, active.Amount, 2); // the slower slow of the two survives
        Assert.Equal(rig.Clock.Now + 10f + TFStunStack.ControlStunAnimTime, active.ExpireTime, 3);
        Assert.Equal(rig.Clock.Now + 10f, active.StartFadeTime, 3);
    }

    [Fact]
    public void The_next_strongest_wins_the_promotion()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, _) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 2f, 0.5f, TFStunFlags.Movement, EntityHandle.None);  // active
        stack.Stun(player, 9f, 0.1f, TFStunFlags.Movement, EntityHandle.None);  // queued, amount 25
        stack.Stun(player, 9f, 0.2f, TFStunFlags.Movement, EntityHandle.None);  // queued, amount 51

        rig.Clock.Now = 2.5f;
        stack.Tick(rig.Clock.Now);

        Assert.Equal(51f, stack.ActiveStun!.Amount, 2);
    }

    [Fact]
    public void The_stack_caps_at_two_hundred_and_forty_nine()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, _) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        for (int i = 0; i < 300; i++)
            stack.Stun(player, 10f, 0.5f, TFStunFlags.Movement, EntityHandle.None);

        Assert.Equal(249, stack.Stuns.Count);
    }

    [Fact]
    public void Losing_the_condition_wipes_the_stack()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, p) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 10f, 0.5f, TFStunFlags.Movement, EntityHandle.None);
        stack.Stun(player, 20f, 0.2f, TFStunFlags.Movement, EntityHandle.None); // queued
        Assert.Equal(2, stack.Stuns.Count);

        p.Conditions.Remove(TFCond.Stunned); // death's RemoveAllCond, any removal

        // The yanked stack, before the tick has run: stock's assert arm (:9816-9826).
        stack.Stun(player, 10f, 0.5f, TFStunFlags.Movement, EntityHandle.None);
        Assert.Empty(stack.Stuns);
        Assert.Null(stack.ActiveStun);

        stack.Tick(rig.Clock.Now); // and the tick wipes it too
        Assert.Empty(stack.Stuns);
    }

    [Fact]
    public void The_queries_report_the_active_stun_through_the_condition()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, p) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 10f, 0.4f, TFStunFlags.Movement, EntityHandle.None);

        Assert.Equal(102f / 255f, stack.AmountStunned(TFStunFlags.Movement), 3);
        Assert.Equal(0f, stack.AmountStunned(TFStunFlags.Controls), 3);
        Assert.True(stack.IsSnared());
        Assert.False(stack.IsControlStunned());

        p.Conditions.Remove(TFCond.Stunned); // the queries answer nothing without the condition
        Assert.Equal(0f, stack.AmountStunned(TFStunFlags.Movement), 3);
    }

    [Fact]
    public void The_spawn_clears_the_stun_stack()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var (player, _) = rig.Players.Add();
        var stack = Attach(rig, carriers, player);

        stack.Stun(player, 10f, 0.5f, TFStunFlags.Movement, EntityHandle.None);
        Assert.Single(stack.Stuns);

        stack.ClearOnSpawn();

        Assert.Empty(stack.Stuns);
        Assert.Null(stack.ActiveStun);
    }

    //=========================================================================
    // The legacy net mirror (UpdateLegacyStunSystem,
    // tf_player_shared.cpp:7441-7465): the four carrier fields the movement's
    // StunMove decides from. Stock calls the copy after the append (:9889)
    // and after the expiry arm (:3211); m_iStunIndex is the member itself, so
    // it follows every stack mutation (:3185, :3198, :9847, :9824) and Spawn
    // takes it to -1 (:1025) while the value fields keep their stale — inert
    // behind the index — words. The mirror's reader has its facts in
    // TFMovementTests.
    //=========================================================================

    /// <summary>
    /// The stack over a LIVE carrier (PlayerRig's), so the mirror writes have
    /// somewhere to land, and its player in the map fake so the condition
    /// seams answer.
    /// </summary>
    private static (TFStunStack Stack, PlayerRig Player) AttachLive(MapItemRig rig)
    {
        var playerRig = new PlayerRig().Join();
        rig.Players.All[PlayerRig.Player] = new FakeMapPlayer { Team = (int)TFTeam.Red };
        var stack = new TFStunStack(playerRig.Carriers, rig.Players, rig.Clock);
        stack.Attach(PlayerRig.Player);
        return (stack, playerRig);
    }

    [Fact]
    public void The_stun_mirrors_the_active_entry_onto_the_carrier()
    {
        var rig = new MapItemRig();
        var (stack, playerRig) = AttachLive(rig);
        rig.Clock.Now = 5f;

        stack.Stun(PlayerRig.Player, 3f, 0.4f,
                   TFStunFlags.Movement | TFStunFlags.NoEffects | TFStunFlags.ByTrigger, EntityHandle.None);

        var state = playerRig.State;
        Assert.Equal(0, state.StunIndex);                                    // the append's tail index (:9847)
        Assert.Equal((int)(TFStunFlags.Movement | TFStunFlags.NoEffects | TFStunFlags.ByTrigger),
                     state.StunFlags);                                       // (:7453)
        Assert.Equal(102f, state.MovementStunAmount, 2);                     // the 0.4 remap (:9791, :7452)
        Assert.Equal(8f, state.StunExpireTime, 2);                           // 5 + 3 (:7451)
    }

    [Fact]
    public void The_control_stun_mirror_carries_the_anim_time()
    {
        var rig = new MapItemRig();
        var (stack, playerRig) = AttachLive(rig);
        rig.Clock.Now = 5f;

        stack.Stun(PlayerRig.Player, 3f, 1f, TFStunFlags.Controls, EntityHandle.None);

        // The entry grows the anim time before the copy (Stun's :9877-9884 arm),
        // so the mirror carries it: 5 + 3 + CONTROL_STUN_ANIM_TIME.
        Assert.Equal(9.5f, playerRig.State.StunExpireTime, 2);
    }

    [Fact]
    public void A_queued_stun_leaves_the_mirror_alone()
    {
        var rig = new MapItemRig();
        var (stack, playerRig) = AttachLive(rig);
        rig.Clock.Now = 5f;

        stack.Stun(PlayerRig.Player, 3f, 0.4f, TFStunFlags.Movement, EntityHandle.None);   // 102 of 255
        stack.Stun(PlayerRig.Player, 10f, 0.2f, TFStunFlags.Movement, EntityHandle.None);  // 51, expires later: queued

        Assert.Equal(2, stack.Stuns.Count);
        var state = playerRig.State;
        Assert.Equal(0, state.StunIndex);                       // the active row never moved
        Assert.Equal(102f, state.MovementStunAmount, 1);        // and the mirror never followed the queue
        Assert.Equal(8f, state.StunExpireTime, 1);
    }

    [Fact]
    public void The_expiry_mirror_follows_the_promotion()
    {
        var rig = new MapItemRig();
        var (stack, playerRig) = AttachLive(rig);
        rig.Clock.Now = 5f;
        stack.Stun(PlayerRig.Player, 5f, 0.784f, TFStunFlags.Movement, EntityHandle.None);   // 200, expires at 10
        stack.Stun(PlayerRig.Player, 12f, 0.392f, TFStunFlags.Movement, EntityHandle.None);  // 100, queued

        stack.Tick(10.1f);   // the active one is past: remove, promote the strongest (:3184-3204), copy (:3211)

        var state = playerRig.State;
        Assert.Equal(0, state.StunIndex);
        Assert.Equal(100f, state.MovementStunAmount, 1);
        Assert.Equal(17f, state.StunExpireTime, 1);
    }

    [Fact]
    public void The_expiry_mirror_clears_the_index_when_the_stack_empties()
    {
        var rig = new MapItemRig();
        var (stack, playerRig) = AttachLive(rig);
        rig.Clock.Now = 5f;
        stack.Stun(PlayerRig.Player, 3f, 0.4f, TFStunFlags.Movement, EntityHandle.None);

        stack.Tick(9f);   // gone by itself: RemoveCond (:3207), and the index — the member stock sends — to -1 (:3185)

        Assert.Equal(-1, playerRig.State.StunIndex);
    }

    [Fact]
    public void The_spawn_clears_the_mirrored_index()
    {
        var rig = new MapItemRig();
        var (stack, playerRig) = AttachLive(rig);
        rig.Clock.Now = 5f;
        stack.Stun(PlayerRig.Player, 3f, 0.4f, TFStunFlags.Movement, EntityHandle.None);

        stack.ClearOnSpawn();   // stock's Spawn clears the stack and the index (:1016, :1025), NOT the value fields

        Assert.Equal(-1, playerRig.State.StunIndex);
    }
}
