//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Econ;

/// <summary>
/// The word tables <c>CTFItemSchema::BInitSchema</c> builds before the parse
/// (tf_item_schema.cpp:1655-1680), as switches (SS0010: no static tables),
/// read the way <c>StringFieldToInt</c> reads them (econ_item.cpp:31-47:
/// case-insensitive, −1 for an empty or unknown word).
/// </summary>
public static class TFItemStrings
{
    /// <summary>
    /// <c>m_iLoadoutSlots</c>'s length: <c>LOADOUT_COUNT</c> (tf_item_constants.h:162),
    /// one per <c>gs_PlayerClassData</c> row.
    /// </summary>
    public const int LoadoutCount = 11;

    /// <summary>
    /// <c>GetClassUsabilityStrings()</c>: <c>gs_PlayerClassData[].m_pchName</c>
    /// (tf_item_schema.cpp:1456-1469), "Undefined" … "Engineer", "Invalid".
    /// Note "Heavy", not the class file's "heavyweapons".
    /// </summary>
    public static int ClassIndex(string word) => word.ToLowerInvariant() switch
    {
        "undefined" => 0,
        "scout" => 1,
        "sniper" => 2,
        "soldier" => 3,
        "demoman" => 4,
        "medic" => 5,
        "heavy" => 6,
        "pyro" => 7,
        "spy" => 8,
        "engineer" => 9,
        "invalid" => 10,
        _ => -1,
    };

    /// <summary>The usability word of a class index; null outside the table.</summary>
    public static string? ClassWord(int index) => index switch
    {
        0 => "Undefined",
        1 => "Scout",
        2 => "Sniper",
        3 => "Soldier",
        4 => "Demoman",
        5 => "Medic",
        6 => "Heavy",
        7 => "Pyro",
        8 => "Spy",
        9 => "Engineer",
        10 => "Invalid",
        _ => null,
    };

    /// <summary>
    /// <c>GetLoadoutStrings( EQUIP_TYPE_CLASS )</c>: <c>g_szLoadoutStrings</c>
    /// (tf_item_schema.cpp:1513-1540). The empty rows (misc2, taunt2-8) match
    /// nothing: StringFieldToInt refuses an empty word before it looks.
    /// </summary>
    public static int ClassSlotIndex(string word) => word.ToLowerInvariant() switch
    {
        "primary" => 0,
        "secondary" => 1,
        "melee" => 2,
        "utility" => 3,
        "building" => 4,
        "pda" => 5,
        "pda2" => 6,
        "head" => 7,
        "misc" => 8,
        "action" => 9,
        "taunt" => 11,
        _ => -1,
    };

    /// <summary>
    /// <c>GetLoadoutStrings( EQUIP_TYPE_ACCOUNT )</c>: <c>g_szAccountLoadoutStrings</c>
    /// (tf_item_schema.cpp:1573-1577), whose missing comma makes it two rows.
    /// </summary>
    public static int AccountSlotIndex(string word) =>
        string.Equals(word, "quest", StringComparison.OrdinalIgnoreCase) ? 0 : -1;

    /// <summary><c>IsWearableSlot</c> (tf_item_constants.h:131-138): head, misc, action, misc2, the taunts.</summary>
    public static bool IsWearableSlot(int slot) =>
        slot is 7 or 8 or 9 or 10 or (>= 11 and <= 18);

    /// <summary><c>g_AttributeDescriptionFormats</c> (econ_item_schema.cpp:109-123).</summary>
    public static AttributeDescriptionFormat DescriptionFormat(string? word) => word?.ToLowerInvariant() switch
    {
        "value_is_percentage" => AttributeDescriptionFormat.Percentage,
        "value_is_inverted_percentage" => AttributeDescriptionFormat.InvertedPercentage,
        "value_is_additive" => AttributeDescriptionFormat.Additive,
        "value_is_additive_percentage" => AttributeDescriptionFormat.AdditivePercentage,
        "value_is_or" => AttributeDescriptionFormat.Or,
        "value_is_date" => AttributeDescriptionFormat.Date,
        "value_is_account_id" => AttributeDescriptionFormat.AccountId,
        "value_is_particle_index" => AttributeDescriptionFormat.ParticleIndex,
        "value_is_killstreakeffect_index" => AttributeDescriptionFormat.KillstreakEffectIndex,
        "value_is_killstreak_idleeffect_index" => AttributeDescriptionFormat.KillstreakIdleEffectIndex,
        "value_is_item_def" => AttributeDescriptionFormat.ItemDef,
        "value_is_from_lookup_table" => AttributeDescriptionFormat.FromLookupTable,
        _ => AttributeDescriptionFormat.None,
    };

    /// <summary><c>g_EffectTypes</c> (econ_item_schema.cpp:125-132).</summary>
    public static AttributeEffectType EffectType(string? word) => word?.ToLowerInvariant() switch
    {
        "unusual" => AttributeEffectType.Unusual,
        "strange" => AttributeEffectType.Strange,
        "neutral" => AttributeEffectType.Neutral,
        "positive" => AttributeEffectType.Positive,
        "negative" => AttributeEffectType.Negative,
        _ => AttributeEffectType.None,
    };

    /// <summary>
    /// <c>GetAttributeType( name )</c> over the types <c>BInitAttributeTypes</c>
    /// registers (econ_item_schema.cpp:4873-4879): no word is the default
    /// type; an unregistered word is null, which stock refuses (:2208-2210).
    /// The lookup is <c>V_strcmp</c>-exact in stock; kept exact here.
    /// </summary>
    public static AttributeValueType? ValueType(string? word) => word switch
    {
        null => AttributeValueType.Default,
        "float" => AttributeValueType.Float,
        "uint64" => AttributeValueType.UInt64,
        "string" => AttributeValueType.String,
        "dynamic_recipe_component_defined_item" => AttributeValueType.DynamicRecipeComponentDefinedItem,
        "item_slot_criteria" => AttributeValueType.ItemSlotCriteria,
        "item_placement" => AttributeValueType.ItemPlacement,
        _ => null,
    };
}
