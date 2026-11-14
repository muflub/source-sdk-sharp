using SourceSharp.Tests;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.OwnerViews;

/// <summary>The character sheet's numbers, as the server derives them, to the owner alone (plan_descent_logic UI3).</summary>
public sealed class OwnerSheetSenderTests
{
    private static int AttributeIndex(OwnerRig rig, string id) =>
        rig.Rules.Content.Attributes.Rows.ToList().FindIndex(a => a.Id == id);

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_attributes_are_the_sheets_base_the_stats_now_and_the_class_cap()
    {
        var rig = new OwnerRig();
        var numbers = rig.Sheet.Of(rig.Hero)!.Value;
        var stats = rig.Hero.Stats!;
        var attributes = rig.Rules.Content.Attributes.Rows;
        var soldier = rig.Rules.Heroes.Class("soldier");

        Assert.Equal(attributes.Select(a => (short)rig.Hero.Sheet!.Base(a.Id)), numbers.Base);
        Assert.Equal(attributes.Select(a => SheetNumbers.Short(stats.Now(a.Id) * 10)), numbers.NowTenths);
        Assert.Equal(attributes.Select(a => (short)soldier.Bounds(a.Id).Cap), numbers.Cap);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void One_more_point_of_grit_adds_the_soldiers_life_per_point()
    {
        var rig = new OwnerRig();

        var numbers = rig.Sheet.Of(rig.Hero)!.Value;

        // classes.json: a Soldier's point of Grit is 3 life, of Smarts 1.5 mana.
        Assert.Equal(30, numbers.LifePerPointTenths[AttributeIndex(rig, "grit")]);
        Assert.Equal(15, numbers.ManaPerPointTenths[AttributeIndex(rig, "smarts")]);
        Assert.Equal(0, numbers.LifePerPointTenths[AttributeIndex(rig, "aim")]);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_resistances_armour_and_crit_are_the_stats()
    {
        var rig = new OwnerRig();
        var stats = rig.Hero.Stats!;

        var numbers = rig.Sheet.Of(rig.Hero)!.Value;

        Assert.Equal(rig.Rules.Content.DamageTypes.Rows.Select(t => SheetNumbers.Short(stats.Resist(t.Id) * 100)), numbers.ResistPercent);
        Assert.Equal((byte)rig.Rules.Content.Hero.ResistCap, numbers.ResistCap);
        Assert.Equal(SheetNumbers.Short(stats.Armour), numbers.Armour);
        Assert.Equal(SheetNumbers.Short(stats.CritChance * 1000), numbers.CritTenths);
        Assert.True(numbers.CritTenths > 0, "a Soldier's aim gives some crit");   // the stimulus is not zero
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_damage_range_is_the_weapon_in_hands()
    {
        var rig = new OwnerRig();
        rig.Damage.Range = (55, 99);

        var numbers = rig.Sheet.Of(rig.Hero)!.Value;

        Assert.Equal((55, 99), ((int)numbers.DamageMin, (int)numbers.DamageMax));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_sheet_change_sends_the_new_numbers_to_the_owner_alone()
    {
        var rig = new OwnerRig();
        rig.Level(3);
        int from = rig.Sent.Count;

        rig.Hero.Apply(s => rig.Rules.Heroes.Spend(s, "aim", 2).Sheet);

        var sent = rig.Of<SheetNumbers>(from).Single();
        Assert.Equal(22, sent.Base[AttributeIndex(rig, "aim")]);
        Assert.All(rig.Sent, s => Assert.Equal(OwnerRig.Index, s.Player));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_same_numbers_are_not_sent_twice()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Hero.Recompute();

        Assert.Empty(rig.Of<SheetNumbers>(from));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_spawn_sends_the_numbers_again()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Sheet.OnSpawned(OwnerRig.Player, 0f);

        Assert.Single(rig.Of<SheetNumbers>(from));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_new_weapon_range_is_sent_at_the_next_look()
    {
        var rig = new OwnerRig();
        for (int i = 0; i < OwnerSheetSender.WeaponCheckFrames; i++)
            rig.Sheet.OnPostThink(0.015f);
        int from = rig.Sent.Count;
        rig.Damage.Range = (10, 20);

        for (int i = 0; i < OwnerSheetSender.WeaponCheckFrames; i++)
            rig.Sheet.OnPostThink(0.015f);

        Assert.Equal(10, rig.Of<SheetNumbers>(from).Single().DamageMin);
    }

    [Fact]
    public void The_fullest_sheet_fits_one_message()
    {
        var full = new SheetNumbers
        {
            Base = new short[SheetNumbers.MaxRows], NowTenths = new short[SheetNumbers.MaxRows], Cap = new short[SheetNumbers.MaxRows],
            LifePerPointTenths = new short[SheetNumbers.MaxRows], ManaPerPointTenths = new short[SheetNumbers.MaxRows],
            ResistPercent = new short[SheetNumbers.MaxRows],
        };

        Assert.InRange(full.Write(new byte[255]), 1, 255);
    }

    [Fact]
    public void A_melee_weapon_has_no_band_and_a_rocket_launcher_a_halved_near_bonus()
    {
        Assert.Equal((0d, 0d), TFHeroWeaponDamage.Band("melee"));
        Assert.Equal((0.25, 0.5), TFHeroWeaponDamage.Band("rocket_launcher"));
        Assert.Equal((0.5, 0.5), TFHeroWeaponDamage.Band("shotgun"));
    }
}
