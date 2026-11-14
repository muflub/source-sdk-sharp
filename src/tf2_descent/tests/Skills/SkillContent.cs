using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The shipped tables and copies of them with a bad row planted (never the shipped files),
/// for the skill and spell facts. An item-base table is not shipped yet (the items package
/// adds it), so <see cref="WithItemBases"/> loads a fixture one beside the shipped tables.
/// </summary>
internal static class SkillContent
{
    public static MemoryContentSource ShippedCopy() =>
        new(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());

    public static DescentContent Shipped() => new ContentLoader().Load(ShippedCopy()).OrThrow();

    public static MemoryContentSource Planted(string table, Action<JsonNode> mutate) => Planted(ShippedCopy(), table, mutate);

    public static MemoryContentSource Planted(MemoryContentSource source, string table, Action<JsonNode> mutate)
    {
        Assert.True(source.TryRead(table, out var bytes), $"no table {table}");
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    public static IReadOnlyList<ContentError> Errors(IContentSource source, IReadOnlyList<ContentTable>? tables = null) =>
        (tables is null ? new ContentLoader() : new ContentLoader(tables)).Load(source).Errors;

    public static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    /// <summary>A row of <see cref="FixtureItemBaseTable"/>: what the validator needs of an item base.</summary>
    public sealed record FixtureItemBase(string Id, string Name) : IItemBaseRow;

    /// <summary>A stand-in for the items package's item-base table: id and display name per row.</summary>
    public sealed class FixtureItemBaseTable : RowTable<FixtureItemBase>
    {
        public override string Name => "item_bases_fixture";

        protected override FixtureItemBase ReadRow(JsonFields f) => new(f.Str("id"), f.Str("name"));
    }

    /// <summary>The shipped tables plus the fixture item bases, and the table list that loads them.</summary>
    public static (MemoryContentSource Source, IReadOnlyList<ContentTable> Tables) WithItemBases(params (string Id, string Name)[] bases)
    {
        var json = JsonSerializer.Serialize(bases.Select(b => new Dictionary<string, string> { ["id"] = b.Id, ["name"] = b.Name }));
        var source = ShippedCopy().Set("item_bases_fixture", Encoding.UTF8.GetBytes(json));
        return (source, [.. ContentTables.All(), new FixtureItemBaseTable()]);
    }

    /// <summary>The fixture's bases: names and ids an item package would ship.</summary>
    public static (string Id, string Name)[] SomeBases =>
    [
        ("black_box", "The Black Box"),
        ("rocket_launcher_stock", "Rocket Launcher"),
        ("shovel", "Shovel"),
    ];
}
