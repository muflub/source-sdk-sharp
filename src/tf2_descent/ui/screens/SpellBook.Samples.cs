using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The spellbook from fakes (the ui-screen loop's sample step): the two-column
// book — known spells with their rank pips and Smarts gate, scroll stacks as
// chips — and the nothing-known empty state. Every #Descent_* copy lives in
// docs/plans/tokens-skills.tsv; the mana costs are ui.Data (server copy).

public static class SpellBookSamples
{
    private static UiScreen Build(UiSampleServices s, FakeSpells spells, FakeSpellHover? hover = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IDescentSpells>(spells);
        s.Add<IDescentSpellActions>(new FakeSpellActions());
        // The hint fake answers both keys (the screen's hover subject and the
        // HintTip part's content): one view-model, as the game's DI wires it.
        // The resting samples leave it inactive — an inactive tip draws
        // nothing, which is the pointer-less state.
        s.Add<IDescentSpellHover>(hover ??= new FakeSpellHover());
        s.Add<IDescentHintTip>(hover);
        return s.Create<SpellBook>();
    }

    /// <summary>Two known spells, the second one inactive: its Smarts gate turned red.</summary>
    [UiSample("two-known-one-inactive")]
    private static UiScreen TwoKnownOneInactive(UiSampleServices s) => Build(s, new FakeSpells([

        new("#Descent_Spell_Fireball", 2, 3, 18, true, "32", false, 0, 0),
        new("#Descent_Spell_ChainLightning", 1, 5, 30, false, "48", false, 0, 0),
    ]));


    /// <summary>One known spell and three scroll stacks, one stack at its maximum.</summary>
    [UiSample("scrolls-stocked")]
    private static UiScreen ScrollsStocked(UiSampleServices s) => Build(s, new FakeSpells([

        new("#Descent_Spell_FrostNova", 3, 3, 21, true, "40", false, 0, 0),
        new("#Descent_Scroll_Fireball", 1, 1, 0, true, "", true, 7, 10),
        new("#Descent_Scroll_Frost", 1, 1, 0, true, "", true, 10, 10),
        new("#Descent_Scroll_Identify", 1, 1, 0, true, "", true, 3, 10),
    ]));


    /// <summary>A character who has read no tomes and holds no scrolls.</summary>
    [UiSample("empty")]
    private static UiScreen Empty(UiSampleServices s) => Build(s, new FakeSpells([]));

    /// <summary>The pointer on the Fireball row (index 0 of the flat list):
    /// the hint popup hangs at the row's edge with the spell description
    /// (the fake names the row; a real pointer reports through
    /// <see cref="IDescentSpellActions.Hover"/>).</summary>
    [UiSample("spell-hovered")]
    private static UiScreen SpellHovered(UiSampleServices s)
        => Build(s, new FakeSpells([
            new("#Descent_Spell_Fireball", 2, 3, 18, true, "32", false, 0, 0),
            new("#Descent_Spell_ChainLightning", 1, 5, 30, false, "48", false, 0, 0),
        ]), hover: new FakeSpellHover { HoveredSpell = 0 });

    /// <summary>The pointer on the first scroll chip (flat index 1, reported
    /// as -2): the same popup on the chip grid.</summary>
    [UiSample("scroll-hovered")]
    private static UiScreen ScrollHovered(UiSampleServices s)
        => Build(s, new FakeSpells([
            new("#Descent_Spell_FrostNova", 3, 3, 21, true, "40", false, 0, 0),
            new("#Descent_Scroll_Fireball", 1, 1, 0, true, "", true, 7, 10),
            new("#Descent_Scroll_Frost", 1, 1, 0, true, "", true, 10, 10),
            new("#Descent_Scroll_Identify", 1, 1, 0, true, "", true, 3, 10),
        ]), hover: new FakeSpellHover { HoveredSpell = -2 });
}

/// <summary>The book's contents as a flat list: known spells and scroll stacks side by side.</summary>
public sealed class FakeSpells(params DescentSpell[] items) : IDescentSpells
{
    private readonly DescentSpell[] _items = items;

    public int SpellCount => _items.Length;

    public DescentSpell Spell(int i) => _items[i];
}

/// <summary>Records the book's pointer reports.</summary>
public sealed class FakeSpellActions : IDescentSpellActions
{
    public List<int> Hovers { get; } = [];
    public void Hover(int index) => Hovers.Add(index);
}

/// <summary>The spell hint's content — the spell's description as the
/// view-model answers it for the row the pointer names. Spell descriptions
/// are server copy, so the fake speaks ui.Data lines.</summary>
public sealed class FakeSpellHover : IDescentSpellHover
{
    /// <summary>Set by a sample to force the popup; -1 (no row) keeps it
    /// inactive. A negative number names a scroll chip (<c>-i - 1</c>), so
    /// activity is "not -1", not "not negative".</summary>
    public int HoveredSpell { get; set; } = -1;

    public bool Active => HoveredSpell != -1;

    public string TitleToken => string.Empty;
    public string TitleData => "Fireball";
    public string SubtitleData => "rank 2 / 3";
    public int LineCount => 1;
    public string LineToken(int i) => string.Empty;
    public string LineData(int i) => "Hurls a ball of fire that bursts on impact.";
    public string LineGood(int i) => null!;
    public bool LineGoodNegative(int i) => false;
    public string ValueToken(int i) => string.Empty;
    public string ValueData(int i) => "32 mana";
    public bool ValueGood(int i) => true;
}
