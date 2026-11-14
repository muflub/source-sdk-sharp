using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;
using TF2.Descent.Server.Party;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// Heroes for the WO6 and WO5 facts, unhosted: <see cref="HeroRig"/>'s heroes in slots
/// <see cref="A"/> and <see cref="B"/> (and more on demand), a fake world at 10 ticks a second
/// (so the rules' seconds read as tens of ticks: 20 s is 200), the downed state, its damage
/// link, the in-memory ledger, the corpses over a fake corpse world and the death costs.
/// A kill in the world is the engine's death path: the player dies and the death hook runs.
/// </summary>
internal sealed class DownedRig
{
    public const int A = 3;
    public const int B = 4;

    public readonly HeroRig Heroes = new();
    public readonly World Clock = new();
    public readonly Listener Told = new();
    public readonly Bodies Corpses = new();
    public readonly RecordingEngine Messages = new();
    public readonly HeroDowned Downed;
    public readonly DownedDamage Damage;
    public readonly InMemoryItemLedger Ledger;
    public readonly HeroKeys Keys;
    public readonly HeroCorpses Graves;
    public readonly HeroDeathCosts Costs;
    public readonly RobotKillerNames Killers;
    public readonly RobotTemplates Robots = new();
    public readonly TF2.Descent.Server.Gear.HeroWear Wear;
    public bool ModeActive { get; set; } = true;

    /// <summary>Whether the rig's heroes were made one party at the start (WO9); <see cref="Strangers"/> makes none.</summary>
    public readonly bool OneParty;

    /// <summary>Heroes in no party: each is solo to the others (WO9).</summary>
    public static DownedRig Strangers(params int[] heroes) => new(false, heroes);

    public DownedRig(params int[] heroes) : this(true, heroes) { }

    private DownedRig(bool oneParty, int[] heroes)
    {
        OneParty = oneParty;
        var mode = new ModeState(this);
        Keys = Heroes.Keys();
        Parties = new InMemoryParties(Heroes.Rules, new HeroRig.ListHooks<IPartyHook>([]));
        Roster = new PartyRoster(Parties, new EnginePartySlots(Heroes.Heroes, Keys));
        Downed = new HeroDowned(mode, Heroes.Heroes, Heroes.Rules, Clock, new HeroRig.ListHooks<IHeroDownedListener>([Told]), Roster, Heroes.Log);
        Killers = new RobotKillerNames(mode, Robots, Heroes.Engine);
        Damage = new DownedDamage(mode, Downed, Heroes.Heroes, Heroes.Engine, Clock, Killers);
        Ledger = new InMemoryItemLedger(Heroes.Rules, new HeroRig.ListHooks<IItemLedgerHook>([]), new StartingKit(Heroes.Rules));
        Graves = new HeroCorpses(mode, Heroes.Heroes, Keys, Ledger, Corpses, Clock, new ClientMessages(Messages), Heroes.Log);
        // The wear's schema is read only by a shot's tally: the death's flush never asks it.
        Wear = new TF2.Descent.Server.Gear.HeroWear(Ledger, Keys, Heroes.Rules, new TF2.Shared.Econ.ItemSchema(Heroes.Engine, Heroes.Log), Heroes.Log);
        Costs = new HeroDeathCosts(mode, Heroes.Heroes, Keys, Ledger, Heroes.Rules, Downed, Graves, Wear, Heroes.Log);
        Clock.Died = player => Costs.OnPlayerDied(new PlayerDeath(player, (int)(player.EntryIndex & 0xFFFF), EntityHandle.None,
                                                                  Clock.Origin(player), EntityHandle.None));
        Heroes.Engine.ReturnsEachCall("Entities.IsPlayer", () => (Heroes.Engine.Calls[^1].ArgAs<EntityHandle>(0).Value & 0xFFFF_0000u) == 0x0001_0000u);
        int[] joined = heroes.Length == 0 ? [A, B] : heroes;
        foreach (int index in joined)
            Join(index);
        if (oneParty && joined.Length >= 2)
            Assert.True(Parties.Make([.. joined.Select(Key)]).Result.Ok);
    }

    /// <summary>The rig's parties (unhosted), and the roster the downed state reads.</summary>
    public readonly InMemoryParties Parties;
    public readonly PartyRoster Roster;

    /// <summary>The heroes in <paramref name="indexes"/> form one party, the first leading, each out of his own first.</summary>
    public void MakeParty(params int[] indexes) => Assert.True(Parties.Make([.. indexes.Select(Key)]).Result.Ok);

    public static EntityHandle PlayerOf(int index) => HeroRig.PlayerOf(index);

    public HeroComponent Hero(int index) => Heroes.Heroes.Of(index)!;

    public HeroNetState State(int index) => Hero(index).State!;

    /// <summary>The ledger's name of the hero in <paramref name="index"/>.</summary>
    public string Key(int index) => Keys.Of(index)!;

    /// <summary>A hero joins: his component attaches, he stands at full life, the ledger loads him (the starting kit).</summary>
    public void Join(int index)
    {
        Heroes.Join(index);
        Clock.Alive.Add(PlayerOf(index));
        Clock.Healths[PlayerOf(index)] = Clock.Max;
        Ledger.Load(Key(index)).Wait();
        // A hero who joins a one-party rig after it was made joins the party (the facts' "another member").
        if (OneParty && Parties.All.Count == 1 && Parties.All[0].Size < Parties.MaxSize)
            MakeParty([.. Parties.All[0].Members.Select(m => Heroes.Heroes.All.First(h => Key(h.PlayerIndex) == m).PlayerIndex), index]);
    }

    /// <summary>The frame: the downed state's and the corpses' think.</summary>
    public void Think() => new DownedFrame(Downed, Graves).OnThink(0.1f);

    /// <summary>Runs the frames from now to <paramref name="tick"/>, one a tick.</summary>
    public void RunTo(int tick)
    {
        while (Clock.Tick < tick)
        {
            Clock.Tick++;
            Think();
        }
    }

    /// <summary>A hit of <paramref name="damage"/> on the hero in <paramref name="index"/> from <paramref name="attacker"/>, as the damage path asks: the rule, then the fold; answers what lands (0 for a refused hit), applied to the fake health.</summary>
    public float Hit(int index, float damage, EntityHandle attacker = default)
    {
        var player = PlayerOf(index);
        var hit = new DamageContext(player, attacker, damage, (int)DamageType.Bullet);
        if (Damage.Decide(in hit) == Decision.No)
            return 0f;
        float lands = Damage.Modify(in hit, damage);
        int left = Clock.Health(player) - (int)lands;
        Clock.Healths[player] = left;
        if (left <= 0 && Clock.Alive.Remove(player))
            Clock.Died?.Invoke(player);
        return lands;
    }

    //-------------------------------------------------------------------------

    internal sealed class World : IDownedWorld
    {
        public int Tick { get; set; } = 1000;
        public double TicksPerSecond => 10;
        public int Max { get; set; } = 200;
        public HashSet<EntityHandle> Alive { get; } = [];
        public HashSet<EntityHandle> Holding { get; } = [];
        public Dictionary<EntityHandle, Vector> Origins { get; } = new();
        public Dictionary<EntityHandle, int> Healths { get; } = new();
        public Dictionary<EntityHandle, float> Frozen { get; } = new();
        public List<EntityHandle> Kills { get; } = [];
        public Action<EntityHandle>? Died { get; set; }

        public bool IsAlive(EntityHandle player) => Alive.Contains(player);
        public Vector Origin(EntityHandle player) => Origins.GetValueOrDefault(player);
        public int Health(EntityHandle player) => Healths.GetValueOrDefault(player);
        public int MaxHealth(EntityHandle player) => Max;
        public void SetHealth(EntityHandle player, int health) => Healths[player] = health;
        public bool HoldingUse(EntityHandle player) => Holding.Contains(player);
        public void Freeze(EntityHandle player, float seconds) => Frozen[player] = seconds;
        public void Unfreeze(EntityHandle player) => Frozen.Remove(player);

        public void Kill(EntityHandle player)
        {
            Kills.Add(player);
            Healths[player] = 0;
            if (Alive.Remove(player))
                Died?.Invoke(player);
        }

        /// <summary>The player stands again (a respawn), at full life.</summary>
        public void Respawn(EntityHandle player)
        {
            Alive.Add(player);
            Healths[player] = Max;
        }
    }

    internal sealed class Listener : IHeroDownedListener
    {
        public List<(int Player, bool Down)> Heard { get; } = [];
        public List<int> Died { get; } = [];
        public List<int> Respawned { get; } = [];
        public void HeroDowned(int playerIndex, bool down, Vector at) => Heard.Add((playerIndex, down));
        public void HeroDied(int playerIndex, Vector at) => Died.Add(playerIndex);
        public void HeroRespawned(int playerIndex, Vector at) => Respawned.Add(playerIndex);
    }

    internal sealed class Bodies : ICorpseWorld
    {
        private uint _next = 0x0003_0000;
        public Dictionary<EntityHandle, (Vector At, CorpseNetState State)> Standing { get; } = new();
        public int Made { get; private set; }

        public (EntityHandle Entity, CorpseNetState State)? Make(Vector at)
        {
            Made++;
            var handle = new EntityHandle(++_next);
            var state = new CorpseNetState();
            state.BindLocal();
            Standing[handle] = (at, state);
            return (handle, state);
        }

        public void Move(EntityHandle corpse, Vector at) => Standing[corpse] = (at, Standing[corpse].State);

        public void Remove(EntityHandle corpse) => Standing.Remove(corpse);
    }

    internal sealed class RobotTemplates : IRobotTemplateOf
    {
        public Dictionary<EntityHandle, string> Bodies { get; } = new();
        public string? TemplateOf(EntityHandle entity) => Bodies.GetValueOrDefault(entity);
    }

    private sealed class ModeState(DownedRig rig) : IDescentModeState
    {
        public bool Active => rig.ModeActive;
    }

    public static string Id(int index) => index.ToString(CultureInfo.InvariantCulture);
}
