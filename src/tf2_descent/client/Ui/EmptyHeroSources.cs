using SourceSharp.Abi;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// The hero's own windows (character sheet, inventory, skill tree, spellbook)
// before UI3 wires them (plan_descent_logic UI1). Each answers "nothing to
// show" and names what the server must send; an action does nothing until
// its client command exists. Never sample data.
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

/// <summary>
/// The character sheet (<see cref="IDescentSheet"/>), empty: no name, class,
/// attributes, resistances or derived stats. Live when the owner's client
/// receives the sheet (UI3 over HE2 and HE3): the four attributes' base, now
/// and cap, the resistances and their cap, armour, crit and the equipped
/// weapon's damage range, and the Australium carried. The hero state already
/// carries level, experience, life, mana and points.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSheet)])]
public sealed class SheetSource : IDescentSheet
{
    public string Name => "";

    public string ClassToken => "";

    public int Level => 0;

    public long Xp => 0;

    public long XpToNext => 0;

    public long Australium => 0;

    public int AttributeCount => 0;

    public DescentAttribute Attribute(int i) => default;

    public int AttributePoints => 0;

    public int Life => 0;

    public int LifeMax => 0;

    public int Mana => 0;

    public int ManaMax => 0;

    public int Armour => 0;

    public int CritPercent => 0;

    public int DamageMin => 0;

    public int DamageMax => 0;

    public int ResistCount => 0;

    public string ResistLabel(int i) => "";

    public int ResistPercent(int i) => 0;

    public int ResistCap => 0;

    public bool HasPartyReviveHint => false;

    public int PendingLifeMax => 0;

    public int PendingManaMax => 0;
}

/// <summary>The sheet's spend plan (<see cref="IDescentSheetActions"/>): nothing is staged or sent until UI3's attribute command.</summary>
[SideService(Side.Client, As = [typeof(IDescentSheetActions)])]
public sealed class SheetActions : IDescentSheetActions
{
    public void Stage(int attributeIndex, int count) { }

    public void Apply() { }

    public void Reset() { }

    public void Hover(int attributeIndex, int derivedIndex) { }
}

/// <summary>The sheet's hover hint (<see cref="IDescentSheetHover"/>): nothing hovered.</summary>
[SideService(Side.Client, As = [typeof(IDescentSheetHover)])]
public sealed class SheetHoverSource : InactiveHint, IDescentSheetHover
{
    public int HoverAttribute => -1;

    public int HoverDerived => -1;
}

/// <summary>
/// The paper doll (<see cref="IDescentEquipment"/>), empty: no slots known.
/// Live with the hero's equipped items on the player's own state (L11, HE4)
/// and their views (E4): each of the nine slots, earned or locked, and what
/// it holds.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentEquipment)])]
public sealed class EquipmentSource : IDescentEquipment
{
    public int SlotCount => 0;

    public DescentEquipSlot Slot(int i) => (DescentEquipSlot)i;

    public bool SlotEarned(int i) => false;

    public DescentItemView? SlotItem(int i) => null;
}

/// <summary>
/// The backpack (<see cref="IDescentItemGridSource"/>), empty: no cells and no
/// capacity. Live with the item-view cache fed by one-item messages and a
/// resync on join (UI1's last part, E4, HE0): the backpack's capacity and each
/// cell's item view.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentItemGridSource)])]
public sealed class BackpackSource : IDescentItemGridSource
{
    public int CellCount => 0;

    public int Capacity => 0;

    public bool Overflow => false;

    public DescentItemView? Cell(int i) => null;
}

/// <summary>What a slot or cell is asked to do (<see cref="IDescentItemActions"/>): nothing, until UI3's equip, use and move commands.</summary>
[SideService(Side.Client, As = [typeof(IDescentItemActions)])]
public sealed class ItemActions : IDescentItemActions
{
    public void Hover(int slotOrCell) { }

    public void Use(int slotOrCell) { }

    public void Move(int fromCell, int toCell) { }
}

/// <summary>The item tooltip (<see cref="IDescentItemHover"/>): nothing hovered. Live with item views and their affix lines (E4, UI3).</summary>
[SideService(Side.Client, As = [typeof(IDescentItemHover)])]
public sealed class ItemHoverSource : IDescentItemHover
{
    public bool Hovering => false;

    public DescentItemView Item => default;

    public int AffixLineCount => 0;

    public DescentAffixLine AffixLine(int i) => default;

    public string Family => "";

    public string UnusualEffect => "";

    public string RequirementToken => "";

    public bool RequirementMet => true;

    public bool CompareHas => false;

    public DescentItemView CompareItem => default;

    public int CompareAffixCount => 0;

    public DescentAffixLine CompareAffix(int i) => default;
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

/// <summary>The Australium the inventory shows (<see cref="IDescentInvPurse"/>): none known. Live with the sheet's Australium (UI3).</summary>
[SideService(Side.Client, As = [typeof(IDescentInvPurse)])]
public sealed class PurseSource : IDescentInvPurse
{
    public long Australium => 0;
}

/// <summary>
/// The skill window (<see cref="IDescentSkills"/>), empty: no tabs. Live when
/// the client holds the class's trees (the rules' skill table) and the
/// hero's ranks (UI3 over HE6): today only the skill bar's slot ids reach
/// the client.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSkills)])]
public sealed class SkillsSource : IDescentSkills
{
    public string ClassToken => "";

    public int HeroLevel => 0;

    public int SkillPoints => 0;

    public int TabCount => 0;

    public IDescentSkillTab Tab(int i) => throw new ArgumentOutOfRangeException(nameof(i), "the skill window has no tabs yet");
}

/// <summary>Buying a skill rank (<see cref="IDescentSkillActions"/>): nothing is sent until UI3 sends <c>tf2_descent_skill_buy</c>.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillActions)])]
public sealed class SkillActions : IDescentSkillActions
{
    public void Buy(int skillId) { }

    public void Hover(int skillId) { }
}

/// <summary>The skill card's hover hint (<see cref="IDescentSkillHover"/>): nothing hovered.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillHover)])]
public sealed class SkillHoverSource : InactiveHint, IDescentSkillHover
{
    public int HoveredSkill => -1;
}

/// <summary>The Mercenary tab's group headings (<see cref="IDescentSkillGroups"/>): none.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillGroups)])]
public sealed class SkillGroupsSource : IDescentSkillGroups
{
    public string GroupTitleToken(int group) => "";
}

/// <summary>The skill window's last tab (<see cref="IDescentSkillTabSelection"/>): the first.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillTabSelection)])]
public sealed class SkillTabSelectionSource : IDescentSkillTabSelection
{
    public int SelectedTab => 0;
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
