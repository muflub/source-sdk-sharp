using System.Security.Cryptography;
using System.Text.Json;
using SourceSharp.Tests;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// A committed scenario recording (plan_hl2mp_ai §6.6, §8.2): the <c>.airec</c>
/// tools/ai-scenario wrote and its JSON summary, read whole. A recording
/// whose map source has changed since it was made is refused, so a fact
/// never replays stale geometry: re-run the scenario.
/// </summary>
internal sealed class ScenarioRecording
{
    public required AiRecordingHeader Header { get; init; }

    public required JsonElement Summary { get; init; }

    /// <summary>Each published view by (tick, bot slot): the bot's sighted actors.</summary>
    public required Dictionary<(int Tick, int Slot), ActorFacts[]> Views { get; init; }

    public required List<AiEvent> Events { get; init; }

    public static string Path(string file) => RepoLayout.Repo("src", "ai", "tests", "recordings", file);

    public static ScenarioRecording Load(string map)
    {
        string json = File.ReadAllText(Path(map + ".json"));
        JsonElement summary = JsonDocument.Parse(json).RootElement.Clone();
        string vmf = RepoLayout.Repo("game", "mod_test", "maps", "src", "ai", map + ".vmf");
        string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(vmf))).ToLowerInvariant();
        if (summary.GetProperty("source_sha256").GetString() != sha)
            throw new InvalidDataException($"{map}'s recording was made from another map source ({summary.GetProperty("source_sha256").GetString()![..12]}, now {sha[..12]}): run tools/ai-scenario");

        using FileStream f = File.OpenRead(Path(map + ".airec"));
        AiRecordFormat.ReadFileHeader(f);
        AiRecordingHeader header = default;
        var views = new Dictionary<(int, int), ActorFacts[]>();
        var events = new List<AiEvent>();
        SnapshotRing? ring = null;
        while (AiRecordFormat.TryReadRecord(f, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload));
            switch (type)
            {
                case AiRecordType.Header:
                    header = AiRecordFormat.ReadHeader(r);
                    ring = new SnapshotRing(2, header.Slots, header.SightedCapacity);
                    break;
                case AiRecordType.Event:
                    events.Add(AiRecordFormat.ReadEvent(r));
                    break;
                case AiRecordType.Snapshot:
                    AiSnapshot s = AiRecordFormat.ReadSnapshot(r, ring!);
                    for (int slot = 0; slot < s.Slots; slot++)
                    {
                        if (s.ViewFor(slot) is { } v)
                            views[(s.Tick.Value, slot)] = v.Sighted.ToArray();
                    }

                    ring!.Publish(s);
                    break;
            }
        }

        if (!header.Map.StartsWith(map + "@" + sha[..12], StringComparison.Ordinal))
            throw new InvalidDataException($"{map}'s recording names {header.Map}, not {map}@{sha[..12]}");
        return new ScenarioRecording { Header = header, Summary = summary, Views = views, Events = events };
    }
}
