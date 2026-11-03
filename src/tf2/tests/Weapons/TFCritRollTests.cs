using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFCritRoll"/>: <c>CalcIsAttackCritical</c> and its helpers
/// (<c>tf_weaponbase.cpp:1529-1786</c>, melee <c>tf_weaponbase_melee.cpp:1040-1131</c>).
/// </summary>
public sealed class TFCritRollTests
{
    private static readonly TFCritSettings Stock = new();
    private static readonly TFCritOwner Plain = new();

    private static TFCritShot Gun(int seed, int weapon = 5, int owner = 3, float now = 10f, bool rapid = false, bool first = true) =>
        new(Melee: false, RapidFire: rapid, Damage: 15f, BulletsPerShot: 1, FireDelay: 0.15f,
            new WeaponCommand(seed, weapon, owner), now, first);

    private static TFCritShot Bat(int seed, int weapon = 5, int owner = 3, bool first = true) =>
        new(Melee: true, RapidFire: false, Damage: 35f, BulletsPerShot: 0, FireDelay: 0.5f,
            new WeaponCommand(seed, weapon, owner), 10f, first);

    /// <summary>The first command seed from <paramref name="from"/> whose draw, under <paramref name="seedOf"/>, lands in [lo, hi).</summary>
    private static int SeedDrawing(Func<int, int> seedOf, int lo, int hi, int from = 1)
    {
        var roll = new TFCritRoll();
        for (int seed = from; seed < from + 200_000; seed++)
        {
            int r = roll.Draw(seedOf(seed));
            if (r >= lo && r < hi)
                return seed;
        }
        throw new InvalidOperationException($"no seed draws in [{lo}, {hi})");
    }

    private static int GunSeedDrawing(int lo, int hi) => SeedDrawing(s => TFCritRoll.GunSeed(new WeaponCommand(s, 5, 3)), lo, hi);

    private static int MeleeSeedDrawing(int lo, int hi) => SeedDrawing(s => TFCritRoll.MeleeSeed(new WeaponCommand(s, 5, 3)), lo, hi);

    [Fact]
    public void The_gun_seed_is_the_command_seed_xor_the_weapon_shifted_eight_and_the_owner()
    {
        Assert.Equal(0x1234567 ^ 0x0503, TFCritRoll.GunSeed(new WeaponCommand(0x1234567, 5, 3)));
    }

    [Fact]
    public void The_melee_seed_shifts_the_weapon_sixteen_and_the_owner_eight()
    {
        Assert.Equal(0x1234567 ^ 0x050300, TFCritRoll.MeleeSeed(new WeaponCommand(0x1234567, 5, 3)));
    }

    [Fact]
    public void A_draw_is_stocks_first_random_int_after_reseeding()
    {
        var roll = new TFCritRoll();
        var stream = new UniformStream(4242);

        Assert.Equal(stream.RandomInt(0, 9999), roll.Draw(4242));
        // And again: the draw remembers nothing between rolls.
        Assert.Equal(new UniformStream(4242).RandomInt(0, 9999), roll.Draw(4242));
    }

    [Fact]
    public void Two_rolls_of_one_command_agree_as_the_server_and_a_client_replay_must()
    {
        for (int seed = 1; seed < 400; seed++)
        {
            var server = new TFCritRoll();
            var client = new TFCritRoll();
            var serverClocks = new TFCritClocks();
            var clientClocks = new TFCritClocks();

            var a = server.Roll(Gun(seed), in Plain, in Stock, new CritBucket(1000f), ref serverClocks);
            var b = client.Roll(Gun(seed, first: false), in Plain, in Stock, new CritBucket(1000f), ref clientClocks);

            Assert.Equal(a.Critical, b.Critical);
            Assert.Equal(a.Roll, b.Roll);
        }
    }

    [Fact]
    public void Another_weapon_index_draws_from_another_seed()
    {
        var roll = new TFCritRoll();
        int differing = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            if (roll.Draw(TFCritRoll.GunSeed(new WeaponCommand(seed, 5, 3))) != roll.Draw(TFCritRoll.GunSeed(new WeaponCommand(seed, 6, 3))))
                differing++;
        }
        Assert.True(differing > 90, $"only {differing} of 100 commands drew differently for another weapon");
    }

    [Fact]
    public void A_single_shot_gun_crits_when_its_draw_is_under_two_percent_and_the_bucket_pays()
    {
        int under = GunSeedDrawing(0, 200);
        int over = GunSeedDrawing(200, 10000);
        var clocks = new TFCritClocks();

        var crit = new TFCritRoll().Roll(Gun(under), in Plain, in Stock, new CritBucket(300f), ref clocks);
        var none = new TFCritRoll().Roll(Gun(over), in Plain, in Stock, new CritBucket(300f), ref clocks);

        Assert.True(crit.Critical);
        Assert.True(crit.Random);
        Assert.InRange(crit.Roll, 0, 199);
        Assert.False(none.Critical);
    }

    [Fact]
    public void A_crit_the_bucket_cannot_pay_for_is_no_crit()
    {
        int under = GunSeedDrawing(0, 200);
        var clocks = new TFCritClocks();

        // 15 in, then a cost of 15 * 3 * 1.889 = 85 > 15.
        var refused = new TFCritRoll().Roll(Gun(under), in Plain, in Stock, new CritBucket(0f), ref clocks);

        Assert.False(refused.Critical);
    }

    [Fact]
    public void With_random_crits_off_no_shot_crits_and_nothing_is_drawn()
    {
        var off = Stock with { RandomCrits = false };
        var bucket = new CritBucket(1000f);
        var clocks = new TFCritClocks();
        var roll = new TFCritRoll();

        for (int seed = 1; seed <= 2000; seed++)
        {
            var result = roll.Roll(Gun(seed), in Plain, in off, bucket, ref clocks);
            Assert.False(result.Critical);
            Assert.Equal(-1, result.Roll);
        }

        // The early-out never reaches the bucket.
        Assert.Equal(1, bucket.Checks);
        Assert.Equal(1000f, bucket.Tokens);
    }

    [Fact]
    public void With_random_crits_off_a_crit_boosted_player_still_crits()
    {
        var off = Stock with { RandomCrits = false };
        var clocks = new TFCritClocks();

        var result = new TFCritRoll().Roll(Gun(GunSeedDrawing(200, 10000)), new TFCritOwner(CritBoosted: true), in off, new CritBucket(0f), ref clocks);

        Assert.True(result.Critical);
    }

    [Fact]
    public void A_crit_boosted_player_crits_without_a_draw_or_a_payment()
    {
        var bucket = new CritBucket(0f);
        var clocks = new TFCritClocks();

        var result = new TFCritRoll().Roll(Gun(GunSeedDrawing(200, 10000)), new TFCritOwner(CritBoosted: true), in Stock, bucket, ref clocks);

        Assert.True(result.Critical);
        Assert.Equal(-1, result.Roll);
        Assert.Equal(0f, bucket.Tokens);
    }

    [Fact]
    public void The_round_winners_crit_even_with_random_crits_off()
    {
        var clocks = new TFCritClocks();

        var result = new TFCritRoll().Roll(Gun(GunSeedDrawing(200, 10000)), new TFCritOwner(WinsCrits: true),
                                           Stock with { RandomCrits = false }, new CritBucket(0f), ref clocks);

        Assert.True(result.Critical);
    }

    [Fact]
    public void A_weapon_that_cannot_fire_a_critical_shot_never_crits_even_boosted()
    {
        var clocks = new TFCritClocks();

        var result = new TFCritRoll().Roll(Gun(GunSeedDrawing(0, 200)), new TFCritOwner(CritBoosted: true, CanFireCriticalShot: false),
                                           in Stock, new CritBucket(1000f), ref clocks);

        Assert.False(result.Critical);
    }

    [Fact]
    public void An_observed_rate_past_the_chance_by_a_tenth_refuses_the_crit()
    {
        int under = GunSeedDrawing(0, 200);
        var clocks = new TFCritClocks();

        var refused = new TFCritRoll().Roll(Gun(under), new TFCritOwner(ObservedCritChance: 0.13f), in Stock, new CritBucket(1000f), ref clocks);
        var allowed = new TFCritRoll().Roll(Gun(under), new TFCritOwner(ObservedCritChance: 0.11f), in Stock, new CritBucket(1000f), ref clocks);

        Assert.False(refused.Critical);
        Assert.True(allowed.Critical);
    }

    [Fact]
    public void The_observed_rate_is_stocks_normalized_share()
    {
        // (rc / 3) over the crit's own triple taken out of the total first
        // (tf_weaponbase.cpp:4651-4652): 300 of 1000 read as 100/800, all-crit
        // as 1, and plain damage as 0.
        Assert.Equal(0.125f, TFCritRoll.ObservedCritRate(300, 1000), 4);
        Assert.Equal(0.1f, TFCritRoll.ObservedCritRate(250, 1000), 4);
        Assert.Equal(1f, TFCritRoll.ObservedCritRate(300, 300), 4);
        Assert.Equal(0f, TFCritRoll.ObservedCritRate(0, 500), 4);
    }

    [Fact]
    public void A_measured_rate_over_the_tolerance_refuses_and_still_offers_the_set()
    {
        // Stock's Set (:4652) runs before the compare (:4660), so the refusal
        // carries the measured value: the writer puts it on the weapon state
        // either way, exactly as stock's member holds it after a refusal.
        int under = GunSeedDrawing(0, 200);
        var clocks = new TFCritClocks();

        var refused = new TFCritRoll().Roll(Gun(under),
            new TFCritOwner(RangedDamage: 1000, RandomCritDamage: 300, StatsMeasured: true),
            in Stock, new CritBucket(1000f), ref clocks);

        Assert.False(refused.Critical);
        Assert.True(refused.Random);
        Assert.Equal(0.125f, refused.ObservedSet!.Value, 4);
    }

    [Fact]
    public void A_measured_round_without_damage_yet_neither_measures_nor_compares()
    {
        // The "Early out" (:4648-4649): a fresh round allows the random crit
        // even with a stale high stored rate, and sets nothing — the early
        // return skips both the Set and the compare.
        int under = GunSeedDrawing(0, 200);
        var clocks = new TFCritClocks();

        var allowed = new TFCritRoll().Roll(Gun(under),
            new TFCritOwner(ObservedCritChance: 0.9f, RangedDamage: 0, StatsMeasured: true),
            in Stock, new CritBucket(1000f), ref clocks);

        Assert.True(allowed.Critical);
        Assert.Null(allowed.ObservedSet);
    }

    [Fact]
    public void A_weapon_held_by_nobody_rolls_no_random_crit()
    {
        // GetTFPlayerOwner(): no owner, no stats, no random crit (:4635-4637)
        // — even one the bucket would pay for and no stored rate would stop.
        int under = SeedDrawing(s => TFCritRoll.GunSeed(new WeaponCommand(s, 5, 0)), 0, 200);
        var clocks = new TFCritClocks();

        var refused = new TFCritRoll().Roll(Gun(under, owner: 0), in Plain, in Stock, new CritBucket(1000f), ref clocks);

        Assert.False(refused.Critical);
        Assert.Null(refused.ObservedSet);
    }

    [Fact]
    public void The_unmeasured_compare_never_offers_a_set_value()
    {
        // The client arm (:4656-4658) compares the networked member and never
        // measures it: nothing rides out for the writer to store.
        int under = GunSeedDrawing(0, 200);
        var clocks = new TFCritClocks();

        var refused = new TFCritRoll().Roll(Gun(under), new TFCritOwner(ObservedCritChance: 0.13f), in Stock, new CritBucket(1000f), ref clocks);

        Assert.False(refused.Critical);
        Assert.Null(refused.ObservedSet);
    }

    [Fact]
    public void A_gun_counts_its_check_on_the_first_prediction_only()
    {
        var bucket = new CritBucket(300f);
        var clocks = new TFCritClocks();
        var roll = new TFCritRoll();

        roll.Roll(Gun(GunSeedDrawing(200, 10000), first: false), in Plain, in Stock, bucket, ref clocks);
        Assert.Equal(1, bucket.Checks);
        // The replay still adds the shot's damage: stock's client bucket grows on every pass.
        Assert.Equal(315f, bucket.Tokens);

        roll.Roll(Gun(GunSeedDrawing(200, 10000), first: true), in Plain, in Stock, bucket, ref clocks);
        Assert.Equal(2, bucket.Checks);
    }

    [Fact]
    public void A_rapid_fire_crit_starts_a_two_second_stream_that_crits_without_drawing()
    {
        // The start chance: total 0.02, non-crit duration 2/0.02 - 2 = 98, 1/98 = 0.0102 -> under 102.04.
        int under = GunSeedDrawing(0, 102);
        var bucket = new CritBucket(1000f);
        for (int i = 0; i < 9; i++)
            bucket.CountCheck();
        var clocks = new TFCritClocks();
        var roll = new TFCritRoll();

        var start = roll.Roll(Gun(under, now: 10f, rapid: true), in Plain, in Stock, bucket, ref clocks);

        Assert.True(start.Critical);
        Assert.Equal(12f, clocks.CritUntil);

        var during = roll.Roll(Gun(GunSeedDrawing(200, 10000), now: 11.5f, rapid: true), in Plain, in Stock, bucket, ref clocks);
        Assert.True(during.Critical);
        Assert.Equal(-1, during.Roll);

        var after = roll.Roll(Gun(GunSeedDrawing(200, 10000), now: 12.5f, rapid: true), in Plain, in Stock, bucket, ref clocks);
        Assert.False(after.Critical);
    }

    [Fact]
    public void A_rapid_fire_crit_prepays_the_stream_capped_at_the_bucket()
    {
        int under = GunSeedDrawing(0, 102);
        var clocks = new TFCritClocks();

        // 15 damage at 0.15 s is 200 over the stream: x3 = 600 under the cap.
        // A fresh bucket (300 + 15) at factor 1.889 is asked 200 * 3 * 1.889 = 1133: refused.
        var refused = new TFCritRoll().Roll(Gun(under, rapid: true), in Plain, in Stock, new CritBucket(300f), ref clocks);
        Assert.False(refused.Critical);
        Assert.Equal(0f, clocks.CritUntil);

        // A full bucket with ten checks behind it pays factor 1: 600 <= 1000.
        var full = new CritBucket(1000f);
        for (int i = 0; i < 9; i++)
            full.CountCheck();
        clocks = new TFCritClocks();
        Assert.True(new TFCritRoll().Roll(Gun(under, rapid: true), in Plain, in Stock, full, ref clocks).Critical);
        Assert.Equal(400f, full.Tokens, 2);
    }

    [Fact]
    public void A_rapid_fire_prepayment_never_asks_more_than_the_bucket_holds()
    {
        // A minigun shot: 9 damage x 4 bullets = 36 at 0.1 s is 720 over the
        // stream, x3 = 2160 past the 1000 cap, so it is cut to 1000 / 3 (:1765-1768):
        // a full bucket with ten checks behind it (factor 1) pays exactly 1000.
        int under = GunSeedDrawing(0, 102);
        var shot = new TFCritShot(Melee: false, RapidFire: true, Damage: 9f, BulletsPerShot: 4, FireDelay: 0.1f,
                                  new WeaponCommand(under, 5, 3), 10f, FirstPrediction: true);
        var full = new CritBucket(1000f);
        for (int i = 0; i < 9; i++)
            full.CountCheck();
        var clocks = new TFCritClocks();

        Assert.True(new TFCritRoll().Roll(in shot, in Plain, in Stock, full, ref clocks).Critical);
        Assert.Equal(0f, full.Tokens, 2);
    }

    [Fact]
    public void A_rapid_fire_weapon_checks_once_a_second()
    {
        var bucket = new CritBucket(300f);
        var clocks = new TFCritClocks();
        var roll = new TFCritRoll();

        var first = roll.Roll(Gun(GunSeedDrawing(200, 10000), now: 10f, rapid: true), in Plain, in Stock, bucket, ref clocks);
        var soon = roll.Roll(Gun(GunSeedDrawing(0, 102), now: 10.5f, rapid: true), in Plain, in Stock, bucket, ref clocks);

        Assert.True(first.Roll >= 0);
        Assert.Equal(10f, clocks.LastRapidCheck);
        Assert.False(soon.Critical);
        Assert.Equal(-1, soon.Roll);
        Assert.Equal(2, bucket.Checks);
    }

    [Fact]
    public void A_melee_swing_crits_under_fifteen_percent()
    {
        int under = MeleeSeedDrawing(1000, 1500);
        int over = MeleeSeedDrawing(1500, 10000);
        var clocks = new TFCritClocks();

        Assert.True(new TFCritRoll().Roll(Bat(under), in Plain, in Stock, new CritBucket(300f), ref clocks).Critical);
        Assert.False(new TFCritRoll().Roll(Bat(over), in Plain, in Stock, new CritBucket(300f), ref clocks).Critical);
    }

    [Fact]
    public void A_melee_swing_counts_every_check_and_pays_half()
    {
        var bucket = new CritBucket(300f);
        var clocks = new TFCritClocks();

        var result = new TFCritRoll().Roll(Bat(MeleeSeedDrawing(0, 1500), first: false), in Plain, in Stock, bucket, ref clocks);

        Assert.True(result.Critical);
        Assert.False(result.Random);
        Assert.Equal(2, bucket.Checks);
        // 300 + 35 - 35 * 3 * 0.5.
        Assert.Equal(282.5f, bucket.Tokens);
    }

    [Fact]
    public void Melee_crits_off_leaves_a_swing_only_its_boost()
    {
        var off = Stock with { MeleeCrits = 0 };
        var clocks = new TFCritClocks();
        int under = MeleeSeedDrawing(0, 1500);

        Assert.False(new TFCritRoll().Roll(Bat(under), in Plain, in off, new CritBucket(1000f), ref clocks).Critical);
        Assert.True(new TFCritRoll().Roll(Bat(under), new TFCritOwner(CritBoosted: true), in off, new CritBucket(1000f), ref clocks).Critical);
    }

    [Fact]
    public void Melee_crits_two_roll_even_with_random_crits_off()
    {
        var forced = Stock with { RandomCrits = false, MeleeCrits = 2 };
        var clocks = new TFCritClocks();

        Assert.True(new TFCritRoll().Roll(Bat(MeleeSeedDrawing(0, 1500)), in Plain, in forced, new CritBucket(1000f), ref clocks).Critical);
        Assert.False(new TFCritRoll().Roll(Bat(MeleeSeedDrawing(0, 1500)), in Plain, Stock with { RandomCrits = false }, new CritBucket(1000f), ref clocks).Critical);
    }

    [Fact]
    public void The_crit_multiplier_and_the_chance_attribute_scale_the_chance()
    {
        // 0.02 * 2 = 4%: a draw in [200, 400) crits only with the multiplier.
        int between = GunSeedDrawing(200, 400);
        var clocks = new TFCritClocks();

        Assert.False(new TFCritRoll().Roll(Gun(between), in Plain, in Stock, new CritBucket(1000f), ref clocks).Critical);
        Assert.True(new TFCritRoll().Roll(Gun(between), new TFCritOwner(CritMult: 2f), in Stock, new CritBucket(1000f), ref clocks).Critical);
        Assert.True(new TFCritRoll().Roll(Gun(between), new TFCritOwner(ChanceScale: 2f), in Stock, new CritBucket(1000f), ref clocks).Critical);
    }
}

/// <summary>The roll's input defaults: <c>new()</c> is stock, which an all-zero struct would not be (no random crits, a zero chance).</summary>
public sealed class TFCritDefaultsTests
{
    private static float Default<T>() => float.Parse(
        System.Reflection.CustomAttributeExtensions.GetCustomAttribute<SourceSharp.Abi.ConVarAttribute>(typeof(T))!.DefaultValue,
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void New_settings_are_the_convars_stock_defaults()
    {
        var settings = new TFCritSettings();

        Assert.Equal(Default<TFWeaponCriticalsConVar>() != 0f, settings.RandomCrits);
        Assert.Equal((int)Default<TFWeaponCriticalsMeleeConVar>(), settings.MeleeCrits);
        Assert.Equal(Default<TFWeaponCriticalsBucketCapConVar>(), settings.BucketCap);
        Assert.Equal(Default<TFWeaponCriticalsBucketBottomConVar>(), settings.BucketBottom);
        Assert.Equal(Default<TFWeaponCriticalsBucketDefaultConVar>(), settings.BucketDefault);
        Assert.True(settings.RandomCrits);
    }

    [Fact]
    public void A_new_owner_is_plain_and_allowed_to_crit()
    {
        var owner = new TFCritOwner();

        Assert.True(owner.CanFireCriticalShot);
        Assert.Equal(1f, owner.CritMult);
        Assert.Equal(1f, owner.ChanceScale);
        Assert.False(owner.CritBoosted);
        Assert.False(owner.StatsMeasured);
        Assert.Equal(0, owner.RangedDamage);
        Assert.Equal(0, owner.RandomCritDamage);
    }
}
