using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Rules;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The parties of a hosted server: the host's (SDK 2.6.0 <c>IParties</c>), through
/// <see cref="IHostParties"/>. A member is named by his character id, so a member on another
/// instance, or in town, stays in the party; a hero on this server is mapped to his character by
/// his join (<see cref="PlayerService"/>). The host decides every verb it has a call for and only
/// what it answered is held; its refusals are told as the in-memory parties' codes where one fits
/// (<see cref="FromHost"/>). The host tells no change made elsewhere: <see cref="Refresh"/> asks
/// again (the facade does for each hero here, now and then).
/// <para>What the SDK has no call for is refused with <see cref="PartyRefusals.HostUnsupported"/>:
/// declining an invitation (it stands on the host until the party ends) and disbanding a party
/// (each member may leave). Creating, inviting, accepting, leaving and kicking are the host's.
/// The dev make is made of those calls.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class HostedParties(IHostParties host, IHostedPartyNames names, TF2DescentRules rules, IHooks<IPartyHook> hooks, ILog log)
    : IParties
{
    private readonly PartyBook _book = new(hooks);
    private readonly Dictionary<string, HostMemberCard> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _asking = new(StringComparer.Ordinal);

    public int MaxSize => rules.Content.RobotRules.Party.MaxMembers;

    public IReadOnlyList<PartyInfo> All => _book.All;

    public string? MemberId(string hero) => names.CharacterOf(hero);

    public string? HeroOf(string member) => names.SteamIdOf(member);

    public PartyInfo? Of(string hero) => MemberId(hero) is { } m ? _book.WithMember(m) : null;

    public IReadOnlyList<PartyInfo> InvitesOf(string hero) => MemberId(hero) is { } m ? _book.Inviting(m) : [];

    /// <summary>A member's card from the host, once asked for (<see cref="AskCard"/>); null before the answer.</summary>
    public HostMemberCard? Card(string member) => _cards.GetValueOrDefault(member);

    /// <summary>The party as the host answers it.</summary>
    public static PartyInfo Info(HostParty p) => new(p.Id, p.Leader, [.. p.Members], [.. p.Invited], p.LiveInstance ?? "");

    /// <summary>A host refusal as a party code: the SDK's party errors by name, anything else as the host's.</summary>
    public static PartyAnswer FromHost(HostRefusal r) => r.Error switch
    {
        HostError.PartyFull => PartyAnswer.Refused(PartyRefusals.Full, r.Reason),
        HostError.NotInParty or HostError.NoParty => PartyAnswer.Refused(PartyRefusals.NotInParty, r.Reason),
        HostError.WrongOwner => PartyAnswer.Refused(PartyRefusals.NotLeader, r.Reason),
        HostError.NoCharacter => PartyAnswer.Refused(PartyRefusals.NotAHero, r.Reason),
        _ => PartyAnswer.Refused(PartyRefusals.Host, $"{r.Error}:{r.Reason}"),
    };

    private PartyAnswer Hold(HostResult<HostParty> r, string? kicked = null)
    {
        if (!r.Ok)
            return FromHost(r.Refusal!);
        var info = Info(r.Value);
        _book.Commit(info.Id, info, kicked);
        return PartyAnswer.Done(info);
    }

    public async Task<PartyAnswer> Create(string hero)
    {
        if (MemberId(hero) is null)
            return PartyAnswer.Refused(PartyRefusals.NotAHero);
        if (Of(hero) is not null)
            return PartyAnswer.Refused(PartyRefusals.InParty);
        return Hold(await host.Create(hero));
    }

    public async Task<PartyAnswer> Invite(string from, string target)
    {
        if (MemberId(from) is null)
            return PartyAnswer.Refused(PartyRefusals.NotAHero);
        if (MemberId(target) is not { } character)
            return PartyAnswer.Refused(PartyRefusals.NoSuchPlayer);
        if (from == target)
            return PartyAnswer.Refused(PartyRefusals.Self);
        var party = Of(from);
        if (party is null)
        {
            var made = Hold(await host.Create(from));
            if (!made.Ok)
                return made;
            party = made.Party!;
        }

        return Hold(await host.Invite(from, party.Id, character));
    }

    public async Task<PartyAnswer> Accept(string hero, string? partyId = null)
    {
        var invitation = partyId is null ? InvitesOf(hero).LastOrDefault() : InvitesOf(hero).FirstOrDefault(p => p.Id == partyId);
        if (invitation is null)
            return PartyAnswer.Refused(PartyRefusals.NoInvite);
        return Hold(await host.Accept(hero, invitation.Id));
    }

    public Task<PartyAnswer> Decline(string hero, string? partyId = null) =>
        PartyAnswer.RefusedTask(PartyRefusals.HostUnsupported, "the host has no call to decline an invitation (SDK 2.6.0)");

    public async Task<PartyAnswer> Leave(string hero)
    {
        if (Of(hero) is not { } party)
            return PartyAnswer.Refused(PartyRefusals.NotInParty);
        var left = await host.Leave(hero, party.Id);
        if (!left.Ok)
            return FromHost(left.Refusal!);
        var after = await host.PartyById(party.Id);
        if (after.Ok && after.Value.Members.Count > 0)
        {
            var info = Info(after.Value);
            _book.Commit(info.Id, info);
            return PartyAnswer.Done(info);
        }

        _book.Commit(party.Id, null);
        return PartyAnswer.Done(null);
    }

    public async Task<PartyAnswer> Kick(string leader, string target)
    {
        if (Of(leader) is not { } party)
            return PartyAnswer.Refused(PartyRefusals.NotInParty);
        if (MemberId(leader) == target)
            return PartyAnswer.Refused(PartyRefusals.Self);
        if (!party.Has(target))
            return PartyAnswer.Refused(PartyRefusals.NotAMember);
        return Hold(await host.Kick(leader, party.Id, target), kicked: target);
    }

    public Task<PartyAnswer> Disband(string leader) =>
        PartyAnswer.RefusedTask(PartyRefusals.HostUnsupported, "the host has no call to disband a party (SDK 2.6.0): each member leaves");

    public async Task<PartyAnswer> Make(IReadOnlyList<string> heroes)
    {
        var distinct = heroes.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0 || distinct.Any(h => MemberId(h) is null))
            return PartyAnswer.Refused(PartyRefusals.NoSuchPlayer);
        if (distinct.Count > MaxSize)
            return PartyAnswer.Refused(PartyRefusals.Full);
        foreach (string hero in distinct)
            if (Of(hero) is not null)
                await Leave(hero);
        var made = await Create(distinct[0]);
        foreach (string hero in distinct.Skip(1))
        {
            if (!made.Ok)
                return made;
            made = await Invite(distinct[0], hero);
            if (made.Ok)
                made = await Accept(hero, made.Party!.Id);
        }

        return made;
    }

    /// <summary>Hosted, the host's party keeps a hero who left this server: he is named by his character still.</summary>
    public void Gone(string hero) { }

    /// <summary>
    /// Asks the host for the hero's party again and holds what it answers: a party joined or
    /// changed elsewhere is seen, and one the hero is no longer in is asked for by its id.
    /// </summary>
    public async Task Refresh(string hero)
    {
        if (MemberId(hero) is not { } member || !_asking.Add(hero))
            return;
        try
        {
            var held = _book.WithMember(member);
            var r = await host.PartyOf(hero);
            if (r.Ok)
            {
                var info = Info(r.Value);
                if (held is not null && held.Id != info.Id)
                    await RefreshParty(held.Id);
                _book.Commit(info.Id, info);
            }
            else if (held is not null)
            {
                await RefreshParty(held.Id);
            }
        }
        catch (Exception e)
        {
            log.Warning($"[TF2Descent/Party] the host's party of {hero} could not be read: {e.Message}\n");
        }
        finally
        {
            _asking.Remove(hero);
        }
    }

    private async Task RefreshParty(string id)
    {
        var r = await host.PartyById(id);
        _book.Commit(id, r.Ok && r.Value.Members.Count > 0 ? Info(r.Value) : null);
    }

    /// <summary>Asks the host for a member's card once (a frame of a member not on this server shows it).</summary>
    public async Task AskCard(string member)
    {
        if (_cards.ContainsKey(member) || !_asking.Add("card " + member))
            return;
        try
        {
            var r = await host.Card(member);
            if (r.Ok)
                _cards[member] = r.Value;
        }
        finally
        {
            _asking.Remove("card " + member);
        }
    }
}

/// <summary>Who a hosted hero is to the host: his character id by his SteamID, and back, for the heroes joined to this server.</summary>
public interface IHostedPartyNames
{
    /// <summary>The character id of the joined player <paramref name="steamId"/>; null for none (not joined, or his join not answered).</summary>
    string? CharacterOf(string steamId);

    /// <summary>The SteamID of the joined player whose character is <paramref name="character"/>; null when he is not on this server.</summary>
    string? SteamIdOf(string character);
}

/// <summary>The joins <see cref="PlayerService"/> holds.</summary>
[SideService(Side.Server, As = [typeof(IHostedPartyNames)])]
public sealed class PlayerPartyNames(PlayerService players) : IHostedPartyNames
{
    public string? CharacterOf(string steamId) => players.BySteamId(steamId)?.CharacterId is { Length: > 0 } c ? c : null;

    public string? SteamIdOf(string character) => players.Players.FirstOrDefault(e => e.CharacterId == character)?.SteamId;
}
