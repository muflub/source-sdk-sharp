//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Econ;

/// <summary><c>EEquipType_t</c>'s two schema words (tf_item_schema.cpp:929-938).</summary>
public enum EquipType
{
    Class,
    Account,
}

/// <summary>One <c>animation_on_wearable_t</c> row (econ_item_schema.h:904-909):
/// an activity a wearable's visuals rename, and its replacement.</summary>
public readonly record struct ActivityReplacement(string Activity, string Replacement);

/// <summary>
/// One item definition after its prefabs are merged: the fields
/// <c>CEconItemDefinition::BInitFromKV</c> (econ_item_schema.cpp:3020-3395)
/// and the TF overlay <c>CTFItemDefinition::BInitFromKV</c>
/// (tf_item_schema.cpp:918-1085) read that this port uses. Every other key is
/// in <see cref="Keys"/>, the merged copy stock keeps as <c>m_pKVItem</c>.
/// </summary>
public sealed class ItemDefinition
{
    /// <summary><c>k_unItemQuality_Any</c> (econ_item_constants.h): the quality of an item that names none.</summary>
    public const int QualityAny = 0xFF;

    private readonly int[] _loadoutSlots;
    private readonly string?[] _playerModels;
    private readonly AttributeValue[] _attributes;

    // Style 0's per-team skins, the CTFStyleInfo arrays: [RED, BLU]. The
    // player-skin arm defaults to 0 (tf_item_schema.cpp:1154-1155), the
    // viewmodel arm to -1 (:1157-1158), the common "skin"/"v_skin" over both
    // (:2831-2847, applied after the per-team words).
    private bool _hasStyles;
    private readonly int[] _styleSkins = [0, 0];
    private readonly int[] _styleViewmodelSkins = [-1, -1];
    private ActivityReplacement[] _activityReplacements = [];

    internal ItemDefinition(int defindex, MergedKeys keys, EquipType equipType, int defaultSlot,
                            int[] loadoutSlots, bool[] usedBy, string?[] playerModels,
                            AttributeValue[] attributes, int quality)
    {
        Defindex = defindex;
        Keys = keys;
        EquipType = equipType;
        DefaultSlotIndex = defaultSlot;
        _loadoutSlots = loadoutSlots;
        _playerModels = playerModels;
        _attributes = attributes;
        Quality = quality;

        var classes = new List<TFClass>();
        for (int i = 0; i < usedBy.Length; i++)
        {
            if (usedBy[i])
                classes.Add((TFClass)i);
        }
        UsedByClasses = classes;

        Name = keys.GetString("name", null) ?? string.Empty;
        ItemClass = keys.GetString("item_class", null);
        ItemName = keys.GetString("item_name", "") ?? string.Empty;
        Enabled = keys.GetBool("enabled");
        BaseItem = keys.GetInt("baseitem", 0) != 0;
        Hidden = keys.GetInt("hidden", 0) != 0;
        AttachToHands = keys.GetInt("attach_to_hands", 0) != 0;
        ModelPlayer = keys.GetString("model_player", null);
        ModelWorld = keys.GetString("model_world", null);

        // The base "visuals" section is team 0's (econ_item_schema.cpp:77-84),
        // and its styles are the only valid ones (:2686-2693): style 0's
        // per-team skins are the item's own answer to
        // <see cref="ViewmodelSkin"/>.
        if (keys.Find("visuals")?.Find("styles") is { } styles && styles.Find("0") is { } style0)
        {
            _hasStyles = true;
            _styleSkins = [style0.GetInt("skin_red", 0), style0.GetInt("skin_blu", 0)];
            _styleViewmodelSkins = [style0.GetInt("v_skin_red", -1), style0.GetInt("v_skin_blu", -1)];

            int common = style0.GetInt("skin", -1);
            if (common != -1)
                _styleSkins = [common, common];
            int commonViewmodel = style0.GetInt("v_skin", -1);
            if (commonViewmodel != -1)
                _styleViewmodelSkins = [commonViewmodel, commonViewmodel];
        }

        // The base visuals' animation_replacement rows, parse order preserved
        // (econ_item_schema.cpp:2551-2558): the activity a viewmodel ask is
        // rewritten to before the role tables see it. Stock keeps them per
        // team and falls back to this block for a team with no visuals of
        // its own (GetBestVisualTeamData, econ_item_schema.h:2240-2253); the
        // per-team blocks are not parsed here, so the base rows answer for
        // every team, as that fallback does for an item without them.
        if (keys.Find("visuals")?.Find("animation_replacement") is { } replacements
            && replacements.Children.Count > 0)
        {
            var rows = new List<ActivityReplacement>(replacements.Children.Count);
            foreach (var child in replacements.Children)
            {
                if (!child.IsBlock && child.Leaf?.AsString(null) is { Length: > 0 } replacement)
                    rows.Add(new ActivityReplacement(child.Name, replacement));
            }

            if (rows.Count > 0)
                _activityReplacements = rows.ToArray();
        }
    }

    public int Defindex { get; }

    /// <summary><c>name</c>: the definition name (<c>TF_WEAPON_SCATTERGUN</c>).</summary>
    public string Name { get; }

    /// <summary><c>item_class</c>: the entity class the item spawns as (<c>tf_weapon_scattergun</c>).</summary>
    public string? ItemClass { get; }

    /// <summary><c>item_name</c>: the localisation token.</summary>
    public string ItemName { get; }

    public bool Enabled { get; }

    /// <summary><c>baseitem</c>: a stock item every player owns (the default loadout).</summary>
    public bool BaseItem { get; }

    public bool Hidden { get; }

    public bool AttachToHands { get; }

    /// <summary><c>model_player</c> (<c>m_pszBaseDisplayModel</c>).</summary>
    public string? ModelPlayer { get; }

    public string? ModelWorld { get; }

    public EquipType EquipType { get; }

    /// <summary>
    /// <c>m_iDefaultLoadoutSlot</c>: the <c>item_slot</c> word's index in the
    /// equip type's table, −1 when there is none. A class item's is a
    /// <see cref="TFLoadoutSlot"/>; an account item's indexes the account table.
    /// </summary>
    public int DefaultSlotIndex { get; }

    /// <summary>The default slot of a class item, <see cref="TFLoadoutSlot.Invalid"/> for none.</summary>
    public TFLoadoutSlot DefaultSlot => EquipType == EquipType.Class ? (TFLoadoutSlot)DefaultSlotIndex : TFLoadoutSlot.Invalid;

    /// <summary>The classes <c>used_by_classes</c> names, in class order.</summary>
    public IReadOnlyList<TFClass> UsedByClasses { get; }

    /// <summary>The static attributes, <c>static_attrs</c> first, then <c>attributes</c> (econ_item_schema.cpp:3328-3360).</summary>
    public IReadOnlyList<AttributeValue> Attributes => _attributes;

    /// <summary><see cref="Attributes"/> as a span, for the hooks' allocation-free walk.</summary>
    public ReadOnlySpan<AttributeValue> StaticAttributes => _attributes;

    /// <summary>
    /// <c>item_quality</c>'s value in the schema's <c>qualities</c> block
    /// (<c>BGetItemQualityFromName</c>, econ_item_schema.cpp:6542-6559), or
    /// <see cref="QualityAny"/> when the item names none or an unknown one, as
    /// stock's member keeps its initial value then.
    /// </summary>
    public int Quality { get; }

    /// <summary>The merged keys: every field a later phase reads that this class has no property for.</summary>
    public MergedKeys Keys { get; }

    /// <summary>
    /// The item's own skin for a team's VIEWMODEL: style 0's
    /// <c>v_skin_red</c>/<c>v_skin_blu</c> where set, else its
    /// <c>skin_red</c>/<c>skin_blu</c> — <c>CEconItemView::GetSkin</c>'s style
    /// arm with <c>bViewmodel</c> true (econ_item_view.cpp:975-1001 through
    /// <c>CEconStyleInfo::GetSkin</c>, econ_item_schema.h:993-1005). Null for
    /// an item with no styles block (its skins are none of the schema's
    /// business: the weapon script's team-skins flag rules instead) and for a
    /// team neither red nor blue.
    /// </summary>
    public int? ViewmodelSkin(TFTeam team)
    {
        int index = team switch
        {
            TFTeam.Red => 0,
            TFTeam.Blue => 1,
            _ => -1,
        };

        if (index < 0 || !_hasStyles)
            return null;

        return _styleViewmodelSkins[index] != -1 ? _styleViewmodelSkins[index] : _styleSkins[index];
    }

    /// <summary>
    /// The item's animation_replacement answer for an activity, stock's
    /// <c>CEconItemDefinition::GetActivityOverride( iTeam, const char* )</c>
    /// (econ_item_schema.cpp:3600-3611): the FIRST row tagged
    /// <paramref name="activity"/>'s name, its replacement. Null when the
    /// item names no row for it.
    /// </summary>
    public string? ActivityOverride(string activity)
    {
        foreach (var row in _activityReplacements)
        {
            if (string.Equals(row.Activity, activity, StringComparison.OrdinalIgnoreCase))
                return row.Replacement;
        }

        return null;
    }

    /// <summary><c>CanBeUsedByClass</c> (tf_item_schema.h:259) for a playable class.</summary>
    public bool UsedBy(TFClass cls)
    {
        foreach (var c in UsedByClasses)
        {
            if (c == cls)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>CTFItemDefinition::GetLoadoutSlot</c> (tf_item_schema.cpp:1271-1282):
    /// the slot <paramref name="cls"/> equips this in; the default slot for a
    /// class outside 1..10, <see cref="TFLoadoutSlot.Invalid"/> for a class
    /// that cannot use it.
    /// </summary>
    public TFLoadoutSlot Slot(TFClass cls)
    {
        int i = (int)cls;
        if (i <= 0 || i >= TFItemStrings.LoadoutCount)
            return (TFLoadoutSlot)DefaultSlotIndex;
        return (TFLoadoutSlot)_loadoutSlots[i];
    }

    /// <summary>
    /// <c>GetPlayerDisplayModel( iClass )</c> (tf_item_schema.h:263): the
    /// <c>model_player_per_class</c> entry, else (class 0) <c>model_player</c>.
    /// Null for a class with neither.
    /// </summary>
    public string? PlayerModel(TFClass cls)
    {
        int i = (int)cls;
        return i >= 0 && i < _playerModels.Length ? _playerModels[i] : null;
    }

    public override string ToString() => $"{Defindex} {Name}";
}
