using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Bots as engine clients (plan_hl2mp_ai §3.1, Phase 3 step 2): the claim of
/// the client the engine puts in while <see cref="BotPlayers.Add"/> waits,
/// <c>FL_FAKECLIENT</c> on the bot's flag word, and the fake client's USERINFO
/// convars read back through the server's own <c>GetClientConVarValue</c>
/// path (<see cref="IClientInfo.ClientConVar"/>).
/// </summary>
public class BotPlayersTests
{
    /// <summary>
    /// The engine's half, as CreateFakeClientEx behaves: a free slot, the
    /// client put in the server (ClientPutInServer, with the name the engine
    /// gave) before the call returns, and the convars kept per slot.
    /// </summary>
    private sealed class Engine : IFakeClients
    {
        public BotPlayers? Bots;
        public int NextIndex = 2;
        public int Capacity = 4;
        public Func<string, string> Rename = n => n;
        public readonly Dictionary<(int, string), string> ConVars = new();

        public int CreateFakeClient(string name)
        {
            if (NextIndex > Capacity)
                return 0;

            int index = NextIndex++;
            Bots!.PutInServer(index, Rename(name));
            return index;
        }

        public bool SetFakeClientConVar(int entIndex, string name, string value)
        {
            ConVars[(entIndex, name)] = value;
            return true;
        }
    }

    /// <summary>The engine's GetClientConVarValue over the slots' convars, and the kicks asked for.</summary>
    private sealed class Clients(Engine engine) : IConnectedClients
    {
        public readonly List<(int EntIndex, string Reason)> Kicks = new();

        public ulong SteamId64(int entIndex) => 0;

        public string? Address(int entIndex) => null;

        public float? AvgLatency(int entIndex) => null;

        public string? ClientConVar(int entIndex, string name)
            => engine.ConVars.TryGetValue((entIndex, name), out string? v) ? v : null;

        public int UserId(int entIndex) => entIndex + 100;

        public void Kick(int entIndex, string reason) => Kicks.Add((entIndex, reason));

        public void ClientCommand(int entIndex, string command) { }

        public void Reconnect(int entIndex) { }
    }

    /// <summary>IEnginePlayers answering only GetHandle, from a slot table; anything else is a failure.</summary>
    public class HandlesOnly : DispatchProxy
    {
        public Dictionary<int, EntityHandle> Slots = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IEnginePlayers.GetHandle)
                ? Slots.TryGetValue((int)args![0]!, out EntityHandle h) ? h : EntityHandle.None
                : throw new InvalidOperationException($"BotPlayers asked IEnginePlayers.{method?.Name}");
    }

    private readonly Engine _engine = new();
    private readonly IEnginePlayers _players = DispatchProxy.Create<IEnginePlayers, HandlesOnly>();
    private readonly ServerPlayerMoves _moves = new();
    private readonly Clients _clients;
    private readonly BotPlayers _bots;

    private static readonly KeyValuePair<string, string>[] Appearance =
    [
        new("cl_playermodel", "models/humans/group03/female_02.mdl"),
        new("cl_defaultweapon", "weapon_smg1"),
    ];

    public BotPlayersTests()
    {
        _clients = new Clients(_engine);
        _bots = new BotPlayers(_engine, _players, _clients, _moves);
        _engine.Bots = _bots;
        for (int i = 1; i <= 8; i++)
            Slots[i] = new EntityHandle((uint)(0x0001_0000 * i + i));
    }

    private Dictionary<int, EntityHandle> Slots => ((HandlesOnly)(object)_players).Slots;

    [Fact]
    public void AddAnswersThePlayerOfTheClientTheEnginePutIn()
    {
        EntityHandle bot = _bots.Add("Alyx", Appearance, out string why);

        Assert.Equal("", why);
        Assert.Equal(Slots[2], bot);
        Assert.Equal([new BotPlayer(Slots[2], 2, "Alyx")], _bots.Bots);
    }

    [Fact]
    public void TheClaimKeepsTheNameTheEngineGave()
    {
        // The engine renames a client whose name is taken ("(1)Alyx"): the
        // claim is by the put-in-server that arrives during the create, not
        // by the name asked for, and the bot is called what the engine says.
        _engine.Rename = n => "(1)" + n;

        EntityHandle bot = _bots.Add("Alyx", Appearance, out _);

        Assert.Equal(Slots[2], bot);
        Assert.Equal("(1)Alyx", Assert.Single(_bots.Bots).Name);
        Assert.Equal("(1)Alyx", _clients.ClientConVar(2, "name"));
    }

    [Fact]
    public void AHumanPutInOutsideAnAddIsNotClaimed()
    {
        _bots.PutInServer(5, "gordon");

        Assert.Empty(_bots.Bots);
        Assert.False(_bots.IsBot(Slots[5]));
    }

    [Fact]
    public void AFullServerIsRefusedAndRecordsNoBot()
    {
        _engine.Capacity = 1;

        EntityHandle bot = _bots.Add("Alyx", Appearance, out string why);

        Assert.Equal(EntityHandle.None, bot);
        Assert.Contains("full", why, StringComparison.Ordinal);
        Assert.Empty(_bots.Bots);
    }

    [Fact]
    public void AnIndexOtherThanTheClaimedOneIsRefused()
    {
        // IndexOfEdict must name the client the engine put in: a mismatch is
        // a wrong claim, never a bot.
        var liar = new LyingEngine { Bots = null };
        var bots = new BotPlayers(liar, _players, _clients, _moves);
        liar.Bots = bots;

        EntityHandle bot = bots.Add("Alyx", Appearance, out string why);

        Assert.Equal(EntityHandle.None, bot);
        Assert.Contains("put client 3", why, StringComparison.Ordinal);
    }

    private sealed class LyingEngine : IFakeClients
    {
        public BotPlayers? Bots;

        public int CreateFakeClient(string name)
        {
            Bots!.PutInServer(3, name);
            return 4;
        }

        public bool SetFakeClientConVar(int entIndex, string name, string value) => true;
    }

    [Fact]
    public void ABotCarriesFakeClientAndClient()
    {
        EntityHandle bot = _bots.Add("Alyx", Appearance, out _);

        int flags = _moves.Find(bot)!.Move.Flags;
        Assert.Equal(1 << 9, ServerCommandRules.FlFakeClient);
        Assert.True((flags & ServerCommandRules.FlFakeClient) != 0, $"flags {flags:x}");
        Assert.True((flags & ServerCommandRules.FlClient) != 0, $"flags {flags:x}");
        Assert.True(_bots.IsBot(bot));
    }

    [Fact]
    public void AHumanIsNotABot()
    {
        _bots.Add("Alyx", Appearance, out _);
        _moves.For(Slots[5]).Spawn(new Vector(0f, 0f, 64f));

        Assert.False(_bots.IsBot(Slots[5]));
    }

    [Fact]
    public void TheFakeClientsUserInfoReadsBackThroughTheClientConVarRead()
    {
        _bots.Add("Alyx", Appearance, out _);

        Assert.Equal("Alyx", _clients.ClientConVar(2, "name"));
        Assert.Equal("models/humans/group03/female_02.mdl", _clients.ClientConVar(2, "cl_playermodel"));
        Assert.Equal("weapon_smg1", _clients.ClientConVar(2, "cl_defaultweapon"));
    }

    [Fact]
    public void KickNamesTheBotsSlotAndOnlyABot()
    {
        EntityHandle bot = _bots.Add("Alyx", Appearance, out _);

        Assert.False(_bots.Kick(Slots[5], "not a bot"));
        Assert.True(_bots.Kick(bot, "kicked"));
        Assert.Equal([(2, "kicked")], _clients.Kicks);
    }

    [Fact]
    public void ADisconnectForgetsTheBot()
    {
        _bots.Add("Alyx", Appearance, out _);
        _bots.Add("Barney", Appearance, out _);

        _bots.OnClientDisconnected(2);

        Assert.Equal(["Barney"], _bots.Bots.Select(b => b.Name));
    }

    [Fact]
    public void ALevelShutdownForgetsEveryBot()
    {
        // The engine clears its fake clients when a level ends
        // (InactivateClients), with no disconnect a participant
        // is told of during the shutdown.
        _bots.Add("Alyx", Appearance, out _);
        _bots.Add("Barney", Appearance, out _);

        _bots.OnLevelShutdown();

        Assert.Empty(_bots.Bots);
    }

    [Fact]
    public void AClientInAFormerBotsSlotIsNotMarked()
    {
        _bots.Add("Alyx", Appearance, out _);
        _bots.OnLevelShutdown();
        var human = new EntityHandle(0x0009_0002u);
        Slots[2] = human;

        _bots.PutInServer(2, "gordon");

        Assert.Empty(_bots.Bots);
        Assert.False(_bots.IsBot(human));
    }

    [Fact]
    public void AnEmptyNameIsRefusedBeforeTheEngineIsAsked()
    {
        EntityHandle bot = _bots.Add(" ", Appearance, out string why);

        Assert.Equal(EntityHandle.None, bot);
        Assert.Equal(2, _engine.NextIndex);
        Assert.Contains("name", why, StringComparison.Ordinal);
    }
}
