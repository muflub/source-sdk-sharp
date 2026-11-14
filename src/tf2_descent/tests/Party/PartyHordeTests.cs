using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Server;
using TF2.Descent.Server.Horde.Director;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Party;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// The spawn director's party size (D11; WO9): the largest party the server holds (at a level
/// change its members are still reconnecting when the level is ready),
/// never fewer than the human clients connected.
/// </summary>
public sealed class PartyHordeTests
{
    private readonly Clients _clients = new();
    private readonly InMemoryParties _parties = new(new TF2DescentRules(), new PartyChanges().Hooks);
    private readonly PartyHordeParty _size;

    public PartyHordeTests() => _size = new PartyHordeParty(new ConnectedHordeParty(_clients, new NoBots()), _parties);

    private static string Slot(int slot) => "slot" + slot;

    [Fact]
    public void With_nobody_connected_the_size_is_one()
    {
        Assert.Equal(1, _size.PartySize());
    }

    [Fact]
    public void Two_strangers_connected_count_two()
    {
        _clients.Connected.UnionWith([1, 2]);

        Assert.Equal(2, _size.PartySize());
    }

    [Fact]
    public async Task A_party_with_a_member_not_connected_counts_every_member()
    {
        _clients.Connected.UnionWith([1, 2]);
        Assert.True((await _parties.Make([Slot(1), Slot(2), "hero-still-travelling"])).Ok);

        Assert.Equal(3, _size.PartySize());
    }

    [Fact]
    public async Task At_a_level_change_the_party_that_travelled_counts_while_its_members_reconnect()
    {
        // Measured live: at level ready after a changelevel no client of the two travelling was connected yet.
        Assert.True((await _parties.Make([Slot(1), Slot(2)])).Ok);

        Assert.Equal(2, _size.PartySize());
    }

    [Fact]
    public async Task The_largest_of_two_parties_counts()
    {
        Assert.True((await _parties.Make([Slot(1), Slot(2)])).Ok);
        Assert.True((await _parties.Make([Slot(3), Slot(4), Slot(5)])).Ok);

        Assert.Equal(3, _size.PartySize());
    }

    private sealed class Clients : IConnectedClients
    {
        public HashSet<int> Connected { get; } = [];
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public int UserId(int entIndex) => Connected.Contains(entIndex) ? entIndex + 100 : -1;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private sealed class NoBots : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => false;
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => [];
    }
}
