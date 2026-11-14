using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// What of the hero's own windows is still empty (plan_descent_logic UI3 made
// the sheet, the inventory and the skill tree live: HeroSources.cs): the
// item inspect modal (the engine's item render surfaces) and the spellbook
// (its scrolls and tomes are the belt package's). Each answers "nothing to
// show". Never sample data.
//=============================================================================

/// <summary>A hover popup with nothing under the pointer: the shape the screens' hover sources share until UI3.</summary>
public abstract class InactiveHint : IDescentHintTip
{
    public bool Active => false;

    public string TitleToken => "";

    public string TitleData => "";

    public string SubtitleData => "";

    public int LineCount => 0;

    public string LineToken(int i) => "";

    public string LineData(int i) => "";

    public string LineGood(int i) => "";

    public bool LineGoodNegative(int i) => false;

    public string ValueToken(int i) => "";

    public string ValueData(int i) => "";

    public bool ValueGood(int i) => false;
}

/// <summary>The inspect modal (<see cref="IDescentInspect"/>): closed. Live with item views and the engine's item render surfaces (UI3).</summary>
[SideService(Side.Client, As = [typeof(IDescentInspect)])]
public sealed class InspectSource : IDescentInspect
{
    public bool Showing => false;

    public DescentItemView? Item => null;

    public bool ShowsMounted => false;

    public bool HasWeaponSurface => false;

    public int WeaponTexture => 0;

    public bool HasMountedSurface => false;

    public int MountedTexture => 0;

    public string CharacterName => "";
}

/// <summary>The inspect modal's buttons (<see cref="IDescentInspectActions"/>): nothing opens.</summary>
[SideService(Side.Client, As = [typeof(IDescentInspectActions)])]
public sealed class InspectActions : IDescentInspectActions
{
    public void OpenInspect(int slotOrCell) { }

    public void Close() { }

    public void ShowMounted(bool mounted) { }
}

/// <summary>The spellbook (<see cref="IDescentSpells"/>), empty. Live with the hero's spell ranks and scrolls (UI3 over HE8, RU6).</summary>
[SideService(Side.Client, As = [typeof(IDescentSpells)])]
public sealed class SpellsSource : IDescentSpells
{
    public int SpellCount => 0;

    public DescentSpell Spell(int i) => default;
}

/// <summary>The spellbook's pointer report (<see cref="IDescentSpellActions"/>): nothing follows it.</summary>
[SideService(Side.Client, As = [typeof(IDescentSpellActions)])]
public sealed class SpellActions : IDescentSpellActions
{
    public void Hover(int index) { }
}

/// <summary>The spellbook's hover hint (<see cref="IDescentSpellHover"/>): nothing hovered.</summary>
[SideService(Side.Client, As = [typeof(IDescentSpellHover)])]
public sealed class SpellHoverSource : InactiveHint, IDescentSpellHover
{
    public int HoveredSpell => -1;
}
