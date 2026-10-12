using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The downed overlay from fakes (the ui-screen loop's sample step): the two
// R26 states that break a dying screen — alone with the clock running, and
// mid-revive. Reuses the HUD's FakeParty for who is where; the downed state
// itself is FakeDownedState (a distinct name so the HUD assembly's fakes
// stay untouched).

public static class DownedSamples
{
    public static UiScreen Build(UiSampleServices s, FakeDownedState downed, FakeParty party)
    {
        s.Add<IDescentDowned>(downed);
        s.Add<IDescentParty>(party);
        return s.Create<Downed>();
    }

    [UiSample("bleeding out, alone")]
    private static UiScreen BleedingOutAlone(UiSampleServices s) => Build(s,
        new FakeDownedState { Downed = true, BleedOutSecondsLeft = 14.4f, BleedOutTotal = 30f,
            NearestMemberName = "Merasmus", NearestMemberDistance = 620 },
        new FakeParty
        {
            Members =
            [
                new("Bugha", "#Descent_Class_Soldier", 18, 0, 140, true, true, false, 0, false),
                new("Merasmus", "#Descent_Class_Spy", 17, 66, 110, false, false, false, 620, false),
                new("Shortround", "#Descent_Class_Medic", 16, 96, 120, false, false, false, 780, true),
            ],
        });

    [UiSample("being revived, 60 percent")]
    private static UiScreen BeingRevived(UiSampleServices s) => Build(s,
        new FakeDownedState { Downed = true, BleedOutSecondsLeft = 6.2f, BleedOutTotal = 30f,
            BeingRevived = true, ReviverName = "Shortround", ReviveFraction = 0.6f,
            NearestMemberName = "Shortround", NearestMemberDistance = 3 },
        new FakeParty
        {
            Members =
            [
                new("Bugha", "#Descent_Class_Soldier", 18, 0, 140, true, true, false, 0, false),
                new("Merasmus", "#Descent_Class_Spy", 17, 66, 110, false, false, false, 240, false),
                new("Shortround", "#Descent_Class_Medic", 16, 96, 120, false, false, false, 3, false),
            ],
        });
}

/// <summary>The downed state (R26): the clock, and the reviver when one is on you.</summary>
public sealed class FakeDownedState : IDescentDowned
{
    public bool Downed { get; init; }
    public float BleedOutSecondsLeft { get; init; }
    public float BleedOutTotal { get; init; } = 30f;
    public bool BeingRevived { get; init; }
    public string ReviverName { get; init; } = "";
    public float ReviveFraction { get; init; }
    public string NearestMemberName { get; init; } = "";
    public int NearestMemberDistance { get; init; }
}
