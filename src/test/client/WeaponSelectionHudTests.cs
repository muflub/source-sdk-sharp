using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The weapon selection against stock's CBaseHudWeaponSelection and HL2's
/// CHudWeaponSelection (weapon_selection.cpp, hl2/hud_weaponselection.cpp).
/// Found beside stock (2026-10-27): here every bind switched at once, as
/// hud_fastswitch 1 does, with no selection to open, step or cancel.
/// </summary>
public class WeaponSelectionHudTests
{
    // Stock HL2MP's buckets for four of its weapons.
    private const int Crowbar = 0, Pistol = 1, Magnum = 2, Smg = 3;

    private sealed class Rig : IWeaponSelectionPlayer, IWeaponSelectionActions, IWeaponSlots, IGameClock
    {
        public bool Exists { get; set; } = true;
        public bool HudAllowed { get; set; } = true;
        public bool FrozenOrDead { get; set; }
        public uint Carried { get; set; } = 0b1111;
        public uint Empty { get; set; }
        public int Active { get; set; } = Pistol;
        public float Now { get; set; } = 10f;
        public int FastSwitch;

        public readonly List<int> Selected = new();
        public readonly List<string> Sounds = new();
        public int LastWeapons;

        public void Select(int weaponType) => Selected.Add(weaponType);
        public void LastWeapon() => LastWeapons++;
        public void Sound(string soundName) => Sounds.Add(soundName);

        public int Count => 4;
        public int Slot(int t) => t switch { Crowbar => 0, Pistol or Magnum => 1, _ => 2 };
        public int Position(int t) => t == Magnum ? 1 : 0;

        public readonly WeaponSelectionHud Hud;

        public Rig() => Hud = new WeaponSelectionHud(this, this, this, this, () => FastSwitch);
    }

    //-------------------------------------------------------------------------
    // hud_fastswitch 0: the buckets
    //-------------------------------------------------------------------------

    [Fact]
    public void A_slot_key_opens_the_bucket_on_its_first_weapon_with_the_move_sound()
    {
        var rig = new Rig();

        rig.Hud.SlotCommand(2);

        Assert.True(rig.Hud.Open && rig.Hud.Showing);
        Assert.Equal(Pistol, rig.Hud.Selected);
        Assert.Equal(["Player.WeaponSelectionMoveSlot"], rig.Sounds);
        Assert.Empty(rig.Selected);
    }

    [Fact]
    public void The_same_key_again_steps_along_the_bucket_and_wraps()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(2);

        rig.Hud.SlotCommand(2);
        Assert.Equal(Magnum, rig.Hud.Selected);

        rig.Hud.SlotCommand(2);
        Assert.Equal(Pistol, rig.Hud.Selected);
    }

    [Fact]
    public void An_empty_weapon_is_passed_over_in_its_bucket()
    {
        var rig = new Rig { Empty = 1u << Pistol };

        rig.Hud.SlotCommand(2);

        Assert.Equal(Magnum, rig.Hud.Selected);
    }

    [Fact]
    public void A_bucket_with_nothing_selectable_opens_nothing_but_still_clicks()
    {
        var rig = new Rig { Carried = 1u << Crowbar };

        rig.Hud.SlotCommand(3);

        Assert.False(rig.Hud.Open);
        Assert.Equal(["Player.WeaponSelectionMoveSlot"], rig.Sounds);
    }

    [Fact]
    public void Attack_draws_the_selected_weapon_and_closes_with_both_sounds()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(3);
        rig.Sounds.Clear();

        Assert.True(rig.Hud.AttackPressed());

        Assert.Equal([Smg], rig.Selected);
        Assert.False(rig.Hud.Open);
        Assert.Equal(["Player.WeaponSelected", "Player.WeaponSelectionClose"], rig.Sounds);
    }

    [Fact]
    public void Attack_with_nothing_open_is_not_swallowed()
        => Assert.False(new Rig().Hud.AttackPressed());

    [Fact]
    public void Next_opens_after_the_weapon_in_hand_and_steps_on()
    {
        var rig = new Rig();

        rig.Hud.NextWeapon();
        Assert.Equal(Magnum, rig.Hud.Selected);
        Assert.True(rig.Hud.Open);

        rig.Hud.NextWeapon();
        Assert.Equal(Smg, rig.Hud.Selected);

        // Past the last, round to the first.
        rig.Hud.NextWeapon();
        Assert.Equal(Crowbar, rig.Hud.Selected);
        Assert.Empty(rig.Selected);
    }

    [Fact]
    public void Prev_opens_before_the_weapon_in_hand_and_wraps_to_the_end()
    {
        var rig = new Rig();

        rig.Hud.PrevWeapon();
        Assert.Equal(Crowbar, rig.Hud.Selected);

        rig.Hud.PrevWeapon();
        Assert.Equal(Smg, rig.Hud.Selected);
    }

    [Fact]
    public void Cycling_passes_an_empty_weapon_over()
    {
        var rig = new Rig { Empty = 1u << Magnum };

        rig.Hud.NextWeapon();

        Assert.Equal(Smg, rig.Hud.Selected);
    }

    [Fact]
    public void The_cancel_key_closes_an_open_selection_and_is_used()
    {
        var rig = new Rig();
        Assert.False(rig.Hud.CancelKey(), "nothing open: the key is not the selection's");

        rig.Hud.SlotCommand(1);
        Assert.True(rig.Hud.CancelKey());
        Assert.False(rig.Hud.Open);
    }

    [Fact]
    public void Cancelselect_closes_with_its_sound_or_answers_false_for_escape()
    {
        var rig = new Rig();
        Assert.False(rig.Hud.Close());

        rig.Hud.SlotCommand(1);
        rig.Sounds.Clear();
        Assert.True(rig.Hud.Close());
        Assert.Equal(["Player.WeaponSelectionClose"], rig.Sounds);
    }

    [Fact]
    public void The_selection_holds_half_a_second_then_fades_and_closes_at_one_and_a_quarter()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(2);

        rig.Now += 0.05f;
        rig.Hud.Think();
        Assert.Equal(0.5f, rig.Hud.Opacity, 3);

        rig.Now = 10.6f;
        rig.Hud.Think();
        Assert.Equal(1f, rig.Hud.Opacity, 3);
        Assert.True(rig.Hud.Open);

        // The fade began at the Think that saw the timeout (10.6): half a second on, a tenth gone.
        rig.Now = 11.2f;
        rig.Hud.Think();
        Assert.Equal(0.9f, rig.Hud.Opacity, 3);

        rig.Now = 11.3f;
        rig.Hud.Think();
        Assert.False(rig.Hud.Open);
    }

    [Fact]
    public void A_key_while_it_fades_brings_it_back()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(2);
        rig.Now = 10.6f;
        rig.Hud.Think();
        rig.Now = 11.2f;
        rig.Hud.Think();

        rig.Hud.SlotCommand(2);
        rig.Hud.Think();

        // From the 0.9 it had faded to, back up over a tenth of a second.
        rig.Now += 0.05f;
        Assert.Equal(0.95f, rig.Hud.Opacity, 3);
        rig.Now += 0.05f;
        Assert.Equal(1f, rig.Hud.Opacity, 3);
    }

    [Fact]
    public void A_dead_or_frozen_player_s_selection_closes()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(2);
        rig.FrozenOrDead = true;

        rig.Hud.Think();

        Assert.False(rig.Hud.Open);
    }

    [Fact]
    public void No_bind_works_while_the_hud_is_hidden()
    {
        var rig = new Rig { HudAllowed = false };

        rig.Hud.SlotCommand(2);
        rig.Hud.NextWeapon();
        rig.Hud.LastWeapon();

        Assert.False(rig.Hud.Open);
        Assert.Equal(0, rig.LastWeapons);
    }

    [Fact]
    public void Lastinv_goes_back_at_once()
    {
        var rig = new Rig();

        rig.Hud.LastWeapon();

        Assert.Equal(1, rig.LastWeapons);
        Assert.False(rig.Hud.Open);
    }

    //-------------------------------------------------------------------------
    // hud_fastswitch 1
    //-------------------------------------------------------------------------

    [Fact]
    public void Fastswitch_takes_the_next_weapon_in_the_bucket_at_once_and_shows_nothing()
    {
        var rig = new Rig { FastSwitch = 1 };

        rig.Hud.SlotCommand(2);

        Assert.Equal([Magnum], rig.Selected);
        Assert.False(rig.Hud.Showing);
        Assert.Empty(rig.Sounds);
    }

    [Fact]
    public void Fastswitch_to_an_empty_bucket_is_denied()
    {
        var rig = new Rig { FastSwitch = 1, Carried = 0b0011 };

        rig.Hud.SlotCommand(3);

        Assert.Empty(rig.Selected);
        Assert.Equal(["Player.DenyWeaponSelection"], rig.Sounds);
    }

    [Fact]
    public void Fastswitch_to_the_only_weapon_in_the_bucket_already_in_hand_does_nothing()
    {
        var rig = new Rig { FastSwitch = 1, Active = Crowbar };

        rig.Hud.SlotCommand(1);

        Assert.Empty(rig.Selected);
        Assert.Empty(rig.Sounds);
    }

    [Fact]
    public void A_selection_open_when_fastswitch_comes_on_is_not_drawn()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(2);

        rig.FastSwitch = 1;

        Assert.True(rig.Hud.Open);
        Assert.False(rig.Hud.Showing);
    }

    [Fact]
    public void Fastswitch_next_switches_at_once()
    {
        var rig = new Rig { FastSwitch = 1 };

        rig.Hud.NextWeapon();

        Assert.Equal([Magnum], rig.Selected);
        Assert.False(rig.Hud.Open);
    }

    [Fact]
    public void An_empty_weapon_is_refused_with_the_deny_sound()
    {
        var rig = new Rig();
        rig.Hud.SlotCommand(2);
        rig.Empty = 1u << Pistol;
        rig.Sounds.Clear();

        rig.Hud.AttackPressed();

        Assert.Empty(rig.Selected);
        Assert.Equal(["Player.DenyWeaponSelection"], rig.Sounds);
        Assert.True(rig.Hud.Open);
    }
}
