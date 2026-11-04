using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// The bots' item source (stage F19): a bot's loadout comes from an
/// in-memory source, not from the SteamID inventory on disk. Every item the
/// bot's class can equip whose row in <c>scripts/items/items_working.tsv</c>
/// is <c>works</c> is eligible; one per class and slot is drawn by a seeded
/// hash, so the same seed gives the same items and a replay holds (no
/// <c>System.Random</c>: its draw order would not). A class and slot whose
/// works set is empty answers the stock item, as <see
/// cref="PlayerInventory.ItemInLoadout"/> does for a player with nothing
/// equipped. <see cref="TFLoadoutProvider.ItemIsAllowed(ItemInstance, TFLoadoutSlot, TFClass)"/>
/// still runs over what is drawn.
/// </summary>
[SideService(Side.Server)]
public sealed class BotInventory(ItemSchema schema, IEngineFiles files, IBotPlayers bots, ILog log)
    : IGameLifecycle
{
    /// <summary>The table's path on the GAME search paths, beside the schema's.</summary>
    public const string TablePath = "scripts/items/items_working.tsv";

    /// <summary>TF2's stage range (999,000-999,999: below the mods' ModStages.First, as TFClassTable.LoadOrder), its own row beside LocalInventory's (+50).</summary>
    public const int Order = 1_000_000 - 1_000 + 45;

    public int LifecycleOrder => Order;

    // The works rows by (class, slot), in table order; rebuilt on every level
    // so a search-path change is picked up with the map.
    private readonly Dictionary<(TFClass Class, TFLoadoutSlot Slot), List<int>> _works = [];
    private bool _tried;

    public void OnLevelInitPreEntity(string mapName) => Reload();

    /// <summary>
    /// The bot at <paramref name="entIndex"/>'s inventory: per class and slot
    /// one item drawn from its works set by <c>hash(seed, class, slot) %
    /// count</c>, equipped there. Null when the table is on no search path —
    /// the same answer <see cref="LocalInventory.Of"/> gives a client with no
    /// inventory, and the stock kit stands.
    /// </summary>
    public PlayerInventory? ForBot(int entIndex, ulong seed)
    {
        if (!_tried)
        {
            Reload();
            _tried = true;
        }

        // The seed is the roster's SeedIndex later (the AI binding); until it
        // supplies one the provider passes 0 for every bot.
        var inventory = new PlayerInventory(0);
        ulong id = 1;
        for (var cls = TFClass.Scout; cls < TFClass.Civilian; cls++)
        {
            for (var slot = TFLoadoutSlot.Primary; slot < TFLoadoutSlot.Count; slot++)
            {
                if (!_works.TryGetValue((cls, slot), out var rows) || rows.Count == 0)
                    continue; // nothing works here: ItemInLoadout answers the stock item itself.

                var definition = schema.Item(rows[PickIndex(seed, cls, slot, rows.Count)]);
                if (definition is null)
                    continue;

                var item = new ItemInstance(id++, definition, definition.Quality, []);
                inventory.Add(item);
                inventory.Equip(cls, slot, item.Id);
            }
        }

        string name = "bot";
        foreach (BotPlayer bot in bots.Bots)
        {
            if (bot.EntIndex == entIndex)
            {
                name = bot.Name;
                break;
            }
        }
        log.Msg($"[TF2/BotInventory] {name} ({entIndex}, seed {seed}): {inventory.Items.Count} items\n");
        return inventory;
    }

    /// <summary>
    /// Reads the table: the defindex of every row whose status is works,
    /// under each class of its classes column and its slot.
    /// </summary>
    private void Reload()
    {
        _works.Clear();
        if (files.ReadGameFile(TablePath) is not { } text)
        {
            log.Warning($"[TF2/BotInventory] {TablePath} is on no GAME search path: bots get the stock kit\n");
            return;
        }

        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Length == 0)
                continue;

            string[] cols = line.Split('\t');
            if (cols.Length < 8 || cols[0] == "defindex" || cols[7] != "works")
                continue;
            if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int defindex))
                continue;
            int slotIndex = TFItemStrings.ClassSlotIndex(cols[3]);
            if (slotIndex < 0)
                continue;

            foreach (var word in cols[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int clsIndex = TFItemStrings.ClassIndex(word);
                if (clsIndex < (int)TFClass.Scout || clsIndex >= (int)TFClass.Civilian)
                    continue;

                var key = ((TFClass)clsIndex, (TFLoadoutSlot)slotIndex);
                if (!_works.TryGetValue(key, out var rows))
                    _works[key] = rows = [];
                if (!rows.Contains(defindex))
                    rows.Add(defindex);
            }
        }
    }

    /// <summary>
    /// The draw: a hash of the seed with the class and slot, mixed enough
    /// that neighbouring seeds move the pick. Pure, so a replay of the same
    /// seed replays the same items.
    /// </summary>
    private static int PickIndex(ulong seed, TFClass cls, TFLoadoutSlot slot, int count)
    {
        unchecked
        {
            int hash = (int)(seed * 486_181_375L) + (int)cls * 31 + (int)slot * 7;
            return (hash & 0x7FFF_FFFF) % count;
        }
    }
}
