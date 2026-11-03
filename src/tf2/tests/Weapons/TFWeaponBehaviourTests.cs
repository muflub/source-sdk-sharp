using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Medigun;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFWeaponBehaviour"/> and its <see cref="ITFWeaponHook"/> chain,
/// driven through the core machine over the shipped scripts.
/// </summary>
public sealed class TFWeaponBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const string Scattergun = "tf_weapon_scattergun";
    private const string Pistol = "tf_weapon_pistol";

    private sealed class Hook(int order, Func<int, int>? clip = null, Func<float, float>? interval = null,
                              Func<float, float>? damage = null, Func<float, float>? reload = null,
                              bool fire = true) : ITFWeaponHook
    {
        public int Asked;
        public int HookOrder => order;
        public int ModifyClip(in TFWeaponHookContext weapon, int c) => clip is null ? c : clip(c);
        public float ModifyInterval(in TFWeaponHookContext weapon, float i) => interval is null ? i : interval(i);
        public float ModifyDamage(in TFWeaponHookContext weapon, float d) => damage is null ? d : damage(d);
        public float ModifyReloadTime(in TFWeaponHookContext weapon, float d) => reload is null ? d : reload(d);
        public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack) { Asked++; return fire; }
    }

    private sealed class Throws(int order) : ITFWeaponHook
    {
        public int HookOrder => order;
        public float ModifyDamage(in TFWeaponHookContext weapon, float d) => throw new InvalidOperationException("boom");
    }

    //-------------------------------------------------------------------------
    // The chain
    //-------------------------------------------------------------------------

    [Fact]
    public void The_chain_asks_tf2s_hooks_before_a_later_layers_whatever_their_order()
    {
        var attributes = new TFAttributeWeaponHook(new AttributeHooks());
        var boost = new TFCritBoostHook();
        var late = new Hook(order: 2);
        var early = new Hook(order: 1);

        var chain = TFWeaponHookChain.Of(late, boost, early, attributes);

        // TF2's layer (TF2.Server here) first, by its orders; then this
        // assembly's (TF2.Tests), by theirs — the test hooks' orders are lower.
        Assert.Equal(new ITFWeaponHook[] { attributes, boost, early, late }, chain.All);
    }

    [Fact]
    public void A_layer_is_the_assembly_name_without_its_side()
    {
        Assert.Equal("TF2", TFWeaponHookChain.Layer(typeof(TFWeaponBehaviour).Assembly));
        Assert.Equal("TF2.Tests", TFWeaponHookChain.Layer(typeof(TFWeaponBehaviourTests).Assembly));
    }

    [Fact]
    public void The_chain_folds_each_value_through_every_hook_in_order()
    {
        var chain = TFWeaponHookChain.Of(new Hook(2, damage: d => d * 2f), new Hook(1, damage: d => d + 1f));
        var script = WeaponFixtures.Parse(Scattergun);

        // (6 + 1) * 2: order 1 first.
        Assert.Equal(14f, chain.ModifyDamage(new TFWeaponHookContext(default, script), 6f));
    }

    [Fact]
    public void A_throwing_hook_is_named_and_the_value_before_it_stands()
    {
        var log = new RecordingLog();
        var chain = new TFWeaponHookChain(TFWeaponHookChainHooks(new Hook(1, damage: d => d + 1f), new Throws(2), new Hook(3, damage: d => d * 10f)), log);
        var script = WeaponFixtures.Parse(Scattergun);

        Assert.Equal(70f, chain.ModifyDamage(new TFWeaponHookContext(default, script), 6f));
        Assert.Single(log.Warnings());
        Assert.Contains(nameof(Throws), log.Warnings()[0]);
    }

    private static IHooks<ITFWeaponHook> TFWeaponHookChainHooks(params ITFWeaponHook[] hooks) => new ListHooks(hooks);

    private sealed class ListHooks(ITFWeaponHook[] hooks) : IHooks<ITFWeaponHook>
    {
        public IReadOnlyList<ITFWeaponHook> All => hooks;
        public void Each(string what, Action<ITFWeaponHook> call) { foreach (var h in hooks) call(h); }
    }

    //-------------------------------------------------------------------------
    // The hooks at the machine's points
    //-------------------------------------------------------------------------

    [Fact]
    public void A_clip_hook_is_the_clip_the_machine_spends_and_fills()
    {
        var world = new TFWeaponWorld(new Hook(5, clip: c => c * 2));

        Assert.Equal(12, world.Behaviour(Scattergun).ModifyClipSize(world.Row(Scattergun), world.Row(Scattergun).ClipSize, TFWeaponWorld.Owner));
    }

    [Fact]
    public void An_interval_hook_moves_the_next_shot()
    {
        var world = new TFWeaponWorld(new Hook(5, interval: i => i * 2f));
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(10f + 1.25f, w.NextPrimaryAttack, 4);
        Assert.Equal(5, w.Clip);
    }

    private static AttributeContainer PercentageItem(int defIndex, string hook, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(defIndex, "attr " + defIndex, hook, AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("x")), value));
        return item;
    }

    /// <summary>
    /// <c>CTFWeaponBase::GetMaxClip1</c> (<c>tf_weaponbase.cpp:571-575</c>) scales
    /// the row's clip by the carried item's <c>mult_clipsize</c>: asked with the
    /// owner, the hook chain reaches that player's item — the same answer the
    /// machine spends down from and fills up to.
    /// </summary>
    [Fact]
    public void An_items_clip_size_scales_the_clip_the_machine_spends_and_fills()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9101, "mult_clipsize", 2f);

        Assert.Equal(12, world.Behaviour(Scattergun).ModifyClipSize(world.Row(Scattergun), world.Row(Scattergun).ClipSize, TFWeaponWorld.Owner));
    }

    /// <summary>
    /// <c>CTFWeaponBase::ApplyFireDelay</c> (<c>tf_weaponbase.cpp:1895-1898</c>)
    /// scales the fire delay by the item's <c>mult_postfiredelay</c>: the machine
    /// asks about the command's owner, so the clock rides the item through the
    /// catch-up loop.
    /// </summary>
    [Fact]
    public void An_items_post_fire_delay_moves_the_next_shot()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9102, "mult_postfiredelay", 2f);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(10f + 1.25f, w.NextPrimaryAttack, 4);   // TimeFireDelay 0.625, doubled
        Assert.Equal(5, w.Clip);
    }

    /// <summary>
    /// Index 0 is nobody, not entity 0 to be found: an ownerless ask (a bare
    /// give, a world-side tick) scales from nothing. The item parked behind
    /// index 0 carries both hooks, so a wiring that asks the owners for
    /// nobody's owner is caught on either arm.
    /// </summary>
    [Fact]
    public void The_clip_and_interval_points_scale_from_nothing_without_an_owner()
    {
        var world = new TFWeaponWorld();
        var atZero = PercentageItem(9103, "mult_clipsize", 2f);
        atZero.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9104, "attr 9104", "mult_postfiredelay", AttributeDescriptionFormat.Percentage,
                                    AttributeEffectType.Positive, false, false, AttributeValueType.Default,
                                    MergedKeys.Block("x")), 2f));
        world.Owners.Items[0] = atZero;

        var behaviour = world.Behaviour(Scattergun);
        var row = world.Row(Scattergun);

        Assert.Equal(6, behaviour.ModifyClipSize(row, row.ClipSize, ownerIndex: 0));
        Assert.Equal(row.Interval, behaviour.ModifyFireInterval(row, row.Interval, ownerIndex: 0), 4);
    }

    [Fact]
    public void A_vetoing_hook_stops_the_shot_and_every_hook_is_still_asked()
    {
        var veto = new Hook(5, fire: false);
        var after = new Hook(6);
        var world = new TFWeaponWorld(veto, after);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f);

        Assert.Empty(world.Arms.Bullets);
        Assert.Equal(6, w.Clip);
        Assert.Equal(1, veto.Asked);
        Assert.Equal(1, after.Asked);
    }

    //-------------------------------------------------------------------------
    // The shot
    //-------------------------------------------------------------------------

    [Fact]
    public void A_scattergun_shot_is_ten_pellets_at_the_scripts_six()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(200, 10000));

        var shot = Assert.Single(world.Arms.Bullets);
        Assert.Equal(10, shot.Pellets);
        Assert.Equal(new ShotDamage(6f, false), shot.Shot);
        Assert.Equal(10.625f, w.NextPrimaryAttack, 4);
    }

    [Fact]
    public void A_crit_boosted_owners_shot_is_flagged_and_not_tripled()
    {
        var world = new TFWeaponWorld();
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.CritboostedUserBuff);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(new ShotDamage(6f, true), Assert.Single(world.Arms.Bullets).Shot);
    }

    [Fact]
    public void The_server_and_a_client_roll_one_command_alike()
    {
        int crit = TFWeaponWorld.GunSeed(0, 200);
        var server = new TFWeaponWorld();
        var client = new TFWeaponWorld();

        var (sw, svm) = server.Armed(Scattergun, clip: 6, reserve: 32);
        var (cw, cvm) = client.Armed(Scattergun, clip: 6, reserve: 32);

        var onServer = server.Behaviour(Scattergun).ModifyShot(server.Attack(sw, svm, 10f, crit), default);
        var onClient = client.Behaviour(Scattergun).ModifyShot(client.Attack(cw, cvm, 10f, crit), default);

        Assert.Equal(new ShotDamage(6f, true), onServer);
        Assert.Equal(onServer, onClient);
    }

    //-------------------------------------------------------------------------
    // The observed rate (CanFireRandomCriticalShot, tf_weaponbase.cpp:4630-4664):
    // the server measures the owner's round's random-crit share of their ranged
    // damage, sets it on the weapon state and refuses the random crit past the
    // chance by the tenth; a side without the round's numbers — stock's client,
    // ours the owners that answer null — compares the stored value alone. The
    // scattergun's chance is the plain 0.02, so the tolerance is 0.12.
    //-------------------------------------------------------------------------

    [Fact]
    public void An_owner_whose_random_crits_are_over_the_tolerance_is_refused_the_next_one()
    {
        // (300/3) / (100 + 700) = 0.125 past 0.12: the drawn crit is refused
        // and the draw alone never repeats what the bucket already paid for.
        int crit = TFWeaponWorld.GunSeed(0, 200);
        var world = new TFWeaponWorld();
        world.Owners.RoundStats = (Total: 1000, RandomCrit: 300);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        var shot = world.Behaviour(Scattergun).ModifyShot(world.Attack(w, vm, 10f, crit), default);

        Assert.Equal(new ShotDamage(6f, false), shot);
        // Stock's Set runs before the compare, so the refusal records the rate too.
        Assert.Equal(0.125f, w.ObservedCritChance, 4);
    }

    [Fact]
    public void An_owner_under_the_tolerance_rolls_the_random_crit_and_records_the_rate()
    {
        // (250/3) / (83.3 + 750) = 0.1 under 0.12: the crit rides, and the
        // measured rate lands on the weapon state for the owner's client.
        int crit = TFWeaponWorld.GunSeed(0, 200);
        var world = new TFWeaponWorld();
        world.Owners.RoundStats = (Total: 1000, RandomCrit: 250);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        var shot = world.Behaviour(Scattergun).ModifyShot(world.Attack(w, vm, 10f, crit), default);

        Assert.Equal(new ShotDamage(6f, true), shot);
        Assert.Equal(0.1f, w.ObservedCritChance, 4);
    }

    [Fact]
    public void A_side_without_the_rounds_numbers_compares_the_stored_rate_and_sets_nothing()
    {
        // Stock's client arm (:4656-4658) reads the networked member and never
        // Sets it; ours is the owners' default null. The stored 0.125 still
        // refuses, but the state the measure would have written stays.
        int crit = TFWeaponWorld.GunSeed(0, 200);
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);
        w.ObservedCritChance = 0.125f;

        var shot = world.Behaviour(Scattergun).ModifyShot(world.Attack(w, vm, 10f, crit), default);

        Assert.Equal(new ShotDamage(6f, false), shot);
        Assert.Equal(0.125f, w.ObservedCritChance, 4);
    }

    [Fact]
    public void A_replay_answers_the_commands_first_roll_and_pays_nothing_again()
    {
        int crit = TFWeaponWorld.GunSeed(0, 200);
        var client = new TFWeaponWorld();
        var behaviour = client.Behaviour(Scattergun);
        var (w, vm) = client.Armed(Scattergun, clip: 6, reserve: 32);
        var bucket = client.Parts.Buckets.For(TFWeaponWorld.WeaponEntity, w.WeaponTypeId, 300f);

        var first = behaviour.ModifyShot(client.Attack(w, vm, 10f, crit), default);
        float paid = bucket.Tokens;

        // Rolled again, the bucket (20 left) would refuse the 540 the second
        // request costs; the replay answers the first roll instead.
        var replay = behaviour.ModifyShot(client.Attack(w, vm, 10f, crit, live: false), default);

        Assert.Equal(new ShotDamage(6f, true), first);
        Assert.Equal(first, replay);
        Assert.Equal(paid, bucket.Tokens);
        Assert.Equal(1, bucket.SeedRequests);
    }

    [Fact]
    public void A_replay_of_a_command_never_rolled_here_leaves_the_bucket_alone()
    {
        var client = new TFWeaponWorld();
        var (w, vm) = client.Armed(Scattergun, clip: 6, reserve: 32);
        var bucket = client.Parts.Buckets.For(TFWeaponWorld.WeaponEntity, w.WeaponTypeId, 300f);

        // The seed says crit, so the roll reaches the bucket (a copy of it).
        client.Behaviour(Scattergun).ModifyShot(client.Attack(w, vm, 10f, TFWeaponWorld.GunSeed(0, 200), live: false), default);

        Assert.Equal(300f, bucket.Tokens);
        Assert.Equal(1, bucket.Checks);
        Assert.Equal(0, bucket.SeedRequests);
    }

    [Fact]
    public void With_random_crits_off_the_seed_that_crit_does_not()
    {
        int crit = TFWeaponWorld.GunSeed(0, 200);
        var world = new TFWeaponWorld();
        world.Settings.Current = new TFCritSettings { RandomCrits = false };
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        Assert.Equal(new ShotDamage(6f), world.Behaviour(Scattergun).ModifyShot(world.Attack(w, vm, 10f, crit), default));
    }

    [Fact]
    public void An_items_damage_bonus_scales_the_shot()
    {
        var world = new TFWeaponWorld();
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(9001, "damage bonus", "mult_dmg", AttributeDescriptionFormat.Percentage,
                                                               AttributeEffectType.Positive, false, false, AttributeValueType.Default,
                                                               MergedKeys.Block("x")), 1.5f));
        world.Owners.Items[TFWeaponWorld.Owner] = item;
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        Assert.Equal(new ShotDamage(9f), world.Behaviour(Scattergun).ModifyShot(world.Attack(w, vm, 10f, TFWeaponWorld.GunSeed(200, 10000)), default));
    }

    [Fact]
    public void A_damage_hook_reaches_the_arms()
    {
        var world = new TFWeaponWorld(new Hook(5, damage: d => d + 4f));
        var (w, vm) = world.Armed(Pistol, clip: 12, reserve: 36);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(new ShotDamage(19f), Assert.Single(world.Arms.Bullets).Shot);
    }

    //-------------------------------------------------------------------------
    // The single-shell reload
    //-------------------------------------------------------------------------

    [Fact]
    public void An_empty_scattergun_loads_one_shell_after_the_start_and_one_more_each_half_second()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        Assert.True(w.InReload);
        Assert.Equal(TFWeaponBehaviour.ModeReloadingSingly, w.BehaviourMode & TFWeaponBehaviour.ModeReloadingSingly);

        // TimeReloadStart 0.1 + TimeReload 0.5.
        world.Run(w, vm, 0, now: 10.55f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.6f);
        Assert.Equal(1, w.Clip);
        Assert.Equal(31, w.Reserve);
        world.Run(w, vm, 0, now: 11.05f);
        Assert.Equal(1, w.Clip);
        world.Run(w, vm, 0, now: 11.1f);
        Assert.Equal(2, w.Clip);
        Assert.True(w.InReload);
    }

    [Fact]
    public void A_single_shell_reload_ends_full_and_hands_back_to_the_machine()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, 0, now: 10f);
        for (float t = 10.05f; t < 14f; t += 0.05f)
            world.Run(w, vm, 0, now: t);

        Assert.Equal(6, w.Clip);
        Assert.Equal(26, w.Reserve);
        Assert.False(w.InReload);
        Assert.Equal(0, w.BehaviourMode & TFWeaponBehaviour.ModeReloadingSingly);
    }

    [Fact]
    public void A_single_shell_reload_stops_when_the_reserve_runs_out()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 2);

        world.Run(w, vm, 0, now: 10f);
        for (float t = 10.05f; t < 14f; t += 0.05f)
            world.Run(w, vm, 0, now: t);

        Assert.Equal(2, w.Clip);
        Assert.Equal(0, w.Reserve);
        Assert.False(w.InReload);
    }

    [Fact]
    public void The_trigger_with_a_shell_in_aborts_the_reload_and_fires()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, 0, now: 10f);
        world.Run(w, vm, 0, now: 10.6f);
        Assert.Equal(1, w.Clip);

        world.Run(w, vm, Attack, now: 10.7f);
        Assert.False(w.InReload);
        Assert.Empty(world.Arms.Bullets);

        world.Run(w, vm, Attack, now: 10.72f, seed: TFWeaponWorld.GunSeed(200, 10000));
        Assert.Single(world.Arms.Bullets);
        Assert.Equal(0, w.Clip);
    }

    [Fact]
    public void An_empty_click_reload_is_taken_over_one_shell_at_a_time()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        // The trigger on an empty clip: the machine's own reload, without Reload.
        world.Run(w, vm, Attack, now: 10f);
        Assert.True(w.InReload);
        Assert.Equal(TFWeaponBehaviour.ModeReloadingSingly, w.BehaviourMode & TFWeaponBehaviour.ModeReloadingSingly);

        world.Run(w, vm, 0, now: 10.6f);
        Assert.Equal(1, w.Clip);
    }

    [Fact]
    public void A_whole_clip_reload_with_no_animation_waits_the_scripts_reload_time()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Pistol, clip: 0, reserve: 36);

        world.Run(w, vm, 0, now: 10f);
        Assert.True(w.InReload);
        world.Run(w, vm, 0, now: 10.45f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.5f);
        Assert.Equal(12, w.Clip);
        Assert.Equal(24, w.Reserve);
    }

    /// <summary>
    /// The whole-clip reload's clock is the viewmodel's reload animation,
    /// finished 0.2 s early — stock's <c>DefaultReload</c>
    /// (<c>tf_weaponbase.cpp:2223-2227</c>): <c>SequenceDuration() - 0.2</c>,
    /// not the script's <c>TimeReload</c> the no-animation path takes.
    /// </summary>
    [Fact]
    public void A_viewmodel_reload_animation_times_the_whole_clip_reload_short_of_its_end()
    {
        var world = new TFWeaponWorld();
        world.Arms.ReloadSeconds = 1.5f;
        var (w, vm) = world.Armed(Pistol, clip: 0, reserve: 36);

        world.Run(w, vm, 0, now: 10f);

        Assert.True(w.InReload);
        Assert.Equal(11.3f, w.NextPrimaryAttack, 4);    // 1.5 - 0.2
        Assert.Equal(11.3f, w.TimeWeaponIdle, 4);
    }

    /// <summary>
    /// The chain scales the animation's clock — stock hands
    /// <c>SequenceDuration() - 0.2</c> to <c>SetReloadTimer</c>
    /// (<c>tf_weaponbase.cpp:2238</c>) — so a x2 reload hook lands on 1.3, the
    /// led time, not the anim's whole 1.5.
    /// </summary>
    [Fact]
    public void The_reload_hooks_scale_the_animation_clock_short_of_its_end()
    {
        var world = new TFWeaponWorld(new Hook(5, reload: d => d * 2f));
        world.Arms.ReloadSeconds = 1.5f;
        var (w, vm) = world.Armed(Pistol, clip: 0, reserve: 36);

        world.Run(w, vm, 0, now: 10f);

        Assert.Equal(12.6f, w.NextPrimaryAttack, 4);    // (1.5 - 0.2) x 2
    }

    /// <summary>
    /// The animation plays the base time over the whole clock, stock's
    /// <c>SetPlaybackRate(flBaseReloadTime / flReloadTime)</c>
    /// (<c>tf_weaponbase.cpp:2320-2327</c>): the 1.5 s anim runs over the
    /// 1.3 s clock, ending with it.
    /// </summary>
    [Fact]
    public void The_reload_animation_plays_at_the_base_over_the_clocked_time()
    {
        var world = new TFWeaponWorld();
        world.Arms.ReloadSeconds = 1.5f;
        var (w, vm) = world.Armed(Pistol, clip: 0, reserve: 36);

        world.Run(w, vm, 0, now: 10f);

        Assert.Equal(1.5f / 1.3f, vm.PlaybackRate, 4);
    }

    //-------------------------------------------------------------------------
    // SetReloadTimer's arms (tf_weaponbase.cpp:2266-2331)
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>CTFWeaponBase::SetReloadTimer</c> scales the reload by the item's
    /// <c>mult_reload_time</c> (:2274): every one of the single-shell reload's
    /// script clocks — the first round's start-plus-round and each round's
    /// interval, stock's <c>UpdateReloadTimers</c> (:2248-2261) — waits the
    /// item's fraction.
    /// </summary>
    [Fact]
    public void An_items_reload_time_scales_the_single_shell_reload()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9110, "mult_reload_time", 0.5f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        Assert.True(w.InReload);

        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(1, w.Clip);        // (0.1 + 0.5) x 0.5
        world.Run(w, vm, 0, now: 10.5f);
        Assert.Equal(1, w.Clip);
        world.Run(w, vm, 0, now: 10.55f);
        Assert.Equal(2, w.Clip);        // one TimeReload x 0.5 later
    }

    /// <summary>
    /// The arm beside it — <c>mult_reload_time_hidden</c> (:2275), the hidden
    /// variant no other hook reaches: an item's alone.
    /// </summary>
    [Fact]
    public void An_items_hidden_reload_time_scales_the_single_shell_reload()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9111, "mult_reload_time_hidden", 0.5f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(1, w.Clip);
    }

    /// <summary><c>fast_reload</c> (:2276), the third of the item's three arms.</summary>
    [Fact]
    public void An_items_fast_reload_attribute_scales_the_single_shell_reload()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9112, "fast_reload", 0.5f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(1, w.Clip);
    }

    /// <summary>
    /// The player's own attribute manager — <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER
    /// (pPlayer, flReloadTime, hwn_mult_reload_time)</c> (:2277), read through
    /// <c>ITFWeaponOwners.ManagerOf</c>, the same carrier answer the max-ammo
    /// walk reaches.
    /// </summary>
    [Fact]
    public void A_players_manager_reload_time_scales_the_single_shell_reload()
    {
        var world = new TFWeaponWorld();
        world.Owners.Managers[TFWeaponWorld.Owner] = PercentageItem(9113, "hwn_mult_reload_time", 0.5f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(1, w.Clip);
    }

    /// <summary>
    /// The king branch's condition half: <c>InCond( TF_COND_KING_BUFFED )</c>
    /// takes three quarters of the reload (:2305-2308); the branch's other
    /// entry, carrying the king rune, has no state to read yet. The condition
    /// is a predicted bit, so both sides scale the same clock.
    /// </summary>
    [Fact]
    public void A_king_buffed_player_reloads_in_three_quarters_the_time()
    {
        var world = new TFWeaponWorld();
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.KingBuffed);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.4f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.45f);
        Assert.Equal(1, w.Clip);        // (0.1 + 0.5) x 0.75
    }

    /// <summary>
    /// The while-healed arm (:2313-2317): one healer on the shooter and their
    /// own walk's <c>mult_reload_time_while_healed</c> scales the reload, the
    /// link stock scales last of the live arms.
    /// </summary>
    [Fact]
    public void A_singly_healed_shooter_reloads_by_the_manager_hook()
    {
        var world = new TFWeaponWorld(new TFHealedReloadHook(new FixedHealers(1), new AttributeHooks()));
        world.Owners.Managers[TFWeaponWorld.Owner] = PercentageItem(9116, "mult_reload_time_while_healed", 0.5f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(1, w.Clip);        // (0.1 + 0.5) x 0.5
    }

    /// <summary>Two healers are not one: the arm is stock's exact count.</summary>
    [Fact]
    public void Two_healers_leave_the_reload_unhooked()
    {
        var world = new TFWeaponWorld(new TFHealedReloadHook(new FixedHealers(2), new AttributeHooks()));
        world.Owners.Managers[TFWeaponWorld.Owner] = PercentageItem(9117, "mult_reload_time_while_healed", 0.5f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(0, w.Clip);        // unscaled, the first shell waits for 10.6
        world.Run(w, vm, 0, now: 10.6f);
        Assert.Equal(1, w.Clip);
    }

    /// <summary>The healing as the reload arm sees it: only the healer count.</summary>
    private sealed class FixedHealers(int count) : ITFPlayerHealing
    {
        public int NumHealers(int patientIndex) => count;

        public void Heal(EntityHandle patient, EntityHandle healer, float healRate, float now,
                         float overhealBonus = ITFPlayerHealing.StandardOverhealBonus,
                         float decayMult = ITFPlayerHealing.StandardOverhealDecayMult,
                         bool dispenserHeal = false, int scorer = -1)
            => throw new InvalidOperationException();
        public float StopHealing(EntityHandle patient, EntityHandle healer, float now) => throw new InvalidOperationException();
        public void RecalculateChargeEffects(EntityHandle player, float now) => throw new InvalidOperationException();
        public void NoteReleasing(int playerIndex, bool releasing) => throw new InvalidOperationException();
        public int MaxBuffedHealth(EntityHandle patient, bool ignoreHealthOverMax = false) => throw new InvalidOperationException();
        public bool CanReceiveMedigunChargeEffect(EntityHandle player) => throw new InvalidOperationException();
        public void RemoveChargeEffect(EntityHandle player) => throw new InvalidOperationException();
        public void TestAndExpireChargeEffect(EntityHandle player, float now) => throw new InvalidOperationException();
        public void TickPatient(EntityHandle patient, float now, float frametime) => throw new InvalidOperationException();
        public void Extinguished(EntityHandle patient) => throw new InvalidOperationException();
    }

    /// <summary>
    /// Stock floors the scaled reload at 0.00001 s (<c>MAX( , 0.00001f )</c>,
    /// :2319) so an attribute cannot make it zero: a fully zeroed reload still
    /// loads a shell at the next command, never hangs and never loads the
    /// whole clip at once.
    /// </summary>
    [Fact]
    public void A_zeroed_reload_still_loads_its_shells_instead_of_hanging()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9114, "mult_reload_time", 0f);
        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);

        world.Run(w, vm, buttons: 0, now: 10f);
        Assert.True(w.InReload);

        world.Run(w, vm, 0, now: 10.05f);
        Assert.Equal(1, w.Clip);        // the floored start-plus-round is due
        Assert.True(w.InReload);        // the machine's own finish stays held off
    }

    /// <summary>
    /// The whole-clip path's no-animation arm — stock's <c>Reload</c> takes
    /// the script's <c>TimeReload</c> and runs it through
    /// <c>SetReloadTimer</c> (:2231, :2259) — waits the item's fraction too.
    /// </summary>
    [Fact]
    public void An_items_reload_time_scales_the_whole_clip_script_reload()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = PercentageItem(9115, "mult_reload_time", 0.5f);
        var (w, vm) = world.Armed(Pistol, clip: 0, reserve: 36);

        world.Run(w, vm, buttons: 0, now: 10f);
        Assert.True(w.InReload);
        world.Run(w, vm, 0, now: 10.2f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(12, w.Clip);       // TimeReload 0.5, halved
        Assert.Equal(24, w.Reserve);
    }

    /// <summary>A mod's hook reaches the reload's duration, the point the chain owns.</summary>
    [Fact]
    public void A_reload_hook_scales_the_reload_through_the_chain()
    {
        var world = new TFWeaponWorld(new Hook(5, reload: d => d * 0.5f));

        Assert.Equal(0.25f, world.Behaviour(Scattergun).ModifyReloadTime(world.Row(Scattergun), 0.5f, TFWeaponWorld.Owner), precision: 4);

        var (w, vm) = world.Armed(Scattergun, clip: 0, reserve: 32);
        world.Run(w, vm, buttons: 0, now: 10f);
        world.Run(w, vm, 0, now: 10.25f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, now: 10.3f);
        Assert.Equal(1, w.Clip);
    }

    //-------------------------------------------------------------------------
    // Aiming and the source
    //-------------------------------------------------------------------------

    [Fact]
    public void A_weapon_that_does_not_aim_clears_the_owners_aiming()
    {
        var world = new TFWeaponWorld();
        var conditions = world.Owners.Of(TFWeaponWorld.Owner);
        conditions.Add(TFCond.Aiming);
        var (w, vm) = world.Armed(Pistol, clip: 12, reserve: 36);

        world.Run(w, vm, 0, now: 10f);

        Assert.False(conditions.Has(TFCond.Aiming));
    }

    [Fact]
    public void Every_gun_and_shotgun_row_resolves_its_behaviour()
    {
        var world = new TFWeaponWorld();

        foreach (string name in new[] { Scattergun, Pistol, "tf_weapon_smg", "tf_weapon_revolver", "tf_weapon_shotgun_soldier" })
            Assert.IsAssignableFrom<TFWeaponBehaviour>(world.Behaviour(name));
        Assert.DoesNotContain(world.Log.Warnings(), l => l.Contains("tf2.gun") || l.Contains("tf2.shotgun"));
    }
}

/// <summary>The TF gun timer: <c>CTFWeaponBaseGun::PrimaryAttack</c>'s curtime + delay.</summary>
public sealed class TFGunCadenceTests
{
    [Fact]
    public void A_held_smg_fires_a_fire_delay_from_each_shot_on_the_tick()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_smg", clip: 25, reserve: 75);
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        // 0.1 s at 15 ms ticks: every seventh tick, so 70 ticks hold 10 shots
        // (the core's catch-up would fit 11).
        for (int k = 0; k < 70; k++)
            world.Run(w, vm, (int)InputButtons.Attack, 10f + k * 0.015f, seed);

        Assert.Equal(10, world.Arms.Bullets.Count);
        Assert.Equal(15, w.Clip);
    }
}
