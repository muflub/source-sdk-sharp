using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

// The HUD and its parts from fakes (the ui-screen loop's sample step): every
// state the in-game HUD is ever in, built through the same container the game
// uses. Item names are ui.Data (game copy, case kept); every #Descent_* token
// lives in game/mod_tf2_descent/resource/descent_english.txt.

public static class DescentHudSamples
{
    public static UiScreen Build(UiSampleServices s, FakeVitals vitals, FakeBelt? belt = null,
        FakeXp? xp = null, FakeParty? party = null, FakeBoss? boss = null,
        FakeCast? cast = null, FakeGearWarn? warn = null, FakeAutomap? map = null)
    {
        s.Add<IDescentVitals>(vitals);
        s.Add<IDescentBelt>(belt ?? new FakeBelt());
        s.Add<IDescentXp>(xp ?? new FakeXp());
        s.Add<IDescentParty>(party ?? new FakeParty());
        s.Add<IDescentBoss>(boss ?? new FakeBoss());
        s.Add<IDescentCastBar>(cast ?? new FakeCast());
        s.Add<IDescentGearWarning>(warn ?? new FakeGearWarn());
        s.Add<IDescentAutomap>(map ?? new FakeAutomap());
        return s.Create<DescentHud>();
    }

    [UiSample("healthy, party of two, map revealed")]
    private static UiScreen Full(UiSampleServices s) => Build(s,
        new FakeVitals(), new FakeBelt(), new FakeXp(),
        new FakeParty
        {
            Members =
            [
                new("Bugha", "#Descent_Class_Soldier", 18, 140, 140, false, true, false, 0, false),
                new("Merasmus", "#Descent_Class_Spy", 17, 88, 110, false, false, false, 340, false),
            ],
        });

    [UiSample("low life, broken belt, boss engaged, casting, gear broken")]
    private static UiScreen Crisis(UiSampleServices s) => Build(s,
        new FakeVitals { Life = 22, Mana = 30 },
        new FakeBelt { SlotReady = false, Names = ["Healing Buff", "", "", ""], Counts = [3, 0, 0, 0] },
        new FakeXp { AttributePoints = 2 },
        new FakeParty
        {
            Members =
            [
                new("Bugha", "#Descent_Class_Soldier", 18, 22, 140, false, true, true, 0, false),
                new("Sentry", "#Descent_Class_Engineer", 16, 0, 130, true, false, false, 128, false),
            ],
        },
        new FakeBoss { Present = true, Name = "Sir Nukesalot", Life = 1600, LifeMax = 4200, PhaseCount = 3, Phase = 1 },
        new FakeCast { Casting = true, SpellName = "Fireball", Fraction = 0.62f },
        new FakeGearWarn { Present = true, ItemName = "Deadhead", Fraction = 0.04f },
        new FakeAutomap());

    [UiSample("interrupted cast")]
    private static UiScreen InterruptedCast(UiSampleServices s) => Build(s,
        new FakeVitals { Mana = 8 },
        cast: new FakeCast { Casting = true, SpellName = "Chain Lightning", Fraction = 0.3f, Interrupted = true });

    [UiSample("solo, no hero yet")]
    private static UiScreen Bare(UiSampleServices s) => Build(s,
        new FakeVitals { HasHero = false, Life = 0, Mana = 0 },
        new FakeBelt { Names = ["", "", "", ""], Ready = true },
        new FakeXp { Xp = 0, SkillPoints = 0 });
}

// ---- fakes -------------------------------------------------------------------

public sealed class FakeVitals : IDescentVitals
{
    public bool HasHero { get; init; } = true;
    public int Life { get; init; } = 140;
    public int LifeMax { get; init; } = 140;
    public int Mana { get; init; } = 96;
    public int ManaMax { get; init; } = 120;
}

public sealed class FakeBelt : IDescentBelt
{
    public string[] Names { get; init; } = ["Healing Buff", "Healing Buff", "Vitality", ""];
    public int[] Counts { get; init; } = [4, 2, 1, 0];
    public bool SlotReady { get; init; } = true;
    public bool Ready { get; init; } = true;
    public int SlotCount => 4;
    public string SlotName(int i) => Names[i];
    public int SlotCountOf(int i) => Counts[i];
}

public sealed class FakeXp : IDescentXp
{
    public int Level { get; init; } = 18;
    public long Xp { get; init; } = 9_420;
    public long XpToNext { get; init; } = 12_800;
    public int AttributePoints { get; init; }
    public int SkillPoints { get; init; } = 1;
}

public sealed class FakeParty : IDescentParty
{
    public DescentPartyMember[] Members { get; init; } = [];
    public int MemberCount => Members.Length;
    public DescentPartyMember Member(int i) => Members[i];
}

public sealed class FakeBoss : IDescentBoss
{
    public bool Present { get; init; }
    public string Name { get; init; } = "";
    public int Life { get; init; }
    public int LifeMax { get; init; }
    public int PhaseCount { get; init; } = 1;
    public int Phase { get; init; }
}

public sealed class FakeCast : IDescentCastBar
{
    public bool Casting { get; init; }
    public string SpellName { get; init; } = "";
    public float Fraction { get; init; }
    public bool Interrupted { get; init; }
}

public sealed class FakeGearWarn : IDescentGearWarning
{
    public bool Present { get; init; }
    public string ItemName { get; init; } = "";
    public float Fraction { get; init; }
}

/// <summary>A revealed three-room stretch of level 3 with a stair, a corpse, a
/// party member, an open chest and a locked one (the lock on the map means
/// locked, plan §2.2).</summary>
public sealed class FakeAutomap : IDescentAutomap
{
    // Room rects on the generator's 1024 grid.
    private readonly (float X, float Y, float W, float H)[] Rooms =
    [
        (96, 448, 224, 160),   // entry hall
        (416, 384, 256, 256),  // crypt
        (768, 448, 160, 160),  // shrine room
        (448, 96, 192, 128),   // upper guardroom (other storey)
    ];

    public int RoomCount => Rooms.Length;
    public float RoomX(int r) => Rooms[r].X;
    public float RoomY(int r) => Rooms[r].Y;
    public float RoomW(int r) => Rooms[r].W;
    public float RoomH(int r) => Rooms[r].H;
    public int RoomStorey(int r) => r == 3 ? 1 : 2;
    public bool RoomCurrentStorey(int r) => r != 3;

    private readonly (float X, float Y, float W, float H)[] Doors =
    [
        (320, 512, 96, 4),
        (672, 512, 96, 4),
        (512, 224, 4, 160),
    ];

    public int DoorStubCount => Doors.Length;
    public float DoorStubX(int i) => Doors[i].X;
    public float DoorStubY(int i) => Doors[i].Y;
    public float DoorStubW(int i) => Doors[i].W;
    public float DoorStubH(int i) => Doors[i].H;

    private readonly DescentMarker[] Markers =
    [
        new(DescentMapMarker.Stairs, 2, 848, 528, 2),
        new(DescentMapMarker.Corpse, 1, 480, 448, 2),
        new(DescentMapMarker.ShrineUnused, 2, 800, 480, 2),
        new(DescentMapMarker.ContainerLocked, 1, 608, 576, 2),
        new(DescentMapMarker.Container, 1, 448, 576, 2),
        new(DescentMapMarker.Party, 1, 544, 512, 2),
    ];

    public int MarkerCount => Markers.Length;
    public DescentMarker Marker(int i) => Markers[i];
    public int StoreyCount => 2;
    public int HeroStorey => 2;
    public float HeroX => 180;
    public float HeroY => 512;
    public float HeroHeadingDeg => 90;
    public int FocusStorey => 2;
}
