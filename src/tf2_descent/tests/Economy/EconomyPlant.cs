using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>The shipped content with economy.json rewritten, loaded: its errors (the unmutated copy is first shown to load clean).</summary>
internal static class EconomyPlant
{
    public static IReadOnlyList<ContentError> Errors(Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.Empty(new ContentLoader().Load(source).Errors);
        source.TryRead(EconomyTable.TableName, out var bytes);
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        source.Set(EconomyTable.TableName, Encoding.UTF8.GetBytes(node.ToJsonString()));
        return new ContentLoader().Load(source).Errors;
    }
}
