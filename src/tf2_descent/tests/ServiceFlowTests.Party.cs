using System.Collections.Immutable;

using SourceSharp.Abi;

using TF2.Descent.Server;
using TF2.Descent.Server.Party;
using TF2.Descent.Tests.Party;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The way down and the party (D8, 7d; WO9): in a party the leader asks for it and the party
/// travels; a member who asks is told the leader picks and nothing goes to the host, unless his
/// party is on a level and he would join it; up is each hero's own.
/// </summary>
public sealed partial class ServiceFlowTests
{
    /// <summary>No party rule: what the travel facts written before parties ask.</summary>
    private sealed class AnyoneTravels : IPartyTravel
    {
        public string? Refusal(int slot, TravelDirection direction) => null;
    }

    /// <summary>A party seen from slot 3: the given member ids, the leader first, with the live instance given.</summary>
    private sealed class OneHostParty(string self, string live, params string[] members) : IParties
    {
        private PartyInfo Party => new("h1", members[0], [.. members], ImmutableArray<string>.Empty, live);
        public int MaxSize => 4;
        public IReadOnlyList<PartyInfo> All => [Party];
        public string? MemberId(string hero) => self;
        public string? HeroOf(string member) => null;
        public PartyInfo? Of(string hero) => Party;
        public IReadOnlyList<PartyInfo> InvitesOf(string hero) => [];
        public HostMemberCard? Card(string member) => null;
        public Task<PartyAnswer> Create(string hero) => throw new NotSupportedException();
        public Task<PartyAnswer> Invite(string from, string target) => throw new NotSupportedException();
        public Task<PartyAnswer> Accept(string hero, string? partyId = null) => throw new NotSupportedException();
        public Task<PartyAnswer> Decline(string hero, string? partyId = null) => throw new NotSupportedException();
        public Task<PartyAnswer> Leave(string hero) => throw new NotSupportedException();
        public Task<PartyAnswer> Kick(string leader, string target) => throw new NotSupportedException();
        public Task<PartyAnswer> Disband(string leader) => throw new NotSupportedException();
        public Task<PartyAnswer> Make(IReadOnlyList<string> heroes) => throw new NotSupportedException();
        public void Gone(string hero) { }
    }

    private sealed class Slot3 : IPartySlots
    {
        public IReadOnlyList<int> Heroes() => [3];
        public string? HeroOf(int slot) => slot == 3 ? "76561197960287930" : null;
        public int SlotOf(string hero) => hero == "76561197960287930" ? 3 : 0;
    }

    private static PartyTravel PartyOf(string live, params string[] members) => new(new OneHostParty("c-1", live, members), new Slot3());

    [Fact]
    public void A_party_member_asking_for_the_way_down_is_told_the_leader_picks_and_the_host_is_not_asked()
    {
        var (w, travel, net) = Travel(reached: 3, party: PartyOf("", "c-leader", "c-1"));

        travel.Request(3, TravelDirection.Down, 1);

        Assert.False(AskedTheHost(w));
        Assert.Equal([(PartyTravel.LeaderPicksToken, "")], NoticesSent(net));
    }

    [Fact]
    public void The_leader_asks_for_the_way_down_for_the_party()
    {
        var (w, travel, _) = Travel(reached: 3, party: PartyOf("", "c-1", "c-2"));

        travel.Request(3, TravelDirection.Down, 2);

        Assert.Contains("travel 76561197960287930 Down hub 2", w.Link.Calls);
    }

    [Fact]
    public void A_member_whose_party_is_on_a_level_may_ask_to_join_its_run()
    {
        var (w, travel, _) = Travel(reached: 3, party: PartyOf("instance-7", "c-leader", "c-1"));

        travel.Request(3, TravelDirection.Down, 1);

        Assert.True(AskedTheHost(w));
    }

    [Fact]
    public void Up_through_the_portal_is_a_members_own()
    {
        Assert.Null(PartyOf("", "c-leader", "c-1").Refusal(3, TravelDirection.Up));
        Assert.Null(PartyOf("", "c-leader", "c-1").Refusal(3, TravelDirection.Corpse));
    }

    [Fact]
    public void A_hero_in_no_party_takes_the_way_down_himself()
    {
        var parties = new InMemoryParties(new Rules.TF2DescentRules(), new PartyChanges().Hooks);

        Assert.Null(new PartyTravel(parties, new Slot3()).Refusal(3, TravelDirection.Down));
    }
}
