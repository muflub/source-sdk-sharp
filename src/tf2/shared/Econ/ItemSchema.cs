//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics;
using System.Globalization;
using System.Text;

using SourceSharp.Abi;

namespace TF2.Shared.Econ;

/// <summary>
/// The item schema, one per side: <c>items_game.txt</c> read through the
/// engine's GAME search paths (stock: <c>CEconItemSystem::ParseItemSchemaFile</c>,
/// econ_item_system.cpp:286-299, <c>BInit( name, "GAME" )</c>) and parsed by
/// <see cref="SchemaParser"/>.
///
/// <para>
/// <b>Loaded once, on first use</b>, as stock loads it once in
/// <c>CEconItemSystem::Init</c> (:95-98) rather than per level; the server
/// asks at its first level so the cost and the counts land in the log there
/// (<c>ItemSchemaLevelLoad</c>). A missing or malformed file is said once and
/// kept as <see cref="Failure"/>: every lookup then answers null, and
/// <see cref="IsLoaded"/> tells that apart from an item that is not there.
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class ItemSchema(IEngineFiles files, ILog log)
{
    /// <summary>The schema's path on the GAME search paths (econ_item_system.cpp:98).</summary>
    public const string FilePath = "scripts/items/items_game.txt";

    private ItemSchemaContents? _contents;
    private bool _tried;

    /// <summary>Why the schema is not loaded, once a load was tried and failed; null otherwise.</summary>
    public string? Failure { get; private set; }

    /// <summary>The last successful load's cost, for the log and the dump command.</summary>
    public ItemSchemaLoadStats? Stats { get; private set; }

    public bool IsLoaded => EnsureLoaded();

    /// <summary>Loads the schema the first time it is asked; true when it is loaded.</summary>
    public bool EnsureLoaded()
    {
        if (_tried)
            return _contents is not null;
        _tried = true;

        var clock = Stopwatch.StartNew();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        byte[]? bytes = files.ReadGameFileBytes(FilePath);
        if (bytes is null)
        {
            Fail($"{FilePath} is on no GAME search path: no items, no attributes");
            return false;
        }

        try
        {
            _contents = SchemaParser.Parse(Decode(bytes));
        }
        catch (ItemSchemaException e)
        {
            Fail($"{FilePath} refused: {e.Message}");
            return false;
        }

        clock.Stop();
        Stats = new ItemSchemaLoadStats(bytes.Length, _contents.Items.Count, _contents.Attributes.Count,
            _contents.PrefabCount, _contents.BaseItems.Count, clock.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        foreach (string warning in _contents.Warnings)
            log.Warning($"[TF2/Schema] {warning}\n");
        log.Msg($"[TF2/Schema] {FilePath}: {Stats}\n");
        return true;
    }

    /// <summary><c>GetItemDefinition( defindex )</c>; null for an unknown index or an unloaded schema.</summary>
    public ItemDefinition? Item(int defindex) =>
        EnsureLoaded() && _contents!.Items.TryGetValue(defindex, out var item) ? item : null;

    /// <summary><c>GetItemDefinitionByName</c>: case-insensitive; null for an unknown name or an unloaded schema.</summary>
    public ItemDefinition? Item(string name) =>
        EnsureLoaded() && _contents!.ItemsByName.TryGetValue(name, out var item) ? item : null;

    /// <summary>The base items in stock's walk order (<see cref="ItemSchemaContents.BaseItems"/>); empty when unloaded.</summary>
    public IReadOnlyList<ItemDefinition> BaseItems => EnsureLoaded() ? _contents!.BaseItems : [];

    /// <summary><c>BGetItemQualityFromName</c>'s table: a quality's value by name, or null.</summary>
    public int? Quality(string name) =>
        EnsureLoaded() && _contents!.Qualities.TryGetValue(name, out int value) ? value : null;

    /// <summary>Every item definition, in no particular order; empty when unloaded.</summary>
    public IEnumerable<ItemDefinition> Items =>
        EnsureLoaded() ? _contents!.Items.Values : [];

    /// <summary>
    /// <c>CTFInventoryManager::GetBaseItemForClass</c> (tf_item_inventory.cpp:656-717):
    /// the first base item, in <see cref="BaseItems"/> order, that
    /// <paramref name="cls"/> equips in <paramref name="slot"/>. Null where
    /// stock answers its invalid default item: a class outside
    /// <c>TF_FIRST_NORMAL_CLASS</c>..<c>TF_CLASS_COUNT</c> (Scout..Civilian),
    /// a slot from Head on (cosmetics; the action slot's spellbook, grappling
    /// hook and canteen arms are game-mode items no mode here turns on), or
    /// no match.
    /// </summary>
    public ItemDefinition? BaseItem(TFClass cls, TFLoadoutSlot slot)
    {
        if (!EnsureLoaded())
            return null;
        if (cls < TFClass.Scout || cls >= TFClass.CountAll || slot < TFLoadoutSlot.Primary || slot >= TFLoadoutSlot.Head)
            return null;

        foreach (var item in _contents!.BaseItems)
        {
            if (item.Slot(cls) == slot)
                return item;
        }
        return null;
    }

    /// <summary><c>GetAttributeDefinition( defindex )</c>.</summary>
    public AttributeDefinition? Attribute(int defindex) =>
        EnsureLoaded() && _contents!.Attributes.TryGetValue(defindex, out var def) ? def : null;

    /// <summary><c>GetAttributeDefinitionByName</c>: case-insensitive.</summary>
    public AttributeDefinition? Attribute(string name) =>
        EnsureLoaded() && _contents!.AttributesByName.TryGetValue(name, out var def) ? def : null;

    /// <summary>The definitions hooked by <paramref name="attributeClass"/>; empty for an unknown class.</summary>
    public IReadOnlyList<AttributeDefinition> AttributesOfClass(string attributeClass) =>
        EnsureLoaded() && _contents!.AttributesByClass.TryGetValue(attributeClass, out var defs) ? defs : [];

    private void Fail(string why)
    {
        Failure = why;
        log.Warning($"[TF2/Schema] {why}\n");
    }

    // The file is UTF-8 (item names in several languages); a byte-order mark
    // is not a token.
    private static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            span = span[3..];
        return Encoding.UTF8.GetString(span);
    }
}

/// <summary>What one schema load cost and found.</summary>
public sealed record ItemSchemaLoadStats(int Bytes, int Items, int Attributes, int Prefabs, int BaseItems,
                                         double Milliseconds, long AllocatedBytes)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Items} items, {Attributes} attributes, {Prefabs} prefabs, {BaseItems} base items from {Bytes} bytes in {Milliseconds:F0} ms ({AllocatedBytes / (1024 * 1024)} MiB allocated)");
}
