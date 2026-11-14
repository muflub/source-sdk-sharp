using System.Text;
using System.Text.Json.Nodes;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using Xunit;

namespace TF2.Descent.Tests.Items;

/// <summary>A row of items_working.tsv: name, item class, slot, the classes that use it, status.</summary>
internal sealed record WorkingItem(string Name, string ItemClass, string Slot, IReadOnlyList<string> Classes, string Status);

/// <summary>Shared fixtures of the item facts: the shipped rules, planted copies of the tables, files of the TF content.</summary>
internal static class ItemFixtures
{
    public static TF2DescentRules NewRules() => new();

    public static ItemRollContext Ctx(int tier, int itemLevel = 20, int depth = 5, string cls = "soldier", string source = "kill", string table = "depth5") =>
        new(cls, itemLevel, depth, itemLevel, tier, table, source);

    public static MemoryContentSource ShippedCopy() =>
        new(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());

    /// <summary>The shipped tables with <paramref name="table"/> rewritten by <paramref name="mutate"/>.</summary>
    public static MemoryContentSource Planted(string table, Action<JsonNode> mutate)
    {
        var source = ShippedCopy();
        Assert.True(source.TryRead(table, out var bytes), $"no table {table}");
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    public static IReadOnlyList<ContentError> Errors(IContentSource source) => new ContentLoader().Load(source).Errors;

    public static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SourceSharp.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("SourceSharp.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>items_working.tsv's rows by name, header checked, the count proving the read worked.</summary>
    public static IReadOnlyDictionary<string, WorkingItem> ItemsWorking()
    {
        string path = Path.Combine(RepoRoot(), "game", "mod_tf2", "scripts", "items", "items_working.tsv");
        Assert.True(File.Exists(path), $"{path} is missing");
        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("defindex\tname\titem_class\tslot\tclasses\tfamily\tattributes\tstatus\tevidence", lines[0]);
        var rows = lines.Skip(1).Select(l => l.Split('\t'))
            .Select(c => new WorkingItem(c[1], c[2], c[3], c[4].Split(','), c[7]))
            .ToDictionary(r => r.Name, StringComparer.Ordinal);
        Assert.True(rows.Count > 400, $"only {rows.Count} rows read");
        return rows;
    }

    /// <summary>TF's item schema as the game's content dir holds it (bin/&lt;config&gt;/content, beside these tests' output).</summary>
    public static string ItemsGameText()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "content", "tf", "scripts", "items", "items_game.txt"));
        Assert.True(File.Exists(path), $"{path} is missing: the game's content dir holds the shipped schema (make setup)");
        string text = File.ReadAllText(path);
        Assert.True(text.Length > 1_000_000, $"only {text.Length} bytes read");
        return text;
    }

    /// <summary>An item of <paramref name="baseId"/> with exactly the affixes given, as the rules would hold it.</summary>
    public static ItemInstance Item(string baseId, int rarity, int itemLevel, params RolledAffix[] affixes) => new()
    {
        BaseId = baseId,
        Context = Ctx(rarity, itemLevel),
        Seed = 1,
        ItemLevel = itemLevel,
        Rarity = rarity,
        Tier = rarity,
        Affixes = [.. affixes],
        Identified = true,
        Durability = 60,
        MaxDurability = 60,
    };

    /// <summary>A Soldier at <paramref name="level"/> with these attribute bases (the rest at class start), points kept consistent enough for Stats.</summary>
    public static HeroSheet Soldier(TF2DescentRules rules, int level = 30, int brawn = 20, int aim = 20)
    {
        var s = rules.Heroes.New("soldier", "test");
        return s with { Level = level, Attributes = s.Attributes.SetItem("brawn", brawn).SetItem("aim", aim) };
    }
}
