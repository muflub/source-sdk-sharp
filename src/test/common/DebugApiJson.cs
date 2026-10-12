using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// The contracts' JSON renderer, frozen here as a reference when
/// DevApi.Contracts was deleted (plan_devapi_interfaces P4): the goldens that
/// held the dev API's build and crossings equal to it keep holding them equal.
/// It reads the Abi statics directly, which only a test may.
/// </summary>
public static class DebugApiJson
{
    /// <summary>The crossing counters one side's state holds.</summary>
    public static string Crossings(Crossings crossings)
    {
        var active = crossings.Active();

        var json = new StringBuilder();
        json.Append("{\"direction\":\"inbound\",\"total\":").Append(crossings.Total)
            .Append(",\"resets\":").Append(crossings.Resets)
            .Append(",\"opCount\":").Append(Abi.Crossings.OpCount)
            .Append(",\"serverFrames\":").Append(crossings.ServerFrames)
            .Append(",\"clientFrames\":").Append(crossings.ClientFrames)
            .Append(",\"ops\":{");

        for (int i = 0; i < active.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append(Quote(active[i].Name)).Append(':').Append(active[i].Count);
        }

        // THE ENTITY OPS, broken out of the one ServerDispatch thunk
        // (plan_entity.md §3.4, step 1f): which entity crossings happened, not
        // only how many. Only the ones that have crossed; an absent name is zero.
        json.Append("},\"entityOps\":{");

        var entityOps = crossings.ActiveEntityOps();

        for (int i = 0; i < entityOps.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append(Quote(entityOps[i].Name)).Append(':').Append(entityOps[i].Count);
        }

        return json.Append("}}").ToString();
    }

    /// <summary>Which build one side's state says it is running: both halves.</summary>
    public static string Build(BuildInfo build)
        => $"{{\"native\":{Half(build.Native, build.NativeFreshness)}," +
           $"\"managed\":{Half(build.Managed, build.ManagedFreshness)}}}";

    /// <summary>
    /// One half of the build. "known" is its own field rather than being implied
    /// by an empty hash, because a page that cannot tell "no stamp" from "a
    /// stamp of nothing" renders the first as the second.
    /// </summary>
    public static string Half(BuildHalf half, BuildFreshness freshness)
        => $"{{\"known\":{(half.Stamp.IsKnown ? "true" : "false")}," +
           $"\"hash\":{Quote(half.Stamp.Hash)}," +
           $"\"at\":{half.Stamp.BuiltAtUnixMs}," +
           $"\"parts\":{half.Stamp.Parts}," +
           $"\"branch\":{Quote(half.Source.IsKnown ? half.Source.BranchName : "")}," +
           $"\"commit\":{Quote(half.Source.Commit)}," +

           // Three states, not two: null is "git could not be asked", and a page
           // must not render that the same way it renders a clean tree.
           $"\"dirty\":{half.Source.Dirty switch { true => "true", false => "false", null => "null" }}," +
           $"\"source\":{Quote(half.Source.Describe())}," +
           $"\"freshness\":{Quote(freshness.ToString().ToLowerInvariant())}," +
           $"\"path\":{Quote(half.Path)}}}";

    /// <summary>A JSON string literal.</summary>
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
}
