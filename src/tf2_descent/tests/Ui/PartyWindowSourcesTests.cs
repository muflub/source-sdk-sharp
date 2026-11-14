extern alias client;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using HeroState = client::TF2.Descent.Client.HeroState;
using IHeroStates = client::TF2.Descent.Client.IHeroStates;
using IPlayerNames = client::SourceSharp.Client.Vgui.IPlayerNames;
using PartyMemberView = client::TF2.Descent.Shared.PartyMemberView;
using PartyRosterSource = client::TF2.Descent.Client.Party.PartyRosterSource;
using PartyState = client::TF2.Descent.Client.Party.PartyState;
using PartyViewState = client::TF2.Descent.Shared.PartyViewState;
using PartyWindowActions = client::TF2.Descent.Client.Party.PartyWindowActions;
using Src = client::TF2.Descent.Client.Ui;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The party window's live sources (WO9): the leader, the invitation, who may be invited (by
/// whom), and each button as the hero's own client verb.
/// </summary>
public sealed class PartyWindowSourcesTests
{
    private const int Local = 1;

    private sealed class Heroes(params int[] slots) : IHeroStates
    {
        private HeroState Of_(int i) => new(i, 3, 5, 0, 0, 0, 100, 0, 0, 0, 0, false, 0, false);
        public HeroState? Of(int playerIndex) => slots.Contains(playerIndex) ? Of_(playerIndex) : null;
        public HeroState? Local() => Of(PartyWindowSourcesTests.Local);
        public IReadOnlyList<HeroState> All() => [.. slots.Order().Select(Of_)];
    }

    private sealed class Names : IPlayerNames
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            info = new ClientPlayerInfo { Name = "Name" + slot };
            return true;
        }
    }

    private sealed class Lines : Src.IDescentCommandLine
    {
        public List<string> Sent { get; } = [];
        public void Send(string line) => Sent.Add(line);
    }

    private static ClientEngine Hud() => new ClientEngine { Strict = false }.Returns("Hud.LocalPlayerIndex", Local);

    /// <summary>The party as the server told it: the members' slots (0 off this server), the leader's place, an inviter.</summary>
    private static PartyState Told(int leader, int[] slots, string inviter = "")
    {
        var state = new PartyState(Hud());
        state.OnPartyView(new PartyViewState([.. slots.Select(s => new PartyMemberView(s, s == 0 ? "Away" : "Name" + s, 3, 5))],
                                             slots.Length == 0 ? -1 : leader, inviter.Length > 0 ? 7 : 0, inviter));
        return state;
    }

    private static (PartyRosterSource Roster, PartyWindowActions Actions, Lines Sent) Window(PartyState party, params int[] heroes)
    {
        var roster = new PartyRosterSource(party, new Heroes(heroes), Hud(), new Names());
        var sent = new Lines();
        return (roster, new PartyWindowActions(roster, party, sent), sent);
    }

    [Fact]
    public void Solo_every_other_hero_here_may_be_invited_and_invite_sends_his_slot()
    {
        var (roster, actions, sent) = Window(Told(-1, []), Local, 2, 5);

        Assert.True(roster.CanInvite);
        Assert.Equal(2, roster.CandidateCount);
        Assert.Equal(["Name2", "Name5"], new[] { roster.CandidateName(0), roster.CandidateName(1) });
        actions.Invite(1);
        Assert.Equal(["tf2_descent_party_invite 5"], sent.Sent);
    }

    [Fact]
    public void The_leader_may_invite_heroes_not_in_his_party_and_a_member_may_not()
    {
        var (lead, _, _) = Window(Told(0, [Local, 2]), Local, 2, 3);
        var (member, _, _) = Window(Told(0, [2, Local]), Local, 2, 3);

        Assert.Equal((true, true, 0), (lead.CanInvite, lead.LocalLeads, lead.LeaderIndex));
        Assert.Equal(1, lead.CandidateCount);
        Assert.Equal("Name3", lead.CandidateName(0));
        Assert.Equal((false, false, 0), (member.CanInvite, member.LocalLeads, member.CandidateCount));
    }

    [Fact]
    public void A_full_party_invites_nobody()
    {
        var (roster, _, _) = Window(Told(0, [Local, 2, 3, 4]), Local, 2, 3, 4, 5);

        Assert.False(roster.CanInvite);
        Assert.Equal(0, roster.CandidateCount);
    }

    [Fact]
    public void The_invitation_names_its_sender_and_the_answers_are_the_verbs()
    {
        var (roster, actions, sent) = Window(Told(-1, [], inviter: "Saul"), Local);

        Assert.Equal("Saul", roster.InviterName);
        actions.Accept();
        actions.Decline();
        actions.Leave();
        Assert.Equal(["tf2_descent_party_accept", "tf2_descent_party_decline", "tf2_descent_party_leave"], sent.Sent);
    }

    [Fact]
    public void Kick_names_a_member_by_his_slot_or_by_name_when_he_is_not_here()
    {
        var (_, actions, sent) = Window(Told(0, [Local, 4, 0]), Local, 4);

        actions.Kick(1);
        actions.Kick(2);

        Assert.Equal(["tf2_descent_party_kick 4", "tf2_descent_party_kick Away"], sent.Sent);
    }
}
