using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server.Party;

/// <summary>A member of a host party as a party frame shows one who is not on this server: the host's character card.</summary>
public sealed record HostMemberCard(string Name, string ClassName, int Level);

/// <summary>
/// The host SDK's party calls (SDK 2.6.0 <c>IParties</c>) as the hosted parties make them, every
/// task completing on the game thread. <see cref="HostLink"/> is the one implementation: the
/// acting player is named by SteamID and his held lease goes on the call; members and targets
/// are character ids, as the host names them. A player with no lease held here is refused
/// locally (<see cref="HostError.NoCharacter"/>), nothing sent.
/// <para>What the SDK has no call for: declining an invitation, disbanding a party, hearing a
/// change made elsewhere (the hosted parties ask again), and the party an instance was booted
/// for (<c>BootInfo</c> carries none).</para>
/// </summary>
public interface IHostParties
{
    /// <summary><c>Parties.Get("", character)</c>: the party the player's character is in.</summary>
    Task<HostResult<HostParty>> PartyOf(string steamId);

    /// <summary><c>Parties.Get(partyId, "")</c>: a party by its id.</summary>
    Task<HostResult<HostParty>> PartyById(string partyId);

    /// <summary><c>Parties.Create(lease)</c>: a party the player leads.</summary>
    Task<HostResult<HostParty>> Create(string steamId);

    /// <summary><c>Parties.Invite(lease, party, character)</c>, the player as the leader.</summary>
    Task<HostResult<HostParty>> Invite(string steamId, string partyId, string targetCharacter);

    /// <summary><c>Parties.Accept(lease, party)</c>.</summary>
    Task<HostResult<HostParty>> Accept(string steamId, string partyId);

    /// <summary><c>Parties.Leave(lease, party)</c>.</summary>
    Task<HostResult> Leave(string steamId, string partyId);

    /// <summary><c>Parties.Kick(lease, party, character)</c>, the player as the leader.</summary>
    Task<HostResult<HostParty>> Kick(string steamId, string partyId, string targetCharacter);

    /// <summary><c>Characters.GetSheet(character)</c>: a member's name, class and level, for a frame of a member not on this server.</summary>
    Task<HostResult<HostMemberCard>> Card(string character);
}
