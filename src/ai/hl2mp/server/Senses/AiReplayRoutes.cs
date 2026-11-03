using System.Globalization;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The record/replay scrubber's routes (plan_hl2mp_ai §7.1, §7.3):
/// <c>POST /ai/replay/load?path=</c> reads an <c>.airec</c>;
/// <c>GET /ai/replay</c> says what is loaded and where the replay stands;
/// <c>GET /ai/replay/bot/&lt;slot&gt;?tick=N</c> answers the bot's inspector
/// record as its brain was at the last snapshot at or before N (the same
/// three parts <c>/ai/bot/&lt;slot&gt;</c> answers live), with the intent the
/// recording holds and the one the replay made at the bot's last decide, and
/// whether they are the same. A seek runs off the game thread; until it is
/// done a request is answered 202 and asked again. A recording made over a
/// world is replayed over this level's graph, so load it on its map.
/// </summary>
[SideService(Side.Server)]
public sealed class AiReplayRoutes(Nav.IHl2mpNavigation nav, ILog log)
{
    private readonly object _gate = new();
    private AiReplayCursor? _cursor;
    private string _path = "";
    private Task? _seeking;
    private int _seekTo = -1;
    private string? _fault;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("POST", "/ai/replay/load", "load an .airec for the scrubber: ?path= (a recording made over a world replays over this level's graph)"),
        new("GET", "/ai/replay", "the loaded recording: path, header, ticks, bots, where the replay stands"),
        new("GET", "/ai/replay/bot/", "a bot's inspector record in the replay: /ai/replay/bot/<slot>?tick=N, with its recorded and replayed intents (202 while seeking)"),
    ];

    public static bool Serves(string path) => path is "/ai/replay/load" or "/ai/replay" || path.StartsWith("/ai/replay/bot/", StringComparison.Ordinal);

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        if (path == "/ai/replay/load")
            return Load(query.GetValueOrDefault("path") ?? "");
        if (path == "/ai/replay")
            return Status();

        string rest = path["/ai/replay/bot/".Length..].Trim('/');
        if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
            || !query.TryGetValue("tick", out string? t) || !int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
            return new DebugRouteReply(400, "{\"error\":\"ask /ai/replay/bot/<slot>?tick=N\"}");
        return Bot(slot, new Tick(tick), wait: query.GetValueOrDefault("wait") == "1");
    }

    /// <summary>Reads the recording and stands the replay before its first snapshot.</summary>
    public DebugRouteReply Load(string path)
    {
        if (!path.EndsWith(".airec", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return new DebugRouteReply(400, $"{{\"error\":{AiJson.Quote($"no .airec at '{path}'")}}}");
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            // The graph is asked once, here on the game thread: the level's, if it is the recording's.
            NavGraph? graph = nav.Current?.Nav.Graph;
            ICellSight? sight = nav.Sight;
            var cursor = new AiReplayCursor(bytes, known =>
            {
                if (known.NavGraphHash != 0 && graph?.Hash != known.NavGraphHash)
                    throw new InvalidDataException($"the recording was made on {known.Map.Map} (nav {known.NavGraphHash:x16}); load it on that map");
                return (graph, sight);
            }, setup => new Trees.Hl2mpDecider(setup));
            lock (_gate)
            {
                if (_seeking is { IsCompleted: false })
                    return new DebugRouteReply(409, "{\"error\":\"a seek is running: load when it is done\"}");
                _cursor = cursor;
                _path = path;
                _fault = null;
                _seekTo = -1;
            }

            log.Msg($"[SourceSharp/Ai] replay: loaded {path} ({cursor.Ticks.Count} ticks, {cursor.Levels.Count} bots)\n");
            return Status();
        }
        catch (InvalidDataException e)
        {
            return new DebugRouteReply(422, $"{{\"error\":{AiJson.Quote(e.Message)}}}");
        }
    }

    private DebugRouteReply Status()
    {
        lock (_gate)
        {
            if (_cursor is not { } c)
                return new DebugRouteReply(404, "{\"error\":\"nothing loaded: POST /ai/replay/load?path=\"}");
            var answer = new
            {
                path = _path,
                map = c.Header.Map,
                seed = c.Header.MatchSeed,
                world = c.World is not null,
                first = c.Ticks.Count > 0 ? c.Ticks[0].Value : -1,
                last = c.Ticks.Count > 0 ? c.Ticks[^1].Value : -1,
                ticks = c.Ticks.Count,
                bots = c.Levels.Select(kv => new { slot = kv.Key, levels = kv.Value.Select(b => (int)b).ToArray() }).ToArray(),
                at = c.At?.Value ?? -1,
                seeking = _seeking is { IsCompleted: false } ? _seekTo : -1,
                fault = _fault,
            };
            return new DebugRouteReply(200, AiJson.Write(answer));
        }
    }

    /// <summary>
    /// The bot's record at <paramref name="tick"/>: from the replay when it
    /// stands there, else a seek is started off the game thread and 202 is the
    /// answer (or, with <paramref name="wait"/>, the facts' and the replay
    /// tool's, the seek runs here).
    /// </summary>
    public DebugRouteReply Bot(int slot, Tick tick, bool wait = false)
    {
        AiReplayCursor c;
        lock (_gate)
        {
            if (_cursor is null)
                return new DebugRouteReply(404, "{\"error\":\"nothing loaded: POST /ai/replay/load?path=\"}");
            c = _cursor;
            if (_seeking is { IsCompleted: false })
                return new DebugRouteReply(202, $"{{\"seeking\":{_seekTo},\"at\":{c.At?.Value ?? -1}}}");
            if (_fault is { } fault)
            {
                _fault = null;
                return new DebugRouteReply(500, $"{{\"error\":{AiJson.Quote(fault)}}}");
            }
        }

        Tick target = Standing(c, tick);
        if (c.At != target)
        {
            if (wait)
            {
                c.SeekTo(target);
            }
            else
            {
                lock (_gate)
                {
                    _seekTo = target.Value;
                    _seeking = Task.Run(() =>
                    {
                        try
                        {
                            c.SeekTo(target);
                        }
                        catch (Exception e) when (e is InvalidDataException or InvalidOperationException)
                        {
                            lock (_gate)
                                _fault = e.Message;
                        }
                    });
                }

                return new DebugRouteReply(202, $"{{\"seeking\":{target.Value},\"at\":{c.At?.Value ?? -1}}}");
            }
        }

        return new DebugRouteReply(200, Record(c, slot));
    }

    /// <summary>The last snapshot's tick at or before <paramref name="tick"/> (the first, before it).</summary>
    private static Tick Standing(AiReplayCursor c, Tick tick)
    {
        Tick best = c.Ticks.Count > 0 ? c.Ticks[0] : tick;
        foreach (Tick t in c.Ticks)
        {
            if (t > tick)
                break;
            best = t;
        }

        return best;
    }

    /// <summary>The scrubber's frame for the bot in <paramref name="slot"/> where the replay stands.</summary>
    public string Record(AiReplayCursor c, int slot)
    {
        Tick at = c.At ?? default;
        if (c.Session.BrainIn(slot) is not { } brain || !c.Levels.TryGetValue(slot, out byte[]? levels))
            return $"{{\"tick\":{at.Value},\"error\":\"no bot in slot {slot} at this tick\"}}";
        var skills = new SkillProfile("recorded", StandardSkills.Ten, [.. levels.Select(b => (int)b)]);
        BotInspectRecord record = BotInspect.Build(brain, skills, at);
        Tick? decide = c.LastDecide(slot, at);
        IntentCommand recorded = default, replayed = default;
        bool hasRecorded = decide is { } d1 && c.TryRecorded(slot, d1, out recorded);
        bool hasReplayed = decide is { } d2 && c.TryReplayed(slot, d2, out replayed);
        bool same = hasRecorded && hasReplayed && recorded == replayed;
        return $"{{\"tick\":{at.Value},\"decide\":{decide?.Value ?? -1},\"same\":{(same ? "true" : "false")},"
               + $"\"recorded\":{(hasRecorded ? AiJson.Write(recorded) : "null")},"
               + $"\"replayed\":{(hasReplayed ? AiJson.Write(replayed) : "null")},"
               + $"\"record\":{AiJson.Write(record)}}}";
    }
}
