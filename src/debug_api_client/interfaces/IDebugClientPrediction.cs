namespace Debug.Api.Client.Interfaces;

/// <summary>One component of a field's difference: predicted, arrived, and the gap.</summary>
public readonly record struct DebugPredictionComponent(double Predicted, double Server, double Delta);

/// <summary>One field of a prediction error, as the client's dev API reads it (P3b).</summary>
/// <param name="Kind">The field kind's number (0 int, 1 float, 2 vector, ...).</param>
/// <param name="Tolerance">The tolerance its declaration states; 0 for none.</param>
public sealed record DebugPredictionField(
    string Name, int Kind, int Offset, int Bytes, float Tolerance, bool Tolerated, int DifferingBytes,
    IReadOnlyList<DebugPredictionComponent> Components);

/// <summary>One prediction error: a compare that found the predicted and arrived bytes different (P3b).</summary>
public sealed record DebugPredictionRecord(
    long Sequence, int Command, uint TypeId, string ClassName, int BlobBytes, int StandingBytes,
    int FirstDiffOffset, int ToleratedFields, DateTime TimeUtc, IReadOnlyList<DebugPredictionField> Fields);

/// <summary>The client's prediction ring's totals, and the records asked for (P3b).</summary>
/// <param name="Held">Records the ring still holds.</param>
/// <param name="Observed">Compares that found any difference, since the last reset.</param>
public sealed record DebugPredictionWindow(
    int Capacity, int Held, long NextSequence, long Observed, long Mispredicted, long ToleratedOnly,
    IReadOnlyList<DebugPredictionRecord> Records);

/// <summary>The client's own prediction errors (P3b; Q1: the server never reads them).</summary>
public interface IDebugClientPrediction
{
    /// <summary>The totals and up to <paramref name="max"/> records from <paramref name="since"/>, oldest first. Any thread.</summary>
    DebugPredictionWindow Since(long since, int max);

    /// <summary>Forgets every record and total, for a reader who wants a clean window.</summary>
    void Reset();
}
