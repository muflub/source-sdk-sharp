using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// The local inventories on disk: one JSON file per user at
/// <c>&lt;mod game dir&gt;/inventory/&lt;SteamID64&gt;.json</c>, named by the
/// SteamID and never by the entity index (an index is a seat, reused by the
/// next player). Stock keeps inventories on the GC (D2); this is the
/// offline stand-in.
/// </summary>
[SideService(Side.Server)]
public sealed class InventoryStore(IModInfo mod, ItemSchema schema, ILog log)
{
    /// <summary>The directory under the mod's game directory.</summary>
    public const string DirectoryName = "inventory";

    public string Directory => Path.Combine(mod.GameDir, DirectoryName);

    /// <summary>The file of <paramref name="steamId"/>.</summary>
    public string PathOf(ulong steamId) =>
        Path.Combine(Directory, steamId.ToString(CultureInfo.InvariantCulture) + ".json");

    /// <summary>
    /// The inventory saved for <paramref name="steamId"/>, or an empty one when
    /// there is no file. An item whose definition the schema no longer has is
    /// dropped and said; an unreadable file is said and read as empty (it is
    /// left on disk, untouched, until the next save replaces it).
    /// </summary>
    public PlayerInventory Load(ulong steamId)
    {
        var inventory = new PlayerInventory(steamId);
        string path = PathOf(steamId);
        if (!File.Exists(path))
            return inventory;

        InventoryFile? file;
        try
        {
            file = JsonSerializer.Deserialize<InventoryFile>(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warning($"[TF2/Inventory] {path} is unreadable ({e.Message}); starting {steamId} empty\n");
            return inventory;
        }

        foreach (var item in file?.Items ?? [])
        {
            if (schema.Item(item.Defindex) is not { } def)
            {
                log.Warning($"[TF2/Inventory] {path}: item #{item.Id} names defindex {item.Defindex}, which the schema does not hold; dropped\n");
                continue;
            }

            var attributes = new List<AttributeValue>();
            foreach (var a in item.Attributes ?? [])
            {
                if (schema.Attribute(a.Defindex) is { } attr)
                    attributes.Add(new AttributeValue(attr, a.Bits, a.Text));
            }

            if (item.Id == ItemInstance.InvalidItemId || inventory.Item(item.Id) is not null)
            {
                log.Warning($"[TF2/Inventory] {path}: item id {item.Id} is invalid or repeated; dropped\n");
                continue;
            }
            inventory.Add(new ItemInstance(item.Id, def, item.Quality, attributes));
        }

        foreach (var (className, slots) in file?.Loadout ?? [])
        {
            if (!Enum.TryParse(className, out TFClass cls))
                continue;
            foreach (var (slotName, id) in slots)
            {
                if (Enum.TryParse(slotName, out TFLoadoutSlot slot))
                    inventory.Equip(cls, slot, id);
            }
        }

        return inventory;
    }

    /// <summary>Writes <paramref name="inventory"/> to its file (a temporary file, then a move over the old one).</summary>
    public void Save(PlayerInventory inventory)
    {
        var file = new InventoryFile
        {
            SteamId = inventory.SteamId.ToString(CultureInfo.InvariantCulture),
            Items = inventory.Items.Select(i => new InventoryItem
            {
                Id = i.Id,
                Defindex = i.Defindex,
                Quality = i.Quality,
                Attributes = i.Attributes.Select(a => new InventoryAttribute
                {
                    Defindex = a.Definition.Defindex,
                    Bits = a.Bits,
                    Text = a.Text,
                }).ToList(),
            }).ToList(),
            Loadout = inventory.Equipped
                .GroupBy(e => e.Key.Class)
                .ToDictionary(g => g.Key.ToString(), g => g.ToDictionary(e => e.Key.Slot.ToString(), e => e.Value)),
        };

        System.IO.Directory.CreateDirectory(Directory);
        string path = PathOf(inventory.SteamId);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(file, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    private sealed class InventoryFile
    {
        [JsonPropertyName("steamid")]
        public string SteamId { get; set; } = "";

        [JsonPropertyName("items")]
        public List<InventoryItem>? Items { get; set; }

        [JsonPropertyName("loadout")]
        public Dictionary<string, Dictionary<string, ulong>>? Loadout { get; set; }
    }

    private sealed class InventoryItem
    {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }

        [JsonPropertyName("defindex")]
        public int Defindex { get; set; }

        [JsonPropertyName("quality")]
        public int Quality { get; set; }

        [JsonPropertyName("attributes")]
        public List<InventoryAttribute>? Attributes { get; set; }
    }

    private sealed class InventoryAttribute
    {
        [JsonPropertyName("defindex")]
        public int Defindex { get; set; }

        /// <summary>The value's 32 bits, so a float and a stored_as_integer value both round-trip exactly.</summary>
        [JsonPropertyName("bits")]
        public uint Bits { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
