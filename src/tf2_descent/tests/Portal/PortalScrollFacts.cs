using SourceSharp.Tests;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Portal;
using TF2.Descent.Tests.Hero;
using Xunit;

namespace TF2.Descent.Tests.Portal;

/// <summary>
/// The Teleporter Blueprint read from the backpack (HE8's <see cref="HeroConsumables"/>) over the
/// real <see cref="TownPortals"/>: every refusal keeps the scroll, an opened portal uses one.
/// </summary>
public sealed class PortalScrollFacts
{
    private sealed class ScrollRig
    {
        public readonly BeltRig Belt = new();
        public readonly PortalRig Portal = new();
        public readonly HeroConsumables Consumables;

        public ScrollRig()
        {
            Portal.World.Heroes.Clear();
            Portal.World.Heroes[BeltRig.Index] = new PortalRig.FakeHero(BeltRig.Key);
            Consumables = new HeroConsumables(Belt.Gear.Rules, Belt.Gear.Hero.Heroes, Belt.Gear.Keys, Belt.Gear.Ledger, Portal.Portals,
                                              Belt.Clock, new ClientMessages(Belt.Messages), Belt.Gear.Log);
        }

        public int Give(int count) => Belt.Give(BeltRig.Carried("town_portal", count));

        public Task<ItemUse> Use(int slot) => Consumables.Use(BeltRig.Index, slot);

        public int Count(int slot) => Belt.Held.Inventory.Backpack.TryGetValue(slot, out var held) ? held.Item.Count : 0;
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_blueprint_read_on_a_level_opens_a_portal_and_uses_one()
    {
        var rig = new ScrollRig();
        rig.Portal.Load(PortalRig.Level);
        int scroll = rig.Give(3);

        var used = await rig.Use(scroll);

        Assert.True(used.Ok, used.Refusal);
        Assert.Equal(2, rig.Count(scroll));
        Assert.Equal(PortalState.Open, Assert.Single(rig.Portal.Portals.All).State);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_blueprint_read_in_town_is_refused_and_kept()
    {
        var rig = new ScrollRig();
        rig.Portal.Load(PortalRig.Town);
        int scroll = rig.Give(3);

        var used = await rig.Use(scroll);

        Assert.Equal((UseRefusals.Portal, TownPortals.InTown), (used.Refusal, used.Did));
        Assert.Equal(3, rig.Count(scroll));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_blueprint_read_with_no_room_in_front_is_refused_and_kept()
    {
        var rig = new ScrollRig();
        rig.Portal.Load(PortalRig.Level);
        rig.Portal.World.Placing = _ => PortalPlace.Refused(PortalPlacement.NoFloor);
        int scroll = rig.Give(3);

        var used = await rig.Use(scroll);

        Assert.Equal((UseRefusals.Portal, PortalPlacement.NoFloor), (used.Refusal, used.Did));
        Assert.Equal(3, rig.Count(scroll));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_blueprint_read_by_a_dead_hero_is_refused_and_kept()
    {
        var rig = new ScrollRig();
        rig.Portal.Load(PortalRig.Level);
        rig.Belt.Clock.Alive = false;
        int scroll = rig.Give(3);

        var used = await rig.Use(scroll);

        Assert.False(used.Ok);
        Assert.Equal(3, rig.Count(scroll));
        Assert.Empty(rig.Portal.Portals.All);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_second_blueprint_moves_the_portal_and_uses_another()
    {
        var rig = new ScrollRig();
        rig.Portal.Load(PortalRig.Level);
        int scroll = rig.Give(3);

        await rig.Use(scroll);
        var second = await rig.Use(scroll);

        Assert.True(second.Ok, second.Refusal);
        Assert.Equal(1, rig.Count(scroll));
        Assert.Equal([PortalState.Gone, PortalState.Open], rig.Portal.Portals.All.Select(r => r.State));
    }
}
