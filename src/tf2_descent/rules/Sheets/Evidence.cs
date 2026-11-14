using System.Collections.Immutable;
using System.Text.Json;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Sheets;

/// <summary>
/// What a game server reports with a checkpoint, beside the sheet (plan_descent_logic
/// RU13, section 7 X4): the experience the hero earned since the last checkpoint and the
/// robots it killed, by template. The rules refuse a sheet whose experience or bestiary
/// grew by more than this says.
/// </summary>
public sealed record CheckpointProof(long XpEarned, ImmutableSortedDictionary<string, int> Kills)
{
    /// <summary>No evidence: nothing was earned. What an empty payload (schema 0) means.</summary>
    public static CheckpointProof None { get; } = new(0, ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal));

    public int KillsOf(string template) => Kills.TryGetValue(template, out var n) ? n : 0;
}

/// <summary>
/// <see cref="CheckpointProof"/>'s bytes, schema 1: canonical JSON
/// <c>{"xp":N,"kills":{"template":count,...}}</c>, kills sorted by template. Bounded:
/// at most <see cref="MaxBytes"/> bytes and <see cref="MaxTemplates"/> templates, so a
/// checkpoint's evidence stays small (a typical one, a dozen templates, is under 400 bytes).
/// An empty payload (the SDK's default, schema 0) is <see cref="CheckpointProof.None"/>.
/// </summary>
public sealed class EvidenceCodec
{
    public const int Schema = 1;
    public const int MaxBytes = 16 * 1024;
    public const int MaxTemplates = 256;

    public CheckpointEvidence Write(CheckpointProof proof)
    {
        if (proof.Kills.Count > MaxTemplates)
            throw new ArgumentException($"{proof.Kills.Count} templates; evidence holds at most {MaxTemplates}", nameof(proof));
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("xp", proof.XpEarned);
            w.WriteStartObject("kills");
            foreach (var (k, v) in proof.Kills) w.WriteNumber(k, v);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return new CheckpointEvidence(buffer.ToArray(), Schema);
    }

    /// <summary>The proof in a payload; <see cref="InvalidDataException"/> when it is not one this codec wrote.</summary>
    public CheckpointProof Read(CheckpointEvidence evidence)
    {
        if (evidence.Data.Length == 0) return CheckpointProof.None;   // an empty payload, of any schema, carries nothing
        if (evidence.SchemaVersion != Schema)
            throw new InvalidDataException($"evidence schema {evidence.SchemaVersion} is not {Schema}");
        if (evidence.Data.Length > MaxBytes)
            throw new InvalidDataException($"evidence is {evidence.Data.Length} bytes; at most {MaxBytes}");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(evidence.Data);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("the evidence is not JSON: " + e.Message, e);
        }
        using (doc)
        {
            var errors = new ContentErrors();
            var f = new JsonFields(doc.RootElement, "evidence", null, errors);
            long xp = f.Long("xp");
            var kills = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach (var (key, value) in f.Map("kills"))
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || n < 0)
                    errors.Add("evidence", key, ContentRule.Malformed, $"kills of '{key}' must be a whole number, at least 0");
                else if (!kills.TryAdd(key, n))
                    errors.Add("evidence", key, ContentRule.DuplicateId, $"'{key}' twice");
            }
            f.End();
            if (xp < 0) errors.Add("evidence", null, ContentRule.OutOfRange, $"xp {xp} is below 0");
            if (kills.Count > MaxTemplates) errors.Add("evidence", null, ContentRule.OutOfRange, $"{kills.Count} templates; at most {MaxTemplates}");
            if (errors.Count > 0)
                throw new InvalidDataException("the evidence does not decode: " + string.Join("; ", errors.All));
            return new CheckpointProof(xp, kills.ToImmutable());
        }
    }
}
