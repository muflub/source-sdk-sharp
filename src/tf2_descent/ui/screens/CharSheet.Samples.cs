using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The character sheet from fakes (the ui-screen loop's sample step): the
/// states C.1's sheet can be in — a fresh level-1 hero, a mid-spend level 18,
/// an attribute at its gold cap, a cursed affix dipping a stat below base, a
/// resistance at its cap — plus the window at its minimum and wide sizes.
/// </summary>
public static class CharSheetSamples
{
    private static UiScreen Build(UiSampleServices s, FakeSheet sheet, MemoryWindowStore? store = null,
                                  FakeSheetHover? hover = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add<IDescentSheet>(sheet);
        s.Add<IDescentSheetActions>(new FakeSheetActions());
        // The hover fake is always registered (its content forces the popup
        // to draw in the hovered samples); the resting samples leave it
        // inactive, which is the pointer-less state: nothing is drawn. One
        // instance answers both keys: the screen reads it as the sheet's
        // hover subject, and the container builds the shared HintTip part
        // from the same view-model.
        hover ??= new FakeSheetHover();
        s.Add<IDescentSheetHover>(hover);
        s.Add<IDescentHintTip>(hover);
        return s.Create<CharSheet>();
    }

    private static FakeSheet MidSheet()
        => new()
        {
            Level = 18,
            Xp = 41_300,
            XpToNext = 8_200,
            Australium = 12,
            AttributePoints = 5,
            Attrs =
            [
                new("#Descent_Attr_Brawn", 45, 62, 62, 0), // spent: base grew with it
                new("#Descent_Attr_Smarts", 30, 30, 62, 0),
                new("#Descent_Attr_Aim", 28, 41, 62, 2), // 2 staged by pending +s
                new("#Descent_Attr_Grit", 62, 62, 62, 0), // at cap already
            ],
            Life = 96,
            LifeMax = 140,
            Mana = 88,
            ManaMax = 88,
            Armour = 118,
            CritPercent = 17,
            DamageMin = 44,
            DamageMax = 58,
        };

    [UiSample("fresh 1 Soldier")]
    private static UiScreen FreshSoldier(UiSampleServices s)
        => Build(s, new FakeSheet { Level = 1, Xp = 0, XpToNext = 400, AttributePoints = 0 });

    [UiSample("mid-spend L18")]
    private static UiScreen MidSpendL18(UiSampleServices s) => Build(s, MidSheet());

    /// <summary>The preview state: points staged by the + buttons and not yet
    /// applied — the Now plates show base+pending in preview ink, the Life and
    /// Mana plates their projected maxima, and the commit row is up with the
    /// staged count.</summary>
    [UiSample("staged preview")]
    private static UiScreen StagedPreview(UiSampleServices s)
        => Build(s, new FakeSheet
        {
            Level = 18,
            Xp = 41_300,
            XpToNext = 8_200,
            Australium = 12,
            AttributePoints = 5,
            Attrs =
            [
                new("#Descent_Attr_Brawn", 45, 62, 62, 1),   // now reads 63
                new("#Descent_Attr_Smarts", 30, 30, 62, 2),  // now reads 32; mana 88 -> 100
                new("#Descent_Attr_Aim", 28, 41, 62, 2),     // now reads 43
                new("#Descent_Attr_Grit", 62, 62, 62, 0),    // at cap: no +
            ],
            Life = 96,
            LifeMax = 140,   // + 1*2 + 0*6 staged -> 142
            Mana = 88,
            ManaMax = 88,
            Armour = 118,
            CritPercent = 17,
            DamageMin = 44,
            DamageMax = 58,
        });

    [UiSample("capped")]
    private static UiScreen Capped(UiSampleServices s)
        => Build(s, new FakeSheet
        {
            Level = 30,
            Xp = 120_000,
            XpToNext = 15_000,
            Australium = 40,
            AttributePoints = 3,
            Attrs =
            [
                new("#Descent_Attr_Brawn", 45, 62, 62, 0),
                new("#Descent_Attr_Smarts", 40, 40, 62, 0),
                new("#Descent_Attr_Aim", 35, 35, 62, 0),
                new("#Descent_Attr_Grit", 62, 62, 62, 0),
            ],
            Armour = 210,
            DamageMin = 52,
            DamageMax = 68,
        });

    [UiSample("cursed")]
    private static UiScreen Cursed(UiSampleServices s)
        => Build(s, new FakeSheet
        {
            Level = 12,
            Xp = 18_400,
            XpToNext = 6_100,
            AttributePoints = 0,
            Attrs =
            [
                new("#Descent_Attr_Brawn", 30, 30, 46, 0),
                new("#Descent_Attr_Smarts", 34, 22, 46, 0), // the affix dips it below base
                new("#Descent_Attr_Aim", 26, 26, 46, 0),
                new("#Descent_Attr_Grit", 30, 30, 46, 0),
            ],
            Life = 60,
            LifeMax = 104,
            Mana = 41,
            ManaMax = 70,
            Armour = 64,
            CritPercent = 12,
            DamageMin = 30,
            DamageMax = 41,
        });

    [UiSample("resists mixed")]
    private static UiScreen ResistsMixed(UiSampleServices s)
        => Build(s, new FakeSheet
        {
            Level = 22,
            Xp = 66_000,
            XpToNext = 9_800,
            Australium = 3,
            AttributePoints = 2,
            Life = 150,
            LifeMax = 150,
            Mana = 95,
            ManaMax = 95,
            Armour = 132,
            CritPercent = 14,
            DamageMin = 38,
            DamageMax = 51,
            ResistPercents = [45, 18, 62, 75],
            HasPartyReviveHint = true,
        });

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s, MidSheet(), new MemoryWindowStore().With("charsheet", new UiWindowPlacement(200, 120, 10, 10)));

    [UiSample("dragged wide")]
    private static UiScreen Wide(UiSampleServices s)
        => Build(s, MidSheet(), new MemoryWindowStore().With("charsheet", new UiWindowPlacement(140, 80, 1_400, 760)));

    /// <summary>The pointer on the Brawn row (the fake names the row and
    /// forces its content; a real pointer reports it through
    /// <see cref="IDescentSheetActions.Hover"/>, and the view-model answers
    /// here): the hint popup hangs at the row's edge.</summary>
    [UiSample("attribute hovered")]
    private static UiScreen AttributeHovered(UiSampleServices s)
        => Build(s, MidSheet(), hover: new FakeSheetHover { HoverAttribute = 0 });

    /// <summary>The pointer on the Armour plate — the derived-plate arm of
    /// the same popup, hung from the right-hand column.</summary>
    [UiSample("combat plate hovered")]
    private static UiScreen PlateHovered(UiSampleServices s)
        => Build(s, MidSheet(), hover: new FakeSheetHover { HoverDerived = 2 });
}

/// <summary>
/// The sheet's fixed answers; every member is read by the screen
/// (UiSourceUseTests). The pending previews use the fake's own per-point
/// weights (Grit feeds life, Smarts mana, Brawn a little of each): the real
/// formula is the server's view-model's, and the screen only prints what this
/// answers, so the weights here only have to move the plate when staging.
/// </summary>
public sealed class FakeSheet : IDescentSheet
{
    public string HeroName { get; init; } = "Blastmaw";
    public string ClassToken { get; init; } = "#Descent_Class_Soldier";
    public int Level { get; init; } = 1;
    public long Xp { get; init; }
    public long XpToNext { get; init; } = 400;
    public long Australium { get; init; }
    public int AttributePoints { get; init; }
    public DescentAttribute[] Attrs { get; init; } =
    [
        new("#Descent_Attr_Brawn", 30, 30, 46, 0),
        new("#Descent_Attr_Smarts", 30, 30, 46, 0),
        new("#Descent_Attr_Aim", 30, 30, 46, 0),
        new("#Descent_Attr_Grit", 30, 30, 46, 0),
    ];

    public int Life { get; init; } = 60;
    public int LifeMax { get; init; } = 60;
    public int Mana { get; init; } = 50;
    public int ManaMax { get; init; } = 50;
    public int Armour { get; init; } = 25;
    public int CritPercent { get; init; } = 10;
    public int DamageMin { get; init; } = 18;
    public int DamageMax { get; init; } = 27;
    public int[] ResistPercents { get; init; } = [0, 0, 0, 0];
    public bool HasPartyReviveHint { get; init; }

    public string Name => HeroName;
    public int AttributeCount => Attrs.Length;
    public DescentAttribute Attribute(int i) => Attrs[i];
    public int ResistCount => ResistPercents.Length;
    public string ResistLabel(int i) => i switch
    {
        0 => "#Descent_Type_Fire",
        1 => "#Descent_Type_Blast",
        2 => "#Descent_Type_Bullet",
        _ => "#Descent_Type_Shock",
    };

    public int ResistPercent(int i) => ResistPercents[i];
    public int ResistCap => 75;

    public int PendingLifeMax =>
        LifeMax + Attrs[3].Pending * 6 + Attrs[0].Pending * 2;   // Grit, Brawn
    public int PendingManaMax => ManaMax + Attrs[1].Pending * 6; // Smarts
}

/// <summary>Records the sheet's staged-spend calls so a test can assert the
/// + / Apply / Reset contract.</summary>
public sealed class FakeSheetActions : IDescentSheetActions
{
    public readonly List<(int Index, int Count)> Stages = [];
    public int Applies { get; private set; }
    public int Resets { get; private set; }

    /// <summary>Every pointer report the screen made, for the contract test.</summary>
    public readonly List<(int Attribute, int Derived)> Hovers = [];

    public void Stage(int attributeIndex, int count) => Stages.Add((attributeIndex, count));
    public void Apply() => Applies++;
    public void Reset() => Resets++;
    public void Hover(int attributeIndex, int derivedIndex) => Hovers.Add((attributeIndex, derivedIndex));
}

/// <summary>
/// The sheet's hint content — the view-model's answers for the nine hoverable
/// rows (four attributes, five combat plates). Which row is hovered is a settable
/// property: a sample forces one, a real host's view-model sets it from the
/// screen's <see cref="IDescentSheetActions.Hover"/> report. Detail copy is
/// game knowledge the server side owns; the fake speaks in tokens the mod's
/// language file carries, exactly as the host would.
/// </summary>
public sealed class FakeSheetHover : IDescentSheetHover
{
    // Instance tables: SSUI005 keeps mutable static state out of UI code.
    private readonly string[] AttrDetails =
    [
        "#Descent_Attr_Brawn_Detail", "#Descent_Attr_Smarts_Detail",
        "#Descent_Attr_Aim_Detail", "#Descent_Attr_Grit_Detail",
    ];

    private readonly string[] PlateTitles =
    [
        "#Descent_Hud_Life", "#Descent_Hud_Mana", "#Descent_Sheet_Armour",
        "#Descent_Sheet_Crit", "#Descent_Sheet_Damage",
    ];

    private readonly string[] PlateDetails =
    [
        "#Descent_Sheet_Life_Detail", "#Descent_Sheet_Mana_Detail",
        "#Descent_Sheet_Armour_Detail", "#Descent_Sheet_Crit_Detail",
        "#Descent_Sheet_Damage_Detail",
    ];

    /// <summary>Set by a sample to force the popup; -1 with
    /// <see cref="HoverDerived"/> keeps the tip inactive.</summary>
    public int HoverAttribute { get; set; } = -1;
    public int HoverDerived { get; set; } = -1;

    /// <summary>The staged gain the attribute hint previews ("+2"), matching
    /// what the Now plate will read; null for the plates.</summary>
    public string? StagedGain { get; init; } = "+2";

    public bool Active => HoverAttribute >= 0 || HoverDerived >= 0;

    public string TitleToken => HoverAttribute >= 0 ? AttrName(HoverAttribute)
                                                     : PlateTitles[HoverDerived];
    public string TitleData => string.Empty;
    public string SubtitleData => HoverAttribute >= 0 ? "45 / 62" : null!;
    public int LineCount => 1;
    public string LineToken(int i) => HoverAttribute >= 0 ? AttrDetails[HoverAttribute] : PlateDetails[HoverDerived];
    public string LineData(int i) => string.Empty;
    public string LineGood(int i) => HoverAttribute >= 0 ? StagedGain! : null!;
    public bool LineGoodNegative(int i) => false;
    public string ValueToken(int i) => string.Empty;
    public string ValueData(int i) => string.Empty;
    public bool ValueGood(int i) => true;

    private static string AttrName(int i) => i switch
    {
        0 => "#Descent_Attr_Brawn",
        1 => "#Descent_Attr_Smarts",
        2 => "#Descent_Attr_Aim",
        _ => "#Descent_Attr_Grit",
    };
}
