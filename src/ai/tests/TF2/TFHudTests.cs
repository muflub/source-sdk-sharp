using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Conditions;
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
    private readonly Dictionary<int, TFConditions> _conds = new();
    private string _xray = "1";

    /// <summary>A player's conditions as the server keeps them (expiring by <see cref="TFConditions.Tick"/>).</summary>
    private sealed class Words : ITFCondWords
    {
        private TFCondMask _bits;

        public TFCondMask Read() => _bits;

        public void Write(in TFCondMask bits) => _bits = bits;
    }

    /// <summary>Player <paramref name="index"/> respawned <paramref name="secondsAgo"/> seconds ago: stock's TF_COND_TEAM_GLOWS for <c>tf_spawn_glows_duration</c> (10), counted down.</summary>
    private void Respawned(int index, float secondsAgo)
    {
        var c = new TFConditions(authoritative: true, new Words());
        c.Add(TFCond.TeamGlows, 10f);
        c.Tick(secondsAgo, 0, default);
        _conds[index] = c;
    }

    private static int IndexOf(EntityHandle h) => h.EntryIndex;

    private TFHudReader Reader(params ITFHudMeters[] meters)
    {
        ITFMapPlayers people = FakeEngine.Of<ITFMapPlayers>((m, a) => m.Name switch
        {
            "Team" => _people.TryGetValue(IndexOf((EntityHandle)a[0]!), out Person? p) ? p.Team : 0,
            "ClassOf" => _people.TryGetValue(IndexOf((EntityHandle)a[0]!), out Person? p) ? p.Class : TFClass.Undefined,
            "Health" => _people[IndexOf((EntityHandle)a[0]!)].Health,
            "MaxHealth" => _people[IndexOf((EntityHandle)a[0]!)].Max,
            "InCond" => _conds.TryGetValue(IndexOf((EntityHandle)a[0]!), out TFConditions? c) && c.Has((TFCond)a[1]!),
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
        return new TFHudReader(_kits, new Hooks<ITFHudMeters>(meters), people, players, entities, _globals, new Actors(_actors), classes,
                               DeclaredConVars.FreshAt<TFSpecXrayConVar>(_xray));
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

    // ---- round 2's S11: the bot's own respawn glow shows its teammates' places

    /// <summary>
    /// A bot respawned 5 s ago sees its teammates through walls (stock's xray while its own TF_COND_TEAM_GLOWS lasts): a
    /// Soldier 3,000 u off, past any sight, is placed, with no number; at 11 s (the glow's 10 s are over) he is not.
    /// </summary>
    [Fact]
    public void ABotRespawned5SecondsAgoPlacesItsTeammatesThroughWalls()
    {
        Me(TFClass.Soldier);
        _people[3] = new Person(2, TFClass.Soldier, 200, 200, new Vector(3000f, 0f, 0f));
        Respawned(BotIndex, 5f);
        Assert.Equal([new TFMateHealth(3, -1, -1, TFMateSeen.Glowing, new AiVector(3000f, 0f, 0f))], Reader().Read(Bot).Mates.ToArray());
        Respawned(BotIndex, 11f);
        Assert.Empty(Reader().Read(Bot).Mates);
    }

    /// <summary>
    /// An enemy never glows to the bot: not while the bot's glow lasts, and not for his own respawn (only the bot's own
    /// condition is read; the respawned player is the one who sees). The control: a teammate beside him does glow.
    /// </summary>
    [Fact]
    public void AnEnemysGlowIsNeverRead()
    {
        Me(TFClass.Soldier);
        _people[3] = new Person(2, TFClass.Soldier, 200, 200, new Vector(900f, 0f, 0f));
        _people[4] = new Person(3, TFClass.Soldier, 200, 200, new Vector(1000f, 0f, 0f));
        Respawned(BotIndex, 2f);
        Assert.Equal(3, Assert.Single(Reader().Read(Bot).Mates).Slot);
        _conds.Clear();
        Respawned(4, 2f);
        Respawned(3, 2f);
        Assert.Empty(Reader().Read(Bot).Mates);
    }

    /// <summary>
    /// An enemy Spy the bot sees only by his disguise (dressed as its team) does not glow (the review's S11-1): stock
    /// force-transmits the glowing player's team by real team, so the Spy is never outlined map-wide. The control: a real
    /// teammate beside him glows; and the Spy is still shown as a teammate where stock's HUD shows him so (calling, to a Medic).
    /// </summary>
    [Fact]
    public void ASpyShownOnlyByHisDisguiseDoesNotGlow()
    {
        Me(TFClass.Soldier);
        _people[3] = new Person(3, TFClass.Spy, 125, 125, new Vector(800f, 0f, 0f));
        _people[4] = new Person(2, TFClass.Soldier, 200, 200, new Vector(900f, 0f, 0f));
        _actors[3] = new(TFClass.Spy, TFTeam.Blue, TFClass.Soldier, TFTeam.Red, 4, 0f, 0, false, false, false, true, false, false, DisguiseHealth: 50);
        Respawned(BotIndex, 2f);
        Assert.Equal(4, Assert.Single(Reader().Read(Bot).Mates).Slot);
        Me(TFClass.Medic);
        Assert.Equal(TFMateSeen.Calling, Reader(Medigun()).Read(Bot).Mates.Single(m => m.Slot == 3).Why);
    }

    /// <summary>With <c>tf_spec_xray</c> 0 no glow is drawn, so none is read.</summary>
    [Fact]
    public void WithTheXrayOffNoTeammateGlows()
    {
        Me(TFClass.Soldier);
        _people[3] = new Person(2, TFClass.Soldier, 200, 200, new Vector(900f, 0f, 0f));
        Respawned(BotIndex, 2f);
        _xray = "0";
        Assert.Empty(Reader().Read(Bot).Mates);
    }

    /// <summary>To a respawned Medic a calling teammate is both a caller and glowing; the beam's patient keeps his number and glows.</summary>
    [Fact]
    public void AGlowAddsToACallAndToThePatient()
    {
        Me(TFClass.Medic);
        _people[3] = new Person(2, TFClass.Soldier, 100, 200, new Vector(500f, 0f, 0f));
        _people[4] = new Person(2, TFClass.HeavyWeapons, 150, 300, new Vector(200f, 0f, 0f));
        _patient = 4;
        Respawned(BotIndex, 1f);
        TFHud hud = Reader(Medigun()).Read(Bot);
        Assert.Equal(new[] { new TFMateHealth(3, -1, -1, TFMateSeen.Calling | TFMateSeen.Glowing, new AiVector(500f, 0f, 0f)),
                             new TFMateHealth(4, 150, 300, TFMateSeen.HealTarget | TFMateSeen.Glowing, new AiVector(200f, 0f, 0f)) },
                     hud.Mates.ToArray());
    }
}
