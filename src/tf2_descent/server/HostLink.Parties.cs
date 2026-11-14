using SourceSharp.Host.Sdk;

using TF2.Descent.Server.Party;

namespace TF2.Descent.Server;

/// <summary>
/// The party calls (SDK 2.6.0 <c>IParties</c>, plan_descent_logic WO9) for the hosted parties:
/// the acting player's held lease on each, members and targets as the host's character ids.
/// </summary>
public sealed partial class HostLink : IHostParties
{
    public Task<HostResult<HostParty>> PartyOf(string steamId) => Leased(steamId, (sdk, held) => sdk.Parties.Get("", held.Lease.CharacterId));

    public Task<HostResult<HostParty>> PartyById(string partyId) => Call(sdk => sdk.Parties.Get(partyId, ""));

    public Task<HostResult<HostParty>> Create(string steamId) => Leased(steamId, (sdk, held) => sdk.Parties.Create(held.Lease));

    public Task<HostResult<HostParty>> Invite(string steamId, string partyId, string targetCharacter) =>
        Leased(steamId, (sdk, held) => sdk.Parties.Invite(held.Lease, partyId, targetCharacter));

    public Task<HostResult<HostParty>> Accept(string steamId, string partyId) =>
        Leased(steamId, (sdk, held) => sdk.Parties.Accept(held.Lease, partyId));

    public Task<HostResult> Leave(string steamId, string partyId) => Call(async sdk =>
        _held.TryGetValue(steamId, out var held) && held.Lease.IsHeld
            ? await sdk.Parties.Leave(held.Lease, partyId)
            : HostResult.Refused(new HostRefusal(HostError.NoCharacter, "no_lease", $"no character of {steamId} is held here", Local: true)));

    public Task<HostResult<HostParty>> Kick(string steamId, string partyId, string targetCharacter) =>
        Leased(steamId, (sdk, held) => sdk.Parties.Kick(held.Lease, partyId, targetCharacter));

    public Task<HostResult<HostMemberCard>> Card(string character) => Call(async sdk =>
        (await sdk.Characters.GetSheet(character)).Map(c => new HostMemberCard(c.Name, c.ClassName, c.Level)));
}
