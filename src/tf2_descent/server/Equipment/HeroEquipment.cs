using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;
using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

using TFItem = TF2.Server.Econ.ItemInstance;

namespace TF2.Descent.Server.Gear;

/// <summary>
/// One hero's equipment as the rules resolve it now: the player, the ledger's key and view of
/// the hero, the sheet the requirements read, each equipped item by equipment slot (in
/// inventory.json's slot order), and <see cref="TF2.Descent.Rules.Items.Equipment.Resolve"/>'s answer over them.
/// </summary>
public sealed record HeroGear(
    int PlayerIndex,
    string Hero,
    HeroSheet Sheet,
    LedgerHero Ledger,
    IReadOnlyList<EquippedItem> Equipped,
    EquipmentResult Result)
{
    /// <summary>The item held in an equipment slot, or null for an empty one.</summary>
    public Held? In(string slot) => Ledger.Inventory.Equipped.GetValueOrDefault(slot);
}

/// <summary>
/// A hero's equipped TF2 Descent items as his TF2 kit (plan_descent_logic HE4): what every
/// other equipment piece asks (the kit's item source, the sheet's contribution source, the
/// settle after a change, the dev commands, and later the inventory screen, the belt and wear).
///
/// <list type="bullet">
/// <item><b>Who is a hero here:</b> a player who is no bot, while the Descent mode is on,
/// whose items the ledger holds, hosted or not (SDK 2.7 equips: the host keeps the equipment
/// slots, and a new character starts with its kit worn).</item>
/// <item><b>The sheet</b> is the hero's live one (<see cref="HeroSheets"/>), never the
/// ledger's copy, which <see cref="Equip"/> brings up to date before it asks.</item>
/// <item><b>Requirements</b> are the rules' (<see cref="TF2.Descent.Rules.Items.Equipment.Resolve"/>): the gear alone
/// feeds them; no other contribution is passed in.</item>
/// <item><b>A weapon slot's TF item</b> is the item's base's TF row (<c>tfItem</c>, an
/// items_game name) under the ledger's id, its rarity's quality and the rules' TF attributes
/// for that slot. A weapon that does not count (inactive, broken: the rules'
/// <see cref="EquippedState.FiresAsStock"/>) is handed over as its Stock self: the same TF
/// row, Stock quality, no rolled attribute. An empty weapon slot is empty in TF2 too.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroEquipment(
    IItemLedger ledger,
    HeroKeys keys,
    HeroSheets sheets,
    TF2DescentRules rules,
    ItemSchema schema,
    IDescentModeState mode,
    IBotPlayers bots,
    IEnginePlayers players,
    ILog log)
{
    /// <summary>The equipment slot (inventory.json's id) that fills a TF weapon slot; null for any other TF slot.</summary>
    public static string? EquipmentSlotOf(TFLoadoutSlot slot) => slot switch
    {
        TFLoadoutSlot.Primary => "primary",
        TFLoadoutSlot.Secondary => "secondary",
        TFLoadoutSlot.Melee => "melee",
        _ => null,
    };

    /// <summary>The TF quality name (items_game's qualities) a rarity's items carry, by the rarity's id (rarities.json); null for none.</summary>
    public static string? QualityNameOf(string rarityId) => rarityId switch
    {
        "stock" => "unique",
        "vintage" => "vintage",
        "strange" => "strange",
        "unusual" => "rarity4",
        "australium" => "strange",
        _ => null,
    };

    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    /// <summary>The rules every resolution is made of.</summary>
    public TF2DescentRules Rules => rules;

    /// <summary>The ledger the hero's items are in.</summary>
    public IItemLedger Ledger => ledger;

    /// <summary>The ledger's key of the hero in <paramref name="playerIndex"/> when this package claims him (see the class summary); null otherwise.</summary>
    public string? HeroOf(int playerIndex)
    {
        if (!mode.Active)
            return null;
        if (bots.IsBot(players.GetHandle(playerIndex)))
            return null;
        return keys.Of(playerIndex) is { } key && ledger.Find(key) is not null ? key : null;
    }

    /// <summary>The hero's gear, resolved now; null for a player this package does not claim or with no sheet.</summary>
    public HeroGear? Of(int playerIndex)
    {
        if (HeroOf(playerIndex) is not { } key || ledger.Find(key) is not { } hero || sheets.Of(playerIndex) is not { } sheet)
            return null;
        return Resolve(playerIndex, key, hero, sheet);
    }

    /// <summary>The rules' resolution of <paramref name="hero"/>'s equipped items against <paramref name="sheet"/>.</summary>
    public HeroGear Resolve(int playerIndex, string key, LedgerHero hero, HeroSheet sheet)
    {
        var equipped = new List<EquippedItem>();
        foreach (var slot in rules.Content.InventorySettings.Equipment)
        {
            if (hero.Inventory.Equipped.TryGetValue(slot.Id, out var held))
                equipped.Add(new EquippedItem(slot.Id, held.Item));
        }
        return new HeroGear(playerIndex, key, sheet, hero, equipped, rules.ItemRules.Equipment.Resolve(sheet, equipped));
    }

    /// <summary>
    /// What a hero carries in a TF loadout slot as <paramref name="cls"/>: no opinion for a
    /// player not claimed or a slot that is not a weapon slot (the engineer's and spy's tools,
    /// the cosmetics: TF2's own answer stands), empty for an empty weapon slot or an item the
    /// class may not use, else the equipped item's TF item.
    /// </summary>
    public TFSlotItem KitAnswer(int playerIndex, TFClass cls, TFLoadoutSlot slot)
    {
        if (EquipmentSlotOf(slot) is not { } equipmentSlot || Of(playerIndex) is not { } gear)
            return TFSlotItem.NoOpinion;
        return KitAnswer(gear, cls, equipmentSlot);
    }

    /// <summary>The answer for one equipment slot of resolved gear (see <see cref="KitAnswer(int, TFClass, TFLoadoutSlot)"/>).</summary>
    public TFSlotItem KitAnswer(HeroGear gear, TFClass cls, string equipmentSlot)
    {
        if (gear.In(equipmentSlot) is not { } held)
            return LoadoutSlotOf(equipmentSlot) is { } tfSlot ? StockWeapon(cls, tfSlot) : TFSlotItem.Empty;
        var row = rules.Content.ItemBases[held.Item.BaseId];
        if (!row.AllowsClass(ClassId(cls)))
            return TFSlotItem.Empty;
        return TFItemOf(gear, equipmentSlot) is { } item ? TFSlotItem.Of(item) : TFSlotItem.NoOpinion;
    }

    /// <summary>The TF loadout slot a weapon equipment slot is handed to TF2 as (<see cref="EquipmentSlotOf"/> the other way); null for none.</summary>
    public static TFLoadoutSlot? LoadoutSlotOf(string equipmentSlot) => equipmentSlot switch
    {
        "primary" => TFLoadoutSlot.Primary,
        "secondary" => TFLoadoutSlot.Secondary,
        "melee" => TFLoadoutSlot.Melee,
        _ => null,
    };

    /// <summary>
    /// THE STOCK WEAPONS OF AN EMPTY SLOT (the user, 2026-11-15: "when you die you should be
    /// respawned with default weapons"; it overrules the melee-only fallback an earlier lane
    /// chose): a hero with nothing in a weapon slot (after a death, all of it is on his corpse)
    /// carries the class's stock weapon there, the schema's base item for that TF slot, so he
    /// respawns with the class's three. It is no Descent item: no ledger id, no affix, no wear,
    /// nothing to drop, lose or sell, and it goes the moment a weapon is equipped in that slot.
    /// The TF slots beyond the three (an Engineer's PDAs, a Spy's watch and sapper) were never
    /// Descent's: TF2 answers them itself (<see cref="TFSlotItem.NoOpinion"/>).
    /// </summary>
    public TFSlotItem StockWeapon(TFClass cls, TFLoadoutSlot slot) =>
        schema.BaseItem(cls, slot) is { } definition ? TFSlotItem.Of(TFItem.Base(definition)) : TFSlotItem.Empty;

    /// <summary>
    /// The TF item an equipped weapon is handed to TF2 as; null for an empty slot, an item
    /// with no TF row (armour) or a row the schema does not know (said once on the log).
    /// </summary>
    public TFItem? TFItemOf(HeroGear gear, string equipmentSlot)
    {
        if (gear.In(equipmentSlot) is not { } held)
            return null;
        var row = rules.Content.ItemBases[held.Item.BaseId];
        if (row.TFItem is not { Length: > 0 } tfRow)
            return null;
        if (schema.Item(tfRow) is not { } definition)
        {
            if (_warned.Add(tfRow))
                log.Warning($"[TF2Descent/Equipment] {row.Id}'s TF row '{tfRow}' is not in the item schema: its slot keeps TF2's own item\n");
            return null;
        }

        bool stock = gear.Result.State(equipmentSlot).FiresAsStock;
        var attributes = new List<AttributeValue>();
        if (!stock)
        {
            foreach (var grant in gear.Result.TFAttributes)
            {
                if (grant.Slot == equipmentSlot && AttributeOf(grant) is { } value)
                    attributes.Add(value);
            }
        }
        return new TFItem(unchecked((ulong)held.Id), definition, Quality(stock ? 0 : held.Item.Rarity, definition), attributes);
    }

    /// <summary>
    /// A TF attribute grant as the schema's attribute: the definitions of the grant's
    /// attribute class (<c>mult_dmg</c>), the one whose effect matches the affix's side (a
    /// cursed affix is a penalty, any other a bonus: the value's side of neutral says nothing,
    /// as a lower <c>mult_postfiredelay</c> is the better), lowest index first; null (said
    /// once) when the schema has none of that class. The hooks read the class, so the choice
    /// only names the attribute a client describes.
    /// </summary>
    public AttributeValue? AttributeOf(TFAttributeGrant grant)
    {
        var definitions = schema.AttributesOfClass(grant.Attribute);
        if (definitions.Count == 0)
        {
            if (_warned.Add("class:" + grant.Attribute))
                log.Warning($"[TF2Descent/Equipment] the item schema has no attribute of class '{grant.Attribute}': {grant.Affix} gives TF2 nothing\n");
            return null;
        }

        bool cursed = rules.Content.Affixes.TryGet(grant.Affix, out var affix) && affix.IsCursed;
        var want = cursed ? AttributeEffectType.Negative : AttributeEffectType.Positive;
        var chosen = definitions
            .Where(d => !d.StoredAsInteger && d.ValueType == AttributeValueType.Default)
            .OrderBy(d => d.EffectType == want ? 0 : 1)
            .ThenBy(d => d.Defindex)
            .FirstOrDefault() ?? definitions[0];
        return AttributeValue.OfFloat(chosen, (float)grant.Value);
    }

    /// <summary>The TF quality of a rarity (<see cref="QualityNameOf"/>), else the definition's own.</summary>
    public int Quality(int rarity, ItemDefinition definition)
    {
        var rarities = rules.Content.Rarities.Rows;
        if (rarity >= 0 && rarity < rarities.Count && QualityNameOf(rarities[rarity].Id) is { } name
            && schema.Quality(name) is { } quality)
            return quality;
        return definition.Quality;
    }

    /// <summary>
    /// Equips the item in a backpack slot through the ledger, the ledger told the hero's live
    /// sheet first (its requirements and capacity read it). The rules' refusal comes back as is.
    /// </summary>
    public Task<LedgerResult> Equip(int playerIndex, int from, string slot)
    {
        if (Sync(playerIndex) is not { } key)
            return Task.FromResult(LedgerResult.Refused(LedgerRefusals.NoHero));
        return ledger.Equip(key, from, slot);
    }

    /// <summary>Takes off what an equipment slot holds, through the ledger, the ledger told the live sheet first.</summary>
    public Task<LedgerResult> Unequip(int playerIndex, string slot)
    {
        if (Sync(playerIndex) is not { } key)
            return Task.FromResult(LedgerResult.Refused(LedgerRefusals.NoHero));
        return ledger.Unequip(key, slot);
    }

    /// <summary>The ledger's key of the player, the ledger given the live sheet; null when there is no hero to ask for.</summary>
    private string? Sync(int playerIndex)
    {
        if (keys.Of(playerIndex) is not { } key)
            return null;
        if (sheets.Of(playerIndex) is { } sheet)
            ledger.SetSheet(key, sheet);
        return key;
    }

    /// <summary>The rules' class id of a TF class ("soldier"); empty for none.</summary>
    public static string ClassId(TFClass cls) => cls switch
    {
        TFClass.Scout => "scout",
        TFClass.Soldier => "soldier",
        TFClass.Pyro => "pyro",
        TFClass.Demoman => "demoman",
        TFClass.HeavyWeapons => "heavy",
        TFClass.Engineer => "engineer",
        TFClass.Medic => "medic",
        TFClass.Sniper => "sniper",
        TFClass.Spy => "spy",
        _ => string.Empty,
    };
}
