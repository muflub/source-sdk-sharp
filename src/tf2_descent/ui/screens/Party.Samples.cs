using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The party window from fakes (the ui-screen loop's sample step). Member
// names are ui.Data (game copy); classes are #Tokens in
// game/mod_tf2_descent/resource/tf2_descent_english.txt.

public static class PartySamples
{
    public static DescentPartyMember Member(string name, string classToken, int level,
                                            int life = 120, int lifeMax = 120, bool downed = false,
                                            bool local = false, bool hardcore = false, int distance = 0, bool offLevel = false)
        => new(name, classToken, level, life, lifeMax, downed, local, hardcore, distance, offLevel);

    public static UiScreen Build(UiSampleServices s, IDescentParty party, IDescentPartyRoster roster, IDescentPartyActions actions,
                                 MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add(party);
        s.Add(roster);
        s.Add(actions);
        return s.Create<Party>();
    }

    /// <summary>Four members, the local hero leading: one downed (with the distance caption), one hardcore, one off the level.</summary>
    [UiSample("four members, leading")]
    private static UiScreen Four(UiSampleServices s)
        => Build(s, new FakePartyCrew
        {
            Members =
            [
                Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true),
                Member("Saul_Gentries", "#Descent_Class_Medic", 11),
                Member("Engineer_88", "#Descent_Class_Engineer", 9, hardcore: true, life: 60, lifeMax: 130),
                Member("Hidden_Dagger", "#Descent_Class_Spy", 10, life: 0, lifeMax: 0, offLevel: true),
            ],
        }, new FakePartyRoster { LeaderIndex = 0, LocalLeads = true, CanInvite = false }, new FakePartyActions());

    /// <summary>A member, not the leader, downed: no Kick, no invite list.</summary>
    [UiSample("member, downed")]
    private static UiScreen AsMember(UiSampleServices s)
        => Build(s, new FakePartyCrew
        {
            Members =
            [
                Member("Saul_Gentries", "#Descent_Class_Medic", 11),
                Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true, downed: true, life: 1, distance: 34),
            ],
        }, new FakePartyRoster { LeaderIndex = 0, CanInvite = false }, new FakePartyActions());

    /// <summary>Alone, with an invitation waiting, and two heroes here to invite (one with a long name).</summary>
    [UiSample("solo, invited, heroes to invite")]
    private static UiScreen Solo(UiSampleServices s)
        => Build(s, new FakePartyCrew
        {
            Members = [Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true)],
        }, new FakePartyRoster { InviterName = "Saul_Gentries", Candidates = ["Merasmus_the_Wizard_of_the_Long_Name", "Bugha"] }, new FakePartyActions());
}

/// <summary>The party of the window and the picker (kept apart from the HUD's FakeParty).</summary>
public sealed class FakePartyCrew : IDescentParty
{
    public DescentPartyMember[] Members { get; init; } = [];
    public int MemberCount => Members.Length;
    public DescentPartyMember Member(int i) => Members[i];
}

/// <summary>The window's leader, invitation and invite list.</summary>
public sealed class FakePartyRoster : IDescentPartyRoster
{
    public int LeaderIndex { get; init; } = -1;
    public bool LocalLeads { get; init; }
    public string InviterName { get; init; } = "";
    public bool CanInvite { get; init; } = true;
    public string[] Candidates { get; init; } = [];
    public int CandidateCount => Candidates.Length;
    public string CandidateName(int i) => Candidates[i];
}

/// <summary>The party window's actions: records what the buttons asked for.</summary>
public sealed class FakePartyActions : IDescentPartyActions
{
    public List<string> Asked { get; } = [];
    public void Invite(int candidate) => Asked.Add("invite " + candidate);
    public void Accept() => Asked.Add("accept");
    public void Decline() => Asked.Add("decline");
    public void Leave() => Asked.Add("leave");
    public void Kick(int member) => Asked.Add("kick " + member);
}
