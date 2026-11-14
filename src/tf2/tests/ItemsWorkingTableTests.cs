using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The working-items table (<c>game/mod_tf2/scripts/items/items_working.tsv</c>,
/// written by <c>tools/tf2-items-audit</c>): one row per class-equippable
/// weapon-slot item. The facts hold what the audit and the port stages build on:
/// one row per selected schema item (the selection re-derived through
/// <see cref="SchemaParser"/> and <see cref="ItemSchema"/> over the shipped
/// schema file), a status from the four the fill stages use, and evidence rules.
/// The table and the schema are read through the engine file seam the way the
/// class scripts are (<see cref="TFClassTable.ScriptDirectory"/>), with
/// <see cref="EconTestFiles"/> as the seam; <c>SOURCESHARP_ITEMS_TABLE</c>
/// overrides where the table lives when a run points at a copy.
/// </summary>
public sealed class ItemsWorkingTableTests
{
    private const string Header =
        "defindex\tname\titem_class\tslot\tclasses\tfamily\tattributes\tstatus\tevidence";

    /// <summary>How many rows: defindex, name, item_class, slot, classes, family, attributes, status, evidence.</summary>
    private const int Columns = 9;

    /// <summary>The five slots the table covers, as the audit selects them.</summary>
    private static readonly string[] Slots = ["primary", "secondary", "melee", "pda2", "building"];

    /// <summary>The four statuses the audit and fill stages write.</summary>
    private static readonly string[] Statuses = ["works", "partial", "missing", "unknown"];

    private static string TableText()
    {
        string path = Environment.GetEnvironmentVariable("SOURCESHARP_ITEMS_TABLE")
            ?? RepoLayout.Repo("game", "mod_tf2", "scripts", "items", "items_working.tsv");
        Assert.True(File.Exists(path), $"{path} is missing: run tools/tf2-items-audit --write");
        var files = new EconTestFiles().With("scripts/items/items_working.tsv", File.ReadAllText(path));
        return files.ReadGameFile("scripts/items/items_working.tsv")!;
    }

    private static string[][] TableRows()
    {
        string[] lines = TableText().ReplaceLineEndings("\n").Split('\n');
        return lines[..^1].Select(line => line.Split('\t')).ToArray();
    }

    /// <summary>The shipped schema file; the game's content dir holds it.</summary>
    private static string ShippedSchemaPath()
    {
        string path = RepoLayout.Repo("bin", RepoLayout.Config, "content", "tf", "scripts", "items", "items_game.txt");
        Assert.True(File.Exists(path), $"{path} is missing: the game's content dir holds the shipped schema");
        return path;
    }

    /// <summary>The shipped schema over the engine file seam.</summary>
    private static ItemSchema Schema() => EconTestFiles.Schema(File.ReadAllText(ShippedSchemaPath()));

    /// <summary>
    /// The audit's selection: slot one of the five (head reading as misc), an
    /// item_class starting tf_, and a used_by_classes block.
    /// </summary>
    private static bool Selected(ItemDefinition item)
    {
        string slot = (item.Keys.GetString("item_slot", "") ?? "").ToLowerInvariant();
        if (slot == "head")
            slot = "misc";
        if (!Slots.Contains(slot))
            return false;
        if (item.ItemClass is not { Length: > 0 } cls
            || !cls.StartsWith("tf_", StringComparison.OrdinalIgnoreCase))
            return false;
        return item.Keys.Find("used_by_classes") is not null;
    }

    /// <summary>
    /// The shipped script a row's item class wears: the fixture named exactly
    /// by it, else the longest fixture name that is its stem
    /// (<c>tf_weapon_shotgun_hwg</c> under <c>tf_weapon_shotgun</c>, the match
    /// <c>TFWeaponTypes.BaseItemFor</c> makes the other way). Milestone fixtures
    /// first, then the extras set. Null when the game ships no script for it at
    /// all, which is what the fact below reads as no behaviour.
    /// </summary>
    private static string? ShippedScriptFor(string itemClass)
    {
        string[] extraDir = Directory.GetFiles(
                RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons-extra"), "tf_weapon_*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(n => n!)
            .ToArray();

        var candidates = WeaponFixtures.Names()
            .Concat(extraDir)
            .Where(n => MatchesBaseClass(n, itemClass) || MatchesBaseClass(itemClass, n))
            .ToArray();

        // The fixture named exactly by the class wins; the longest stem only
        // breaks ties among the suffixed ones (tf_weapon_shotgun_hwg under
        // tf_weapon_shotgun).
        return candidates.FirstOrDefault(n => string.Equals(n, itemClass, StringComparison.OrdinalIgnoreCase))
            ?? candidates.OrderByDescending(n => n.Length).FirstOrDefault();

        // TFWeaponTypes.BaseItemFor's match: equal, or a stem at an underscore.
        static bool MatchesBaseClass(string longer, string baseClass) =>
            string.Equals(longer, baseClass, StringComparison.OrdinalIgnoreCase)
            || (longer.Length > baseClass.Length
                && longer[baseClass.Length] == '_'
                && longer.StartsWith(baseClass, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The fixture's script, from the milestone dir or the extras dir.</summary>
    private static TFWeaponScript ParseShipped(string fixtureName)
    {
        if (WeaponFixtures.Names().Contains(fixtureName, StringComparer.Ordinal))
            return WeaponFixtures.Parse(fixtureName);

        string path = RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons-extra", fixtureName + ".txt");
        var document = KeyValuesText.Parse(File.ReadAllText(path));
        Assert.NotEmpty(document.Children);
        return TFWeaponScriptParser.Parse(document.Children[0], fixtureName);
    }

    /// <summary>TFWeaponTypes.BaseItemFor's match: equal, or a stem at an underscore.</summary>
    private static bool HasKindRow(string itemClass, IReadOnlyList<ItemDefinition> baseItems)
    {
        foreach (var baseItem in baseItems)
        {
            if (baseItem.ItemClass is not { Length: > 0 } baseClass)
                continue;
            if (string.Equals(itemClass, baseClass, StringComparison.OrdinalIgnoreCase))
                return true;
            if (itemClass.Length > baseClass.Length
                && itemClass[baseClass.Length] == '_'
                && itemClass.StartsWith(baseClass, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The table holds exactly one data row per selected schema item, counted
    /// through the managed parse, and its header is the agreed one.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void TheTableHasOneRowPerSchemaItem()
    {
        string[][] rows = TableRows();
        Assert.Equal(Header, string.Join('\t', rows[0]));

        var schema = Schema();
        Assert.True(schema.IsLoaded);
        int expected = schema.Items.Count(Selected);

        Assert.Equal(expected, rows.Length - 1);
    }

    /// <summary>Every row's status is one of works, partial, missing or unknown.</summary>
    [Fact]
    public void EveryRowsStatusIsOneOfTheFour()
    {
        string[][] rows = TableRows();
        foreach (string[] row in rows[1..])
        {
            Assert.InRange(row.Length, Columns, Columns + 1);
            Assert.Contains(row[7], Statuses);
        }
    }

    /// <summary>
    /// A works row carries evidence, unless it is a stock row with no
    /// behaviour attribute whose item_class has a behaviour row of its own —
    /// the only works rows the audit itself may write.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void NoRowIsWorksWithoutEvidenceExceptNoAttributeStockRows()
    {
        string[][] rows = TableRows();
        var schema = Schema();
        IReadOnlyList<ItemDefinition> baseItems = schema.BaseItems;

        foreach (string[] row in rows[1..])
        {
            if (row.Length < Columns || row[7] != "works")
                continue;
            if (row.Length > 8 && row[8].Length > 0)
                continue;
            Assert.Equal("", row[6]);
            Assert.True(HasKindRow(row[2], baseItems),
                $"{row[0]} {row[1]}: status works with no evidence and no behaviour attributes needs a kind-table row for {row[2]}");
        }
    }

    /// <summary>
    /// A works row's weapon carries a behaviour the game itself can run: the
    /// kind the carried weapon resolves to through the shipped scripts — the
    /// fixture set of <see cref="WeaponFixtures"/>, read the way
    /// <see cref="TFWeaponTypes"/> reads them — is not
    /// <see cref="TFWeaponKind.Inert"/>. A class with neither a shipped script
    /// nor a case in <see cref="TFWeaponKinds.Of"/> falls through to the
    /// no-attack kind, so its works row promises a behaviour the game never
    /// picks up. The exceptions, each justified from stock: a wearable
    /// (<c>tf_wearable*</c>) is an attribute holder whose stock weapon class
    /// adds no attack of its own; the cloak watch (<c>tf_weapon_invis</c>) has
    /// one stock attack which only toggles invisibility through the shared
    /// cloak clock (<c>ActivateInvisibilityWatch</c>, tf_weapon_invis.cpp:206-263),
    /// and that half rides the player's predicted cloak meter here — the watch
    /// itself fires nothing, which is exactly what the no-attack kind says.
    /// Their works rows stand on the no-attack kind.
    /// </summary>
    [Fact]
    public void EveryWorksRowWeaponHasARunningBehaviour()
    {
        foreach (string[] row in TableRows()[1..])
        {
            if (row.Length < Columns || row[7] != "works")
                continue;

            string itemClass = row[2];
            if (DoesNothingOnFireInStock(itemClass))
                continue;

            string? fixture = ShippedScriptFor(itemClass);
            Assert.True(fixture is not null,
                $"{row[0]} {row[1]} ({itemClass}): status works but the shipped scripts hold no script for {itemClass}"
                + " and TFWeaponKinds.Of has no case for its id, so the game picks the no-attack kind for it");
            if (fixture is null)
                return;

            TFWeaponKind kind = TFWeaponKinds.Of(ParseShipped(fixture));
            Assert.True(kind != TFWeaponKind.Inert,
                $"{row[0]} {row[1]} ({itemClass}): status works but its script makes no bullet or melee answer,"
                + " so TFWeaponKinds.Of falls through to the no-attack kind");
        }
    }

    /// <summary>The item classes whose stock weapon really does nothing on fire; see <see cref="EveryWorksRowWeaponHasARunningBehaviour"/>.</summary>
    private static bool DoesNothingOnFireInStock(string itemClass) =>
        itemClass.StartsWith("tf_wearable", StringComparison.OrdinalIgnoreCase)
        || string.Equals(itemClass, "tf_weapon_invis", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first class of the classes column's comma list, matched on the script spellings.</summary>
    private static TFClass FirstClass(string classes)
    {
        string first = classes.Split(',')[0];
        foreach (var c in Enum.GetValues<TFClass>())
        {
            if (string.Equals(TFClassNames.Raw(c), first, StringComparison.OrdinalIgnoreCase)
                || string.Equals(TFClassNames.NonLocalized(c), first, StringComparison.OrdinalIgnoreCase))
                return c;
        }

        throw new ArgumentException($"classes column names no TF class: {classes}");
    }

    /// <summary>
    /// Every works row whose item class is a weapon (not a <c>tf_wearable*</c>,
    /// which is an attribute holder, not a hand-held weapon) reaches the
    /// player's hands by name: through the first class's own translation of the
    /// item class (<see cref="TFItemClassTranslation.Translate"/>), the name has
    /// a catalogue row — the name the kit's give asks with, so the give hands
    /// the unlock out rather than falling through empty-handed. The catalogue is
    /// built the way the kit builds it (the shipped class scripts, the shipped
    /// scripts with their extras, the shipped schema).
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void EveryWorksRowWeaponHasACatalogRowAfterTranslation()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        files.Bytes[ItemSchema.FilePath] = File.ReadAllBytes(ShippedSchemaPath());
        var log = new RecordingLog();
        var table = new TFClassTable(log, files);
        var types = new TFWeaponTypes(new TFWeaponScripts(log, files), table, new ItemSchema(files, log));
        var catalog = new WeaponCatalog(() => [types], log);

        foreach (string[] row in TableRows()[1..])
        {
            if (row.Length < Columns || row[7] != "works")
                continue;

            string itemClass = row[2];
            if (itemClass.StartsWith("tf_wearable", StringComparison.OrdinalIgnoreCase))
                continue;

            string weapon = TFItemClassTranslation.Translate(itemClass, table[FirstClass(row[4])]);
            Assert.True(catalog.Contains(weapon),
                $"{row[0]} {row[1]} ({itemClass}): status works but the catalogue has no row for {weapon},"
                + $" so the kit's give falls through and the player holds nothing [catalogue rows={catalog.Count}]");
        }
    }
}
