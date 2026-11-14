using TF2.Descent.Rules.Spells;
using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// Scrolls and tomes used from the backpack (HE8): a tome teaches then raises its spell on the
/// sheet and is used up, the sixth is refused and kept; the Dossier identifies its target and
/// one is used; the town portal's stand-in keeps the scroll.
/// </summary>
public sealed class ConsumableFacts
{
    [Fact]
    public void The_first_tome_teaches_its_spell_at_rank_one_and_is_used_up()
    {
        var rig = new BeltRig();
        int slot = rig.Give(BeltRig.Carried("tome_fireball"));

        var r = rig.Use(slot);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(1, SpellRules.Rank(rig.Hero.Sheet!, "fireball"));
        Assert.False(rig.Held.Inventory.Backpack.ContainsKey(slot));
    }

    [Fact]
    public void The_sheet_command_shows_the_spell_a_tome_taught()
    {
        var rig = new BeltRig();
        Assert.Contains("| spells none |", HeroCommandGate.Describe(BeltRig.Index, rig.Hero));

        rig.Use(rig.Give(BeltRig.Carried("tome_fireball")));

        Assert.Contains("| spells fireball 1 |", HeroCommandGate.Describe(BeltRig.Index, rig.Hero));
    }

    [Fact]
    public void A_second_tome_raises_the_spell_a_rank()
    {
        var rig = new BeltRig();
        rig.Use(rig.Give(BeltRig.Carried("tome_lightning_ball")));

        rig.Use(rig.Give(BeltRig.Carried("tome_lightning_ball")));

        Assert.Equal(2, SpellRules.Rank(rig.Hero.Sheet!, "lightning_ball"));
        Assert.True(rig.Gear.Rules.Heroes.Audit(rig.Hero.Sheet!).Ok);
    }

    [Fact]
    public void A_sixth_tome_is_refused_and_kept()
    {
        var rig = new BeltRig();
        for (int i = 0; i < 5; i++)
            Assert.True(rig.Use(rig.Give(BeltRig.Carried("tome_fireball"))).Ok);
        int sixth = rig.Give(BeltRig.Carried("tome_fireball"));

        var r = rig.Use(sixth);

        Assert.Equal("at_max_rank", r.Refusal);
        Assert.Equal(5, SpellRules.Rank(rig.Hero.Sheet!, "fireball"));
        Assert.Equal("tome_fireball", rig.Held.Inventory.Backpack[sixth].Item.BaseId);
    }

    [Fact]
    public void A_dossier_identifies_its_target_and_one_is_used()
    {
        var rig = new BeltRig();
        int target = rig.Give(TF2.Descent.Tests.Items.ItemFixtures.Item("shovel", 1, 5) with { Identified = false });
        int dossier = rig.Give(BeltRig.Carried("dossier", 2));

        var r = rig.Use(dossier, target);

        Assert.True(r.Ok, r.Refusal);
        var items = rig.Held.Inventory.Backpack.Values.ToList();
        Assert.Contains(items, h => h.Item.BaseId == "shovel" && h.Item.Identified);
        Assert.Equal(1, Assert.Single(items, h => h.Item.BaseId == "dossier").Item.Count);
    }

    [Fact]
    public void A_dossier_with_no_target_is_refused_and_kept()
    {
        var rig = new BeltRig();
        int dossier = rig.Give(BeltRig.Carried("dossier", 2));

        Assert.Equal(UseRefusals.NeedsTarget, rig.Use(dossier).Refusal);
        Assert.Equal(2, rig.Held.Inventory.Backpack[dossier].Item.Count);
    }

    [Fact]
    public void A_portal_that_does_not_open_refuses_and_the_blueprint_is_kept()
    {
        var rig = new BeltRig();
        int scroll = rig.Give(BeltRig.Carried("town_portal", 3));

        var r = rig.Use(scroll);

        Assert.Equal(UseRefusals.Portal, r.Refusal);
        Assert.Equal(BeltRig.Portal.Why, r.Did);
        Assert.Equal(1, rig.Portals.Asked);
        Assert.Equal(3, rig.Held.Inventory.Backpack[scroll].Item.Count);
    }

    [Fact]
    public void A_portal_that_opens_uses_one_blueprint()
    {
        var rig = new BeltRig();
        rig.Portals.Opens = true;
        int scroll = rig.Give(BeltRig.Carried("town_portal", 3));

        Assert.True(rig.Use(scroll).Ok);
        Assert.Equal(2, rig.Held.Inventory.Backpack[scroll].Item.Count);
    }

    [Fact]
    public void A_potion_in_the_backpack_is_not_used_from_there()
    {
        var rig = new BeltRig();
        int potion = rig.Give(BeltRig.Carried("health_small"));

        Assert.Equal(UseRefusals.NotUsable, rig.Use(potion).Refusal);
        Assert.True(rig.Held.Inventory.Backpack.ContainsKey(potion));
    }

    [Fact]
    public void A_dead_hero_reads_nothing()
    {
        var rig = new BeltRig();
        int slot = rig.Give(BeltRig.Carried("tome_fireball"));
        rig.Clock.Alive = false;

        Assert.Equal(BeltRefusals.Dead, rig.Use(slot).Refusal);
        Assert.Equal(0, SpellRules.Rank(rig.Hero.Sheet!, "fireball"));
    }
}
