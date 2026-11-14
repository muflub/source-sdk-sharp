using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFCritOwner.AddedChance"/>: a chance a mod adds to the random
/// crit's (a character sheet's crit chance), summed with stock's scaled chance
/// before the draw is compared. Deterministic: each fact picks a command seed
/// whose draw falls where it must.
/// </summary>
public sealed class TFCritAddedChanceTests
{
    private static readonly TFCritSettings Stock = new();

    private static TFCritShot Gun(int seed, bool rapid = false) =>
        new(Melee: false, RapidFire: rapid, Damage: 15f, BulletsPerShot: 1, FireDelay: 0.1f,
            new WeaponCommand(seed, 5, 3), 10f, FirstPrediction: true);

    private static TFCritShot Bat(int seed) =>
        new(Melee: true, RapidFire: false, Damage: 35f, BulletsPerShot: 0, FireDelay: 0.5f,
            new WeaponCommand(seed, 5, 3), 10f, FirstPrediction: true);

    /// <summary>The first command seed whose draw, under <paramref name="seedOf"/>, lands in [lo, hi).</summary>
    private static int SeedDrawing(Func<int, int> seedOf, int lo, int hi)
    {
        var roll = new TFCritRoll();
        for (int seed = 1; seed < 200_000; seed++)
        {
            int r = roll.Draw(seedOf(seed));
            if (r >= lo && r < hi)
                return seed;
        }
        throw new InvalidOperationException($"no seed draws in [{lo}, {hi})");
    }

    private static int GunSeed(int lo, int hi) => SeedDrawing(s => TFCritRoll.GunSeed(new WeaponCommand(s, 5, 3)), lo, hi);

    private static int MeleeSeed(int lo, int hi) => SeedDrawing(s => TFCritRoll.MeleeSeed(new WeaponCommand(s, 5, 3)), lo, hi);

    /// <summary>
    /// One roll with a bucket deep enough to pay any crit here (a rapid-fire
    /// crit prepays its whole stream at three times its damage), so the draw
    /// alone decides.
    /// </summary>
    private static bool Roll(in TFCritShot shot, float added)
    {
        var settings = Stock with { BucketCap = 100_000f };
        var clocks = new TFCritClocks();
        return new TFCritRoll().Roll(in shot, new TFCritOwner { AddedChance = added }, in settings, new CritBucket(100_000f),
                                     ref clocks).Critical;
    }

    [Fact]
    public void A_plain_owner_adds_nothing()
    {
        Assert.Equal(0f, new TFCritOwner().AddedChance);
    }

    [Fact]
    public void A_guns_draw_past_stocks_two_percent_crits_with_five_points_added()
    {
        // 2 % is 200 of 10,000; 2 % + 5 % is 700.
        var shot = Gun(GunSeed(200, 700));

        Assert.False(Roll(in shot, 0f));
        Assert.True(Roll(in shot, 0.05f));
    }

    [Fact]
    public void A_guns_draw_past_the_sum_stays_a_miss()
    {
        var shot = Gun(GunSeed(700, 10_000));

        Assert.False(Roll(in shot, 0.05f));
    }

    [Fact]
    public void A_melee_draw_past_stocks_fifteen_percent_crits_with_five_points_added()
    {
        // 15 % is 1,500 of 10,000; 20 % is 2,000.
        var shot = Bat(MeleeSeed(1_500, 2_000));

        Assert.False(Roll(in shot, 0f));
        Assert.True(Roll(in shot, 0.05f));
    }

    [Fact]
    public void A_rapid_fire_weapons_start_chance_grows_with_the_added_share_of_crit_time()
    {
        // Stock: 2 % of the time critting in 2 s streams starts one every 98 s, 1/98 = 102 of 10,000.
        // With 5 points added, 7 %: one every 2/0.07 - 2 = 26.57 s, 1/26.57 = 376 of 10,000.
        var shot = Gun(GunSeed(110, 370), rapid: true);

        Assert.False(Roll(in shot, 0f));
        Assert.True(Roll(in shot, 0.05f));
    }
}
