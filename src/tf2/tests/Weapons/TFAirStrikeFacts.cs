using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Air Strike at the machine (<c>CTFRocketLauncher_AirStrike :
/// CTFRocketLauncher</c>, tf_weapon_rocketlauncher.h:109): the launcher's
/// projectile firing behind its rockets, the blast-jump fire rate
/// (<c>tf_weaponbase_gun.cpp:114-121</c>) and the clip the kill grows on the
/// held weapon (<c>tf_weaponbase.cpp:594-600</c>).
/// </summary>
public sealed class TFAirStrikeFacts
{
    private const string Airstrike = "tf_weapon_rocketlauncher_airstrike";
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>A seed whose gun roll draws no crit: the strike's shot is the plain one here.</summary>
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    /// <summary>A launcher item carrying one float attribute.</summary>
    private static AttributeContainer PercentageItem(int defIndex, string hook, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(defIndex, "attr " + defIndex, hook,
                                                                AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default,
                                                                MergedKeys.Block("x")), value));
        return item;
    }

    [Fact]
    public void The_air_strike_runs_the_projectile_behaviour()
    {
        // The stock subclass inherits the rocket launcher's firing, so the
        // kind the script resolves to is the launcher's projectile shell, and
        // the catalogue hands the row its behaviour.
        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(AirstrikeScript()));

        var world = new TFWeaponWorld(extras: true);
        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.Projectile), world.Row(Airstrike).Behaviour);
        Assert.NotNull(world.Behaviour(Airstrike));
    }

    /// <summary>
    /// <c>CTFWeaponBaseGun::PrimaryAttack</c>' pair
    /// (<c>tf_weaponbase_gun.cpp:114-121</c>): the blast-jumping owner takes
    /// the item's <c>rocketjump_attackrate_bonus</c>, everyone else the plain
    /// <c>mul_nonrocketjump_attackrate</c> — both scale the fire delay, so the
    /// next-shot clock tells which arm answered. The item carries both at
    /// different values, so a wrong arm is visible, not just the right one.
    /// </summary>
    [Fact]
    public void A_blast_jump_shot_is_clocked_on_the_items_rocket_jump_bonus()
    {
        var world = new TFWeaponWorld(extras: true);
        world.Owners.Items[TFWeaponWorld.Owner] = AttackRateItem();
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Blastjumping);
        var (w, vm) = world.Armed(Airstrike, clip: 4, reserve: 20);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.Equal(10f + 1.6f, w.NextPrimaryAttack, 4);   // TimeFireDelay 0.8, doubled by the bonus
    }

    /// <summary>The plain arm of the same pair, without the jump condition.</summary>
    [Fact]
    public void A_shot_from_the_ground_is_clocked_on_the_plain_attack_rate()
    {
        var world = new TFWeaponWorld(extras: true);
        world.Owners.Items[TFWeaponWorld.Owner] = AttackRateItem();
        var (w, vm) = world.Armed(Airstrike, clip: 4, reserve: 20);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.Equal(10f + 2.4f, w.NextPrimaryAttack, 4);   // TimeFireDelay 0.8, tripled by the plain rate
    }

    /// <summary>
    /// The stock launcher's <c>m_bReloadsSingly = true</c>
    /// (<c>tf_weapon_rocketlauncher.cpp:159</c>, inherited by the Air Strike
    /// through <c>CTFRocketLauncher_AirStrike</c>,
    /// <c>tf_weapon_rocketlauncher.h:109</c>): one press of the reload button
    /// loads one rocket per round clock, not the whole clip at once — so a
    /// frame taken mid-reload shows exactly one round moved.
    /// </summary>
    [Fact]
    public void The_air_strike_reloads_one_rocket_at_a_time()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Airstrike, clip: 2, reserve: 20);

        world.Run(w, vm, (int)InputButtons.Reload, now: 10f);   // the button's reload starts
        world.Run(w, vm, 0, now: 11f);                           // first round's clock (0.1 + 0.83) has run

        Assert.Equal(3, w.Clip);      // one round loaded, not the whole wanted two
        Assert.Equal(19, w.Reserve);

        world.Run(w, vm, 0, now: 11.8f);                         // second round's clock has run

        Assert.Equal(4, w.Clip);      // the clip is full only after the second clock
        Assert.Equal(18, w.Reserve);
    }

    /// <summary>
    /// The ceiling half of stock's blast arm (<c>tf_weaponbase.cpp:594-611</c>):
    /// <c>Min(GetDecapitations(), clipsize_increase_on_kill)</c> grows the MAX
    /// clip, and the reload fills to that (<c>flClip + nProjectiles</c>, :611).
    /// One head under a cap of two lifts the ceiling one round past the
    /// script's four — the clip fills to it one round per round clock, so the
    /// fifth round is loaded on the third command.
    /// </summary>
    [Fact]
    public void The_reload_ceiling_grows_one_round_per_head_under_the_cap()
    {
        var world = new TFWeaponWorld(new FakePredictedData { Carrier = Heads(1) },
                                      files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));
        world.Owners.Items[TFWeaponWorld.Owner] = CeilingItem(cap: 2f);
        var (w, vm) = world.Armed(Airstrike, clip: 2, reserve: 20);

        world.Run(w, vm, (int)InputButtons.Reload, now: 10f);   // the button's reload starts
        world.Run(w, vm, 0, now: 11f);                           // clip 3
        world.Run(w, vm, 0, now: 11.8f);                         // clip 4, the script's own max

        Assert.Equal(4, w.Clip);                                 // still one head's room left
        world.Run(w, vm, 0, now: 12.6f);                         // clip 5: the head's round on top

        Assert.Equal(5, w.Clip);
        Assert.Equal(17, w.Reserve);                             // three rounds moved, not four
    }

    /// <summary>The cap half of the same Min (:599): three heads under a cap of
    /// two still add only two — the third head has no round to give.</summary>
    [Fact]
    public void The_reload_ceiling_stops_at_the_items_cap()
    {
        var world = new TFWeaponWorld(new FakePredictedData { Carrier = Heads(3) },
                                      files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));
        world.Owners.Items[TFWeaponWorld.Owner] = CeilingItem(cap: 2f);
        var (w, vm) = world.Armed(Airstrike, clip: 2, reserve: 20);

        world.Run(w, vm, (int)InputButtons.Reload, now: 10f);
        world.Run(w, vm, 0, now: 11f);                           // clip 3
        world.Run(w, vm, 0, now: 11.8f);                         // clip 4
        world.Run(w, vm, 0, now: 12.6f);                         // clip 5
        world.Run(w, vm, 0, now: 13.5f);                         // clip 6, the capped ceiling

        Assert.Equal(6, w.Clip);                                 // Min(3 heads, cap 2), not Min(3, ∞)
        Assert.Equal(16, w.Reserve);
    }

    /// <summary>A head count the world's predicted carrier answers — a
    /// locally bound state, the way <see cref="TFWeaponWorld.Armed"/> makes its
    /// own pair.</summary>
    private static TFPlayerNetState Heads(int count)
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.Decapitations = count;
        return state;
    }

    /// <summary>A launcher item answering the kill-cap for the ceiling facts.</summary>
    private static AttributeContainer CeilingItem(float cap)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(30052, "clip grow", "clipsize_increase_on_kill",
                                                                AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("c")), cap));
        return item;
    }

    /// <summary>The held item of both facts: bonus two, plain three, so the
    /// arms answer different clocks and cannot stand in for each other.</summary>
    private static AttributeContainer AttackRateItem()
    {
        var item = PercentageItem(9110, "rocketjump_attackrate_bonus", 2f);
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(9111, "attr 9111", "mul_nonrocketjump_attackrate",
                                                                AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default,
                                                                MergedKeys.Block("x")), 3f));
        return item;
    }

    private static TFWeaponScript AirstrikeScript()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get(Airstrike)
            ?? throw new InvalidOperationException($"no script {Airstrike}");
    }
}
