using System.Text.Json;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Death;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Horde;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Rules.Content;

/// <summary>
/// The tables a load reads, in the order the content hash covers them. A new table is
/// appended (appending changes the hash, as any content change does).
/// </summary>
public static class ContentTables
{
    public const string Stats = "stats";
    public const string Attributes = "attributes";
    public const string Classes = "classes";
    public const string Hero = "hero";
    public const string DamageTypes = "damage_types";
    public const string Families = "families";
    public const string Death = "death";
    public const string Bestiary = "bestiary";
    public const string Furniture = "furniture";
    public const string RobotRules = "robot_rules";
    public const string Robots = "robots";
    public const string Champions = "champions";
    public const string Levels = "levels";
    public const string Status = "status";
    public const string SkillSettings = "skill_settings";
    public const string SkillTabs = "skill_tabs";
    public const string Skills = "skills";
    public const string Spells = "spells";
    public const string Horde = "horde";
    public const string RobotConditions = "robot_conditions";

    public static IReadOnlyList<ContentTable> All() =>
    [
        new StatTable(), new AttributeTable(), new ClassTable(), new HeroTable(),
        new DamageTypeTable(),
        new FamilyTable(),
        new DeathTable(),
        new BestiaryTable(),
        new FurnitureTable(),
        new RobotRulesTable(),
        new RobotTemplateTable(),
        new ChampionAffixTable(),
        new DepthTable(),
        new StatusTable(),
        new TF2.Descent.Rules.Skills.SkillSettingsTable(),
        new TF2.Descent.Rules.Skills.SkillTabTable(),
        new TF2.Descent.Rules.Skills.SkillTable(),
        new TF2.Descent.Rules.Spells.SpellTable(),
        new Items.TFAttributeTable(), new Items.RarityTable(), new Items.ItemBaseTable(), new Items.AffixTable(),
        new Items.UnusualEffectTable(), new Items.UniqueTable(), new Items.ItemSettingsTable(),
        new Items.DurabilityTable(),
        new Economy.EconomyTable(),
        new Inventory.InventoryTable(), new Economy.VendorTable(),
        new HordeTable(),
        new Loot.LootTable(), new Loot.ExperienceTable(),
        new Items.StartingKitTable(),
        new Items.ConsumableTable(),
        new RobotConditionTable(),
        new RobotAttackTable(),
        new World.LampTable(), new World.HazardTable(),
        new Bosses.BossTable(),
        new Economy.Model.ModelTable(),
    ];

    /// <summary>The names of <see cref="All"/>, in order.</summary>
    public static IReadOnlyList<string> Names() => [.. All().Select(t => t.Name)];
}

/// <summary>A stat amount: "+0.5 of damage_blast_pct".</summary>
public sealed record StatAmount(string Stat, double Amount)
{
    internal static StatAmount Read(JsonElement e, string table, string? row, ContentErrors errors)
    {
        var f = new JsonFields(e, table, row, errors);
        var s = new StatAmount(f.Str("stat"), f.Num("amount"));
        f.End();
        return s;
    }

    internal static IReadOnlyList<StatAmount> List(JsonFields f, string field) =>
        [.. f.Array(field).Select(e => Read(e, f.Table, f.Row, f.Errors))];
}

/// <summary>
/// stats.json: the stat vocabulary. Every stat a row names, and every amount given to a
/// stat, is checked against it: an unknown name is <see cref="ContentRule.UnknownStat"/>,
/// an amount outside [min, max] is <see cref="ContentRule.OutOfRange"/>. Percent stats end
/// in _pct and are in percent (1.0 = 1 %).
/// </summary>
public sealed record StatRow(string Id, double Min, double Max) : IContentRow;

public sealed class StatTable : RowTable<StatRow>
{
    public override string Name => ContentTables.Stats;

    protected override StatRow ReadRow(JsonFields f) => new(f.Str("id"), f.Num("min"), f.Num("max"));

    protected override void ValidateRow(DescentContent content, StatRow row, ContentErrors errors) =>
        ContentChecks.Roll(errors, Name, row.Id, "min..max", row.Min, row.Max);
}

/// <summary>
/// attributes.json: the four invested attributes (Appendix C) and what each point buys for
/// every class (C.2). What a point buys only for some classes (life per Grit, mana per
/// Smarts) is the class's (<see cref="ClassRow.PerPoint"/>). Each attribute is also a stat,
/// so gear can grant it.
/// </summary>
public sealed record AttributeRow(string Id, string Name, IReadOnlyList<StatAmount> PerPoint) : IContentRow;

public sealed class AttributeTable : RowTable<AttributeRow>
{
    public override string Name => ContentTables.Attributes;

    protected override AttributeRow ReadRow(JsonFields f) =>
        new(f.Str("id"), f.Str("name"), StatAmount.List(f, "perPoint"));

    protected override void ValidateRow(DescentContent content, AttributeRow row, ContentErrors errors)
    {
        ContentChecks.Reference<StatRow>(content, errors, Name, row.Id, "id", row.Id, ContentTables.Stats);
        foreach (var p in row.PerPoint)
            ContentChecks.Stat(content, errors, Name, row.Id, p.Stat, p.Amount);
    }
}

/// <summary>An attribute's class start and cap (C.2).</summary>
public sealed record AttributeBounds(string Attribute, int Start, int Cap);

/// <summary>"Each point of <paramref name="Attribute"/> gives <paramref name="Amount"/> of <paramref name="Stat"/>."</summary>
public sealed record PointAmount(string Attribute, string Stat, double Amount);

/// <summary>
/// classes.json: the nine hero classes (R1). Each has a start and a cap per attribute
/// (C.2), stats it has at level 0 (life and speed from TF's class table), stats it gains
/// per level, and what a point buys for it alone.
/// </summary>
public sealed record ClassRow(
    string Id,
    string Name,
    IReadOnlyList<AttributeBounds> Attributes,
    IReadOnlyList<StatAmount> Base,
    IReadOnlyList<StatAmount> PerLevel,
    IReadOnlyList<PointAmount> PerPoint) : IContentRow
{
    /// <summary>
    /// The words TF2 appends to a shared weapon's item class for this class's copy
    /// (<c>tf_weapon_shotgun</c> is the Soldier's <c>tf_weapon_shotgun_soldier</c>, the Heavy's
    /// <c>_hwg</c>): <see cref="DescentContent.TryGetFamilyOfItemClass"/> strips one to find the
    /// family. No word belongs to two classes.
    /// </summary>
    public IReadOnlyList<string> ItemClassWords { get; init; } = [];

    public AttributeBounds Bounds(string attribute) =>
        Attributes.FirstOrDefault(a => a.Attribute == attribute)
        ?? throw new KeyNotFoundException($"class '{Id}' has no bounds for '{attribute}'");
}

public sealed class ClassTable : RowTable<ClassRow>
{
    public override string Name => ContentTables.Classes;

    protected override ClassRow ReadRow(JsonFields f)
    {
        var bounds = new List<AttributeBounds>();
        foreach (var (key, value) in f.Map("attributes"))
        {
            var b = new JsonFields(value, f.Table, f.Row, f.Errors);
            bounds.Add(new AttributeBounds(key, b.Int("start"), b.Int("cap")));
            b.End();
        }
        var perPoint = new List<PointAmount>();
        foreach (var e in f.Array("perPoint"))
        {
            var p = new JsonFields(e, f.Table, f.Row, f.Errors);
            perPoint.Add(new PointAmount(p.Str("attribute"), p.Str("stat"), p.Num("amount")));
            p.End();
        }
        return new ClassRow(f.Str("id"), f.Str("name"), bounds,
            StatAmount.List(f, "base"), StatAmount.List(f, "perLevel"), perPoint)
        { ItemClassWords = f.Strings("itemClassWords", required: true) };
    }

    protected override void ValidateTable(DescentContent content, Table<ClassRow> table, ContentErrors errors)
    {
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
            foreach (var word in row.ItemClassWords)
                if (!owner.TryAdd(word, row.Id))
                    errors.Add(Name, row.Id, ContentRule.DuplicateId, $"item class word '{word}' is already class '{owner[word]}''s");
    }

    protected override void ValidateRow(DescentContent content, ClassRow row, ContentErrors errors)
    {
        foreach (var b in row.Attributes)
        {
            ContentChecks.Reference<AttributeRow>(content, errors, Name, row.Id, "attributes", b.Attribute, ContentTables.Attributes);
            if (ContentChecks.Range(errors, Name, row.Id, b.Attribute + ".start", b.Start, 0, 1000))
                ContentChecks.Range(errors, Name, row.Id, b.Attribute + ".cap", b.Cap, b.Start, 1000);
        }
        if (content.TryGet<Table<AttributeRow>>(ContentTables.Attributes, out var attributes))
            foreach (var a in attributes.Rows.Where(a => row.Attributes.All(b => b.Attribute != a.Id)))
                errors.Add(Name, row.Id, ContentRule.DanglingReference, $"attribute '{a.Id}' has no start and cap in this class");
        if (row.ItemClassWords.Count == 0)
            errors.Add(Name, row.Id, ContentRule.Malformed, "'itemClassWords' names no word");
        foreach (var word in row.ItemClassWords.Where(w => w.Length == 0 || !w.All(char.IsAsciiLetterLower)))
            errors.Add(Name, row.Id, ContentRule.Malformed, $"item class word '{word}' is not lower-case letters");
        foreach (var s in row.Base.Concat(row.PerLevel))
            ContentChecks.Stat(content, errors, Name, row.Id, s.Stat, s.Amount);
        foreach (var p in row.PerPoint)
        {
            ContentChecks.Reference<AttributeRow>(content, errors, Name, row.Id, "perPoint.attribute", p.Attribute, ContentTables.Attributes);
            ContentChecks.Stat(content, errors, Name, row.Id, p.Stat, p.Amount);
        }
    }
}
