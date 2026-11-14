using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Server.Party;
using TF2.Descent.Tests.Hero;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// The parties the game is given (<see cref="Parties"/>), unhosted: a client who leaves the server
/// leaves his party at the next frame; the departures a level change reports are the travellers',
/// and the party is kept across the change (found live: both members reported gone at the change,
/// and the party was lost).
/// </summary>
public sealed class PartiesTests
{
    private const int A = 3, B = 4, C = 5;

    private readonly HeroRig _rig = new();
    private readonly InMemoryParties _memory;
    private readonly Parties _parties;

    public PartiesTests()
    {
        _memory = new InMemoryParties(_rig.Rules, new PartyChanges().Hooks);
        var hosted = new HostedParties(new NoHost(), new NoNames(), _rig.Rules, new PartyChanges().Hooks, new RecordingLog());
        _parties = new Parties(_rig.Host, _memory, hosted, _rig.Heroes, _rig.Keys());
    }

    private string Key(int slot) => _rig.Keys().Of(slot)!;

    private async Task Party(params int[] slots) => Assert.True((await _parties.Make([.. slots.Select(Key)])).Ok);

    [Fact]
    public async Task A_client_who_leaves_leaves_his_party_at_the_next_frame()
    {
        await Party(A, B, C);

        _parties.OnClientDisconnected(C);
        Assert.Equal(3, _parties.Of(Key(A))!.Size);   // not yet: a level change would take it back
        _parties.OnThink(0.015f);

        Assert.Null(_parties.Of(Key(C)));
        Assert.Equal(2, _parties.Of(Key(A))!.Size);
    }

    [Fact]
    public async Task The_departures_a_level_change_reports_keep_the_travellers_in_their_party()
    {
        await Party(A, B);

        _parties.OnClientDisconnected(A);
        _parties.OnClientDisconnected(B);
        _parties.OnLevelInit("tf2_descent-1-s1");
        _parties.OnThink(0.015f);

        Assert.Equal([Key(A), Key(B)], _parties.Of(Key(B))!.Members.ToArray());
    }

    private sealed class NoNames : IHostedPartyNames
    {
        public string? CharacterOf(string steamId) => null;
        public string? SteamIdOf(string character) => null;
    }

    private sealed class NoHost : IHostParties
    {
        private static Task<HostResult<HostParty>> No() => throw new InvalidOperationException("unhosted: no host call");
        public Task<HostResult<HostParty>> PartyOf(string steamId) => No();
        public Task<HostResult<HostParty>> PartyById(string partyId) => No();
        public Task<HostResult<HostParty>> Create(string steamId) => No();
        public Task<HostResult<HostParty>> Invite(string steamId, string partyId, string targetCharacter) => No();
        public Task<HostResult<HostParty>> Accept(string steamId, string partyId) => No();
        public Task<HostResult> Leave(string steamId, string partyId) => throw new InvalidOperationException("unhosted: no host call");
        public Task<HostResult<HostParty>> Kick(string steamId, string partyId, string targetCharacter) => No();
        public Task<HostResult<HostMemberCard>> Card(string character) => throw new InvalidOperationException("unhosted: no host call");
    }
}
