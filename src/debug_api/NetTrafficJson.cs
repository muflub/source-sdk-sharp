using System.Globalization;
using System.Text;
using SourceSharp.Server.Interface;

namespace Debug.Api;

/// <summary>
/// What the profiler has measured, as JSON.
///
/// <para>
/// Hand-built like the rest of this assembly, for the reason given in
/// NetTableJson: a serializer would be less code and would also pull a
/// dependency and a chunk of reflection into an assembly whose whole design goal
/// is to be absent unless somebody asked for it.
/// </para>
/// </summary>
internal static class NetTrafficJson
{
    public static string Report(DebugNetTraffic? report)
    {
        var json = new StringBuilder("{");

        if (report is null)
        {
            // Not the same as "nothing moved", and the difference matters: a
            // reader looking at an empty list should not conclude the payload is
            // free. This says the server never installed the hook at all.
            json.Append("\"available\":false,");
            json.Append("\"note\":\"this build's server module does not report payload traffic\",");
            json.Append("\"classes\":[]}");
            return json.ToString();
        }

        var value = report;

        json.Append("\"available\":true,");
        json.Append("\"sampling\":").Append(value.Sampling ? "true" : "false").Append(',');

        // The one thing a reader most needs told, because an unsampled view
        // looks exactly like a quiet one.
        if (!value.Sampling)
        {
            json.Append("\"note\":\"not sampling — set cs_sv_nettable_profile 1; ")
                .Append("figures below are whatever was measured last\",");
        }

        json.Append("\"elapsedSeconds\":").Append(Number(value.ElapsedSeconds)).Append(',');
        json.Append("\"trackedEntities\":").Append(value.TrackedEntities).Append(',');
        json.Append("\"bitsPerChangedWord\":").Append(value.BitsPerChangedWord)
            .Append(',');
        json.Append("\"windowSeconds\":").Append(value.WindowSeconds).Append(',');

        // THE UNIT, said in the payload rather than left to a reader who might
        // reasonably assume bytes. See the server's traffic sampler: Part 13 says the diff
        // is the delta byte for byte, which stopped being true when Part 1B made
        // the element a dword.
        json.Append("\"unit\":\"a changed payload dword costs ")
            .Append(value.BitsPerChangedWord)
            .Append(" bits on the wire; one changed BYTE costs the whole word it sits in\",");

        json.Append("\"fieldsNote\":\"a word holding two changed fields is charged to both, ")
            .Append("so the per-field column can exceed the class total\",");

        json.Append("\"classes\":[");

        var classes = value.Classes;

        for (int i = 0; i < classes.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            AppendClass(json, classes[i], value.WordBytes);
        }

        json.Append("]}");
        return json.ToString();
    }

    private static void AppendClass(StringBuilder json, DebugTrafficClass row, int wordBytes)
    {
        json.Append("{\"className\":").Append(DebugApiServer.Quote(row.ClassName)).Append(',');
        json.Append("\"entityFramesSeen\":").Append(row.Seen).Append(',');
        json.Append("\"entityFramesChanged\":").Append(row.ChangedFrames).Append(',');
        json.Append("\"wordsTouched\":").Append(row.WordsTouched).Append(',');
        json.Append("\"bytesChanged\":").Append(row.BytesChanged).Append(',');
        json.Append("\"bytesPerSecond\":").Append(Number(row.BytesPerSecond)).Append(',');
        json.Append("\"recentBytesPerSecond\":").Append(Number(row.RecentBytesPerSecond))
            .Append(',');

        // How much of each changed word was real. A class whose payload is
        // mostly padding shows up here as a low ratio, which is exactly the
        // signal Part 10's packing is meant to improve — so it is worth
        // reporting rather than leaving to be divided out by hand.
        double density = row.WordsTouched > 0
            ? row.BytesChanged / (double)(row.WordsTouched * wordBytes)
            : 0.0;

        json.Append("\"wordFill\":").Append(Number(density)).Append(',');
        json.Append("\"fields\":[");

        for (int i = 0; i < row.Fields.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            var field = row.Fields[i];

            json.Append("{\"name\":").Append(DebugApiServer.Quote(field.Name))
                .Append(",\"words\":").Append(field.Words)
                .Append(",\"bytesPerSecond\":").Append(Number(field.BytesPerSecond))
                .Append('}');
        }

        json.Append("]}");
    }

    /// <summary>
    /// Two decimal places, invariant. A rate is read rather than computed with,
    /// and seventeen significant figures of a number that changes every second
    /// is noise in a panel.
    /// </summary>
    private static string Number(double value)
        => double.IsFinite(value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : "0";
}
