using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Econ;
using TF2.Server.Rules;
using TF2.Tests.Player;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The bots' item source (stage F19): <see cref="BotInventory"/> answers a
/// bot's <see cref="PlayerInventory"/> from the works rows of
/// <c>scripts/items/items_working.tsv</c> — no SteamID file, no drops, no
/// ownership — and <see cref="TFLoadoutProvider"/> asks it for bots where a
/// human keeps its <see cref="LocalInventory"/>. The draw is a pure hash of
/// the seed with class and slot, so the same seed replays the same items.
/// The shipped table and the trimmed schema go through the engine file seam
/// the way the class scripts do (<see cref="EconTestFiles"/>); a small
/// crafted table serves the facts about statuses and seeds.
/// </summary>
public sealed class BotInventoryTests
{
    private const int Seat = 3;

    private const string Header =
        "defindex\tname\titem_class\tslot\tclasses\tfamily\tattributes\tstatus\tevidence";

    private static RecordingLog Log() => new();

    /// <summary>The shipped works table over the file seam, as the game's search paths hold it.</summary>
    private static EconTestFiles ShippedTable()
    {
        string path = Environment.GetEnvironmentVariable("SOURCESHARP_ITEMS_TABLE")
            ?? RepoLayout.Repo("game", "mod_tf2", "scripts", "items", "items_working.tsv");
        Assert.True(File.Exists(path), $"{path} is missing: run tools/tf2-items-audit --write");
        return new EconTestFiles().With(BotInventory.TablePath, File.ReadAllText(path));
    }

    private static EconTestFiles CraftedTable(params string[] rows) =>
        new EconTestFiles().With(BotInventory.TablePath,
            string.Join('\n', [Header, .. rows]) + "\n");

    private static BotInventory Source(IEngineFiles files, ItemSchema schema) =>
        new(schema, files, new TestBots(isBot: true), Log());

    /// <summary>Every drawn defindex of this inventory, with the class and slot it answers.</summary>
    private static List<((TFClass Class, TFLoadoutSlot Slot) Key, int Defindex)> Draws(PlayerInventory inventory) =>
        inventory.Equipped.Select(pair => (pair.Key, inventory.Item(pair.Value)!.Defindex)).ToList();

    private sealed class TestBots(bool isBot, params BotPlayer[] bots) : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "the test fake adds no clients";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => isBot;

        public bool Kick(EntityHandle bot, string reason) => false;

        public IReadOnlyList<BotPlayer> Bots { get; } = bots;
    }

    private static BotPlayer Bot(int seat, string name) => new(new EntityHandle((uint)seat), seat, name);

    /// <summary>The round the loadout reads, fixed at one state (the harness of TFPlayerLoadoutTests).</summary>
    private sealed class FixedRound(RoundState state) : ITeamRoundState
    {
        public RoundState State { get; } = state;
        public bool InWaitingForPlayers => false;
        public bool InSetup => false;
        public float MapResetTime => 0f;
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    private sealed class StalemateSettings : ITFStalemateSettings
    {
        public bool MeleeOnly => true;
        public float ChangeClassTime => 20f;
    }

    /// <summary>Every item the bot draw equips answers a works row of the shipped table.</summary>
    [Fact]
    public void ABotsLoadoutHoldsOnlyWorksRows()
    {
        EconTestFiles files = ShippedTable();
        var statuses = new Dictionary<int, string>();
        foreach (var line in files.ReadGameFile(BotInventory.TablePath)!.ReplaceLineEndings("\n").Split('\n'))
        {
            string[] cols = line.Split('\t');
            if (cols.Length >= 8 && cols[0] != "defindex" && int.TryParse(cols[0], out int def))
                statuses[def] = cols[7];
        }

        var schema = EconTestFiles.TrimmedSchema();
        PlayerInventory inventory = Source(files, schema).ForBot(Seat, 0)!;

        Assert.NotEmpty(inventory.Equipped);
        foreach (KeyValuePair<(TFClass, TFLoadoutSlot), ulong> pair in inventory.Equipped)
        {
            ItemInstance item = inventory.Item(pair.Value)!;
            Assert.NotNull(item);
            Assert.True(statuses.TryGetValue(item!.Defindex, out string? status));
            Assert.Equal("works", status);
        }
    }

    /// <summary>
    /// A works item that bars its holder from the intelligence (the Rocket Jumper's
    /// cannot_pick_up_intelligence) is never a bot's, over 200 seeds: live on ctf_2fort every bot
    /// drew it and none could take the flag it stood on.
    /// </summary>
    [Fact]
    public void NoBotIsGivenAnItemThatBarsItFromTheObjective()
    {
        EconTestFiles files = CraftedTable(
            "22\tTF_WEAPON_PISTOL\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t",
            // The fixture schema has no Rocket Jumper (237): another of its items carries the Jumper's attribute classes here.
            "23\tTF_WEAPON_PISTOL_SCOUT\ttf_weapon_pistol\tsecondary\tscout\t\tmult_dmg;no_self_blast_dmg;cannot_pick_up_intelligence\tworks\t");
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);
        var drawn = new HashSet<int>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            foreach (((TFClass Class, TFLoadoutSlot Slot) key, int defindex) in Draws(source.ForBot(Seat, seed)!))
                drawn.Add(defindex);
        }

        // What is drawn, not what the loadout would answer for it: the schema's class check stands behind the draw.
        Assert.Equal([22], drawn);
    }

    /// <summary>
    /// The negative: a partial row alongside the works row of one class and slot
    /// never comes between the bot and its works items, over 200 seeds.
    /// </summary>
    [Fact]
    public void NoDrawnItemIsPartialOrMissing()
    {
        EconTestFiles files = CraftedTable(
            "22\tTF_WEAPON_PISTOL\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t",
            "23\tTF_WEAPON_PISTOL_SCOUT\ttf_weapon_pistol\tsecondary\tscout\t\t\tpartial\tF19 fixture");
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);

        for (ulong seed = 0; seed < 200; seed++)
        {
            ItemInstance? item = source.ForBot(Seat, seed)!.ItemInLoadout(TFClass.Scout, TFLoadoutSlot.Secondary, schema);
            Assert.NotNull(item);
            Assert.Equal(22, item!.Defindex); // 23 is the partial row: never drawn.
        }
    }

    /// <summary>The same seed draws the same items: the replay holds without a System.Random.</summary>
    [Fact]
    public void TheSameSeedDrawsTheSameItems()
    {
        EconTestFiles files = ShippedTable();
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);

        var first = Draws(source.ForBot(Seat, 7)!);
        var second = Draws(source.ForBot(Seat, 7)!);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    /// <summary>Over 50 seeds, a slot with two works rows answers both: the seed reaches the draw.</summary>
    [Fact]
    public void DifferentSeedsDrawDifferentItemsSomewhere()
    {
        EconTestFiles files = CraftedTable(
            "22\tTF_WEAPON_PISTOL\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t",
            "23\tTF_WEAPON_PISTOL_SCOUT\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t");
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);

        var seen = new HashSet<int>();
        for (ulong seed = 0; seed < 50; seed++)
            seen.Add(source.ForBot(Seat, seed)!.ItemInLoadout(TFClass.Scout, TFLoadoutSlot.Secondary, schema)!.Defindex);

        Assert.Equal(2, seen.Count);
    }

    /// <summary>
    /// Through the loadout provider each bot draws by its own name: eight bots with the roster's
    /// names draw both of a slot's two works items. Live every TF2 bot was seeded 0 and all shared
    /// one loadout.
    /// </summary>
    [Fact]
    public void BotsWithDifferentNamesDrawDifferentLoadouts()
    {
        EconTestFiles files = CraftedTable(
            "22\tTF_WEAPON_PISTOL\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t",
            "23\tTF_WEAPON_PISTOL_SCOUT\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t");
        var schema = EconTestFiles.TrimmedSchema();
        string[] names = ["Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel"];
        var bots = new TestBots(true, [.. names.Select((name, i) => Bot(i + 1, name))]);
        var source = new BotInventory(schema, files, bots, Log());
        var human = new LocalInventory(
            new InventoryStore(new ModInfo(_ => null), schema, Log()), schema, new TFWeaponOwnersTests.NoClients(), Log());
        var provider = LoadoutDeps.Loadout(human, schema, botInventory: source, bots: bots);

        var drawn = new HashSet<int>();
        for (int seat = 1; seat <= names.Length; seat++)
            drawn.Add(Assert.Single(provider.Loadout(seat, TFClass.Scout), e => e.Slot == TFLoadoutSlot.Secondary).Item.Defindex);

        Assert.Equal([22, 23], drawn.Order());
    }

    /// <summary>A bot's draw follows its name, not the seat it was given: the same roster replays the same items.</summary>
    [Fact]
    public void ABotsDrawFollowsItsNameNotItsSeat()
    {
        EconTestFiles files = ShippedTable();
        var schema = EconTestFiles.TrimmedSchema();
        var first = new BotInventory(schema, files, new TestBots(true, Bot(3, "Delta")), Log());
        var second = new BotInventory(schema, files, new TestBots(true, Bot(9, "Delta")), Log());

        Assert.Equal(BotInventory.SeedOfName("Delta"), first.SeedOf(3));
        Assert.Equal(Draws(first.ForBot(3)!), Draws(second.ForBot(9)!));
    }

    /// <summary>A class and slot whose rows are all partial or missing answers the stock item, owning nothing.</summary>
    [Fact]
    public void AnEmptyWorksSetFallsBackToTheStockItem()
    {
        EconTestFiles files = CraftedTable();
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);

        PlayerInventory inventory = source.ForBot(Seat, 0)!;

        Assert.Empty(inventory.Items);
        ItemInstance? item = inventory.ItemInLoadout(TFClass.Soldier, TFLoadoutSlot.Primary, schema);
        Assert.NotNull(item);
        Assert.True(item!.IsBaseItem);
        Assert.Equal(schema.BaseItem(TFClass.Soldier, TFLoadoutSlot.Primary)!.Defindex, item.Defindex);
    }

    /// <summary>
    /// The mode restriction binds the bot's drawn items too: in a melee-only
    /// sudden death only the melee slot's drawn item survives; the rest is the
    /// stock kit, as for a human (tf_player.cpp:4441-4448).
    /// </summary>
    [Fact]
    public void ItemIsAllowedStillRefusesForABot()
    {
        EconTestFiles files = CraftedTable(
            "22\tTF_WEAPON_PISTOL\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t",
            "10\tTF_WEAPON_SHOTGUN_SOLDIER\ttf_weapon_shotgun\tsecondary\tsoldier\t\t\tworks\t",
            "6\tTF_WEAPON_SHOVEL\ttf_weapon_shovel\tmelee\tsoldier\t\t\tworks\t");
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);
        var bots = new TestBots(isBot: true);
        var human = new LocalInventory(
            new InventoryStore(new ModInfo(_ => null), schema, Log()), schema, new TFWeaponOwnersTests.NoClients(), Log());

        var open = LoadoutDeps.Loadout(human, schema, botInventory: source, bots: bots);
        Assert.Equal([TFLoadoutSlot.Primary, TFLoadoutSlot.Secondary, TFLoadoutSlot.Melee],
                     open.Loadout(Seat, TFClass.Soldier).Select(e => e.Slot));

        var stalemate = LoadoutDeps.Loadout(human, schema,
            round: new FixedRound(RoundState.Stalemate), stalemate: new StalemateSettings(), botInventory: source, bots: bots);
        LoadoutItem melee = Assert.Single(stalemate.Loadout(Seat, TFClass.Soldier));
        Assert.Equal(TFLoadoutSlot.Melee, melee.Slot);
        Assert.Equal(6, melee.Item.Defindex); // the drawn shovel survives; the drawn gun does not.
    }

    /// <summary>A human player keeps its local inventory: the bot source does not answer for it.</summary>
    [Fact]
    public void AHumansLoadoutIsUntouched()
    {
        EconTestFiles files = CraftedTable(
            "18\tTF_WEAPON_ROCKETLAUNCHER\ttf_weapon_rocketlauncher\tprimary\tsoldier\t\t\tworks\t");
        var schema = EconTestFiles.TrimmedSchema();
        BotInventory source = Source(files, schema);
        var bots = new TestBots(isBot: false);
        var human = new LocalInventory(
            new InventoryStore(new ModInfo(_ => null), schema, Log()), schema, new TFWeaponOwnersTests.NoClients(), Log());
        human.OnClientActive(Seat);
        ItemInstance? cow = human.Grant(Seat, 441);
        Assert.NotNull(cow);
        Assert.True(human.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, cow!.Id));

        var provider = LoadoutDeps.Loadout(human, schema, botInventory: source, bots: bots);
        LoadoutItem primary = Assert.Single(provider.Loadout(Seat, TFClass.Soldier), e => e.Slot == TFLoadoutSlot.Primary);

        Assert.Equal(441, primary.Item.Defindex); // the human's own equipped item, not the table's 18.
        Assert.False(primary.Item.IsBaseItem);
    }
}
