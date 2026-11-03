extern alias client;

using Xunit;

using CTFPlayer = client::TF2.Client.Player;
using ClientBuckets = client::TF2.Shared.TFCritBuckets;

namespace TF2.Tests.Player;

/// <summary>
/// The client's half of stock's weapon constructor refilling a fresh
/// weapon's crit bucket (basecombatweapon_shared.cpp:103-106): when the
/// arsenal adopts a new entity for the local player, the respawn's weapon
/// starts its bucket full, not with the dead one's tokens — even at a
/// recycled entity index, which is exactly what the respawn's kit hands out.
/// </summary>
public sealed class TFCritBucketWatchTests
{
    /// <summary>Stock's <c>tf_weapon_criticals_bucket_default</c>.</summary>
    private const float BucketDefault = 1000f;

    private const int Weapon = 65;

    [Fact]
    public void A_readopted_weapon_starts_its_bucket_full_again()
    {
        var buckets = new ClientBuckets();
        var watcher = new CTFPlayer.TFClientCritBucketWatch(buckets);

        // The weapon fought its bucket dry; an ask with the default cannot
        // revive an entry that exists.
        Assert.Equal(0f, buckets.For(Weapon, 2, 0f).Tokens);
        Assert.Equal(0f, buckets.For(Weapon, 2, BucketDefault).Tokens);

        // The respawn: the kit's fresh entity, same recycled index, adopted.
        watcher.LocalWeaponSpawned(Weapon);

        Assert.Equal(BucketDefault, buckets.For(Weapon, 2, BucketDefault).Tokens);
    }

    [Fact]
    public void A_watch_on_another_weapon_leaves_this_ones_bucket_alone()
    {
        var buckets = new ClientBuckets();
        var watcher = new CTFPlayer.TFClientCritBucketWatch(buckets);
        Assert.Equal(0f, buckets.For(Weapon, 2, 0f).Tokens);

        watcher.LocalWeaponSpawned(Weapon + 1);

        Assert.Equal(0f, buckets.For(Weapon, 2, BucketDefault).Tokens);
    }
}
