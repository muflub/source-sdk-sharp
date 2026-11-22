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
public sealed class BotInventory(ItemSchema schema, IEngineFiles files, IBotPlayers bots, ILog log, IBotItemChoice? choice = null)
    : IGameLifecycle
{
    /// <summary>The table's path on the GAME search paths, beside the schema's.</summary>
    public const string TablePath = "scripts/items/items_working.tsv";

    /// <summary>TF2's stage range (999,000-999,999: below the mods' ModStages.First, as TFClassTable.LoadOrder), its own row beside LocalInventory's (+50).</summary>
    public const int Order = 1_000_000 - 1_000 + 45;

    public int LifecycleOrder => Order;

    // The works rows by (class, slot), in table order; rebuilt on every level
    // so a search-path change is picked up with the map.
    private readonly Dictionary<(TFClass Class, TFLoadoutSlot Slot), List<BotWorksItem>> _works = [];
    private bool _tried;

    public void OnLevelInitPreEntity(string mapName) => Reload();

    /// <summary>
    /// The bot at <paramref name="entIndex"/>'s inventory: per class and slot
    /// one item drawn from its works set by <c>hash(seed, class, slot) %
    /// count</c>, equipped there. Null when the table is on no search path —
    /// the same answer <see cref="LocalInventory.Of"/> gives a client with no
    /// inventory, and the stock kit stands.
    /// </summary>
    public PlayerInventory? ForBot(int entIndex) => ForBot(entIndex, SeedOf(entIndex));

    /// <summary>
    /// The seed of the bot at <paramref name="entIndex"/>: a hash of its name, which the roster gives
    /// it by its slot, so bots draw different loadouts and a replay of the same roster draws the
    /// same ones; a player the bots do not list is seeded by its index.
    /// </summary>
    public ulong SeedOf(int entIndex)
    {
        foreach (BotPlayer bot in bots.Bots)
        {
            if (bot.EntIndex == entIndex)
                return SeedOfName(bot.Name);
        }

        return (ulong)entIndex;
    }

    /// <summary>A bot name's seed: FNV-1a over its characters, the same on every run and machine (no string.GetHashCode).</summary>
    public static ulong SeedOfName(string name)
    {
        unchecked
        {
            ulong hash = 14_695_981_039_346_656_037UL;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 1_099_511_628_211UL;
            }

            return hash;
        }
    }

    /// <summary>The bot at <paramref name="entIndex"/>'s inventory drawn by <paramref name="seed"/> (<see cref="ForBot(int)"/> seeds it by the bot's name).</summary>
    public PlayerInventory? ForBot(int entIndex, ulong seed)
    {
        if (!_tried)
        {
            Reload();
            _tried = true;
        }

        var inventory = new PlayerInventory(0);
        ulong id = 1;
        for (var cls = TFClass.Scout; cls < TFClass.Civilian; cls++)
        {
            for (var slot = TFLoadoutSlot.Primary; slot < TFLoadoutSlot.Count; slot++)
            {
                if (!_works.TryGetValue((cls, slot), out var rows) || rows.Count == 0)
                    continue; // nothing works here: ItemInLoadout answers the stock item itself.

                // The AI's choice for the bot (its archetype's, P7 C.3.1) when it names one of the works rows; else the seeded draw
                // over the rows a draw may give (not a flagged family: BotWorksItem.Flagged). An item the inventory refuses for
                // the slot is said and the draw goes on to the next row, so no refusal leaves the stock item standing unsaid.
                int chosen = choice?.Choose(entIndex, cls, slot, rows) ?? -1;
                List<BotWorksItem> drawable = rows.FindAll(r => !r.Flagged);
                int start = drawable.Count == 0 ? 0 : PickIndex(seed, cls, slot, drawable.Count);
                bool chosenOk = chosen >= 0 && rows.Exists(r => r.Defindex == chosen);
                for (int tried = chosenOk ? -1 : 0; tried < drawable.Count; tried++)
                {
                    int defindex = tried < 0 ? chosen : drawable[(start + tried) % drawable.Count].Defindex;
                    if (schema.Item(defindex) is not { } definition)
                        continue;
                    var item = new ItemInstance(id++, definition, definition.Quality, []);
                    inventory.Add(item);
                    if (inventory.Equip(cls, slot, item.Id))
                        break;
                    Refused++;
                    log.Warning($"[TF2/BotInventory] {definition} refused as {cls}'s {slot}: drawing again\n");
                }
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
            // An item that bars its holder from the objective is no bot's (plan_bots_teams_tf2 P6):
            // live, every bot drew the Rocket Jumper (cannot_pick_up_intelligence, no damage), and
            // on ctf_2fort bots stood on the enemy's intelligence and could not take it.
            if (BarsTheObjective(cols[6]))
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

                // The slot the class takes the item in (stock's GetLoadoutSlot per class), not the column's, which is the
                // item's default: a multi-class shotgun is the Engineer's primary and every other class's secondary.
                var cls = (TFClass)clsIndex;
                var slot = schema.Item(defindex) is { } definition && definition.UsedBy(cls) ? definition.Slot(cls) : (TFLoadoutSlot)slotIndex;
                var key = (cls, slot);
                if (!_works.TryGetValue(key, out var rows))
                    _works[key] = rows = [];
                if (!rows.Exists(r => r.Defindex == defindex))
                    rows.Add(new BotWorksItem(defindex, cols[2], cols[6], cols[5]));
            }
        }
    }

    /// <summary>The works rows a bot of <paramref name="cls"/> may draw for <paramref name="slot"/>, in table order: empty for none.</summary>
    public IReadOnlyList<BotWorksItem> WorksFor(TFClass cls, TFLoadoutSlot slot)
    {
        if (!_tried)
        {
            Reload();
            _tried = true;
        }

        return _works.TryGetValue((cls, slot), out var rows) ? rows : [];
    }

    /// <summary>Items the inventory refused for the slot a draw put them in (each said on the log), since this source was made.</summary>
    public int Refused { get; private set; }

    /// <summary>The attribute class that marks its holder for death while it is out (the Escape Plan, MvM's GRU): only an archetype's choice gives one.</summary>
    public const string MarkForDeath = "self_mark_for_death";

    /// <summary>Whether a row's attribute classes mark its holder for death.</summary>
    public static bool MarksForDeath(string attributes)
        => attributes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(MarkForDeath, StringComparer.Ordinal);

    /// <summary>The attribute class that bars a holder from taking the intelligence (CTFPlayer::IsAllowedToPickUpFlag).</summary>
    public const string NoIntelligence = "cannot_pick_up_intelligence";

    /// <summary>Whether a row's attribute classes (the table's semicolon list) bar its holder from the objective.</summary>
    public static bool BarsTheObjective(string attributes)
        => attributes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(NoIntelligence, StringComparer.Ordinal);

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

/// <summary>A works row a bot may draw: its defindex, its item class and its attribute classes (the table's semicolon list).</summary>
public readonly record struct BotWorksItem(int Defindex, string ItemClass, string Attributes, string Family = "")
{
    /// <summary>
    /// A family a brain must know to use (plan_bots_teams_tf2 C.3.1): a lunchbox, a drink, a shield, a banner, the
    /// builder or a PDA (the table's family column names it after the item class), a worn passive item, or an item that
    /// marks its holder for death while out (the Escape Plan, MvM's GRU: a Soldier's archetype may play one). The seeded
    /// draw gives none of them: only an archetype that plays it chooses one, and the stock item stands otherwise.
    /// </summary>
    public bool Flagged => Family.Contains(' ', StringComparison.Ordinal) || ItemClass.StartsWith("tf_wearable", StringComparison.Ordinal)
                           || BotInventory.MarksForDeath(Attributes);
}

/// <summary>
/// Who chooses a bot's item before the seeded draw (plan_bots_teams_tf2 C.3.1, P7): the AI, which knows the way of
/// playing the bot drew for the class, answers one of the works rows offered, or -1 to leave the slot to the draw. A
/// mod's choice that names no offered row is the draw's too, so only a works item is ever a bot's.
/// </summary>
public interface IBotItemChoice
{
    /// <summary>The defindex, one of <paramref name="works"/>', the bot at <paramref name="entIndex"/> carries in <paramref name="slot"/> as <paramref name="cls"/>; -1 for no choice.</summary>
    int Choose(int entIndex, TFClass cls, TFLoadoutSlot slot, IReadOnlyList<BotWorksItem> works);
}
