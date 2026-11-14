using System.Buffers;
using System.Text.Json;

using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <c>/match</c> (plan_bots_teams_tf2 D.3, D.5), the web UI's Match card, read
/// only: the level and its mode, the mode's settings with the values they hold
/// and any waiting for a round or a level, the vote on now with its counts,
/// the map the level changes to next and the candidates a vote may choose.
/// Written with a <see cref="Utf8JsonWriter"/>, never the serializer, whose
/// caches would hold the server's load context past its unload (AiJson's note).
/// </summary>
public sealed class MatchRoutes(GameModeCatalog catalog, ModeSettingWrites writes, VoteOffice office, IGameClock clock, Intermission intermission,
                                NextLevelConVar nextLevel, MapCandidates candidates) : IDebugRouteProvider, IOrderedHook
{
    public const string Path = "/match";

    /// <summary>
    /// Its place among the route providers: every registered one is collected as a
    /// chain, and two at one place throw at the first request (the AI's routes are 0),
    /// which took every mod's route down with it.
    /// </summary>
    public const int Order = 100;

    public int HookOrder => Order;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Path, "the match: {map, mode{id,title,teamplay}, settings[{key,value,applies,votable,adminOnly,pending}], vote (null, or {kind,detail,caller,phase,options,counts,yes,no,voters,secondsLeft}), nextMap, nextLevelSet, gameOver, candidates[{map,label,modes,hasNav}]}"),
    ];

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
        => path == Path ? new DebugRouteReply(200, Json()) : new DebugRouteReply(404, "{\"error\":\"no such route\"}");

    /// <summary>The card's answer as it stands this tick.</summary>
    public string Json()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("map", catalog.CurrentMap);
            WriteMode(w, catalog.Active);
            WriteSettings(w, catalog.Active);
            WriteVote(w);
            w.WriteString("nextMap", intermission.PeekNextMap());
            w.WriteBoolean("nextLevelSet", nextLevel.Value.Length > 0);
            w.WriteBoolean("gameOver", intermission.GameOver);
            w.WriteStartArray("candidates");
            foreach (var c in candidates.All())
            {
                w.WriteStartObject();
                w.WriteString("map", c.Map);
                w.WriteString("label", c.Label);
                if (c.Modes is { } modes)
                {
                    w.WriteStartArray("modes");
                    foreach (string m in modes)
                        w.WriteStringValue(m);
                    w.WriteEndArray();
                }
                else
                {
                    w.WriteNull("modes");
                }
                w.WriteBoolean("hasNav", c.HasNav);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteMode(Utf8JsonWriter w, ModeSpec? mode)
    {
        if (mode is null)
        {
            w.WriteNull("mode");
            return;
        }
        w.WriteStartObject("mode");
        w.WriteString("id", mode.Id);
        w.WriteString("title", mode.Title);
        w.WriteBoolean("teamplay", mode.Teamplay);
        w.WriteEndObject();
    }

    private void WriteSettings(Utf8JsonWriter w, ModeSpec? mode)
    {
        w.WriteStartArray("settings");
        foreach (var s in mode?.Settings ?? [])
        {
            w.WriteStartObject();
            w.WriteString("key", s.Key);
            if (writes.Current(s.Key) is { } value)
                w.WriteString("value", value);
            else
                w.WriteNull("value");
            w.WriteString("applies", s.Applies.ToString());
            w.WriteBoolean("votable", s.Votable);
            w.WriteBoolean("adminOnly", s.AdminOnly);
            if (writes.Pending.TryGetValue(s.Key, out var pending))
                w.WriteString("pending", pending.Value);
            else
                w.WriteNull("pending");
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private void WriteVote(Utf8JsonWriter w)
    {
        if (office.Current is not { IsVoteActive: true } vote)
        {
            w.WriteNull("vote");
            return;
        }
        var state = ServerVotes.State(vote, office.Caller, office.Target, clock.Now, office.Kind, office.Detail);
        w.WriteStartObject("vote");
        w.WriteString("kind", office.Kind.ToString());
        w.WriteString("detail", office.Detail);
        w.WriteNumber("caller", office.Caller);
        w.WriteString("phase", ((VotePhase)state.Phase).ToString());
        w.WriteStartArray("options");
        foreach (string option in vote.IsYesNoVote ? ["Yes", "No"] : vote.Options)
            w.WriteStringValue(option);
        w.WriteEndArray();
        w.WriteStartArray("counts");
        foreach (byte count in state.Counts ?? [])
            w.WriteNumberValue(count);
        w.WriteEndArray();
        w.WriteNumber("yes", state.Yes);
        w.WriteNumber("no", state.No);
        w.WriteNumber("voters", state.Voters);
        w.WriteNumber("secondsLeft", MathF.Round(state.SecondsLeft, 1));
        w.WriteEndObject();
    }
}
