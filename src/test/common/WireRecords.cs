extern alias client;
using PredictionErrorRecord = client::SourceSharp.Abi.PredictionErrorRecord;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using SourceSharp.Abi;
using Debug.Api;

namespace SourceSharp.Tests;

/// <summary>
/// The game's prediction records as the debug channel carries them
/// (plan_devapi_interfaces P4): the codec and the server's mirror speak
/// Debug.Api.Shared's copies, and tests build the game's records and convert.
/// </summary>
internal static class WireRecords
{
    public static WirePredictionRecord ToWire(this PredictionErrorRecord r)
        => new(r.Sequence, r.Command, r.TypeId, r.ClassName, r.BlobBytes, r.StandingBytes, r.FirstDiffOffset,
               r.ToleratedFields, r.TimeUtc,
               r.Fields.Select(f => new WirePredictionField(f.Name, f.Kind, f.Offset, f.Bytes, f.Tolerance, f.Tolerated,
                   f.DifferingBytes, f.Components.Select(c => new WirePredictionComponent(c.Predicted, c.Server, c.Delta)).ToArray()))
                .ToArray());

    public static WirePredictionRecord[] ToWire(this IEnumerable<PredictionErrorRecord> records)
        => records.Select(ToWire).ToArray();

    /// <summary>A mirror holding what the ring holds, added in the same order.</summary>
    public static PredictionMirror ToMirror(this PredictionErrorRing ring)
    {
        var mirror = new PredictionMirror(ring.Capacity);
        foreach (var record in ring.Since(0, ring.Capacity))
            mirror.Add(record.ToWire());
        return mirror;
    }
}
