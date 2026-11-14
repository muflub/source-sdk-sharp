using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Mode;
using TF2.Descent.Shared;
using TF2.Server.Player;
using TF2.Shared;

using HeroSheets = TF2.Descent.Server.Hero.HeroSheets;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// Heroes for the HE2 facts, unhosted: the core's per-player composite over a
/// <see cref="RecordingEngine"/>, TF2 Descent's hero source in it, the shipped
/// rules, and a <see cref="HeroEntity"/> per player the engine "creates".
/// </summary>
internal sealed class HeroRig
{
    public const int Index = 3;
    public const ulong SteamId = 76561197960287930UL;

    public readonly RecordingEngine Engine = new();
    public readonly RecordingLog Log = new();
    public readonly TF2DescentRules Rules = new();
    public readonly Heroes Heroes = new();
    public readonly HeroSheets Sheets;
    public readonly HostService Host;
    public readonly Clients Players = new();
    public readonly Resupply Health = new();
    public readonly List<IHeroContributionSource> Contributions = [];
    public readonly List<IHeroStatsWatcher> Watchers = [];
    public readonly PlayerComposites Composites;
    public readonly Dictionary<EntityHandle, HeroEntity> Entities = new();
    public bool ModeActive { get; set; } = true;
    public HashSet<EntityHandle> Bots { get; } = [];

    public static EntityHandle PlayerOf(int index) => new(0x0001_0000u | (uint)index);

    /// <summary>The hero entity the engine makes for the <paramref name="n"/>th create.</summary>
    public static EntityHandle EntityOf(int n) => new(0x0002_0100u | (uint)n);

    private int _created;
    private readonly IHostLink _link;

    /// <summary>The player service over the rig's host and sheets (HO13's camps find a character's sheet through it).</summary>
    public PlayerService NewPlayers() => new(_link, Host, Players, new ClientMessages(Engine), new NeverScheduler(), Log, Sheets);

    /// <summary>The item ledger's names for the slots, as the item commands derive them (HE5's ledger sheet).</summary>
    public TF2.Descent.Server.Items.HeroKeys Keys() => KeysOver(Players);

    /// <summary>The ledger's names over clients of the fact's own (which slots are connected).</summary>
    public TF2.Descent.Server.Items.HeroKeys KeysOver(IConnectedClients clients) =>
        new(Host, new PlayerService(_link, Host, clients, new ClientMessages(Engine), new NeverScheduler(), Log, Sheets), clients);

    public HeroRig(HostingMode mode = HostingMode.Unhosted)
    {
        Engine.ReturnsEachCall("Entities.Create", () =>
        {
            var handle = EntityOf(++_created);
            var entity = new HeroEntity(Engine);
            entity.State.BindLocal();
            Entities[handle] = entity;
            return handle;
        });
        Engine.Returns("Entities.IsValid", true);
        Engine.Returns("Entities.GetOrigin", default(Vector));
        Engine.ReturnsEachCall("Entities.GetIndex", () => (int)(Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex & 0xFFFF));
        Engine.ReturnsEachCall("Players.GetHandle", () => PlayerOf(Engine.Calls[^1].ArgAs<int>(0)));
        Engine.Returns("Entities.GetHealth", 100);

        var link = _link = new ModeOnlyLink(mode);
        Host = new HostService(link, new NoPort(), new NeverScheduler(), new NoConsole(), new NoBootHooks(), Log);
        Host.OnPostInit();
        Players.Steam[Index] = SteamId;

        Sheets = new HeroSheets(Rules, Host, Players, Log);
        var source = new HeroComponents(new ModeState(this), new BotList(this), Engine, new Directory(this), Sheets, Heroes,
                                        new ListHooks<IHeroContributionSource>(Contributions),
                                        new ListHooks<IHeroStatsWatcher>(Watchers), Health, Log);
        Composites = new PlayerComposites(Log, new ListHooks<IPlayerComponentSource>([source]), Engine, Engine);
    }

    /// <summary>The player in <paramref name="index"/> becomes active: the composite attaches its components.</summary>
    public HeroComponent? Join(int index = Index)
    {
        ((IClientLifecycle)Composites).OnClientActive(index);
        return Heroes.Of(index);
    }

    public void Leave(int index = Index) => ((IClientLifecycle)Composites).OnClientDisconnected(index);

    /// <summary>The level ends: every component detaches, as at a map change.</summary>
    public void LevelShutdown() => ((IGameLifecycle)Composites).OnLevelShutdown();

    /// <summary>How many hero entities the engine was asked to make, and to remove.</summary>
    public int Created => Engine.Calls.Count(c => c.Member == "Entities.Create" && c.ArgAs<string>(0) == HeroNetState.ClassName);
    public int Removed => Engine.Calls.Count(c => c.Member == "Entities.Remove" && Entities.ContainsKey(c.ArgAs<EntityHandle>(0)));

    /// <summary>A sheet raised to <paramref name="level"/> by the rules, every point it earned unspent.</summary>
    public HeroSheet AtLevel(HeroSheet sheet, int level) =>
        Rules.Heroes.GainXp(sheet, Rules.Heroes.XpAtLevel(level) - sheet.Xp).Sheet;

    //-------------------------------------------------------------------------

    internal sealed class ListHooks<T>(IReadOnlyList<T> hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    internal sealed class Clients : IConnectedClients
    {
        public Dictionary<int, ulong> Steam { get; } = new();
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public ulong SteamId64(int entIndex) => Steam.GetValueOrDefault(entIndex);
        public string? Address(int entIndex) => null;
        public int UserId(int entIndex) => entIndex + 100;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    internal sealed class Resupply : ITFPlayerResupply
    {
        public int Max { get; set; } = 200;
        public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) { }
        public int MaxHealth(EntityHandle player) => Max;
        public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false) => 0;
        public int MaxAmmo(EntityHandle player, TFAmmo ammo) => 0;
        public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => 0;
    }

    private sealed class ModeState(HeroRig rig) : IDescentModeState
    {
        public bool Active => rig.ModeActive;
    }

    private sealed class BotList(HeroRig rig) : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots here";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => rig.Bots.Contains(player);
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => [];
    }

    private sealed class Directory(HeroRig rig) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => rig.Entities.GetValueOrDefault(h);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == HeroNetState.ClassName;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>A link that answers how the server runs, and nothing else: unhosted facts never reach the host.</summary>
    private sealed class ModeOnlyLink(HostingMode mode) : IHostLink
    {
        public HostingMode Mode() => mode;
        public void Start() { }
        public Task<HostResult<BootInfo>> Booting(string map) => throw new NotSupportedException();
        public Task<HostResult<int>> MapReady(int port, string map) => throw new NotSupportedException();
        public void Players(int count) { }
        public Task<JoinOutcome> Join(string peer, string engineSteamId, BootInfo boot, string className) => throw new NotSupportedException();
        public Task<bool> Checkpoint(string steamId, SheetCheckpoint? sheet) => throw new NotSupportedException();
        public Task<bool> Leave(string steamId, string peer, SheetCheckpoint? sheet) => throw new NotSupportedException();
        public void ReplyExec(string commandId, string output) { }
        public Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot, int depth) => throw new NotSupportedException();
        public Task<TicketAnswer> SubmitTicket(string steamId, string nonce, byte[] ticket) => throw new NotSupportedException();
        public Task<bool> RecordDeath(string steamId, Vector position) => throw new NotSupportedException();
        public Task<LootAnswer> LootCorpse(string steamId) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class NoPort : IBoundPort { public int? Read() => null; }

    private sealed class NeverScheduler : IFrameScheduler
    {
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default) => new TaskCompletionSource().Task;
    }

    private sealed class NoConsole : IServerConsole
    {
        public string Execute(string command) => "";
        public void Quit(int exitCode) => throw new InvalidOperationException("quit " + exitCode);
    }

    private sealed class NoBootHooks : IHooks<IHostBootHook>
    {
        public IReadOnlyList<IHostBootHook> All => [];
        public void Each(string what, Action<IHostBootHook> call) { }
    }
}
