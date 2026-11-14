using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;

using TF2.Descent.Client.Ui;

namespace TF2.Descent.Client.Party;

/// <summary>
/// The party window's roster (<see cref="IDescentPartyRoster"/>, WO9) from the party the server
/// told this client (<see cref="IPartyState"/>): the leader's place in the frames' list (which
/// follows the view's order), whether the local hero leads, the inviter's name, and the heroes
/// this client holds who may be invited: when the local hero is in no party, or leads one with
/// room, every other hero who is not a member, by slot. Whether such a hero is in another party
/// is the server's to say (it refuses the invitation).
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentPartyRoster)])]
public sealed class PartyRosterSource(IPartyState party, IHeroStates heroes, IEngineHud hud, IPlayerNames names) : IDescentPartyRoster
{
    /// <summary>The rules' party size (robot_rules' party.maxMembers): a full party invites nobody.</summary>
    public const int MaxMembers = 4;

    private readonly List<int> _candidates = [];

    public int LeaderIndex => party.View.InParty ? party.View.Leader : -1;

    public bool LocalLeads => party.LocalLeads;

    public string InviterName => party.View.InviterName;

    public bool CanInvite => !party.View.InParty || (party.LocalLeads && party.View.Members.Count < MaxMembers);

    /// <summary>The slots the list shows, read once each time the count is asked.</summary>
    public IReadOnlyList<int> CandidateSlots => _candidates;

    public int CandidateCount
    {
        get
        {
            _candidates.Clear();
            if (!CanInvite)
                return 0;
            int local = hud.LocalPlayerIndex;
            foreach (var hero in heroes.All())
                if (hero.PlayerIndex != local && !party.IsMember(hero.PlayerIndex))
                    _candidates.Add(hero.PlayerIndex);
            return _candidates.Count;
        }
    }

    public string CandidateName(int i) =>
        i >= 0 && i < _candidates.Count && names.TryGet(_candidates[i], out var info) && info.Name.Length > 0
            ? info.Name
            : i >= 0 && i < _candidates.Count ? "player " + _candidates[i].ToString(CultureInfo.InvariantCulture) : "";
}

/// <summary>
/// The party window's actions (<see cref="IDescentPartyActions"/>, WO9): each the hero's own
/// client verb (<c>tf2_descent_party_*</c>), which the server decides and answers with a notice.
/// A candidate is invited by his slot; a member is kicked by his slot, or by name when he is not
/// on this server.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentPartyActions)])]
public sealed class PartyWindowActions(PartyRosterSource roster, IPartyState party, IDescentCommandLine line) : IDescentPartyActions
{
    public void Invite(int candidate)
    {
        if (candidate >= 0 && candidate < roster.CandidateSlots.Count)
            line.Send("tf2_descent_party_invite " + roster.CandidateSlots[candidate].ToString(CultureInfo.InvariantCulture));
    }

    public void Accept() => line.Send("tf2_descent_party_accept");

    public void Decline() => line.Send("tf2_descent_party_decline");

    public void Leave() => line.Send("tf2_descent_party_leave");

    public void Kick(int member)
    {
        var members = party.View.Members;
        if (member < 0 || member >= members.Count)
            return;
        var m = members[member];
        line.Send(m.Slot > 0
            ? "tf2_descent_party_kick " + m.Slot.ToString(CultureInfo.InvariantCulture)
            : "tf2_descent_party_kick " + m.Name);
    }
}
