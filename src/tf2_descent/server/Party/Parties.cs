using SourceSharp.Abi;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The <see cref="IParties"/> game code is given: the host's parties when this server runs
/// against a hosting service, the in-memory ones when it runs <c>-unhosted</c>, chosen by
/// <see cref="HostService.Mode"/> at each call, as <see cref="ItemLedger"/> chooses its ledger.
/// It also tells the parties when a hero disconnects (unhosted he leaves his party; a level
/// change's reported departures are the travellers' and are dropped), and,
/// hosted, asks the host for each hero's party when he is first seen and every
/// <see cref="RefreshFrames"/> frames after (the host tells no change made elsewhere).
/// </summary>
[SideService(Side.Server, As = [typeof(IParties)])]
public sealed class Parties(HostService host, InMemoryParties memory, HostedParties hosted, Heroes heroes, HeroKeys keys)
    : IParties, IClientLifecycle, IFrameLifecycle, IGameLifecycle
{
    /// <summary>TF2 Descent's party block (WO9).</summary>
    public const int Order = ModStages.First + 1600;

    /// <summary>How often, hosted, each hero's party is asked of the host again: about ten seconds.</summary>
    public const int RefreshFrames = 660;

    /// <summary>Before PlayerService (First + 10) forgets who a leaving client was.</summary>
    public int ClientLifecycleOrder => ModStages.First + 8;

    public int FrameOrder => Order;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private int _frame;

    private bool Hosted => host.Mode == HostingMode.Hosted;

    private IParties Current => Hosted ? hosted : memory;

    public int MaxSize => Current.MaxSize;
    public IReadOnlyList<PartyInfo> All => Current.All;
    public string? MemberId(string hero) => Current.MemberId(hero);
    public string? HeroOf(string member) => Current.HeroOf(member);
    public PartyInfo? Of(string hero) => Current.Of(hero);
    public IReadOnlyList<PartyInfo> InvitesOf(string hero) => Current.InvitesOf(hero);
    public HostMemberCard? Card(string member) => Current.Card(member);
    public Task<PartyAnswer> Create(string hero) => Current.Create(hero);
    public Task<PartyAnswer> Invite(string from, string target) => Current.Invite(from, target);
    public Task<PartyAnswer> Accept(string hero, string? partyId = null) => Current.Accept(hero, partyId);
    public Task<PartyAnswer> Decline(string hero, string? partyId = null) => Current.Decline(hero, partyId);
    public Task<PartyAnswer> Leave(string hero) => Current.Leave(hero);
    public Task<PartyAnswer> Kick(string leader, string target) => Current.Kick(leader, target);
    public Task<PartyAnswer> Disband(string leader) => Current.Disband(leader);
    public Task<PartyAnswer> Make(IReadOnlyList<string> heroes) => Current.Make(heroes);
    public void Gone(string hero) => Current.Gone(hero);

    /// <summary>
    /// A client went: he is gone from the parties at the next frame, not now. A level change
    /// reports every travelling client as gone (measured live: the core's player removal at the
    /// change; RejoinScores records them too) and each comes back on the new level in his slot,
    /// so a departure the next level's init finds still pending is a traveller's, and is dropped.
    /// </summary>
    public void OnClientDisconnected(int entIndex)
    {
        if (keys.Of(entIndex) is { } hero)
            _departing.Add(hero);
    }

    /// <summary>A new level: the departures the change reported are the travellers', who stay in their parties.</summary>
    public void OnLevelInit(string mapName) => _departing.Clear();

    public int LifecycleOrder => Order;

    private readonly HashSet<string> _departing = new(StringComparer.Ordinal);

    /// <summary>The departures of the last frame, carried out; the heroes who have left this server.</summary>
    public IReadOnlyCollection<string> Departing => _departing;

    /// <summary>
    /// The frame: a departure reported since the last frame is a client who left (unhosted he
    /// leaves his party); hosted, a hero first seen has his party asked for, every hero's again
    /// now and then, and the cards of members not here.
    /// </summary>
    public void OnThink(float frameTime)
    {
        if (_departing.Count > 0)
        {
            foreach (string hero in _departing)
            {
                Gone(hero);
                _seen.Remove(hero);
            }
            _departing.Clear();
        }

        if (!Hosted)
            return;
        bool all = ++_frame % RefreshFrames == 0;
        foreach (var hero in heroes.All)
        {
            if (keys.Of(hero.PlayerIndex) is not { } key)
                continue;
            if (_seen.Add(key) || all)
                _ = hosted.Refresh(key);
        }

        if (all)
            foreach (var party in hosted.All)
                foreach (string member in party.Members.Where(m => hosted.HeroOf(m) is null))
                    _ = hosted.AskCard(member);
    }
}

/// <summary>The party chain's dispatch, and the host link as the one <see cref="IHostParties"/>.</summary>
[SideService(Side.Server)]
public sealed class PartyRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IPartyHook>();
        registry.AddFactory<IHostParties>(services => services.Require<HostLink>());
    }
}
