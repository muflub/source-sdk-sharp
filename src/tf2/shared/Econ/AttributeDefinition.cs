//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Econ;

/// <summary>
/// <c>attrib_description_format</c>: the order of
/// <c>g_AttributeDescriptionFormats</c> (econ_item_schema.cpp:109-123), so
/// the value is stock's index. <see cref="None"/> is StringFieldToInt's −1
/// for a missing or unknown word, which the hook math leaves untouched
/// (attribute_manager.cpp:625-628).
/// </summary>
public enum AttributeDescriptionFormat
{
    None = -1,
    Percentage = 0,
    InvertedPercentage = 1,
    Additive = 2,
    AdditivePercentage = 3,
    Or = 4,
    Date = 5,
    AccountId = 6,
    ParticleIndex = 7,
    KillstreakEffectIndex = 8,
    KillstreakIdleEffectIndex = 9,
    ItemDef = 10,
    FromLookupTable = 11,
}

/// <summary><c>attrib_effect_types_t</c> (econ_item_schema.h:694-703); −1 for a missing word.</summary>
public enum AttributeEffectType
{
    None = -1,
    Unusual = 0,
    Strange = 1,
    Neutral = 2,
    Positive = 3,
    Negative = 4,
}

/// <summary>
/// The schema's attribute data types (<c>CEconItemSchema::BInitAttributeTypes</c>,
/// econ_item_schema.cpp:4873-4879). The type decides which iterator overload
/// a value reaches: the float hooks see only <see cref="Default"/>
/// (<c>attrib_value_t</c>), the string hooks only <see cref="String"/>.
/// </summary>
public enum AttributeValueType
{
    /// <summary>No <c>attribute_type</c> key: <c>CSchemaAttributeType_Default</c>, 32 bits read as a float or an integer.</summary>
    Default,
    Float,
    UInt64,
    String,
    DynamicRecipeComponentDefinedItem,
    ItemSlotCriteria,
    ItemPlacement,
}

/// <summary>
/// One attribute definition from the schema's <c>attributes</c> block: the
/// fields <c>CEconItemAttributeDefinition::BInitFromKV</c>
/// (econ_item_schema.cpp:2175-2251) reads that this port uses, and the whole
/// block as <see cref="Keys"/> for the fields a later phase wants.
/// </summary>
public sealed class AttributeDefinition
{
    public AttributeDefinition(int defindex, string name, string? attributeClass,
                               AttributeDescriptionFormat descriptionFormat, AttributeEffectType effectType,
                               bool storedAsInteger, bool hidden, AttributeValueType valueType, MergedKeys keys)
    {
        Defindex = defindex;
        Name = name;
        AttributeClass = attributeClass;
        DescriptionFormat = descriptionFormat;
        EffectType = effectType;
        StoredAsInteger = storedAsInteger;
        Hidden = hidden;
        ValueType = valueType;
        Keys = keys;
    }

    public int Defindex { get; }

    /// <summary><c>name</c>, or stock's <c>"(unnamed)"</c>.</summary>
    public string Name { get; }

    /// <summary><c>attribute_class</c>: what <c>CALL_ATTRIB_HOOK_*</c> names; null when absent.</summary>
    public string? AttributeClass { get; }

    public AttributeDescriptionFormat DescriptionFormat { get; }

    public AttributeEffectType EffectType { get; }

    /// <summary><c>stored_as_integer</c>: a <c>static_attrs</c> value is kept as integer bits, not a float.</summary>
    public bool StoredAsInteger { get; }

    public bool Hidden { get; }

    public AttributeValueType ValueType { get; }

    /// <summary>The definition's block as written.</summary>
    public MergedKeys Keys { get; }

    public override string ToString() => $"{Defindex} \"{Name}\"";
}
