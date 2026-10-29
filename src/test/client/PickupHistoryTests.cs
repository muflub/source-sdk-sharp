using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The pickup history against stock's CHudHistoryResource
/// (history_resource.cpp). Found beside stock (2026-10-27): nothing showed
/// what a pickup gave; hud_drawhistory_time was declared and read by nothing.
/// </summary>
public class PickupHistoryTests
{
    private const int Pistol = 1, Smg = 3;

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private static (PickupHistory History, Clock Clock) Made(float drawTime = 5f)
    {
        var clock = new Clock();
        return (new PickupHistory(clock, () => drawTime), clock);
    }

    private static PickupHistory.Kind[] Kinds(PickupHistory h) => h.Slots.Select(e => e.Type).ToArray();

    [Fact]
    public void A_weapon_is_listed_once_and_an_exhaustible_one_not_at_all()
    {
        var (h, _) = Made();

        h.AddWeapon(Pistol, "pistol", exhaustible: false);
        h.AddWeapon(Pistol, "pistol", exhaustible: false);
        h.AddWeapon(7, "frag", exhaustible: true);

        var entry = Assert.Single(h.Slots);
        Assert.Equal((PickupHistory.Kind.Weapon, Pistol, "pistol", 15f), (entry.Type, entry.Id, entry.Icon, entry.DisplayTime));
    }

    [Fact]
    public void Ammo_is_listed_with_its_count_and_none_is_not_listed()
    {
        var (h, _) = Made();

        h.AddAmmo(Smg, 0, "ammo_smg1");
        h.AddAmmo(Smg, 45, "ammo_smg1");

        var entry = Assert.Single(h.Slots);
        Assert.Equal((PickupHistory.Kind.Ammo, 45), (entry.Type, entry.Count));
    }

    [Fact]
    public void A_refusal_stays_half_as_long_and_is_renewed_not_repeated()
    {
        var (h, clock) = Made();

        h.AmmoDenied(Smg, "ammo_smg1");
        Assert.Equal(12.5f, h.Slots[0].DisplayTime);

        clock.Now = 11f;
        h.AmmoDenied(Smg, "ammo_smg1");

        var entry = Assert.Single(h.Slots);
        Assert.Equal((PickupHistory.Kind.AmmoDenied, 13.5f), (entry.Type, entry.DisplayTime));
    }

    [Fact]
    public void A_refusal_of_ammo_listed_as_picked_up_is_not_listed()
    {
        var (h, _) = Made();
        h.AddAmmo(Smg, 45, "ammo_smg1");

        h.AmmoDenied(Smg, "ammo_smg1");

        Assert.Equal([PickupHistory.Kind.Ammo], Kinds(h));
    }

    [Fact]
    public void A_pickup_of_refused_ammo_takes_the_refusal_s_row()
    {
        var (h, _) = Made();
        h.AddWeapon(Pistol, "pistol", exhaustible: false);
        h.AmmoDenied(Smg, "ammo_smg1");
        h.AddWeapon(Smg + 10, "smg1", exhaustible: false);

        h.AddAmmo(Smg, 30, "ammo_smg1");

        Assert.Equal([PickupHistory.Kind.Weapon, PickupHistory.Kind.Ammo, PickupHistory.Kind.Weapon], Kinds(h));
        Assert.Equal(30, h.Slots[1].Count);
    }

    [Fact]
    public void Rows_wrap_to_the_bottom_after_five_and_a_refusal_never_takes_a_showing_row()
    {
        var (h, _) = Made();
        for (int i = 0; i < PickupHistory.MaxSlots; i++)
            h.AddWeapon(i, "w" + i, exhaustible: false);

        h.AmmoDenied(Smg, "ammo_smg1");
        Assert.Equal(PickupHistory.Kind.Weapon, h.Slots[0].Type);

        h.AddItem("item_battery");
        Assert.Equal((PickupHistory.Kind.Item, "item_battery"), (h.Slots[0].Type, h.Slots[0].Icon));
        Assert.Equal(PickupHistory.MaxSlots, h.Slots.Count);
    }

    [Fact]
    public void An_expired_row_empties_and_with_all_empty_the_next_starts_at_the_bottom()
    {
        var (h, clock) = Made();
        h.AddWeapon(Pistol, "pistol", exhaustible: false);
        h.AddWeapon(Smg, "smg1", exhaustible: false);
        Assert.True(h.Showing);

        clock.Now = 15f;
        h.Think();

        Assert.All(h.Slots, e => Assert.Equal(PickupHistory.Kind.Empty, e.Type));
        Assert.False(h.Showing);

        h.AddItem("item_battery");
        Assert.Equal(PickupHistory.Kind.Item, h.Slots[0].Type);
    }

    [Fact]
    public void Lowering_the_draw_time_shortens_rows_already_up()
    {
        float drawTime = 5f;
        var clock = new Clock();
        var h = new PickupHistory(clock, () => drawTime);
        h.AddWeapon(Pistol, "pistol", exhaustible: false);

        drawTime = 1f;
        h.Think();

        Assert.Equal(11f, h.Slots[0].DisplayTime);
    }

    [Fact]
    public void A_row_fades_at_eighty_a_second_out_of_full()
    {
        var (h, clock) = Made();
        h.AddWeapon(Pistol, "pistol", exhaustible: false);

        Assert.Equal(1f, h.Opacity(0));
        clock.Now = 14f;
        Assert.Equal(80f / 255f, h.Opacity(0), 4);
        Assert.Equal(0f, h.Opacity(3));
    }
}
