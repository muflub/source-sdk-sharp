using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;

using Xunit;

namespace TF2.Descent.Tests.Loot;

/// <summary>
/// A kill's ammo drop by its robot's size class (loot.json's <c>ammo</c>,
/// <see cref="LootRules.Ammo"/>): a runt a small pack one kill in ten, an
/// ordinary robot one in five, a giant a medium pack every kill, a boss a full
/// one; seeded like the loot, so a replay agrees.
/// </summary>
public sealed class AmmoDropRulesTests
{
    private static readonly TF2DescentRules Rules = new();

    private const int Kills = 4000;

    /// <summary>The packs <paramref name="template"/> drops over <see cref="Kills"/> kills of instance <paramref name="seed"/>.</summary>
    private static List<string?> Drops(string template, ulong seed = 0x5EED) =>
        [.. Enumerable.Range(0, Kills).Select(k => Rules.Loot.Ammo(seed, (uint)k, template, 1, Rules.Salt))];

    private static double Rate(List<string?> drops) => 100.0 * drops.Count(d => d is not null) / drops.Count;

    [Fact]
    public void An_ordinary_robot_drops_a_small_pack_one_kill_in_five()
    {
        var drops = Drops("soldier");

        Assert.InRange(Rate(drops), 18, 22);
        Assert.All(drops.Where(d => d is not null), d => Assert.Equal(AmmoPacks.Small, d));
    }

    [Fact]
    public void A_runt_drops_a_small_pack_one_kill_in_ten()
    {
        var drops = Drops("runt_scout");

        Assert.InRange(Rate(drops), 8.5, 11.5);
        Assert.All(drops.Where(d => d is not null), d => Assert.Equal(AmmoPacks.Small, d));
    }

    [Fact]
    public void A_giant_drops_a_medium_pack_every_kill()
    {
        Assert.All(Drops("giant_heavy").Take(200), d => Assert.Equal(AmmoPacks.Medium, d));
    }

    [Fact]
    public void A_boss_drops_a_full_pack_every_kill()
    {
        Assert.All(Drops("major_crits").Take(200), d => Assert.Equal(AmmoPacks.Full, d));
    }

    [Fact]
    public void A_replay_of_the_same_kills_drops_the_same_packs()
    {
        Assert.Equal(Drops("scout"), Drops("scout"));
    }

    [Fact]
    public void Another_instance_drops_on_other_kills()
    {
        Assert.NotEqual(Drops("scout", 1), Drops("scout", 2));
    }

    [Fact]
    public void A_champions_affixes_ride_the_key_and_keep_its_size_classes_odds()
    {
        var drops = Enumerable.Range(0, Kills).Select(k => Rules.Loot.Ammo(7, (uint)k, "soldier+extra_fast", 1, Rules.Salt)).ToList();

        Assert.InRange(Rate(drops), 18, 22);
    }
}
