using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The skill window from fakes (the ui-screen loop's sample step): the four
// tabs, the tier ladder with its prerequisite links, the Mercenary groups.
// Every #Descent_* copy lives in docs/plans/tokens-skills.tsv and lands in
// game/mod_tf2_descent/resource/descent_english.txt; synergy hints are
// ui.Data (server copy, case kept).

public static class SkillTreeSamples
{
    public static UiScreen Build(UiSampleServices s, FakeSkillTree skills, FakeSkillHover? hover = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IDescentSkills>(skills);
        s.Add<IDescentSkillActions>(new FakeSkillActions());
        // The hint fake answers both keys (the screen's hover subject and the
        // HintTip part's content): one view-model, as the game's DI wires it.
        // The resting samples leave it inactive — an inactive tip draws
        // nothing, which is the pointer-less state.
        s.Add<IDescentSkillHover>(hover ??= new FakeSkillHover());
        s.Add<IDescentHintTip>(hover);
        return s.Create<SkillTree>();
    }

    /// <summary>A Soldier at 24 with 3 points: the Ordnance ladder mid-build, prereq links visible.</summary>
    [UiSample("soldier-mid")]
    private static UiScreen SoldierMid(UiSampleServices s) => Build(s, SoldierTree(3, 0));

    /// <summary>A fresh character: tier 1 open, everything above it level-locked.</summary>
    [UiSample("fresh")]
    private static UiScreen Fresh(UiSampleServices s) => Build(s, SoldierTree(0, 0, fresh: true));

    /// <summary>One card buyable at the top tier with the points to spend: the accent frame.</summary>
    [UiSample("capstone-ready")]
    private static UiScreen CapstoneReady(UiSampleServices s)
    {
        var tree = SoldierTree(1, 0);
        tree.BuyCapstone();
        return Build(s, tree);
    }

    /// <summary>The Mercenary tab, grouped under Capacity / Sustain / Fortune; the Fortune rank level-locked.</summary>
    [UiSample("mercenary-heavy")]
    private static UiScreen MercenaryHeavy(UiSampleServices s)
    {
        var tree = SoldierTree(1, 3);
        return Build(s, tree);
    }


    /// <summary>The pointer on Frag Out: the hint popup hangs at the card's
    /// edge with the rank description (the fake names the card; a real
    /// pointer reports through <see cref="IDescentSkillActions.Hover"/>).</summary>
    [UiSample("card-hovered")]
    private static UiScreen CardHovered(UiSampleServices s)
        => Build(s, SoldierTree(3, 0), hover: new FakeSkillHover { HoveredSkill = 102 });
    // ---- the Soldier's four tabs (Appendix A) --------------------------------

    private static FakeSkillTree SoldierTree(int points, int tab, bool fresh = false)
        => new()
        {
            HeroLevel = fresh ? 2 : 24,
            SkillPoints = points,
            SelectedTab = tab,
            Tabs =
            [
                new FakeSkill
                {
                    TitleToken = "#Descent_Tab_Ordnance",
                    Skills =
                    [
                        Sk(101, "#Descent_Skill_RocketJump", DescentSkillKind.Action, 3, 5, 1, 1, 1, fresh: fresh),
                        Sk(102, "#Descent_Skill_FragOut", DescentSkillKind.Upgrade, 1, 4, 0, 2, fresh ? 6 : 8, fresh: fresh),
                        Sk(103, "#Descent_Skill_Splash", DescentSkillKind.Upgrade, 0, 1, 0, 2, fresh ? 6 : 8, fresh: fresh, prereqMet: false),
                        Sk(104, "#Descent_Skill_RocketFamily", DescentSkillKind.UpgradeFamily, 0, 1, 0, 3, fresh ? 12 : 12, fresh: fresh, family: "#Descent_Skill_FamilyBullet"),
                        Sk(105, "#Descent_Skill_Grenadier", DescentSkillKind.Capstone, 0, 1, 0, 4, fresh ? 18 : 18, fresh: fresh, prereqMet: false),
                    ],
                    PrereqIds = [[], [101], [101], [102], [103, 104]],
                    Synergy = [["Rocket Jump carries farther at speed"]],
                },
                new FakeSkill
                {
                    TitleToken = "#Descent_Tab_Airborne",
                    Skills =
                    [
                        Sk(201, "#Descent_Skill_DoubleJump", DescentSkillKind.Action, 1, 3, 0, 1, 1, fresh: fresh),
                        Sk(202, "#Descent_Skill_AirDash", DescentSkillKind.Action, 1, 1, 0, 2, fresh ? 5 : 7, fresh: fresh),
                        Sk(203, "#Descent_Skill_SteadyLand", DescentSkillKind.Utility, 1, 1, 0, 1, 1, fresh: fresh),
                        Sk(204, "#Descent_Skill_HeightBoost", DescentSkillKind.Upgrade, 0, 5, 0, 3, fresh ? 11 : 11, fresh: fresh, prereqMet: false),
                    ],
                    PrereqIds = [[], [201], [], [202]],
                },
                new FakeSkill
                {
                    TitleToken = "#Descent_Tab_Command",
                    Skills =
                    [
                        Sk(301, "#Descent_Skill_RallyFlag", DescentSkillKind.Toggle, 1, 1, 0, 1, 1, "30 s", fresh: fresh),
                        Sk(302, "#Descent_Skill_BattleDrum", DescentSkillKind.Toggle, 0, 3, 0, 2, fresh ? 7 : 9, fresh: fresh),
                        Sk(303, "#Descent_Skill_Leadership", DescentSkillKind.Capstone, 0, 1, 0, 3, fresh ? 14 : 14, fresh: fresh, prereqMet: false),
                    ],
                    PrereqIds = [[], [301], [302]],
                },
                new FakeMercTab(),
            ],
        };

    private static DescentSkill Sk(int id, string name, DescentSkillKind kind, int rank, int max, int bonus,
        int tier, int tierLevel, string cooldown = "", bool fresh = false, bool prereqMet = true, string family = "")
        => new(id, name, kind, fresh && tier > 1 ? 0 : rank, max, bonus, tier, tierLevel, 0,
               Unlocked: !fresh || tier <= 1,
               // Buyable: the card can take a rank right now (points spend there).
               Buyable: rank < max && prereqMet && (!fresh || tier <= 1) && tierLevel <= (fresh ? 2 : 24),
               "", cooldown, family, prereqMet);

    /// <summary>The Mercenary tab: three heading groups, the top one level-locked.</summary>
    private sealed class FakeMercTab : IDescentSkillTab, IDescentSkillGroups
    {
        private readonly DescentSkill[] _skills =
        [
            new(401, "#Descent_Skill_PackMule", DescentSkillKind.Upgrade, 3, 5, 1, 1, 1, 0, true, false, "", "", "", true),
            new(402, "#Descent_Skill_BeltPouches", DescentSkillKind.Upgrade, 1, 3, 0, 1, 1, 0, true, true, "", "", "", true),
            new(403, "#Descent_Skill_QuickDraw", DescentSkillKind.UpgradeTriggered, 2, 3, 0, 2, 8, 0, true, true, "", "", "", true),
            new(404, "#Descent_Skill_ThickSkin", DescentSkillKind.Upgrade, 0, 5, 0, 2, 8, 0, true, true, "", "", "", true),
            new(405, "#Descent_Skill_SecondWind", DescentSkillKind.UpgradeTriggered, 1, 3, 0, 2, 8, 0, true, true, "", "", "", true),
            new(406, "#Descent_Skill_FindersKeepers", DescentSkillKind.Upgrade, 0, 3, 0, 3, 18, 0, false, false, "", "", "", false),
            new(407, "#Descent_Skill_Greed", DescentSkillKind.Utility, 0, 3, 0, 3, 21, 0, false, false, "", "", "", false),
        ];

        public string TitleToken => "#Descent_Tab_Mercenary";
        public int SkillCount => _skills.Length;
        public DescentSkill Skill(int i) => _skills[i];
        public int PrereqCount(int skillIndex) => 0;
        public int PrereqId(int skillIndex, int i) => 0;
        public int SynergyCount(int skillIndex) => 0;
        public string SynergyText(int skillIndex, int i) => "";
        public string GroupTitleToken(int group) => group switch
        {
            1 => "#Descent_Skill_Group_Capacity",
            2 => "#Descent_Skill_Group_Sustain",
            _ => "#Descent_Skill_Group_Fortune",
        };
    }
}

// ---- fakes -------------------------------------------------------------------

public sealed class FakeSkillTree : IDescentSkills, IDescentSkillTabSelection
{
    public string ClassToken { get; init; } = "#Descent_Class_Soldier";
    public int HeroLevel { get; init; } = 24;
    public int SkillPoints { get; init; } = 3;
    public IDescentSkillTab[] Tabs { get; init; } = [];
    public int SelectedTab { get; init; }
    public int TabCount => Tabs.Length;
    public IDescentSkillTab Tab(int i) => Tabs[i];

    /// <summary>Open the Ordnance capstone for the "capstone-ready" sample.</summary>
    public void BuyCapstone()
    {
        var tab = (FakeSkill)Tabs[0];
        tab.Skills[4] = tab.Skills[4] with { Unlocked = true, Buyable = true, PrereqMet = true, HeroLevel = 24 };
    }
}

public sealed class FakeSkill : IDescentSkillTab
{
    public string TitleToken { get; init; } = "#Descent_Tab_Ordnance";
    public DescentSkill[] Skills { get; set; } = [];
    public int[][] PrereqIds { get; init; } = [];
    public string[][] Synergy { get; init; } = [];

    public int SkillCount => Skills.Length;
    public DescentSkill Skill(int i) => Skills[i];
    public int PrereqCount(int skillIndex) => Ids(skillIndex).Length;
    public int PrereqId(int skillIndex, int i) => Ids(skillIndex)[i];
    public int SynergyCount(int skillIndex) => Texts(skillIndex).Length;
    public string SynergyText(int skillIndex, int i) => Texts(skillIndex)[i];

    private int[] Ids(int skillIndex) => skillIndex < PrereqIds.Length ? PrereqIds[skillIndex] : [];
    private string[] Texts(int skillIndex) => skillIndex < Synergy.Length ? Synergy[skillIndex] : [];
}

public sealed class FakeSkillActions : IDescentSkillActions
{
    public List<int> Buys { get; } = [];
    public List<int> Hovers { get; } = [];
    public void Buy(int skillId) => Buys.Add(skillId);
    public void Hover(int skillId) => Hovers.Add(skillId);
}

/// <summary>The skill hint's content — the card's description as the
/// view-model answers it for the id the pointer names. Skill descriptions
/// are server copy, so the fake speaks ui.Data lines.</summary>
public sealed class FakeSkillHover : IDescentSkillHover
{
    /// <summary>Set by a sample to force the popup; -1 keeps it inactive.</summary>
    public int HoveredSkill { get; set; } = -1;

    public bool Active => HoveredSkill >= 0;
    public string TitleToken => string.Empty;
    public string TitleData => "Frag Out";
    public string SubtitleData => "rank 1 / 4";
    public int LineCount => 1;
    public string LineToken(int i) => string.Empty;
    public string LineData(int i) => "Explosions you cause lob further and stagger.";
    public string LineGood(int i) => "+15%";
    public bool LineGoodNegative(int i) => false;
    public string ValueToken(int i) => string.Empty;
    public string ValueData(int i) => string.Empty;
    public bool ValueGood(int i) => true;
}
