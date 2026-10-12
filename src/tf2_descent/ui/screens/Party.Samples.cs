using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The party window from fakes (the ui-screen loop's sample step). Member
// names are ui.Data (game copy); classes are #Tokens in
// game/mod_tf2_descent/resource/descent_english.txt.

public static class PartySamples
{
    public static DescentPartyMember Member(string name, string classToken, int level,
                                            int life = 120, int lifeMax = 120, bool downed = false,
                                            bool local = false, bool hardcore = false, int distance = 0)
        => new(name, classToken, level, life, lifeMax, downed, local, hardcore, distance, false);

    public static UiScreen Build(UiSampleServices s, IDescentParty party, IDescentPartyActions actions,
                                 MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add(party);
        s.Add(actions);
        return s.Create<Party>();
    }

    /// <summary>Four members: one downed (with the distance caption), one hardcore.</summary>
    [UiSample("four members")]
    private static UiScreen Four(UiSampleServices s)
        => Build(s, new FakePartyCrew
        {
            Members =
            [
                Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true),
                Member("Saul_Gentries", "#Descent_Class_Medic", 11),
                Member("Engineer_88", "#Descent_Class_Engineer", 9, hardcore: true, life: 60, lifeMax: 130),
                Member("Hidden_Dagger", "#Descent_Class_Spy", 10, life: 0, lifeMax: 110, downed: true, distance: 34),
            ],
        }, new FakePartyActions());

    /// <summary>Alone, with a code in hand and one typed into the field.</summary>
    [UiSample("solo, code entered")]
    private static UiScreen Solo(UiSampleServices s)
        => Build(s, new FakePartyCrew
        {
            Members = [Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true)],
        }, new FakePartyActions { PartyCode = "FRIEND-42" });
}

/// <summary>The party of the window and the picker (kept apart from the HUD's FakeParty).</summary>
public sealed class FakePartyCrew : IDescentParty
{
    public DescentPartyMember[] Members { get; init; } = [];
    public int MemberCount => Members.Length;
    public DescentPartyMember Member(int i) => Members[i];
}

/// <summary>The party window's actions: records what the buttons asked for.</summary>
public sealed class FakePartyActions : IDescentPartyActions
{
    public string PartyCode { get; init; } = "BUDDY-7";
    public int Invites { get; private set; }
    public int Leaves { get; private set; }
    public string? Joined { get; private set; }
    public void Invite() => Invites++;
    public void Leave() => Leaves++;
    public void Join(string code) => Joined = code;
}
