using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot's own HUD past its SelfState (plan_bots_teams_tf2 P7 step 2; the coordinator's A7 ruling of 2026-11-20):
/// its kit read off its own loadout, its meters, and the teammates' health its HUD shows it (the beam's patient; to a
/// Medic, the callers at 75 % or less within 1,000 u), kept as one object while nothing on it changes.
/// </summary>
public sealed class TFHudTests : IDisposable
{
    private const int BotIndex = 2;
    private static readonly EntityHandle Bot = new(0x0001_0002u);

    private sealed class Kits : ITFOwnKit
    {
        public List<TFOwnKitItem> Items { get; } = [];
        public int At { get; set; } = 1;

        public IReadOnlyList<TFOwnKitItem> KitOf(int entIndex) => entIndex == BotIndex ? Items : [];

        public int Version(int entIndex) => At;
    }

    private sealed class Hooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All { get; } = all;

        public void Each(string what, Action<T> call)
        {
            foreach (T t in All)
                call(t);
        }
    }

    /// <summary>A player of the fake world: team, class, health, max, where.</summary>
    private sealed record Person(int Team, TFClass Class, int Health, int Max, Vector At);

    private readonly Dictionary<int, Person> _people = new();
    private readonly Kits _kits = new();
    private readonly TestGlobals _globals = new(maxClients: 8);
    private float _charge;
    private int _patient = -1;
    private readonly Dictionary<int, int> _beams = new();
    private readonly Dictionary<int, global::TF2.Server.Player.TFActorFacts> _actors = new();

    private static int IndexOf(EntityHandle h) => h.EntryIndex;

    private TFHudReader Reader(params ITFHudMeters[] meters)
    {
        ITFMapPlayers people = FakeEngine.Of<ITFMapPlayers>((m, a) => m.Name switch
        {
            "Team" => _people.TryGetValue(IndexOf((EntityHandle)a[0]!), out Person? p) ? p.Team : 0,
            "ClassOf" => _people.TryGetValue(IndexOf((EntityHandle)a[0]!), out Person? p) ? p.Class : TFClass.Undefined,
            "Health" => _people[IndexOf((EntityHandle)a[0]!)].Health,
            "MaxHealth" => _people[IndexOf((EntityHandle)a[0]!)].Max,
            _ => throw FakeEngine.Unasked(m),
        });
        IEnginePlayers players = FakeEngine.Of<IEnginePlayers>((m, a) => m.Name switch
        {
            "GetHandle" => _people.ContainsKey((int)a[0]!) ? new EntityHandle(0x0001_0000u | (uint)(int)a[0]!) : EntityHandle.None,
            "IsAlive" => true,
            _ => throw FakeEngine.Unasked(m),
        });
        IEngineEntities entities = FakeEngine.Of<IEngineEntities>((m, a) => m.Name == "GetOrigin" ? _people[IndexOf((EntityHandle)a[0]!)].At : throw FakeEngine.Unasked(m));
        var classes = new TFClassTable(new TFBelievedSpeedTests.NoLog(), System.Reflection.DispatchProxy.Create<IEngineFiles, TFBelievedSpeedTests.Scripts>());
        classes.Load();
        return new TFHudReader(_kits, new Hooks<ITFHudMeters>(meters), people, players, entities, _globals, new Actors(_actors), classes);
    }

    private TFMedigunMeters Medigun()
        => new(FakeEngine.Of<ITFMedigunWeapons>((m, a) => m.Name switch
        {
            "ChargeLevel" => _charge,
            "IsReleasing" => false,
            "HealingTargetIndex" => (int)a[0]! == BotIndex ? _patient : _beams.GetValueOrDefault((int)a[0]!, -1),
            _ => throw FakeEngine.Unasked(m),
        }), _globals);

    private sealed class Actors(Dictionary<int, global::TF2.Server.Player.TFActorFacts> by) : global::TF2.Server.Player.ITFActorFacts
    {
        public bool TryRead(int playerIndex, out global::TF2.Server.Player.TFActorFacts facts) => by.TryGetValue(playerIndex, out facts);
    }

    public void Dispose() => _globals.Dispose();

    private void Me(TFClass c) => _people[BotIndex] = new Person(2, c, 150, 150, new Vector(0f, 0f, 0f));

    /// <summary>The bot's kit reaches its HUD as its loadout holds it: slot, defindex, item class, weapon kind and its own attributes.</summary>
    [Fact]
    public void TheKitIsReadOffTheBotsOwnLoadout()
    {
        Me(TFClass.Soldier);
        _kits.Items.Add(new TFOwnKitItem(TFLoadoutSlot.Primary, 228, "tf_weapon_rocketlauncher", "tf_weapon_rocketlauncher", 12,
                                         [new TFOwnKitAttribute("mult_clipsize", 0.75f), new TFOwnKitAttribute("add_onhit_addhealth", 20f)]));
        _kits.Items.Add(new TFOwnKitItem(TFLoadoutSlot.Secondary, 133, "tf_wearable", "", -1, []));
        TFHud hud = Reader().Read(Bot);

        Assert.True(hud.TryWielded(TFLoadoutSlot.Primary, out TFKitItem primary));
        Assert.Equal((228, "tf_weapon_rocketlauncher", 12, 0.75f), (primary.Defindex, primary.ItemClass, primary.WeaponKind, primary.Attribute("mult_clipsize", 1f)));
        Assert.False(hud.TryWielded(TFLoadoutSlot.Secondary, out _));
        Assert.True(hud.TryItem(TFLoadoutSlot.Secondary, out TFKitItem worn) && !worn.Wielded && worn.Defindex == 133);
    }

    /// <summary>While nothing on it changes the HUD is the same object; a new kit (its version moved) is a new one.</summary>
    [Fact]
    public void TheHudIsKeptUntilSomethingOnItChanges()
    {
        Me(TFClass.Soldier);
        _kits.Items.Add(new TFOwnKitItem(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", "tf_weapon_rocketlauncher", 12, []));
        TFHudReader reader = Reader();
        TFHud first = reader.Read(Bot);
        Assert.Same(first, reader.Read(Bot));
        _kits.Items[0] = _kits.Items[0] with { Defindex = 127 };
        _kits.At++;
        TFHud second = reader.Read(Bot);
        Assert.NotSame(first, second);
        Assert.Equal((18, 127, 2), (first.Kit[0].Defindex, second.Kit[0].Defindex, reader.Made));
    }

    /// <summary>A Medic's HUD shows his charge; a Soldier's shows none.</summary>
    [Fact]
    public void AMedicsHudShowsHisCharge()
    {
        _charge = 0.4f;
        Me(TFClass.Medic);
        Assert.Equal(0.4f, Reader(Medigun()).Read(Bot).Meter(TFMedigunMeters.Charge));
        Me(TFClass.Soldier);
        Assert.Equal(-1f, Reader(Medigun()).Read(Bot).Meter(TFMedigunMeters.Charge));
    }

    /// <summary>
    /// To a Medic, a teammate at 75 % or less within 1,000 u calls: a flag and a place, no number. Not one at 80 %, not one
    /// 1,200 u off, not an enemy at 50 %, and to a Soldier nobody.
    /// </summary>
    [Fact]
    public void AMedicSeesWhichTeammatesCall()
    {
        Me(TFClass.Medic);
        _people[3] = new Person(2, TFClass.Soldier, 140, 200, new Vector(500f, 0f, 0f));   // 70 %, near: calls
        _people[4] = new Person(2, TFClass.Soldier, 160, 200, new Vector(500f, 0f, 0f));   // 80 %
        _people[5] = new Person(2, TFClass.Soldier, 100, 200, new Vector(1200f, 0f, 0f));  // far
        _people[6] = new Person(3, TFClass.Soldier, 100, 200, new Vector(100f, 0f, 0f));   // an enemy
        TFHud medic = Reader(Medigun()).Read(Bot);
        Assert.Equal([new TFMateHealth(3, -1, -1, TFMateSeen.Calling, new AiVector(500f, 0f, 0f))], medic.Mates.ToArray());

        Me(TFClass.Soldier);
        Assert.Empty(Reader(Medigun()).Read(Bot).Mates);
    }

    /// <summary>An enemy Spy disguised as the bot's team calls to its Medic as a teammate does, as on stock's HUD; the same Spy undisguised does not.</summary>
    [Fact]
    public void ASpyDisguisedAsTheTeamCallsToItsMedic()
    {
        Me(TFClass.Medic);
        _people[3] = new Person(3, TFClass.Spy, 60, 125, new Vector(300f, 0f, 0f));
        // His disguise shows 100 of a Soldier's 200: he calls, as stock's HUD has it (his real 60 of 125 is not read).
        _actors[3] = new(TFClass.Spy, TFTeam.Blue, TFClass.Soldier, TFTeam.Red, 4, 0f, 0, false, false, false, true, false, false, DisguiseHealth: 100);
        Assert.Equal(TFMateSeen.Calling, Assert.Single(Reader(Medigun()).Read(Bot).Mates).Why);
        _actors[3] = _actors[3] with { Disguised = false };
        Assert.Empty(Reader(Medigun()).Read(Bot).Mates);
    }

    /// <summary>
    /// A disguised enemy Spy shows his disguise's health, not his own (review-p7f F5): beamed, his panel reads 200 of a
    /// Soldier's 200 though he has 60 of 125; and at that he does not call, where his real 48 % would have.
    /// </summary>
    [Fact]
    public void ADisguisedSpyShowsHisDisguisesHealth()
    {
        Me(TFClass.Medic);
        _people[3] = new Person(3, TFClass.Spy, 60, 125, new Vector(300f, 0f, 0f));
        _actors[3] = new(TFClass.Spy, TFTeam.Blue, TFClass.Soldier, TFTeam.Red, 4, 0f, 0, false, false, false, true, false, false, DisguiseHealth: 200);
        Assert.Empty(Reader(Medigun()).Read(Bot).Mates);
        _patient = 3;
        Assert.Equal((200, 200), (Reader(Medigun()).Read(Bot).Mates[0].Health, Reader(Medigun()).Read(Bot).Mates[0].MaxHealth));
    }

    /// <summary>The beam's patient's exact health shows whatever it is, a full one too: the Medic's heal-target panel. It is not a caller as well.</summary>
    [Fact]
    public void TheBeamsPatientsHealthShows()
    {
        Me(TFClass.Medic);
        _people[3] = new Person(2, TFClass.HeavyWeapons, 100, 300, new Vector(200f, 0f, 0f));
        _patient = 3;
        Assert.Equal([new TFMateHealth(3, 100, 300, TFMateSeen.HealTarget, new AiVector(200f, 0f, 0f))], Reader(Medigun()).Read(Bot).Mates.ToArray());
        _patient = -1;
        Assert.Equal(TFMateSeen.Calling, Assert.Single(Reader(Medigun()).Read(Bot).Mates).Why);
    }

    /// <summary>A bot a Medic's beam is on sees its healer and his charge (the Healer panel, his target ID); with no beam on it, neither.</summary>
    [Fact]
    public void ABotSeesTheMedicHealingItAndHisCharge()
    {
        Me(TFClass.Soldier);
        _people[5] = new Person(2, TFClass.Medic, 150, 150, new Vector(100f, 0f, 0f));
        _beams[5] = BotIndex;
        _charge = 0.6f;
        TFHud healed = Reader(Medigun()).Read(Bot);
        Assert.Equal((5f, 0.6f), (healed.Meter(TFMedigunMeters.HealedBy), healed.Meter(TFMedigunMeters.HealerCharge)));
        _beams.Clear();
        Assert.Equal(-1f, Reader(Medigun()).Read(Bot).Meter(TFMedigunMeters.HealedBy));
    }
}
