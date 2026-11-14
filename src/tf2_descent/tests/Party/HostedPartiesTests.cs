using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Rules;
using TF2.Descent.Server.Party;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// The hosted parties (WO9) over a fake host: each verb makes its SDK call under the acting
/// player and holds only what the host answered; the host's refusals become the party codes;
/// what the SDK lacks (decline, disband) is refused with a reason and sends nothing.
/// </summary>
public sealed class HostedPartiesTests
{
    private readonly FakeHostParties _host = new();
    private readonly FakeNames _names = new();
    private readonly PartyChanges _told = new();
    private readonly HostedParties _parties;

    public HostedPartiesTests()
    {
        _parties = new HostedParties(_host, _names, new TF2DescentRules(), _told.Hooks, new RecordingLog());
        _names.Joined["steamA"] = "charA";
        _names.Joined["steamB"] = "charB";
        _names.Joined["steamC"] = "charC";
    }

    private static PartyAnswer Do(Task<PartyAnswer> verb)
    {
        Assert.True(verb.IsCompleted);
        return verb.GetAwaiter().GetResult();
    }

    [Fact]
    public void Members_are_named_by_character_and_mapped_to_the_heroes_here()
    {
        Assert.Equal("charA", _parties.MemberId("steamA"));
        Assert.Equal("steamB", _parties.HeroOf("charB"));
        Assert.Null(_parties.HeroOf("charZ"));
    }

    [Fact]
    public void Invite_from_a_hero_in_no_party_creates_his_party_on_the_host_then_invites_by_character()
    {
        var asked = Do(_parties.Invite("steamA", "steamB"));

        Assert.True(asked.Ok);
        Assert.Equal(["Create steamA", "Invite steamA h1 charB"], _host.Calls);
        Assert.Equal("charA", asked.Party!.Leader);
        Assert.Equal(["charB"], asked.Party.Invited.ToArray());
        Assert.Single(_parties.InvitesOf("steamB"));
    }

    [Fact]
    public void Accept_sends_the_invitations_party_and_holds_the_answer()
    {
        Do(_parties.Invite("steamA", "steamB"));

        var joined = Do(_parties.Accept("steamB"));

        Assert.True(joined.Ok);
        Assert.Equal("Accept steamB h1", _host.Calls[^1]);
        Assert.Equal(["charA", "charB"], _parties.Of("steamB")!.Members.ToArray());
        Assert.Contains((PartyChangeKind.Joined, "charB"), _told.Kinds("h1"));
    }

    [Fact]
    public void Leave_sends_the_leave_and_holds_the_party_the_host_has_after()
    {
        Do(_parties.Invite("steamA", "steamB"));
        Do(_parties.Accept("steamB"));
        Do(_parties.Invite("steamA", "steamC"));
        Do(_parties.Accept("steamC"));

        var left = Do(_parties.Leave("steamC"));

        Assert.True(left.Ok);
        Assert.Equal(["Leave steamC h1", "Get h1"], _host.Calls[^2..]);
        Assert.Null(_parties.Of("steamC"));
        Assert.Equal(["charA", "charB"], _parties.Of("steamA")!.Members.ToArray());
    }

    [Fact]
    public void Kick_sends_the_target_character_and_tells_a_kick()
    {
        Do(_parties.Make(["steamA", "steamB", "steamC"]));

        var kicked = Do(_parties.Kick("steamA", "charC"));

        Assert.True(kicked.Ok);
        Assert.Equal("Kick steamA h1 charC", _host.Calls[^1]);
        Assert.Contains((PartyChangeKind.Kicked, "charC"), _told.Kinds("h1"));
    }

    [Fact]
    public void A_full_party_on_the_host_is_refused_as_full()
    {
        _host.Refuse = new HostRefusal(HostError.PartyFull, "party_full", "", false);

        var refused = Do(_parties.Invite("steamA", "steamB"));

        Assert.Equal(PartyRefusals.Full, refused.Refusal);
        Assert.Empty(_parties.InvitesOf("steamB"));
    }

    [Fact]
    public void The_hosts_other_refusals_carry_its_error_and_reason()
    {
        _host.Refuse = new HostRefusal(HostError.Unreachable, "down", "", true);

        var refused = Do(_parties.Create("steamA"));

        Assert.Equal(PartyRefusals.Host, refused.Refusal);
        Assert.Equal("Unreachable:down", refused.Reason);
        Assert.Empty(_parties.All);
    }

    [Fact]
    public void The_hosts_party_errors_map_to_the_party_codes()
    {
        Assert.Equal(PartyRefusals.NotInParty, HostedParties.FromHost(new HostRefusal(HostError.NotInParty, "", "", false)).Refusal);
        Assert.Equal(PartyRefusals.NotInParty, HostedParties.FromHost(new HostRefusal(HostError.NoParty, "", "", false)).Refusal);
        Assert.Equal(PartyRefusals.NotLeader, HostedParties.FromHost(new HostRefusal(HostError.WrongOwner, "", "", false)).Refusal);
    }

    [Fact]
    public void Decline_is_refused_as_unsupported_and_sends_nothing()
    {
        Do(_parties.Invite("steamA", "steamB"));
        int calls = _host.Calls.Count;

        var refused = Do(_parties.Decline("steamB"));

        Assert.Equal(PartyRefusals.HostUnsupported, refused.Refusal);
        Assert.Contains("decline", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(calls, _host.Calls.Count);
        Assert.Single(_parties.InvitesOf("steamB"));
    }

    [Fact]
    public void Disband_is_refused_as_unsupported_and_sends_nothing()
    {
        Do(_parties.Make(["steamA", "steamB"]));
        int calls = _host.Calls.Count;

        var refused = Do(_parties.Disband("steamA"));

        Assert.Equal(PartyRefusals.HostUnsupported, refused.Refusal);
        Assert.Equal(calls, _host.Calls.Count);
        Assert.NotNull(_parties.Of("steamA"));
    }

    [Fact]
    public void Inviting_a_player_not_joined_here_is_refused_before_the_host()
    {
        var refused = Do(_parties.Invite("steamA", "steamZ"));

        Assert.Equal(PartyRefusals.NoSuchPlayer, refused.Refusal);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public void A_member_who_leaves_this_server_stays_in_the_party_named_by_his_character()
    {
        Do(_parties.Make(["steamA", "steamB"]));

        _parties.Gone("steamB");
        _names.Joined.Remove("steamB");

        Assert.Equal(["charA", "charB"], _parties.Of("steamA")!.Members.ToArray());
        Assert.Null(_parties.HeroOf("charB"));
    }

    [Fact]
    public void Refresh_holds_a_party_joined_elsewhere()
    {
        _host.Seed(new HostParty("h9", "charZ", ["charZ", "charA"], [], "inst-3"));

        Assert.True(_parties.Refresh("steamA").IsCompleted);

        Assert.Equal("h9", _parties.Of("steamA")!.Id);
        Assert.Equal("Get char charA", _host.Calls[^1]);
    }

    [Fact]
    public void Refresh_lets_go_of_a_party_the_hero_left_elsewhere()
    {
        Do(_parties.Make(["steamA", "steamB"]));
        _host.Drop("charB");

        Assert.True(_parties.Refresh("steamB").IsCompleted);

        Assert.Null(_parties.Of("steamB"));
    }

    [Fact]
    public void A_members_card_is_asked_once()
    {
        Assert.True(_parties.AskCard("charZ").IsCompleted);
        Assert.True(_parties.AskCard("charZ").IsCompleted);

        Assert.Equal("Name of charZ", _parties.Card("charZ")!.Name);
        Assert.Single(_host.Calls, c => c == "Card charZ");
    }

    //-------------------------------------------------------------------------

    private sealed class FakeNames : IHostedPartyNames
    {
        public Dictionary<string, string> Joined { get; } = new(StringComparer.Ordinal);
        public string? CharacterOf(string steamId) => Joined.GetValueOrDefault(steamId);
        public string? SteamIdOf(string character) => Joined.FirstOrDefault(p => p.Value == character).Key;
    }

    /// <summary>A host with the SDK's party semantics, enough for the facts: every call recorded, a refusal on demand.</summary>
    private sealed class FakeHostParties : IHostParties
    {
        private readonly Dictionary<string, HostParty> _parties = new(StringComparer.Ordinal);
        private int _next;

        public List<string> Calls { get; } = [];
        public HostRefusal? Refuse { get; set; }

        private static string Char(string steamId) => "char" + steamId[^1..];

        public void Seed(HostParty party) => _parties[party.Id] = party;

        public void Drop(string character)
        {
            foreach (var (id, p) in _parties.ToList())
                _parties[id] = p with { Members = [.. p.Members.Where(m => m != character)] };
        }

        private Task<HostResult<HostParty>> Answer(Func<HostParty> f) =>
            Task.FromResult(Refuse is { } r ? HostResult<HostParty>.Refused(r) : HostResult<HostParty>.Success(f()));

        public Task<HostResult<HostParty>> PartyOf(string steamId)
        {
            Calls.Add($"Get char {Char(steamId)}");
            var p = _parties.Values.FirstOrDefault(x => x.Members.Contains(Char(steamId)));
            return Task.FromResult(p is null
                ? HostResult<HostParty>.Refused(new HostRefusal(HostError.NotInParty, "not_in_party", "", false))
                : HostResult<HostParty>.Success(p));
        }

        public Task<HostResult<HostParty>> PartyById(string partyId)
        {
            Calls.Add($"Get {partyId}");
            return Task.FromResult(_parties.TryGetValue(partyId, out var p)
                ? HostResult<HostParty>.Success(p)
                : HostResult<HostParty>.Refused(new HostRefusal(HostError.NoParty, "no_party", "", false)));
        }

        public Task<HostResult<HostParty>> Create(string steamId)
        {
            Calls.Add($"Create {steamId}");
            return Answer(() =>
            {
                var p = new HostParty("h" + (++_next), Char(steamId), [Char(steamId)], [], "");
                _parties[p.Id] = p;
                return p;
            });
        }

        public Task<HostResult<HostParty>> Invite(string steamId, string partyId, string targetCharacter)
        {
            Calls.Add($"Invite {steamId} {partyId} {targetCharacter}");
            return Answer(() => _parties[partyId] = _parties[partyId] with { Invited = [.. _parties[partyId].Invited, targetCharacter] });
        }

        public Task<HostResult<HostParty>> Accept(string steamId, string partyId)
        {
            Calls.Add($"Accept {steamId} {partyId}");
            var p = _parties[partyId];
            return Answer(() => _parties[partyId] = p with
            {
                Members = [.. p.Members, Char(steamId)],
                Invited = [.. p.Invited.Where(i => i != Char(steamId))],
            });
        }

        public Task<HostResult> Leave(string steamId, string partyId)
        {
            Calls.Add($"Leave {steamId} {partyId}");
            Drop(Char(steamId));
            return Task.FromResult(Refuse is { } r ? HostResult.Refused(r) : HostResult.Success);
        }

        public Task<HostResult<HostParty>> Kick(string steamId, string partyId, string targetCharacter)
        {
            Calls.Add($"Kick {steamId} {partyId} {targetCharacter}");
            Drop(targetCharacter);
            return Answer(() => _parties[partyId]);
        }

        public Task<HostResult<HostMemberCard>> Card(string character)
        {
            Calls.Add($"Card {character}");
            return Task.FromResult(HostResult<HostMemberCard>.Success(new HostMemberCard("Name of " + character, "soldier", 7)));
        }
    }
}
