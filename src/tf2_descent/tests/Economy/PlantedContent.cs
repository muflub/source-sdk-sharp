using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// The shipped content with values planted, for the model's self-tests and sweeps: each edit is
/// <c>table:path=value</c>, the path dotted, an array element named by its index or, in
/// brackets, by its "id" (<c>robots:[heavy].life=600</c>, <c>robot_rules:levelCurve.lifePerLevelPct=6</c>).
/// </summary>
public static class PlantedContent
{
    public static DescentContent With(params string[] edits)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        foreach (var group in edits.Where(e => e.Length > 0).GroupBy(e => e[..e.IndexOf(':')]))
        {
            Assert(source.TryRead(group.Key, out var bytes), $"no table '{group.Key}'");
            var root = JsonNode.Parse(bytes)!;
            foreach (var edit in group)
            {
                var (path, value) = (edit[(edit.IndexOf(':') + 1)..edit.IndexOf('=')], edit[(edit.IndexOf('=') + 1)..]);
                var segments = path.Split('.');
                JsonNode node = root;
                for (int i = 0; i < segments.Length - 1; i++)
                    node = Step(node, segments[i]);
                var last = segments[^1];
                var parsed = JsonNode.Parse(value.Length > 0 && (char.IsDigit(value[0]) || value[0] == '-' || value[0] is '[' or '{' or '"' or 't' or 'f')
                    ? value : "\"" + value + "\"");
                if (last.StartsWith('[') || int.TryParse(last, out _))
                    throw new ArgumentException($"'{edit}': the last segment names a field");
                node.AsObject()[last] = parsed;
            }
            source = source.Set(group.Key, Encoding.UTF8.GetBytes(root.ToJsonString()));
        }
        return new ContentLoader().Load(source).OrThrow();
    }

    private static JsonNode Step(JsonNode node, string segment)
    {
        if (segment.StartsWith('['))
        {
            string id = segment[1..^1];
            return node.AsArray().Single(n => (string?)n!["id"] == id)!;
        }
        if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return node.AsArray()[index]!;
        return node[segment] ?? throw new ArgumentException($"no field '{segment}'");
    }

    private static void Assert(bool ok, string why)
    {
        if (!ok) throw new InvalidOperationException(why);
    }
}
