using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server.Interop;
using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The crosshair's target ID (<see cref="TFTargetIdMeters"/>; the coordinator's ruling of 2026-11-20): the health of the
/// player under the bot's crosshair, by stock's rules: a teammate's always, an enemy's only to a Spy or with the Solemn Vow,
/// a disguised enemy's disguise health, a cloaked one's never, and nothing for a class that does not read it.
/// </summary>
public sealed class TFMedicTargetIdTests : IDisposable
{
    private const int BotIndex = 2, TargetIndex = 3;
    private static readonly EntityHandle Bot = new(0x0001_0002u), Target = new(0x0001_0003u);

    private sealed record Person(int Team, int Health, int Max);

    private readonly Dictionary<int, Person> _people = new() { [BotIndex] = new Person(2, 150, 150) };
    private readonly Dictionary<int, TFActorFacts> _actors = new();
    private readonly List<TFOwnKitItem> _kit = [];
    private readonly TestGlobals _globals = new(maxClients: 8);
    private EntityHandle _hit = Target;
    private bool _startSolid;

    public void Dispose() => _globals.Dispose();

    private sealed class Kits(List<TFOwnKitItem> kit) : ITFOwnKit
    {
        public IReadOnlyList<TFOwnKitItem> KitOf(int entIndex) => entIndex == BotIndex ? kit : [];

        public int Version(int entIndex) => 1;
    }

    private sealed class Actors(Dictionary<int, TFActorFacts> by) : ITFActorFacts
    {
        public bool TryRead(int playerIndex, out TFActorFacts facts) => by.TryGetValue(playerIndex, out facts);
    }

    private sealed class Resists(Func<int> resist) : ITFMedigunResists
    {
        public int ResistType(int ownerIndex) => resist();
    }

    private int _resist = -1;

    private sealed class Lines(Func<EntityHandle> hit, Func<bool> startSolid) : ITraceQueries
    {
        public int Asked { get; private set; }

        public TraceAnswer Line(Vector start, Vector end, uint mask, EntityHandle pass)
        {
            Asked++;
            return hit() == EntityHandle.None ? new TraceAnswer(1f, end, EntityHandle.None, false) : new TraceAnswer(0.1f, end, hit(), startSolid());
        }

        public TraceAnswer Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass) => throw new NotSupportedException();
    }

    private Lines? _lines;

    private TFTargetIdMeters Reader()
    {
        int IndexOf(object? h) => ((EntityHandle)h!).EntryIndex;
        ITFMapPlayers people = FakeEngine.Of<ITFMapPlayers>((m, a) => m.Name switch
        {
            "Team" => _people.TryGetValue(IndexOf(a[0]), out Person? p) ? p.Team : 0,
            "Health" => _people[IndexOf(a[0])].Health,
            "MaxHealth" => _people[IndexOf(a[0])].Max,
            _ => throw FakeEngine.Unasked(m),
        });
        IEnginePlayers players = FakeEngine.Of<IEnginePlayers>((m, a) => m.Name switch
        {
            "GetHandle" => _people.ContainsKey((int)a[0]!) ? new EntityHandle(0x0001_0000u | (uint)(int)a[0]!) : EntityHandle.None,
            "IsAlive" => true,
            _ => throw FakeEngine.Unasked(m),
        });
        IEngineEntities entities = FakeEngine.Of<IEngineEntities>((m, a) => m.Name switch
        {
            "GetEyePosition" => new Vector(0f, 0f, 64f),
            "GetEyeAngles" => new Vector(0f, 0f, 0f),
            _ => throw FakeEngine.Unasked(m),
        });
        var classes = new TFClassTable(new TFBelievedSpeedTests.NoLog(), System.Reflection.DispatchProxy.Create<IEngineFiles, TFBelievedSpeedTests.Scripts>());
        classes.Load();
        _lines = new Lines(() => _hit, () => _startSolid);
        return new TFTargetIdMeters(entities, players, _globals, _lines, people, new Actors(_actors), classes, new Kits(_kit), new Resists(() => _resist));
    }

    private (float Slot, float Health, float Most, float Enemy) Read(TFClass viewer)
    {
        var into = new List<TFMeter>();
        Reader().Read(Bot, viewer, into);
        float M(string name) => into.FirstOrDefault(m => m.Name == name, new TFMeter(name, -1f)).Value;
        return (M(TFTargetIdMeters.Slot), M(TFTargetIdMeters.Health), M(TFTargetIdMeters.Most), M(TFTargetIdMeters.Enemy));
    }

    private static TFActorFacts Facts(TFClass c, TFTeam team, bool disguised = false, TFClass disguiseClass = TFClass.Civilian, TFTeam disguiseTeam = TFTeam.Unassigned,
                                      int disguiseHealth = 0, bool cloaked = false)
        => new(c, team, disguiseClass, disguiseTeam, disguised ? 4 : 0, cloaked ? 1f : 0f, 0, false, false, false, disguised, cloaked, false, DisguiseHealth: disguiseHealth);

    /// <summary>
    /// A Medic's Vaccinator on blast shows it on his own meter (medic.resist 1, round 2); a Spy's HUD has no such meter, and a Medic whose
    /// medigun is no Vaccinator (the source answers -1) shows none.
    /// </summary>
    [Fact]
    public void AVaccinatorMedicReadsItsOwnResist()
    {
        float Resist(TFClass viewer)
        {
            var into = new List<TFMeter>();
            Reader().Read(Bot, viewer, into);
            return into.FirstOrDefault(m => m.Name == TFTargetIdMeters.Resist, new TFMeter("", -1f)).Value;
        }

        _resist = 1;
        (float medic, float spy) = (Resist(TFClass.Medic), Resist(TFClass.Spy));
        _resist = -1;
        Assert.Equal((1f, -1f, -1f), (medic, spy, Resist(TFClass.Medic)));
    }

    /// <summary>A Medic's crosshair on a teammate Soldier at 120 of 200 reads him: slot, 120, 200, not an enemy.</summary>
    [Fact]
    public void AMedicReadsTheTeammateUnderItsCrosshair()
    {
        _people[TargetIndex] = new Person(2, 120, 200);
        _actors[TargetIndex] = Facts(TFClass.Soldier, TFTeam.Red);
        Assert.Equal((3f, 120f, 200f, 0f), Read(TFClass.Medic));
    }

    /// <summary>Nothing under the crosshair, nothing read.</summary>
    [Fact]
    public void NobodyUnderTheCrosshairReadsNothing()
    {
        _people[TargetIndex] = new Person(2, 120, 200);
        _hit = EntityHandle.None;
        Assert.Equal(-1f, Read(TFClass.Medic).Slot);
    }

    /// <summary>An enemy under a Medic's crosshair shows him no health; under a Spy's, the enemy's own.</summary>
    [Fact]
    public void AnEnemysHealthIsTheSpysAlone()
    {
        _people[TargetIndex] = new Person(3, 90, 300);
        _actors[TargetIndex] = Facts(TFClass.HeavyWeapons, TFTeam.Blue);
        Assert.Equal(-1f, Read(TFClass.Medic).Slot);
        Assert.Equal((3f, 90f, 300f, 1f), Read(TFClass.Spy));
    }

    /// <summary>A Medic with the Solemn Vow (see_enemy_health) reads an enemy's health too.</summary>
    [Fact]
    public void TheSolemnVowShowsAnEnemysHealth()
    {
        _people[TargetIndex] = new Person(3, 90, 300);
        _actors[TargetIndex] = Facts(TFClass.HeavyWeapons, TFTeam.Blue);
        _kit.Add(new TFOwnKitItem(TFLoadoutSlot.Melee, 413, "tf_weapon_bonesaw", "tf_weapon_bonesaw", 7, [new TFOwnKitAttribute(TFTargetIdMeters.SeeEnemyHealth, 1f)]));
        Assert.Equal((3f, 90f, 300f, 1f), Read(TFClass.Medic));
    }

    /// <summary>An enemy Spy disguised as a red Soldier shows the Medic his disguise's health against a Soldier's 200, never his own 60.</summary>
    [Fact]
    public void ADisguisedEnemyShowsHisDisguisesHealth()
    {
        _people[TargetIndex] = new Person(3, 60, 125);
        _actors[TargetIndex] = Facts(TFClass.Spy, TFTeam.Blue, disguised: true, TFClass.Soldier, TFTeam.Red, disguiseHealth: 180);
        Assert.Equal((3f, 180f, 200f, 0f), Read(TFClass.Medic));
    }

    /// <summary>A cloaked enemy shows a Spy nothing.</summary>
    [Fact]
    public void ACloakedEnemyShowsNothing()
    {
        _people[TargetIndex] = new Person(3, 60, 125);
        _actors[TargetIndex] = Facts(TFClass.Spy, TFTeam.Blue, cloaked: true);
        Assert.Equal(-1f, Read(TFClass.Spy).Slot);
        _actors[TargetIndex] = Facts(TFClass.Spy, TFTeam.Blue);
        Assert.Equal(3f, Read(TFClass.Spy).Slot);
    }

    /// <summary>A line starting inside a teammate shows nobody; inside an enemy (pressed against the bot), a Spy still reads him.</summary>
    [Fact]
    public void StartingInsideATeammateShowsNobody()
    {
        _startSolid = true;
        _people[TargetIndex] = new Person(2, 120, 200);
        _actors[TargetIndex] = Facts(TFClass.Soldier, TFTeam.Red);
        Assert.Equal(-1f, Read(TFClass.Medic).Slot);
        _people[TargetIndex] = new Person(3, 120, 200);
        _actors[TargetIndex] = Facts(TFClass.Soldier, TFTeam.Blue);
        Assert.Equal(3f, Read(TFClass.Spy).Slot);
    }

    /// <summary>A class that does not read the target ID traces nothing: a Soldier's HUD costs no line.</summary>
    [Fact]
    public void AClassThatDoesNotReadItTracesNothing()
    {
        _people[TargetIndex] = new Person(2, 120, 200);
        Assert.Equal(-1f, Read(TFClass.Soldier).Slot);
        Assert.Equal(0, _lines!.Asked);
        Read(TFClass.Medic);
        Assert.True(_lines!.Asked > 0);
    }
}
