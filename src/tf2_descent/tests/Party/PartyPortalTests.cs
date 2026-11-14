using TF2.Descent.Rules;
using TF2.Descent.Server.Party;
using TF2.Descent.Server.Portal;
using TF2.Descent.Tests.Portal;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// A hero's town portal and the party (WO9; death plan Q5): a member of the owner's party may use
/// it, a stranger may not, and one who leaves the party loses the use while the portal stays its
/// owner's. Over the portals' rig with the party seam the server registers.
/// </summary>
public sealed class PartyPortalTests
{
    private readonly InMemoryParties _parties = new(new TF2DescentRules(), new PartyChanges().Hooks);

    private PortalRig Rig() => new(party: new PartyPortalParty(_parties));

    private async Task Pair()
    {
        Assert.True((await _parties.Invite(PortalRig.Hero, PortalRig.OtherHero)).Ok);
        Assert.True((await _parties.Accept(PortalRig.OtherHero)).Ok);
    }

    [Fact]
    public void The_owner_uses_his_own_and_a_stranger_may_not()
    {
        var may = new PartyPortalParty(_parties);

        Assert.True(may.MayUse("a", "a"));
        Assert.False(may.MayUse("a", "b"));
    }

    [Fact]
    public async Task A_party_member_uses_the_owners_pad()
    {
        await Pair();
        var rig = Rig();
        var portal = rig.Opened();
        rig.World.Heroes[PortalRig.Other] = new PortalRig.FakeHero(PortalRig.OtherHero);

        rig.StandOn(PortalRig.Other, portal.Position, TownPortals.ChargeSeconds);

        Assert.Equal([$"up {PortalRig.Other} {PortalRig.OtherHero} portal {portal.Id}"], rig.Route.Calls);
    }

    [Fact]
    public void A_stranger_on_the_owners_pad_goes_nowhere()
    {
        var rig = Rig();
        var portal = rig.Opened();
        rig.World.Heroes[PortalRig.Other] = new PortalRig.FakeHero(PortalRig.OtherHero);

        rig.StandOn(PortalRig.Other, portal.Position, TownPortals.ChargeSeconds);

        Assert.Empty(rig.Route.Calls);
        Assert.Equal(PortalState.Open, portal.State);
    }

    [Fact]
    public async Task A_member_who_leaves_the_party_loses_the_use_and_the_portal_stays_the_owners()
    {
        await Pair();
        var rig = Rig();
        var portal = rig.Opened();
        rig.World.Heroes[PortalRig.Other] = new PortalRig.FakeHero(PortalRig.OtherHero);
        Assert.True((await _parties.Leave(PortalRig.OtherHero)).Ok);

        rig.StandOn(PortalRig.Other, portal.Position, TownPortals.ChargeSeconds);

        Assert.Empty(rig.Route.Calls);
        Assert.Equal((PortalState.Open, PortalRig.Hero), (portal.State, portal.Owner));
    }

    [Fact]
    public async Task The_owners_use_is_his_own_whatever_his_party()
    {
        await Pair();
        var rig = Rig();
        var portal = rig.Opened();

        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);

        Assert.Equal([$"up {PortalRig.Slot} {PortalRig.Hero} portal {portal.Id}"], rig.Route.Calls);
    }
}
