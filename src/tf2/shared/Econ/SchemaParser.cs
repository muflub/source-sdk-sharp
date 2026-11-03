//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Econ;

/// <summary>
/// What <see cref="SchemaParser.Parse"/> makes of <c>items_game.txt</c>: the
/// three blocks the economy reads (prefabs, attributes, items), indexed.
/// </summary>
public sealed class ItemSchemaContents
{
    internal ItemSchemaContents(Dictionary<int, ItemDefinition> items, ItemDefinition? defaultItem,
                                Dictionary<int, AttributeDefinition> attributes,
                                Dictionary<string, AttributeDefinition> attributesByName,
                                Dictionary<string, AttributeDefinition[]> attributesByClass,
                                int prefabCount, IReadOnlyList<ItemDefinition> baseItems,
                                IReadOnlyDictionary<string, int> qualities, IReadOnlyList<string> warnings)
    {
        Qualities = qualities;
        Items = items;
        ItemsByName = items.Values.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                           .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        DefaultItem = defaultItem;
        Attributes = attributes;
        AttributesByName = attributesByName;
        AttributesByClass = attributesByClass;
        PrefabCount = prefabCount;
        BaseItems = baseItems;
        Warnings = warnings;
    }

    public IReadOnlyDictionary<int, ItemDefinition> Items { get; }

    /// <summary>By <c>name</c>, case-insensitively (<c>GetItemDefinitionByName</c>; names are unique, the parse checks).</summary>
    public IReadOnlyDictionary<string, ItemDefinition> ItemsByName { get; }

    /// <summary>The <c>"default"</c> item (<c>m_pDefaultItemDefinition</c>), or null.</summary>
    public ItemDefinition? DefaultItem { get; }

    public IReadOnlyDictionary<int, AttributeDefinition> Attributes { get; }

    /// <summary>By <c>name</c>, case-insensitively (<c>GetAttributeDefinitionByName</c>, :6780-6797).</summary>
    public IReadOnlyDictionary<string, AttributeDefinition> AttributesByName { get; }

    /// <summary>By <c>attribute_class</c>, case-insensitively, as the game string pool compares hook names.</summary>
    public IReadOnlyDictionary<string, AttributeDefinition[]> AttributesByClass { get; }

    public int PrefabCount { get; }

    /// <summary>The <c>qualities</c> block: each quality's name and its <c>value</c>, by name case-insensitively.</summary>
    public IReadOnlyDictionary<string, int> Qualities { get; }

    /// <summary>
    /// The <c>baseitem</c> items in the order <c>CTFInventoryManager::GenerateBaseItems</c>
    /// (tf_item_inventory.cpp:245-252) lists them: it walks <c>m_mapBaseItems</c>
    /// in key order, but from node 0, the first base item the file inserted,
    /// so a base item whose defindex is below that one's is never listed. The
    /// shipped file is in defindex order and loses none.
    /// </summary>
    public IReadOnlyList<ItemDefinition> BaseItems { get; }

    /// <summary>What stock only asserts on (an unknown prefab name), said rather than dropped.</summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// The schema text is malformed, or names something the schema does not
/// hold. A KeyValues error carries the line; a definition error names the
/// definition. Stock's <c>SCHEMA_INIT_CHECK</c> collects every error and the
/// next <c>SCHEMA_INIT_SUBSTEP</c> fails the whole init (schemainitutils.h:16-37),
/// so a schema with any error is refused whole here too.
/// </summary>
public sealed class ItemSchemaException : Exception
{
    public ItemSchemaException(string message, int line = 0, IReadOnlyList<string>? errors = null)
        : base(line > 0 ? $"line {line}: {message}" : message)
    {
        Line = line;
        Errors = errors ?? [message];
    }

    /// <summary>The 1-based line of a KeyValues error, 0 for a definition error.</summary>
    public int Line { get; }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// <c>CEconItemSchema::BInitSchema</c>'s economy half (econ_item_schema.cpp:4625-4830)
/// over the text form: the prefabs first (<c>BInitDefinitionPrefabs</c>,
/// :4894-4911), then the attribute definitions (<c>BInitAttributes</c>,
/// :5219-5258), then the items (<c>BInitItems</c>, :5269-5432) with the TF
/// overlay (<c>CTFItemDefinition::BInitFromKV</c>). The other blocks
/// (qualities, rarities, loot lists, recipes, …) are GC and store data and
/// are not read (docs/divergences.md).
/// </summary>
public static class SchemaParser
{
    public static ItemSchemaContents Parse(string text)
    {
        KeyValuesText document;
        try
        {
            document = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException e)
        {
            throw new ItemSchemaException(e.Message, e.LineAt(text));
        }

        // LoadFromBuffer makes the first top-level key the schema root,
        // whatever it is called ("items_game").
        if (document.Children.Count == 0 || !document.Children[0].IsBlock)
            throw new ItemSchemaException("the file holds no schema block");

        return new Builder(document.Children[0]).Build();
    }

    private sealed class Builder(KeyValuesText root)
    {
        private readonly List<string> _errors = [];
        private readonly List<string> _warnings = [];
        private readonly Dictionary<string, KeyValuesText> _prefabs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, AttributeDefinition> _attributes = [];
        private readonly Dictionary<string, AttributeDefinition> _attributesByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _prefabStack = [];
        private readonly Dictionary<string, int> _qualities = new(StringComparer.OrdinalIgnoreCase);

        public ItemSchemaContents Build()
        {
            if (root.Find("prefabs") is { } prefabs)
                InitPrefabs(prefabs);

            if (root.Find("qualities") is { } qualities)
                InitQualities(qualities);
            else
                _errors.Add("Required key \"qualities\" missing.");

            if (root.Find("attributes") is { } attributes)
                InitAttributes(attributes);
            else
                _errors.Add("Required key \"attributes\" missing.");

            var items = new Dictionary<int, ItemDefinition>();
            ItemDefinition? defaultItem = null;
            var firstBase = -1;
            if (root.Find("items") is { } itemBlock)
                defaultItem = InitItems(itemBlock, items, ref firstBase);
            else
                _errors.Add("Required key \"items\" missing.");

            if (_errors.Count > 0)
                throw new ItemSchemaException($"{_errors.Count} schema error(s); the first: {_errors[0]}", 0, _errors);

            var byClass = new Dictionary<string, List<AttributeDefinition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in _attributes.Values)
            {
                if (def.AttributeClass is not { Length: > 0 } cls)
                    continue;
                if (!byClass.TryGetValue(cls, out var list))
                    byClass[cls] = list = [];
                list.Add(def);
            }

            // GenerateBaseItems' walk: key order from the first inserted node.
            var baseItems = items.Values.Where(i => i.BaseItem && i.Defindex >= firstBase)
                                 .OrderBy(i => i.Defindex).ToArray();

            return new ItemSchemaContents(items, defaultItem, _attributes, _attributesByName,
                byClass.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                _prefabs.Count, baseItems, _qualities, _warnings);
        }

        // BInitDefinitionPrefabs (:4894-4911): FOR_EACH_TRUE_SUBKEY, a repeated
        // name is an error.
        private void InitPrefabs(KeyValuesText prefabs)
        {
            foreach (var prefab in prefabs.Children)
            {
                if (!prefab.IsBlock)
                    continue;
                if (!_prefabs.TryAdd(prefab.Name, prefab))
                    _errors.Add($"Duplicate prefab name ({prefab.Name})");
            }
        }

        // BInitQualities (:4974-5012): FOR_EACH_TRUE_SUBKEY, keyed by "value";
        // a repeated value or name is an error. Only the name and the value
        // are kept: the rest of a quality is store and GC data.
        private void InitQualities(KeyValuesText qualities)
        {
            var values = new HashSet<int>();
            foreach (var quality in qualities.Children)
            {
                if (!quality.IsBlock)
                    continue;

                int value = quality.GetInt("value", 0);
                if (!values.Add(value))
                    _errors.Add($"Duplicate quality value ({value})");
                else if (!_qualities.TryAdd(quality.Name, value))
                    _errors.Add($"Quality definition {value}: Duplicate quality name {quality.Name}");
            }
        }

        // BGetItemQualityFromName (:6542-6559): "any" is k_unItemQuality_Any;
        // an unknown name leaves the item's quality where it started (Any).
        private int QualityNamed(string? name)
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, "any", StringComparison.OrdinalIgnoreCase))
                return ItemDefinition.QualityAny;
            return _qualities.TryGetValue(name, out int value) ? value : ItemDefinition.QualityAny;
        }

        // BInitAttributes (:5219-5258) with CEconItemAttributeDefinition::BInitFromKV (:2175-2251).
        private void InitAttributes(KeyValuesText attributes)
        {
            foreach (var kv in attributes.Children)
            {
                if (!kv.IsBlock)
                    continue;

                int index = CNumbers.Atoi(kv.Name);
                if (index < 0)
                {
                    _errors.Add($"Attribute definition index {index} must be greater than or equal to zero");
                    continue;
                }
                if (_attributes.ContainsKey(index))
                {
                    _errors.Add($"Duplicate attribute definition index ({index})");
                    continue;
                }

                var keys = MergedKeys.CopyOf(kv);
                string name = keys.GetString("name", "(unnamed)")!;
                if (keys.Find("name") is null)
                    _errors.Add($"Attribute definition {kv.Name}: Missing required field \"name\"");

                string? typeWord = keys.GetString("attribute_type", null);
                if (TFItemStrings.ValueType(typeWord) is not { } valueType)
                {
                    _errors.Add($"Attribute definition {name}: Unable to find attribute data type '{typeWord}'");
                    continue;
                }

                var def = new AttributeDefinition(
                    index, name, keys.GetString("attribute_class", null),
                    TFItemStrings.DescriptionFormat(keys.GetString("description_format", "")),
                    TFItemStrings.EffectType(keys.GetString("effect_type", "")),
                    keys.GetInt("stored_as_integer", 0) != 0,
                    keys.GetInt("hidden", 0) != 0,
                    valueType, keys);

                _attributes[index] = def;
                if (!_attributesByName.TryAdd(name, def))
                    _errors.Add($"Attribute definition {index}: Duplicate name \"{name}\"");
            }
        }

        // BInitItems (:5269-5366): FOR_EACH_TRUE_SUBKEY; "default" is the
        // fallback definition, every other key a defindex.
        private ItemDefinition? InitItems(KeyValuesText block, Dictionary<int, ItemDefinition> items, ref int firstBase)
        {
            ItemDefinition? defaultItem = null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in block.Children)
            {
                if (!kv.IsBlock)
                    continue;

                if (string.Equals(kv.Name, "default", StringComparison.OrdinalIgnoreCase))
                {
                    if (defaultItem is not null)
                        _errors.Add("Duplicate 'default' item definition.");
                    defaultItem = InitItem(kv, 0);
                    continue;
                }

                int index = CNumbers.Atoi(kv.Name);
                if (items.ContainsKey(index))
                {
                    _errors.Add($"Duplicate item definition ({index})");
                    continue;
                }
                if (index < 0)
                {
                    _errors.Add($"Item definition index {index} must be greater than or equal to zero");
                    continue;
                }

                if (InitItem(kv, index) is not { } item)
                    continue;

                items[index] = item;
                if (item.BaseItem && firstBase < 0)
                    firstBase = index;

                // The integrity pass after the items (:5394-5407): names are unique, case-insensitively.
                if (!names.Add(item.Name))
                    _errors.Add($"Item definition {item.Name}: Duplicate name on index {index}");
            }
            return defaultItem;
        }

        private ItemDefinition? InitItem(KeyValuesText kv, int defindex)
        {
            // CEconItemDefinition::BInitFromKV (:3022-3024): a fresh block, the
            // prefabs merged in, then the item's own keys over them.
            var keys = MergedKeys.Block(kv.Name);
            _prefabStack.Clear();
            MergeDefinitionPrefab(keys, kv);

            int errorsBefore = _errors.Count;
            string label = keys.GetString("name", null) ?? kv.Name;

            if (keys.Find("name") is null)
                _errors.Add($"Item definition {kv.Name}: Missing required field \"name\"");
            if (keys.Find("item_class") is null)
                _errors.Add($"Item definition {kv.Name}: Missing required field \"item_class\"");

            // CTFItemDefinition::BInitFromKV (tf_item_schema.cpp:929-955).
            EquipType equipType = EquipType.Class;
            string equipWord = keys.GetString("equip_type", "class")!;
            if (string.Equals(equipWord, "account", StringComparison.OrdinalIgnoreCase))
                equipType = EquipType.Account;
            else if (!string.Equals(equipWord, "class", StringComparison.OrdinalIgnoreCase))
                _errors.Add($"Item definition {defindex} \"{label}\" used uknown equip type: {equipWord}!");

            int defaultSlot = -1;
            string slotWord = keys.GetString("item_slot", "")!;
            if (slotWord.Length > 0)
            {
                if (slotWord == "head")
                    slotWord = "misc";
                defaultSlot = equipType == EquipType.Class
                    ? TFItemStrings.ClassSlotIndex(slotWord)
                    : TFItemStrings.AccountSlotIndex(slotWord);
                if (defaultSlot < 0)
                    _errors.Add($"Item definition {defindex} \"{label}\" used unknown loadout slot: {slotWord}!");
            }

            // used_by_classes (:957-992): each named class takes the default
            // slot, or the slot its value names when the value is not "1".
            var loadoutSlots = new int[TFItemStrings.LoadoutCount];
            Array.Fill(loadoutSlots, -1);
            var usedBy = new bool[TFItemStrings.LoadoutCount];
            if (keys.Find("used_by_classes") is { } classes)
            {
                foreach (var entry in classes.Children)
                {
                    int cls = TFItemStrings.ClassIndex(entry.Name);
                    if (cls < 0)
                        continue;

                    usedBy[cls] = true;
                    loadoutSlots[cls] = defaultSlot;

                    string value = entry.Value;
                    if (value.Length == 0 || value[0] != '1')
                    {
                        int slot = TFItemStrings.ClassSlotIndex(value);
                        if (slot != -1)
                            loadoutSlots[cls] = slot;
                    }
                }
            }

            // :994-1012: never a wearable slot for one class and a weapon slot for another.
            bool wearable = false, nonWearable = false;
            foreach (int slot in loadoutSlots)
            {
                if (slot == -1)
                    continue;
                if (TFItemStrings.IsWearableSlot(slot))
                    wearable = true;
                else
                    nonWearable = true;
            }
            if (wearable && nonWearable)
                _errors.Add($"Item definition {defindex} \"{label}\" used in both wearable and not wearable slots!");

            var playerModels = InitPerClassStrings(keys.Find("model_player_per_class"));
            // "Stomp duplicate properties" (:1057-1061).
            playerModels[0] ??= keys.GetString("model_player", null);

            var attributes = new List<AttributeValue>();
            // Single-line static attributes (econ_item_schema.cpp:3328-3344).
            if (keys.Find("static_attrs") is { } staticAttrs)
            {
                foreach (var entry in staticAttrs.Children)
                {
                    if (AttributeNamed(entry.Name, label, "static_attrs") is { } def)
                        attributes.Add(AttributeValue.Parse(def, entry.Value, multiLine: false));
                }
            }
            // Old-style attribute blocks (:3346-3360).
            if (keys.Find("attributes") is { } attributeBlock)
            {
                foreach (var entry in attributeBlock.Children)
                {
                    if (AttributeNamed(entry.Name, label, "attributes") is { } def)
                        attributes.Add(AttributeValue.Parse(def, entry.GetString("value", null), multiLine: true));
                }
            }

            if (_errors.Count > errorsBefore)
                return null;

            return new ItemDefinition(defindex, keys, equipType, defaultSlot, loadoutSlots, usedBy, playerModels,
                                      attributes.ToArray(), QualityNamed(keys.GetString("item_quality", "")));
        }

        private AttributeDefinition? AttributeNamed(string name, string item, string block)
        {
            if (_attributesByName.TryGetValue(name, out var def))
                return def;
            _errors.Add($"Context '{item}': Attribute \"{name}\" in \"{block}\" did not match any attribute definitions");
            return null;
        }

        // MergeDefinitionPrefab (:2940-2968): the prefab words from LAST to
        // first ("Iterate backwards so adjectives get applied over the noun
        // prefab"), each prefab's own prefabs before its keys, then the
        // source's keys over everything.
        private void MergeDefinitionPrefab(MergedKeys write, KeyValuesText source)
        {
            if (source.GetString("prefab", null) is { } prefabWords)
            {
                string[] names = prefabWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = names.Length - 1; i >= 0; i--)
                {
                    if (!_prefabs.TryGetValue(names[i], out var prefab))
                    {
                        _warnings.Add($"Unable to find prefab \"{names[i]}\" (item {write.Name}).");
                        continue;
                    }

                    // Stock recurses without a guard; a cycle here would never end.
                    if (_prefabStack.Contains(names[i], StringComparer.OrdinalIgnoreCase))
                    {
                        _errors.Add($"Prefab cycle through \"{names[i]}\" (item {write.Name}).");
                        continue;
                    }

                    _prefabStack.Add(names[i]);
                    MergeDefinitionPrefab(write, prefab);
                    _prefabStack.RemoveAt(_prefabStack.Count - 1);
                }
            }

            write.InheritFrom(source);
        }

        // InitPerClassStringArray (tf_item_schema.cpp:489-547): a class's own
        // key, else the "basename" pattern with the usability word (Demoman's
        // models are "demo"); slot 0 is the first class's answer.
        private static string?[] InitPerClassStrings(MergedKeys? perClass)
        {
            var output = new string?[TFItemStrings.LoadoutCount];
            if (perClass is null)
                return output;

            string? baseName = perClass.GetString("basename", null);
            for (int i = (int)TFClass.Scout; i < (int)TFClass.Civilian; i++)
            {
                string word = TFItemStrings.ClassWord(i)!;
                string? value = perClass.GetString(word, null);
                if (string.IsNullOrEmpty(value))
                    value = baseName is null ? null : FormatThree(baseName, i == (int)TFClass.Demoman ? "demo" : word);

                output[i] = value;
                output[0] ??= output[i];
            }
            return output;
        }

        // CFmtStr::sprintf( pszBaseName, word, word, word ): up to three %s.
        private static string FormatThree(string pattern, string word)
        {
            var sb = new System.Text.StringBuilder(pattern.Length + 3 * word.Length);
            int used = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == '%' && i + 1 < pattern.Length && pattern[i + 1] == 's' && used < 3)
                {
                    sb.Append(word);
                    used++;
                    i++;
                    continue;
                }
                sb.Append(pattern[i]);
            }
            return sb.ToString();
        }
    }
}
