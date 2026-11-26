using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using TF2.Tests.Ctf;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The cloak meter (<c>CTFPlayerShared::UpdateCloakMeter</c>,
/// tf_player_shared.cpp:14165-14253) and the watch's toggle
/// (<c>CTFWeaponInvis::ActivateInvisibilityWatch</c>, tf_weapon_invis.cpp:206-263).
/// </summary>
public sealed class TFCloakTests
{
    [Fact]
    public void A_cloaked_spy_burns_the_consume_rate_a_second()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        // tf_spy_cloak_consume_rate's default 10.0 (tf_player_shared.cpp:166).
        Assert.Equal(90f, rig.State.CloakMeter, 4);
    }

    [Fact]
    public void An_uncloaked_spy_refills_at_the_regen_rate()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 50f;

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        // tf_spy_cloak_regen_rate's default 3.3 (tf_player_shared.cpp:167).
        Assert.Equal(53.3f, rig.State.CloakMeter, 3);
    }

    [Fact]
    public void The_meter_out_ends_the_cloak()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 0.5f;
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        // The classic drain does not clamp (tf_player_shared.cpp:14204-14206):
        // the meter runs past zero and regenerates from the overshoot, and
        // the cloak fades.
        Assert.Equal(-9.5f, rig.State.CloakMeter, 4);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void The_meter_refills_to_its_cap_of_100()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 99.5f;

        rig.Slice.Tick(100f);
        rig.Slice.Tick(102f);

        Assert.Equal(100f, rig.State.CloakMeter, 4);
    }

    /// <summary>A plain bonus attribute of one hook class, as the audit rows carry it.</summary>
    private static IAttributeProvider Attr(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5110, "attr 5110", hook, AttributeDescriptionFormat.Percentage,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    [Fact]
    public void ConsumeRateScalesTheDrain()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Attr("mult_cloak_meter_consume_rate", 0.75f);
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        // tf_weapon_invis.cpp:326-338: the schema's inverse-scaled 0.75 is
        // corrected to the factor 1/(2 - 0.75) = 0.8, so the drain answers
        // 10/sec * 0.8 — stock's own worked example (:329-332).
        Assert.Equal(92f, rig.State.CloakMeter, 4);
    }

    [Fact]
    public void RegenRateScalesTheRefill()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 50f;
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Attr("mult_cloak_meter_regen_rate", 2f);

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        // tf_weapon_invis.cpp:340-343: the regen hook multiplies the convar
        // outright: 3.3/sec * 2.
        Assert.Equal(56.6f, rig.State.CloakMeter, 3);
    }

    [Fact]
    public void AStockWatchKeepsStocksRates()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = new AttributeContainer(null, AttributeProviderType.Generic);
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);
        Assert.Equal(90f, rig.State.CloakMeter, 4);

        rig.Conditions.Remove(SpyRig.Spy, TFCond.Stealthed);
        rig.Slice.Tick(102f);
        Assert.Equal(93.3f, rig.State.CloakMeter, 3);
    }

    [Fact]
    public void Cloaking_refuses_under_a_tenth_and_starts_over_it()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 8f;

        // tf_weapon_invis.cpp:243: "must have over 10% cloak to start".
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));

        rig.State.CloakMeter = 9f;
        rig.Clock.Now = 100.2f;    // the refusal armed its own 0.1 s cooldown
        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void The_toggle_waits_half_a_second_after_a_change()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;

        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));

        // Stock arms m_flStealthNextChangeTime 0.5 s after a real change.
        rig.Clock.Now = 100.4f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));

        rig.Clock.Now = 100.5f;
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void A_non_spy_is_never_cloaked()
    {
        var rig = new SpyRig(TFClass.Scout);
        rig.State.CloakMeter = 100f;

        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));

        // The think's safety net strips a cloak a class change left behind.
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);
        rig.Slice.Tick(100f);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void A_taunting_spy_cannot_toggle_the_cloak()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Conditions.Add(SpyRig.Spy, TFCond.Taunting, provider: SpyRig.Spy);

        // DoClassSpecialSkill's gate (tf_player_shared.cpp:12357-12359).
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    // ---- CanGoInvisible (tf_player_shared.cpp:12697-12717), the watch's cloak-start arm ----

    /// <summary>A spy at index 3 (the handle <see cref="SpyRig.Spy"/>) who stole
    /// ctf_2fort's red intelligence on a CTF flag of <paramref name="gameType"/>.</summary>
    private static CtfRig CarryingSpy(string gameType = "0")
    {
        var ctf = new CtfRig();
        var red = ctf.Flag(TFTeam.Red, ("GameType", gameType));
        var carrier = ctf.Players.Add(3, TFTeam.Blue);
        ctf.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);
        return ctf;
    }

    [Fact]
    public void A_cloak_refuses_while_the_intelligence_rides_the_spy()
    {
        var rig = new SpyRig(ctf: CarryingSpy().Rules);
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;

        // The watch's cloak-start arm (tf_weapon_invis.cpp:243,
        // CanGoInvisible tf_player_shared.cpp:12700-12717): a carried
        // intelligence refuses, and the refusal arms the 0.1 s cooldown.
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void A_spy_carried_by_a_player_destruction_flag_may_still_cloak()
    {
        // The flag arm exempts TF_FLAGTYPE_PLAYER_DESTRUCTION (:12706).
        var rig = new SpyRig(ctf: CarryingSpy("6").Rules);
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;

        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void The_cloak_refuses_once_another_team_has_won()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Winners.WinningTeam = TFTeam.Blue;    // the spy is red.

        // GR_STATE_TEAMWIN with a winning team that is not his own (:12713-12716).
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void The_winning_team_keeps_its_cloak()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Winners.WinningTeam = TFTeam.Red;

        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void The_cloak_command_toggles_the_watch()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        var cloak = new TF2.Server.Player.TFSpyCloakCommand(rig.Spies);

        Assert.True(cloak.Handle(SpyRig.Spy, "cloak", ReadOnlySpan<string>.Empty));

        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void A_cloak_arms_its_fade_in_over_the_invis_time()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;

        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));

        // OnAddStealthed's clock (tf_player_shared.cpp:6997-6999) over
        // tf_spy_invis_time's default 1.0.
        Assert.Equal(101f, rig.State.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void An_uncloak_arms_its_fade_out_over_the_unstealth_time()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);

        rig.Clock.Now = 100.5f;
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);

        // FadeInvis's clock (tf_player_shared.cpp:7958-7971) over
        // tf_spy_invis_unstealth_time's default 2.0.
        Assert.Equal(102.5f, rig.State.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void An_attack_arms_nothing_on_someone_not_cloaked()
    {
        var rig = new SpyRig();
        rig.Clock.Now = 100f;

        // CTFPlayer::RemoveInvisibility's early return (tf_player.cpp:15990-
        // 15991): "called when player attacks", it reaches FadeInvis only when
        // the cloak is on — an engineer's swings arm no fade clock.
        rig.Spies.RemoveInvisibility(SpyRig.Spy.EntryIndex);

        Assert.Equal(0f, rig.State.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void A_forced_remove_invisibility_fades_at_half_rate()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);

        // CTFPlayer::RemoveInvisibility (tf_player.cpp:15988-16005): 0.5 of
        // the unstealth time.
        rig.Spies.RemoveInvisibility(SpyRig.Spy.EntryIndex);

        Assert.Equal(101f, rig.State.InvisChangeCompleteTime, 4);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    // ---- the motion cloak (item 60, Cloak and Dagger): set_weapon_mode 2,
    // UpdateCloakMeter's movement arms, tf_player_shared.cpp:14153-14192 ----

    /// <summary>The held watch's mode on the spy's manager, as the shipped
    /// schema carries it (<c>AttributeValue.OfFloat</c> on a
    /// <c>Default</c>-typed <c>set_weapon_mode</c>, read by
    /// <see cref="AttributeHooks.Int"/>).</summary>
    private static AttributeContainer WatchMode(float mode)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(6101, "attr 6101", "set_weapon_mode", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), mode));
        return container;
    }

    [Fact]
    public void The_motion_cloak_drains_only_while_the_spy_moves()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = new() { [TF2.Server.Player.TFSpyComponent.WatchScript] = WatchMode(2f) };   // INVIS_MOTION_CLOAK
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        // Moving at the class maximum: RemapVal answers 1, stock's extra half
        // makes the drain 10/sec * 1.5 (:14171-14176).
        float maxSpeed = rig.Classes[TFClass.Spy].MaxSpeed;
        rig.Engine.Returns("Entities.GetVelocity", new Vector(maxSpeed, 0f, 0f));

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        Assert.Equal(85f, rig.State.CloakMeter, 4);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    [Fact]
    public void A_standing_motion_cloak_refills_to_its_cap()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 98.4f;
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = new() { [TF2.Server.Player.TFSpyComponent.WatchScript] = WatchMode(2f) };
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        // Still (the rig's zero velocity): the refill rides the regen hook,
        // 3.3/sec (:14158-14167).
        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);
        Assert.Equal(100f, rig.State.CloakMeter, 4);    // 98.4 + 3.3 clamps to the cap (:14163-14166)

        rig.Slice.Tick(102f);
        Assert.Equal(100f, rig.State.CloakMeter, 4);    // the cap holds (:14163-14166)
    }

    [Fact]
    public void The_motion_cloak_refill_waits_a_second_of_being_still()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 50f;
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = new() { [TF2.Server.Player.TFSpyComponent.WatchScript] = WatchMode(2f) };
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        // The moving tick stamps the exposure (:14158, one second of grace).
        float maxSpeed = rig.Classes[TFClass.Spy].MaxSpeed;
        rig.Engine.Returns("Entities.GetVelocity", new Vector(maxSpeed, 0f, 0f));
        rig.Slice.Tick(100f);

        // Stilled at once the meter stands still, and after the second it
        // regenerates (:14158-14168).
        rig.Engine.Returns("Entities.GetVelocity", default(Vector));
        rig.Slice.Tick(101f);
        Assert.Equal(50f, rig.State.CloakMeter, 4);

        rig.Slice.Tick(102f);
        Assert.Equal(53.3f, rig.State.CloakMeter, 3);
    }

    [Fact]
    public void An_empty_motion_cloak_clamps_at_zero_and_keeps_the_spy_stealthed()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 1f;
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = new() { [TF2.Server.Player.TFSpyComponent.WatchScript] = WatchMode(2f) };
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);

        float maxSpeed = rig.Classes[TFClass.Spy].MaxSpeed;
        rig.Engine.Returns("Entities.GetVelocity", new Vector(maxSpeed, 0f, 0f));

        rig.Slice.Tick(100f);
        rig.Slice.Tick(101f);

        // The motion arm clamps at zero and fades nothing: the empty meter
        // leaves the spy stealthed, half-visible (:14177-14180, :14189-14192).
        Assert.Equal(0f, rig.State.CloakMeter, 4);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }
}
