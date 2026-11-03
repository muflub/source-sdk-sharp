using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFDamageRecords"/>: the last hit, and stock's damager history
/// (<c>CHistoryVector</c>: distinct, newest first, four slots).
/// </summary>
public sealed class TFDamageRecordsTests
{
    private static TFHitRecord From(uint attacker, float time) =>
        new(new EntityHandle(attacker), 10f, DamageType.Bullet, TFCritType.None, TFDamageCustom.None, TFWeaponId.Pistol, "tf_weapon_pistol", time);

    [Fact]
    public void The_second_most_recent_damager_within_the_window_is_the_assister()
    {
        var records = new TFDamageRecords();
        records.Note(3, From(0x10005u, 1f), damager: true);
        records.Note(3, From(0x10006u, 2f), damager: true);

        Assert.Equal(new EntityHandle(0x10006u), records.RecentDamager(3, 0, 3f, 2.5f));
        Assert.Equal(new EntityHandle(0x10005u), records.RecentDamager(3, 1, 3f, 2.5f));
        Assert.Equal(EntityHandle.None, records.RecentDamager(3, 1, 3f, 4.5f));
    }

    [Fact]
    public void A_damager_hitting_again_moves_to_the_front_and_the_history_keeps_four()
    {
        var records = new TFDamageRecords();
        for (uint i = 1; i <= 5; i++)
            records.Note(3, From(0x10000u + i, i), damager: true);
        records.Note(3, From(0x10003u, 6f), damager: true);

        Assert.Equal(new EntityHandle(0x10003u), records.RecentDamager(3, 0, 10f, 6f));
        Assert.Equal(new EntityHandle(0x10005u), records.RecentDamager(3, 1, 10f, 6f));
        Assert.Equal(new EntityHandle(0x10004u), records.RecentDamager(3, 2, 10f, 6f));
        Assert.Equal(new EntityHandle(0x10002u), records.RecentDamager(3, 3, 10f, 6f));
        Assert.Equal(EntityHandle.None, records.RecentDamager(3, 4, 10f, 6f));
    }

    [Fact]
    public void A_teammates_hit_is_the_last_hit_but_no_damager_and_a_clear_forgets_both()
    {
        var records = new TFDamageRecords();
        records.Note(3, From(0x10005u, 1f), damager: false);
        Assert.NotNull(records.LastHit(3));
        Assert.Equal(EntityHandle.None, records.RecentDamager(3, 0, 10f, 1f));

        records.Note(3, From(0x10006u, 1f), damager: true);
        records.Clear(3);
        Assert.Null(records.LastHit(3));
        Assert.Equal(EntityHandle.None, records.RecentDamager(3, 0, 10f, 1f));
    }
}
