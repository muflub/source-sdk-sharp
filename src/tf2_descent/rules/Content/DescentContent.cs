using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Death;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Content;

/// <summary>
/// Every content table, loaded and validated (plan_tf2_rpg 1b, plan_descent_logic RU1).
/// Instance state: the rules build one in their constructor and hand it to what reads it,
/// because a mod holds no static state (SS0010).
/// </summary>
public sealed class DescentContent
{
    private readonly IReadOnlyDictionary<string, object> _tables;

    internal DescentContent(IReadOnlyDictionary<string, object> tables, string hash)
    {
        _tables = tables;
        Hash = hash;
    }

    /// <summary>
    /// SHA-256, lower-case hex, over the canonical bytes of every table in
    /// <see cref="ContentTables.All"/>'s order. The game and the host each log it, so a
    /// mismatch between what they replay is visible.
    /// </summary>
    public string Hash { get; }

    public bool TryGet<T>(string table, out T value) where T : class
    {
        if (_tables.TryGetValue(table, out var v) && v is T t)
        {
            value = t;
            return true;
        }
        value = null!;
        return false;
    }

    public T Get<T>(string table) where T : class =>
        TryGet<T>(table, out var v) ? v : throw new InvalidOperationException($"content table '{table}' did not load");

    public Table<StatRow> Stats => Get<Table<StatRow>>(ContentTables.Stats);
    public Table<AttributeRow> Attributes => Get<Table<AttributeRow>>(ContentTables.Attributes);
    public Table<ClassRow> Classes => Get<Table<ClassRow>>(ContentTables.Classes);
    public HeroSettings Hero => Get<HeroSettings>(ContentTables.Hero);
    public DeathSettings Death => Get<DeathSettings>(ContentTables.Death);
    public BestiarySettings Bestiary => Get<BestiarySettings>(ContentTables.Bestiary);
    public FurnitureSettings Furniture => Get<FurnitureSettings>(ContentTables.Furniture);
    public RobotRulesSettings RobotRules => Get<RobotRulesSettings>(ContentTables.RobotRules);
    public Table<RobotTemplate> Robots => Get<Table<RobotTemplate>>(ContentTables.Robots);
    public Table<ChampionAffix> Champions => Get<Table<ChampionAffix>>(ContentTables.Champions);
    public Table<DepthRow> Levels => Get<Table<DepthRow>>(ContentTables.Levels);
    public Table<StatusRow> Status => Get<Table<StatusRow>>(ContentTables.Status);

    /// <summary>The four damage types, in file order; look one up by id with the table's indexer or TryGet.</summary>
    public Table<DamageTypeRow> DamageTypes => Get<Table<DamageTypeRow>>(ContentTables.DamageTypes);

    /// <summary>The weapon families, in file order; look one up by id with the table's indexer or TryGet.</summary>
    public Table<FamilyRow> Families => Get<Table<FamilyRow>>(ContentTables.Families);

    /// <summary>The shared numbers of skills and spells (tier levels, rank limits, the action damage formula).</summary>
    public Skills.SkillSettings SkillSettings => Get<Skills.SkillSettings>(ContentTables.SkillSettings);

    /// <summary>The skill tabs: each class's three and the shared Mercenary tab, with the attribute an action of the tab scales with.</summary>
    public Table<Skills.SkillTabRow> SkillTabs => Get<Table<Skills.SkillTabRow>>(ContentTables.SkillTabs);

    /// <summary>Every skill of every tree, and the Mercenary tab's.</summary>
    public Table<Skills.SkillDef> Skills => Get<Table<Skills.SkillDef>>(ContentTables.Skills);

    /// <summary>The cross-class spells taught by tomes and cast from scrolls (R27).</summary>
    public Table<Spells.SpellDef> Spells => Get<Table<Spells.SpellDef>>(ContentTables.Spells);

    /// <summary>
    /// The ids and display names of every item base: the rows of any loaded table whose rows
    /// are <see cref="IItemBaseRow"/>. Empty while no such table is loaded.
    /// </summary>
    public ItemBaseKeys KnownItemBases()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in _tables.Values.OfType<IRowSource>())
            foreach (var row in table.UntypedRows.OfType<IItemBaseRow>())
            {
                ids.Add(row.Id);
                names.Add(ItemBaseKeys.Normal(row.Name));
            }
        return new ItemBaseKeys(ids, names);
    }

    /// <summary>The family a TF item class (items_working.tsv's item_class) belongs to; false for a class no family holds.</summary>
    public bool TryGetFamilyOfItemClass(string itemClass, out FamilyRow family)
    {
        foreach (var f in Families.Rows)
            if (f.ItemClasses.Contains(itemClass, StringComparer.Ordinal))
            {
                family = f;
                return true;
            }
        family = null!;
        return false;
    }

    /// <summary>The TF attributes an affix may grant (RU3).</summary>
    public Table<TFAttributeRow> TFAttributes => Get<Table<TFAttributeRow>>(ItemTables.TFAttributes);

    /// <summary>The rarity ladder, in tier order (RU3, D5).</summary>
    public Table<RarityRow> Rarities => Get<Table<RarityRow>>(ItemTables.Rarities);

    /// <summary>The item bases (RU3).</summary>
    public Table<ItemBaseRow> ItemBases => Get<Table<ItemBaseRow>>(ItemTables.Bases);

    /// <summary>The affix pools, positive and cursed (RU3, R28).</summary>
    public Table<AffixRow> Affixes => Get<Table<AffixRow>>(ItemTables.Affixes);

    /// <summary>The unusual effects (RU3).</summary>
    public Table<UnusualEffectRow> UnusualEffects => Get<Table<UnusualEffectRow>>(ItemTables.UnusualEffects);

    /// <summary>The hand-authored Australium uniques (RU3).</summary>
    public Table<UniqueRow> Uniques => Get<Table<UniqueRow>>(ItemTables.Uniques);

    /// <summary>The roll's numbers (RU3).</summary>
    public ItemSettings ItemSettings => Get<ItemSettings>(ItemTables.Items);

    /// <summary>Wear rates, repair prices and the field repair (RU8).</summary>
    public DurabilitySettings DurabilitySettings => Get<DurabilitySettings>(DurabilityTable.TableName);

    /// <summary>Values, fees, metal, recipes and gambling (RU9).</summary>
    public Economy.EconomySettings EconomySettings => Get<Economy.EconomySettings>(Economy.EconomyTable.TableName);

    /// <summary>Capacity by skill rank, equipment slots, the belt, buy-back (RU5).</summary>
    public Inventory.InventorySettings InventorySettings => Get<Inventory.InventorySettings>(Inventory.InventoryTable.TableName);

    /// <summary>The town's vendors (RU5).</summary>
    public Table<Economy.VendorRow> Vendors => Get<Table<Economy.VendorRow>>(Economy.VendorTable.TableName);

    /// <summary>The shipped content; throws <see cref="ContentLoadException"/> naming every error if it does not load.</summary>
    public static DescentContent Shipped() => new ContentLoader().Load(new EmbeddedContentSource()).OrThrow();
}

/// <summary>
/// The item bases' ids and display names (normalised by <see cref="Normal"/>), for the
/// rules that keep skills apart from items (R17).
/// </summary>
public sealed record ItemBaseKeys(IReadOnlySet<string> Ids, IReadOnlySet<string> Names)
{
    /// <summary>A display name as compared: trimmed, lower case, a leading "the " dropped ("The Black Box" is "black box").</summary>
    public static string Normal(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.StartsWith("the ", StringComparison.Ordinal) ? n[4..].TrimStart() : n;
    }

    public bool HasId(string id) => Ids.Contains(id);

    public bool HasName(string name) => Names.Contains(Normal(name));
}

/// <summary>A load's content (partial when there are errors) and its errors.</summary>
public sealed record ContentLoadResult(DescentContent Content, IReadOnlyList<ContentError> Errors)
{
    public bool Ok => Errors.Count == 0;

    public DescentContent OrThrow() => Ok ? Content : throw new ContentLoadException(Errors);
}

/// <summary>Reads every table from a source, parses, validates and hashes them.</summary>
public sealed class ContentLoader
{
    private readonly IReadOnlyList<ContentTable> _tables;

    public ContentLoader() : this(ContentTables.All()) { }

    public ContentLoader(IReadOnlyList<ContentTable> tables) => _tables = tables;

    public ContentLoadResult Load(IContentSource source)
    {
        var errors = new ContentErrors();
        var parsed = new Dictionary<string, object>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var table in _tables)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(table.Name + "\n"));
            if (!source.TryRead(table.Name, out var bytes))
            {
                hash.AppendData("-\n"u8);
                if (table.Required)
                    errors.Add(table.Name, null, ContentRule.MissingTable, $"Content/{table.Name}.json is missing");
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
            }
            catch (JsonException e)
            {
                hash.AppendData(bytes);
                hash.AppendData("\n"u8);
                errors.Add(table.Name, null, ContentRule.Malformed, "not JSON: " + e.Message);
                continue;
            }

            using (doc)
            {
                hash.AppendData(Canonical(doc.RootElement));
                hash.AppendData("\n"u8);
                if (table.Parse(doc.RootElement, errors) is { } value)
                    parsed[table.Name] = value;
            }
        }

        var content = new DescentContent(parsed, Convert.ToHexStringLower(hash.GetHashAndReset()));
        foreach (var table in _tables)
            table.Validate(content, errors);
        return new ContentLoadResult(content, errors.All);
    }

    /// <summary>
    /// A table's canonical bytes: its JSON re-written with no insignificant whitespace,
    /// properties and numbers exactly as written. Line endings and indentation do not
    /// change the hash; any change to a name or a value does.
    /// </summary>
    public static byte[] Canonical(JsonElement root)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
            root.WriteTo(writer);
        return buffer.ToArray();
    }
}
