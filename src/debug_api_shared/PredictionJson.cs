using System.Globalization;
using System.Text;

namespace Debug.Api;

/// <summary>
/// What the client's prediction compares have seen, as JSON.
///
/// <para>
/// Hand-built like the rest of this assembly, for NetTableJson's reason: a
/// serializer would be less code and would also pull a dependency and a chunk of
/// reflection into an assembly whose whole design goal is to be absent unless
/// somebody asked for it.
/// </para>
///
/// <para>
/// In Debug.Api.Shared because both halves render it (plan_devapi_interfaces
/// P4): the server for its debug channel's mirror, the client for its own
/// ring, which the server proxies at /client/{slot}/prediction. Neither reads
/// the game's ring itself; they render wire records.
/// </para>
/// </summary>
public static class PredictionJson
{
    /// <summary>
    /// The report. <paramref name="since"/> lets a follower ask only for what it
    /// has not seen.
    /// </summary>
    public static string Report(PredictionMirror ring, long since, int max)
        => Report(ring.Capacity, ring.Count, ring.NextSequence, ring.Observed, ring.Mispredicted,
                  ring.ToleratedOnly, ring.Since(since, max));

    /// <summary>The same report from a ring's numbers and the records asked for (the client's window, P3).</summary>
    public static string Report(int capacity, int held, long nextSequence, long observed, long mispredicted,
                                long toleratedOnly, IReadOnlyList<WirePredictionRecord> records)
    {
        var json = new StringBuilder("{");

        json.Append("\"available\":true,");
        json.Append("\"capacity\":").Append(capacity).Append(',');
        json.Append("\"held\":").Append(held).Append(',');
        json.Append("\"nextSequence\":").Append(nextSequence).Append(',');

        // THE TOTALS ARE NOT DERIVABLE FROM THE RECORDS and are sent separately
        // for that reason: the ring is a few hundred deep and has forgotten most
        // of a session, so a rate computed from what survives would read as
        // though the game had only ever done this many compares.
        json.Append("\"observed\":").Append(observed).Append(',');
        json.Append("\"mispredicted\":").Append(mispredicted).Append(',');
        json.Append("\"toleratedOnly\":").Append(toleratedOnly).Append(',');

        // WHAT A COMPARE IS, said here rather than assumed, because the two
        // populations are easy to conflate and mean opposite things: "observed"
        // counts compares that found ANY difference, and a prediction that held
        // byte for byte is not among them. The denominator for "is prediction
        // working" is packets, which this side does not count -- so the rate
        // below is the share of DIFFERING compares that broke through their
        // tolerances, and it is labelled as that.
        json.Append("\"unit\":\"observed counts compares that found any difference; ")
            .Append("a prediction that held byte for byte is never recorded\",");

        json.Append("\"records\":[");

        for (int i = 0; i < records.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            Record(json, records[i]);
        }

        json.Append("]}");
        return json.ToString();
    }

    /// <summary>
    /// The answer for a process whose client never wired the capture — a
    /// dedicated server, or a build without it.
    ///
    /// <para>
    /// SAID OUT LOUD rather than answered with an empty list, which is the same
    /// mistake the traffic pane exists to avoid: "no client here" and "prediction
    /// is holding perfectly" produce identical empty tables and mean opposite
    /// things.
    /// </para>
    /// </summary>
    public static string Unavailable(string note)
        => $"{{\"available\":false,\"note\":{DebugJson.Quote(note)},\"records\":[]}}";

    private static void Record(StringBuilder json, WirePredictionRecord record)
    {
        json.Append("{\"sequence\":").Append(record.Sequence)
            .Append(",\"command\":").Append(record.Command)
            .Append(",\"typeId\":").Append(record.TypeId)
            .Append(",\"class\":").Append(DebugJson.Quote(record.ClassName))
            .Append(",\"blobBytes\":").Append(record.BlobBytes)
            .Append(",\"standingBytes\":").Append(record.StandingBytes)
            .Append(",\"firstDiffOffset\":").Append(record.FirstDiffOffset)
            .Append(",\"toleratedFields\":").Append(record.ToleratedFields)
            .Append(",\"mispredicted\":").Append(record.Mispredicted ? "true" : "false")
            .Append(",\"atUtc\":").Append(DebugJson.Quote(
                record.TimeUtc.ToString("o", CultureInfo.InvariantCulture)))
            .Append(",\"fields\":[");

        for (int i = 0; i < record.Fields.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            Field(json, record.Fields[i]);
        }

        json.Append("]}");
    }

    private static void Field(StringBuilder json, WirePredictionField field)
    {
        json.Append("{\"name\":").Append(DebugJson.Quote(field.Name))
            .Append(",\"kind\":").Append(field.Kind)
            .Append(",\"kindName\":").Append(DebugJson.Quote(KindName(field.Kind)))
            .Append(",\"offset\":").Append(field.Offset)
            .Append(",\"bytes\":").Append(field.Bytes)
            .Append(",\"tolerance\":").Append(Number(field.Tolerance))
            .Append(",\"tolerable\":").Append(field.Tolerable ? "true" : "false")
            .Append(",\"tolerated\":").Append(field.Tolerated ? "true" : "false")
            .Append(",\"differingBytes\":").Append(field.DifferingBytes)
            .Append(",\"maxAbsDelta\":").Append(Number(field.MaxAbsDelta));

        // Null and not zero when no tolerance is engaged. A ratio of zero would
        // read as a field sitting comfortably inside a limit it does not have.
        json.Append(",\"toleranceRatio\":")
            .Append(field.ToleranceRatio is { } ratio ? Number(ratio) : "null");

        json.Append(",\"components\":[");

        for (int i = 0; i < field.Components.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            var component = field.Components[i];

            json.Append("{\"predicted\":").Append(Number(component.Predicted))
                .Append(",\"server\":").Append(Number(component.Server))
                .Append(",\"delta\":").Append(Number(component.Delta))
                .Append('}');
        }

        json.Append("]}");
    }

    /// <summary>The ABI's kind numbering, spelled out. -1 is a byte no field claims.</summary>
    private static string KindName(int kind) => kind switch
    {
        0 => "int",
        1 => "float",
        2 => "vector",
        3 => "bool",
        4 => "string",
        5 => "uint",
        6 => "color32",
        7 => "qangle",
        _ => "padding",
    };

    /// <summary>
    /// Enough decimal places to be read against a tolerance.
    ///
    /// <para>
    /// NOT the two places the traffic report uses, and the difference is the
    /// point of this whole view: Origin's declared tolerance is 0.0625, so two
    /// decimal places would round a delta of 0.06 and one of 0.0624 to the same
    /// "0.06" and put them on opposite sides of a limit they are both under. Six
    /// places is past what a float carries, which is the right place to stop.
    /// </para>
    /// </summary>
    private static string Number(double value)
        => double.IsFinite(value)
            ? value.ToString("0.######", CultureInfo.InvariantCulture)
            : "0";
}
