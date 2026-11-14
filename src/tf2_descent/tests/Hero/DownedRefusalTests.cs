extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Skills;
using TF2.Descent.Tests.Skills;
using TF2.Shared;

using Xunit;

using ClientHeroState = client::TF2.Descent.Client.HeroState;
using ClientHeroStates = client::TF2.Descent.Client.IHeroStates;
using ClientWeapons = client::TF2.Descent.Client.DownedWeaponsClient;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// What a downed hero may not do (WO6): cast, drink, read a scroll or tome, fire a TF weapon
/// (the server's veto and the client's predicted twin). His movement is TF's controls stun,
/// the downed state's freeze (<see cref="DownedTests.A_downed_hero_is_frozen_for_his_window"/>).
/// </summary>
public sealed class DownedRefusalTests
{
    private static void Down(HeroComponent hero) => hero.ShowDowned(true, 1000, 1200);

    [Fact]
    public void A_downed_hero_casts_nothing_and_spends_no_mana()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);
        rig.Learn("patriot_missile");
        rig.Slot(1, "patriot_missile");
        var hero = rig.Hero();
        double mana = hero.Mana;
        Down(hero);

        var use = rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal(SkillUseKind.Downed, use.Kind);
        Assert.Equal(mana, hero.Mana, 6);
        Assert.Empty(rig.World.Spawned);
    }

    [Fact]
    public void A_downed_hero_drinks_nothing_and_keeps_the_potion()
    {
        var rig = new BeltRig();
        Down(rig.Hero);

        Assert.Equal(BeltRefusals.Downed, rig.Drink(1).Refusal);
        Assert.NotNull(rig.BeltSlot(0));
    }

    [Fact]
    public void A_downed_hero_reads_no_tome_and_keeps_it()
    {
        var rig = new BeltRig();
        int slot = rig.Give(BeltRig.Carried("tome_fireball"));
        Down(rig.Hero);

        Assert.Equal(BeltRefusals.Downed, rig.Use(slot).Refusal);
        Assert.True(rig.Held.Inventory.Backpack.ContainsKey(slot));
    }

    private sealed class Mode(bool active) : TF2.Descent.Server.Mode.IDescentModeState
    {
        public bool Active => active;
    }

    private static TFWeaponHookContext Weapon(int owner) => new(default, null!, owner);

    [Fact]
    public void A_downed_heros_weapon_is_vetoed_and_a_standing_ones_is_not()
    {
        var rig = new DownedRig();
        var veto = new DownedWeapons(new Mode(true), rig.Heroes.Heroes);
        rig.Hit(DownedRig.A, 500);

        Assert.False(veto.BeforeFire(Weapon(DownedRig.A), default));
        Assert.True(veto.BeforeFire(Weapon(DownedRig.B), default));
    }

    private sealed class States(params ClientHeroState[] all) : ClientHeroStates
    {
        public ClientHeroState? Of(int playerIndex) => all.FirstOrDefault(h => h.PlayerIndex == playerIndex);
        public ClientHeroState? Local() => all.FirstOrDefault();
        public IReadOnlyList<ClientHeroState> All() => all;
    }

    private static ClientHeroState Hero(int index, bool downed) =>
        new(index, 3, 1, 0, 0, 100, 200, 50, 50, 0, 0, downed, downed ? 1200 : 0, false);

    private static client::TF2.Shared.TFWeaponHookContext ClientWeapon(int owner) => new(default, null!, owner);

    [Fact]
    public void The_client_predicts_the_veto_from_the_heros_networked_state()
    {
        var veto = new ClientWeapons(new States(Hero(3, downed: true), Hero(4, downed: false)));

        Assert.False(veto.BeforeFire(ClientWeapon(3), default));
        Assert.True(veto.BeforeFire(ClientWeapon(4), default));
    }
}
