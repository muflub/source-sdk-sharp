namespace TF2.Descent.Client.Ui;

// THE DATA INTERFACES THE DESCENT SCREENS READ (docs/plans/descent_ui.md §4).
// Same contract as src/client/interface/Ui/UiSources.cs: a screen never sees
// the engine; index-and-count shapes so reading them each frame allocates
// nothing; one interface per concern, and a panel takes only what it reads.
// The wiring (netstate → these) lives in src/tf2_descent/client; the samples
// answer them with fixed data, which is what makes every screen previewable.

/// <summary>Which of D5's rarity rungs an item sits on (0..4, the ladder order).</summary>
public enum DescentRarity : byte
{
    Stock,
    Vintage,
    Strange,
    Unusual,
    Australium,
}

/// <summary>What a <see cref="DescentItemSlot"/> holds: the 2i <c>ItemView</c> shape the server sends.</summary>
public readonly record struct DescentItemView(
    string BaseName,           // ui.Data: the item's base type, case kept
    DescentRarity Rarity,
    int ItemLevel,
    bool Identified,           // false: affixes and unusual hidden (D22) — the view carries none
    int Durability,
    int MaxDurability,         // 0 = no durability (never wear)
    int AffixCount,
    bool HasUnusual,           // the Star chip; its name only when Identified
    bool Broken,               // durability 0: equipped, grants nothing (D21)
    bool Inactive,             // an unmet requirement: contributes nothing (C.3)
    bool NegativeAffix);       // R28: at least one affix is a curse (red rows)

/// <summary>One affix line of an item tooltip (never present on an unidentified view).</summary>
public readonly record struct DescentAffixLine(string Name, int Value);

/// <summary>The four damage types heroes and robots share (R25): fire, blast, bullet, shock.</summary>
public static class DescentDamageTypes
{
    public const string Fire = "#Descent_Type_Fire";
    public const string Blast = "#Descent_Type_Blast";
    public const string Bullet = "#Descent_Type_Bullet";
    public const string Shock = "#Descent_Type_Shock";
}

// ---- HUD --------------------------------------------------------------------

/// <summary>Life and mana, the two orbs: current and maximum each, and what a point of regen looks like.</summary>
public interface IDescentVitals
{
    bool HasHero { get; }
    int Life { get; }
    int LifeMax { get; }
    int Mana { get; }
    int ManaMax { get; }
}

/// <summary>The potion belt: four fixed slots with a shared cooldown (D16, A.5 Field Rations).</summary>
public interface IDescentBelt
{
    int SlotCount { get; }                       // 4
    string SlotName(int i);                      // ui.Data, "" for an empty slot
    int SlotCountOf(int i);                      // remaining uses
    bool SlotReady { get; }                      // the shared cooldown
}

/// <summary>The XP bar and the level-up chip.</summary>
public interface IDescentXp
{
    int Level { get; }
    long Xp { get; }
    long XpToNext { get; }                       // round(100·L^2.2), from the server
    int AttributePoints { get; }
    int SkillPoints { get; }
}

/// <summary>One party member, as the HUD's frames and the party window show them (R26).</summary>
public readonly record struct DescentPartyMember(
    string Name, string ClassToken, int Level,
    int Life, int LifeMax, bool Downed, bool Local, bool Hardcore,
    int DistanceUnits, bool OffLevel);

/// <summary>The party, up to four (R8).</summary>
public interface IDescentParty
{
    int MemberCount { get; }
    DescentPartyMember Member(int i);
}

/// <summary>The mini-boss or boss bar, when one is alive and in sight.</summary>
public interface IDescentBoss
{
    bool Present { get; }
    string Name { get; }                         // ui.Data: Sir Nukesalot (D13)
    int Life { get; }
    int LifeMax { get; }
    int PhaseCount { get; }                      // ticks along the bar
    int Phase { get; }                           // current, 0-based
}

/// <summary>The spell being cast or channelled (R27's actions).</summary>
public interface IDescentCastBar
{
    bool Casting { get; }
    string SpellName { get; }                    // ui.Data
    float Fraction { get; }                      // 0..1
    bool Interrupted { get; }
}

/// <summary>Equipped gear at or below a durability warning line (Phase 8's HUD warning).</summary>
public interface IDescentGearWarning
{
    bool Present { get; }
    string ItemName { get; }                     // ui.Data
    float Fraction { get; }
}
/// <summary>What the automap marks on a revealed room (2m's marker kinds).</summary>
public enum DescentMapMarker : byte
{
    Stairs,
    Boss,
    Corpse,           // a downed hero or a cleared pack worth looting
    Party,            // a party member's position
    ShrineUnused,
    ShrineUsed,
    Container,        // opened or openable
    ContainerLocked,  // needs a key or a kit
    Portal,           // the town return
    Trap,
    Fountain,         // a healing fountain
}

/// <summary>One attribute row of the sheet (C.1): base, rolled now, cap, and the pending +.</summary>
public readonly record struct DescentAttribute(
    string NameToken,      // #Descent_Attr_Brawn …
    int Base,              // class base + distributed points
    int Now,               // base + item modifiers (never below 1, C.1)
    int Cap,               // class cap (10 per level −1, plus set/covenant bonuses)
    int Pending);          // points staged by the sheet's + buttons, unconfirmed
/// <summary>One revealed marker.</summary>
public readonly record struct DescentMarker(DescentMapMarker Kind, int RoomIndex, float X, float Y, int Storey);

/// <summary>
/// The revealed room graph, as the server sent it (2m: a room's outline
/// arrives when it is revealed and not before; the client draws only this).
/// Rooms are axis-aligned rects on the layout generator's abstract du grid
/// (the room prefabs are boxes); doors are short ticks across a shared wall.
/// </summary>
public interface IDescentAutomap
{
    int RoomCount { get; }
    float RoomX(int room);
    float RoomY(int room);
    float RoomW(int room);
    float RoomH(int room);
    int RoomStorey(int room);
    bool RoomCurrentStorey(int room);             // the hero's storey, drawn brighter
    int DoorStubCount { get; }
    float DoorStubX(int i);
    float DoorStubY(int i);
    float DoorStubW(int i);
    float DoorStubH(int i);
    int MarkerCount { get; }
    DescentMarker Marker(int i);
    int StoreyCount { get; }
    int HeroStorey { get; }
    float HeroX { get; }
    float HeroY { get; }
    float HeroHeadingDeg { get; }
    int FocusStorey { get; }                       // the storey the view shows; the map screen's tabs set it
}

/// <summary>The whole sheet: C.1's fields, left to right, in read order.</summary>
public interface IDescentSheet
{
    string Name { get; }                          // ui.Data
    string ClassToken { get; }                    // #Descent_Class_Soldier …
    int Level { get; }
    long Xp { get; }
    long XpToNext { get; }
    long Australium { get; }
    int AttributeCount { get; }                   // 4: Brawn, Smarts, Aim, Grit
    DescentAttribute Attribute(int i);
    int AttributePoints { get; }                  // points to distribute
    int Life { get; }
    int LifeMax { get; }
    int Mana { get; }
    int ManaMax { get; }
    int Armour { get; }
    int CritPercent { get; }
    int DamageMin { get; }
    int DamageMax { get; }                        // the equipped weapon's real range (C.1)
    int ResistCount { get; }                      // 4
    string ResistLabel(int i);                    // #Descent_Type_* / #Descent_Resist_*
    int ResistPercent(int i);
    int ResistCap { get; }                        // 75
    bool HasPartyReviveHint { get; }
    /// <summary>The vitals as they will read once the staged points apply
    /// (the sheet's preview ink); equal to the live values with nothing
    /// staged. The view-model computes them with the same formula the server
    /// uses — native code and screens never make the derived-stat decision.</summary>
    int PendingLifeMax { get; }
    int PendingManaMax { get; }
}

/// <summary>
/// The staged-spend contract (the sheet's apply/reset flow): + buttons stage
/// points on one attribute at a time; nothing reaches the server until the
/// hero applies the plan, and a reset hands every staged point back. The
/// staging itself lives in the sheet's view-model (the server is the apply's
/// endpoint), so the screen only ever reads <see cref="DescentAttribute.Pending"/>
/// and <see cref="IDescentSheet.PendingLife"/>-style previews of the plan.
/// </summary>
public interface IDescentSheetActions
{
    void Stage(int attributeIndex, int count);
    void Apply();
    void Reset();

    /// <summary>The attribute row or derived plate under the pointer (-1 on
    /// the other); the host's view-model builds the hint from it
    /// (see <see cref="IDescentSheetHover"/>).</summary>
    void Hover(int attributeIndex, int derivedIndex);
}

// ---- items, inventory, equipment (D4, R29) -----------------------------------

/// <summary>Which paper-doll slot a cell is. Fixed order; the amulet is R29's.</summary>
public enum DescentEquipSlot : byte
{
    Primary, Secondary, Melee, Hat, Body, Feet, Trinket1, Trinket2, Amulet,
}

/// <summary>The equipment slots and their earned/locked state (unearned = skill-gated, drawn locked).</summary>
public interface IDescentEquipment
{
    int SlotCount { get; }                        // 9
    DescentEquipSlot Slot(int i);
    bool SlotEarned(int i);
    DescentItemView? SlotItem(int i);             // null: empty
}
/// <summary>The shape both the backpack and the stash present to <see cref="Parts.ItemGrid"/>: a grid of cells.</summary>
public interface IDescentItemGridSource
{
    int CellCount { get; }                        // filled cells (may exceed Capacity when Overflow)
    int Capacity { get; }                         // the rolled capacity; cells past it read withdraw-only
    bool Overflow { get; }                        // capacity &lt; CellCount: the tail is withdraw-only (2d)
    DescentItemView? Cell(int i);                 // null: empty slot
}

 // (There is no backpack-only interface: the backpack is an
 // IDescentItemGridSource, and the belt strip on the inventory's footer
 // reads IDescentBelt — an empty marker interface would be data nobody
 // reads wearing a name.)

/// <summary>What a panel asks of an item: hover, use, move. Index is slot-or-cell keyed by the owner screen.</summary>
public interface IDescentItemActions
{
    void Hover(int slotOrCell);
    void Use(int slotOrCell);
    void Move(int fromCell, int toCell);
}

/// <summary>An item hovered anywhere: the tooltip's subject, plus the compare target when relevant.</summary>
public interface IDescentItemHover
{
    bool Hovering { get; }
    DescentItemView Item { get; }
    int AffixLineCount { get; }
    DescentAffixLine AffixLine(int i);
    string Family { get; }                        // ui.Data: "rocket launcher" (R17)
    string UnusualEffect { get; }                 // ui.Data, "" when none/hidden
    string RequirementToken { get; }              // "#Descent_Req_Brawn 14"-style copy, "" when none
    bool RequirementMet { get; }
    bool CompareHas { get; }                      // the equipped sibling to show the delta against
    DescentItemView CompareItem { get; }          // named in the compare row: what the delta is against
    int CompareAffixCount { get; }
    DescentAffixLine CompareAffix(int i);
}

// ---- the inspect modal ---------------------------------------------------------
//
// The item inspect (docs/plans/descent_ui.md 7: real 3D rasterisation is
// engine wiring, out of the UI's scope): the UI declares each view through a
// texture handle from a data interface, exactly as the hover/tooltip sources
// are fake-driven, and the screens never touch the engine. The view-model
// owns the state (which cell is being inspected, which view is showing);
// the part reads it and reports the pointer.

/// <summary>
/// The inspect modal's subject: the item under inspection (null: nothing),
/// which of the two views is showing (false: the weapon alone, the default
/// and the answer while the engine has sent nothing else), and the two
/// surfaces it may show - the weapon by itself, and the weapon mounted on
/// the hero, each a backend texture id the preview marks with its handle.
/// A surface the engine has not sent reads Has*Surface false, and the
/// viewport waits for it (the plan's rasterisation stays engine-side).
/// </summary>
public interface IDescentInspect
{
    bool Showing { get; }
    DescentItemView? Item { get; }
    bool ShowsMounted { get; }
    bool HasWeaponSurface { get; }
    int WeaponTexture { get; }
    bool HasMountedSurface { get; }
    int MountedTexture { get; }
    string CharacterName { get; }                 // ui.Data: who wears it in the mounted view
}

/// <summary>
/// The inspect modal's sink: a slot's corner button opens the modal for
/// that cell (the owner screen's keying, as <see cref="IDescentItemActions"/>'
/// indices), the close button and Esc close it, and the two-view switch
/// picks the mounted view. The view-model answers on the next frame
/// through <see cref="IDescentInspect"/>.
/// </summary>
public interface IDescentInspectActions
{
    void OpenInspect(int slotOrCell);
    void Close();
    void ShowMounted(bool mounted);
}

// ---- skills and spells (Appendix A, R27) -------------------------------------

/// <summary>What kind of skill card this is (A.1).</summary>
public enum DescentSkillKind : byte
{
    Action, Upgrade, UpgradeFamily, UpgradeTriggered, Toggle, Capstone, Utility,
}

/// <summary>One skill on a tier ladder or the Mercenary tab.</summary>
public readonly record struct DescentSkill(
    int Id, string NameToken, DescentSkillKind Kind,
    int Rank, int MaxRank, int BonusRanks,
    int Tier, int TierUnlockLevel, int HeroLevel,
    bool Unlocked, bool Buyable,
    string ManaCost, string Cooldown,             // ui.Data-style copy strings ("12 / 10 s"), "" for none
    string FamilyTag,                             // the family token after the card's accent dot, "" unless UpgradeFamily
    bool PrereqMet);

/// <summary>A tab's worth of skills.</summary>
public interface IDescentSkillTab
{
    string TitleToken { get; }                    // #Descent_Tab_Ordnance …
    int SkillCount { get; }
    DescentSkill Skill(int i);
    int PrereqCount(int skillIndex);
    int PrereqId(int skillIndex, int i);          // the needed skill id
    int SynergyCount(int skillIndex);
    string SynergyText(int skillIndex, int i);    // ui.Data caption
}

/// <summary>The skill window's data.</summary>
public interface IDescentSkills
{
    string ClassToken { get; }
    int HeroLevel { get; }
    int SkillPoints { get; }
    int TabCount { get; }                         // 3 class tabs + Mercenary
    IDescentSkillTab Tab(int i);
}

/// <summary>Buying a rank (server-side, permanent short of a respec).</summary>
public interface IDescentSkillActions
{
    void Buy(int skillId);

    /// <summary>The card the pointer rests on; the host's view-model builds
    /// the card's hint from it (see <see cref="IDescentSkillHover"/>). The
    /// sample fake forces the content instead of waiting for the
    /// pointer.</summary>
    void Hover(int skillId);
}

/// <summary>One known spell or scroll stack (R27).</summary>
public readonly record struct DescentSpell(
    string NameToken, int Rank, int MaxRank,
    int SmartsRequirement, bool SmartsMet,        // below: inactive, not forgotten
    string ManaCost, bool IsScroll, int ScrollCount, int ScrollStackMax);

/// <summary>The spellbook's data.</summary>
public interface IDescentSpells
{
    int SpellCount { get; }
    DescentSpell Spell(int i);
}

/// <summary>The spellbook's one report: the known-spell row (or scroll chip,
/// as <c>-i - 1</c>) under the pointer. The book itself is read-only (R27 —
/// casts are belt/keys), so this is its only sink; the host's view-model
/// builds the hint from it (see <see cref="IDescentSpellHover"/>).</summary>
public interface IDescentSpellActions
{
    void Hover(int index);
}

// ---- town: vendors, stash, church, crafting, crates ---------------------------

/// <summary>Which vendor's face and stance the panel wears (D15).</summary>
public enum DescentVendorKind : byte
{
    Weapons,        // Saxton Hale
    Armour,         // Engineer: also repair + the bench
    Healing,        // Medic: potions
    Magic,          // Merasmus: tomes, respec
    Identify,       // Spy (service stance)
    Repair,         // Engineer's service stance (single / all)
    Gambling,       // Scout's crate stall
}
/// <summary>One vendor row: the ware as the 2i projection, its price in
/// Australium, and whether the player can pay it (the row dims when not).</summary>
public readonly record struct DescentWare(DescentItemView Item, long Price, bool Affordable);

/// <summary>The vendor panel's data.</summary>
public interface IDescentVendor
{
    DescentVendorKind Kind { get; }
    string VendorNameToken { get; }               // #Descent_Vendor_Hale …
    string StanceToken { get; }                   // one-line pitch / service caption
    long PlayerAustralium { get; }
    int BuyCount { get; }
    DescentWare Buy(int i);
    int SellCount { get; }
    DescentWare Sell(int i);
    long ServiceFee { get; }                      // identify / repair one; 0 = n/a
    long ServiceFeeAll { get; }                   // repair-all; 0 = n/a
    bool SellTab { get; }                         // whether the Sell tab exists (shops only)
}

/// <summary>Buy / sell / service actions.</summary>
public interface IDescentVendorActions
{
    void Buy(int wareIndex);
    void Sell(int wareIndex);
    void ServiceOne(int itemIndex);
    void ServiceAll();

    /// <summary>The ware row under the pointer: the popup's subject
    /// (see <see cref="IDescentVendorHover"/>).</summary>
    void HoverWare(int wareIndex);

    /// <summary>The player's backpack cell under the pointer (the identify
    /// bench's items, the crate stall's rollable stack): the popup's
    /// subject, keyed like <see cref="ServiceOne"/>'s index.</summary>
    void HoverBackpack(int itemIndex);
}

/// <summary>
/// The vendor's 3D portrait (D15's face; docs/plans/descent_ui.md 7 keeps the
/// rasterisation engine-side): the panel declares the surface, the engine
/// paints it. An inactive portrait draws nothing, so a vendor mid-sentence
/// and a vendor in the background are different pictures, and every sample
/// without the source renders the plain window.
/// </summary>
public interface IDescentVendorPortrait
{
    bool Active { get; }
    int Texture { get; }
}

/// <summary>The stash locker: the account's shared grid, capacity = highest Storage Plan rank (D4).</summary>
public interface IDescentStash : IDescentItemGridSource
{
    int StoragePlanRankShown { get; }             // the account's highest living rank
}

/// <summary>A depth row in the church's picker (R6, D8).</summary>
public readonly record struct DescentDepthRow(
    int Depth, string TilesetToken, bool Reached, bool LeaderReached, bool Locked, bool Boss);

/// <summary>The church's descent list.</summary>
public interface IDescentDescent
{
    string PartyLeaderToken { get; }              // ui.Data caption (D8)
    int RowCount { get; }
    DescentDepthRow Row(int i);
}

/// <summary>Descend now (the leader decides; members follow).</summary>
public interface IDescentDescentActions
{
    void Descend(int depth);
}

/// <summary>The crate stall: the base card, the published odds table (D23), the opening state.</summary>
public interface IDescentCrate
{
    string BaseName { get;                        // ui.Data: the chosen base type
    }
    long PriceAustralium { get; }
    bool Affordable { get; }
    int OddsRowCount { get; }
    DescentRarity OddsRarity(int i);
    long OddsWeight(int i);                       // x of 100,000
    bool Opening { get; }                         // the reveal state
    DescentItemView? OpenedItem { get; }
}

/// <summary>Open it (in-game Australium only; no real-money path, D23).</summary>
public interface IDescentCrateActions
{
    void Open();
}

/// <summary>The crafting bench (D24): recipe list, item under work, metal pockets.</summary>
public interface IDescentCraft
{
    int RecipeTabCount { get; }
    string RecipeTabToken(int i);                 // #Descent_Craft_Choose …
    int RecipeTabIndex { get; }
    string InputsToken { get; }                   // what this recipe eats, caption
    DescentItemView? ItemUnderTest { get; }
    int AffixLineCount { get; }
    DescentAffixLine AffixLine(int i);
    int LockedCount { get; }                      // affixes that stay (re-roll keeps them byte-identical)
    bool RerollAffixLocked(int i);
    int RerollCount { get; }                      // the rising cost on this item
    long RerollCost { get; }
    int Scrap { get; }
    int Reclaimed { get; }
    int Refined { get; }
    bool CanCraft { get; }
    string PreviewToken { get; }                  // "a random &lt;base&gt; at your level"
}

/// <summary>Craft actions.</summary>
public interface IDescentCraftActions
{
    void SelectRecipeTab(int i);
    void ToggleLock(int affixIndex);
    void Craft();
}

// ---- the hover hint tip (every subject screen anchors one popup beside the
// row the pointer rests on). One content shape; each subject wires its own
// sub-interface so the source-use facts see a screen reading only its own
// subject. The content is the host's view-model — like ItemTooltip's affix
// lines, the screen never decides what a stat, skill or spell means; it only
// reports which row the pointer is on (through the subject's actions sink)
// and anchors the popup (docs/plans/descent_ui.md §3 HintTip). ----

/// <summary>The content of the small hover popup: a title, an optional muted
/// subtitle, and copy lines each with an optional centred value under them.</summary>
public interface IDescentHintTip
{
    bool Active { get; }                            // false: draw nothing
    string TitleToken { get; }                      // "#Descent_Attr_Brawn"; empty: use TitleData
    string TitleData { get; }                       // the title as game data
    string SubtitleData { get; }                    // the muted line beside the title ("1/3", "20s"); null: none
    int LineCount { get; }                          // 0: no detail lines
    string LineToken(int i);                        // a copy line as a token; empty: use LineData
    string LineData(int i);                         // a copy line as game data
    string LineGood(int i);                         // a value pinned right at this line; null: none
    bool LineGoodNegative(int i);                   // the pinned value is a cost (the red rungs)
    string ValueToken(int i);                       // the emphasised value line as a token; empty: use ValueData
    string ValueData(int i);                        // the emphasised value — a number, not copy
    bool ValueGood(int i);                          // the value reads positive (tone.Positive) rather than sheet text
}

/// <summary>The character sheet's hover subject: the attribute row or derived
/// plate under the pointer; -1 is inactive. Combat plates are
/// <c>HoverDerived</c> 0..4 (Life, Mana, Armour, Crit, Damage, the
/// <c>Derived()</c> row order). Resist rows stay un-hovered: their row is
/// 24 du tall, under the lint's interactive hit target.</summary>
public interface IDescentSheetHover : IDescentHintTip
{
    int HoverAttribute { get; }
    int HoverDerived { get; }
}

/// <summary>The skill tree's hover subject: the card id under the pointer;
/// -1 is inactive.</summary>
public interface IDescentSkillHover : IDescentHintTip
{
    int HoveredSkill { get; }
}

/// <summary>The spellbook's hover subject: the known-spell index, or a scroll
/// chip's as <c>-i - 1</c>; -1 means no row (an inactive tip).</summary>
public interface IDescentSpellHover : IDescentHintTip
{
    int HoveredSpell { get; }
}

/// <summary>The vendor's hover subject: the ware row under the pointer;
/// -1 is inactive. Player items (backpack grid, service list) are items and
/// ride <see cref="IDescentItemHover"/>'s tooltip instead.</summary>
public interface IDescentVendorHover : IDescentHintTip
{
    int WareIndex { get; }
}
// ---- downed (R26) -------------------------------------------------------------

/// <summary>The local hero is down: bleed-out timer, revive progress.</summary>
public interface IDescentDowned
{
    bool Downed { get; }
    float BleedOutSecondsLeft { get; }
    float BleedOutTotal { get; }
    bool BeingRevived { get; }
    string ReviverName { get; }                   // ui.Data
    float ReviveFraction { get; }
    string NearestMemberName { get; }             // ui.Data
    int NearestMemberDistance { get; }
}
