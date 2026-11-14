using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Party;

/// <summary>
/// Each hero's client is told his own party (WO9): <c>party_view</c> to every hero a change
/// touches (the party's members here, the member it names, the heroes it invites) and to a hero
/// at each spawn, so a client that joined, or a level that changed, holds it again. A hero is
/// told only his own party and his own invitation.
/// </summary>
[SideService(Side.Server)]
public sealed class PartySync(IParties parties, IPartyPlayers players, IUserMessageSender sender)
    : IPartyHook, IPlayerSpawnHook, IOrderedHook
{
    /// <summary>In TF2 Descent's party block, after the parties' own frame.</summary>
    public const int Order = Parties.Order + 1;

    public int HookOrder => Order;

    /// <summary>Views sent since the server started, for the facts and the dev print.</summary>
    public int Sent { get; private set; }

    public void OnPartyChanged(in PartyChange change)
    {
        var told = new SortedSet<int>();
        foreach (string member in change.Party.Members.Append(change.Member).Concat(change.Party.Invited))
            if (parties.HeroOf(member) is { } hero && players.SlotOf(hero) is > 0 and var slot)
                told.Add(slot);
        foreach (int slot in told)
            Send(slot);
    }

    public void OnSpawning(EntityHandle player, float now) { }

    /// <summary>
    /// A spawn sends the hero his party, and every member here the party again: a member who
    /// arrives after the others (each client comes back on its own after a level change) is
    /// on this server from now, where the others were told he was not.
    /// </summary>
    public void OnSpawned(EntityHandle player, float now)
    {
        int slot = (int)(player.EntryIndex & 0xFFFF);
        if (players.HeroOf(slot) is not { } hero)
            return;
        Send(slot);
        if (parties.Of(hero) is not { } party)
            return;
        foreach (string member in party.Members)
            if (parties.HeroOf(member) is { } mate && mate != hero && players.SlotOf(mate) is > 0 and var other)
                Send(other);
    }

    /// <summary>Sends the hero in <paramref name="slot"/> his party as it is now.</summary>
    public void Send(int slot)
    {
        if (View(slot) is not { } view)
            return;
        sender.Send(view, slot);
        Sent++;
    }

    /// <summary>The <c>party_view</c> of the hero in <paramref name="slot"/>; null for no hero there.</summary>
    public PartyView? View(int slot)
    {
        if (players.HeroOf(slot) is not { } hero)
            return null;

        var slots = new List<byte>();
        var classes = new List<byte>();
        var levels = new List<byte>();
        var names = new List<string>();
        int leader = 255;
        if (parties.Of(hero) is { } party)
        {
            foreach (string member in party.Members.Take(PartyView.MaxMembers))
            {
                if (member == party.Leader)
                    leader = slots.Count;
                var (s, name, classId, level) = Card(member);
                slots.Add((byte)s);
                classes.Add((byte)classId);
                levels.Add((byte)Math.Clamp(level, 0, 255));
                names.Add(PartyView.Cut(name));
            }
        }

        int inviterSlot = 0;
        string inviterName = "";
        if (parties.InvitesOf(hero).LastOrDefault() is { } invitation)
        {
            var (s, name, _, _) = Card(invitation.Leader);
            inviterSlot = s;
            inviterName = PartyView.Cut(name);
        }

        return new PartyView
        {
            Leader = (byte)leader,
            Slot = [.. slots],
            ClassId = [.. classes],
            Level = [.. levels],
            Names = string.Join('\n', names),
            InviterSlot = (byte)inviterSlot,
            InviterName = inviterName,
        };
    }

    /// <summary>A member as a frame shows him: a hero here from his slot, one elsewhere from the host's card (his id when it has not answered).</summary>
    private (int Slot, string Name, int ClassId, int Level) Card(string member)
    {
        if (parties.HeroOf(member) is { } hero && players.SlotOf(hero) is > 0 and var slot)
            return (slot, players.NameOf(slot), players.ClassOf(slot), players.LevelOf(slot));
        return parties.Card(member) is { } card
            ? (0, card.Name, ClassNumber(card.ClassName), card.Level)
            : (0, member, 0, 0);
    }

    /// <summary>TF2's class number of a class name as the host spells it (3 for "soldier"); 0 for one it does not know.</summary>
    public static int ClassNumber(string className) => className.ToLowerInvariant() switch
    {
        "scout" => 1,
        "sniper" => 2,
        "soldier" => 3,
        "demoman" => 4,
        "medic" => 5,
        "heavy" or "heavyweapons" => 6,
        "pyro" => 7,
        "spy" => 8,
        "engineer" => 9,
        _ => 0,
    };
}

/// <summary>
/// The party's notices (WO9), each to the heroes it concerns on this server: a member joined,
/// left or was put out (and to him, that he is out), a new leader, an invitation (to the invited
/// hero, how to answer it, and to the leader, that it went), one declined, the party ended.
/// </summary>
[SideService(Side.Server)]
public sealed class PartyNotices(IParties parties, IPartyPlayers players, ClientMessages messages) : IPartyHook, IOrderedHook
{
    public const int Order = Parties.Order + 2;

    public const string JoinedToken = "#Descent_Party_Joined";
    public const string LeftToken = "#Descent_Party_Left";
    public const string YouLeftToken = "#Descent_Party_YouLeft";
    public const string KickedToken = "#Descent_Party_Kicked";
    public const string YouWereKickedToken = "#Descent_Party_YouWereKicked";
    public const string NewLeaderToken = "#Descent_Party_NewLeader";
    public const string InvitedToken = "#Descent_Party_Invited";
    public const string InviteSentToken = "#Descent_Party_InviteSent";
    public const string DeclinedToken = "#Descent_Party_Declined";
    public const string DisbandedToken = "#Descent_Party_Disbanded";

    /// <summary>Every token this class sends, for the language file's fact.</summary>
    public static IReadOnlyList<string> Tokens() =>
        [JoinedToken, LeftToken, YouLeftToken, KickedToken, YouWereKickedToken, NewLeaderToken, InvitedToken, InviteSentToken, DeclinedToken, DisbandedToken];

    public int HookOrder => Order;

    public void OnPartyChanged(in PartyChange change)
    {
        string name = NameOf(change.Member);
        var party = change.Party;
        switch (change.Kind)
        {
            case PartyChangeKind.Joined:
                ToMembers(party, JoinedToken, name);
                break;
            case PartyChangeKind.Left:
                ToMembers(party, LeftToken, name);
                To(change.Member, YouLeftToken);
                break;
            case PartyChangeKind.Kicked:
                ToMembers(party, KickedToken, name);
                To(change.Member, YouWereKickedToken);
                break;
            case PartyChangeKind.LeaderChanged:
                ToMembers(party, NewLeaderToken, name);
                break;
            case PartyChangeKind.Invited:
                To(change.Member, InvitedToken, NameOf(party.Leader));
                To(party.Leader, InviteSentToken, name);
                break;
            case PartyChangeKind.Declined:
                To(party.Leader, DeclinedToken, name);
                break;
            case PartyChangeKind.Disbanded:
                ToMembers(party, DisbandedToken, "");
                break;
        }
    }

    private void ToMembers(PartyInfo party, string token, string arg)
    {
        foreach (string member in party.Members)
            To(member, token, arg);
    }

    private void To(string member, string token, string arg = "")
    {
        if (parties.HeroOf(member) is { } hero && players.SlotOf(hero) is > 0 and var slot)
            messages.Notice(slot, token, arg);
    }

    private string NameOf(string member) =>
        parties.HeroOf(member) is { } hero && players.SlotOf(hero) is > 0 and var slot ? players.NameOf(slot)
        : parties.Card(member)?.Name ?? member;
}
