using System.Text;

namespace Debug.Api;

/// <summary>
/// JSON both halves of the dev API write the same way (plan_devapi_interfaces
/// §5.6). Each half maps its own Interface's records onto these parameters, so
/// this assembly needs no copy of them.
/// </summary>
public static class DebugJson
{
    /// <summary>A JSON string literal, quotes included.</summary>
    public static string Quote(string text)
    {
        var json = new StringBuilder(text.Length + 2);
        json.Append('"');

        foreach (char c in text)
        {
            switch (c)
            {
                case '"':  json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (c < ' ')
                        json.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        json.Append(c);
                    break;
            }
        }

        return json.Append('"').ToString();
    }

    /// <summary>One half of a build: the object /status and the client's status carry.</summary>
    public static string BuildHalf(bool known, string hash, long builtAtUnixMs, int parts, string branch,
                                   string commit, bool? dirty, string source, string freshness, string path)
        => $"{{\"known\":{(known ? "true" : "false")}," +
           $"\"hash\":{Quote(hash)}," +
           $"\"at\":{builtAtUnixMs}," +
           $"\"parts\":{parts}," +
           $"\"branch\":{Quote(branch)}," +
           $"\"commit\":{Quote(commit)}," +

           // Three states, not two: null is "git could not be asked", and a page
           // must not render that the same way it renders a clean tree.
           $"\"dirty\":{dirty switch { true => "true", false => "false", null => "null" }}," +
           $"\"source\":{Quote(source)}," +
           $"\"freshness\":{Quote(freshness)}," +
           $"\"path\":{Quote(path)}}}";

    /// <summary>A build: its two halves, already rendered by <see cref="BuildHalf"/>.</summary>
    public static string Build(string nativeHalf, string managedHalf)
        => $"{{\"native\":{nativeHalf},\"managed\":{managedHalf}}}";

    /// <summary>The inbound crossing counters, with only the operations that have crossed.</summary>
    public static string Crossings(long total, long resets, int opCount, long serverFrames, long clientFrames,
                                   IEnumerable<(string Name, long Count)> ops,
                                   IEnumerable<(string Name, long Count)> entityOps)
    {
        var json = new StringBuilder();
        json.Append("{\"direction\":\"inbound\",\"total\":").Append(total)
            .Append(",\"resets\":").Append(resets)
            .Append(",\"opCount\":").Append(opCount)
            .Append(",\"serverFrames\":").Append(serverFrames)
            .Append(",\"clientFrames\":").Append(clientFrames)
            .Append(",\"ops\":{");
        Counts(json, ops);

        // THE ENTITY OPS, broken out of the one ServerDispatch thunk
        // (plan_entity.md §3.4, step 1f): which entity crossings happened, not
        // only how many. Only the ones that have crossed; an absent name is zero.
        json.Append("},\"entityOps\":{");
        Counts(json, entityOps);

        return json.Append("}}").ToString();
    }

    private static void Counts(StringBuilder json, IEnumerable<(string Name, long Count)> counts)
    {
        bool first = true;
        foreach (var (name, count) in counts)
        {
            if (!first)
                json.Append(',');
            first = false;
            json.Append(Quote(name)).Append(':').Append(count);
        }
    }
}
