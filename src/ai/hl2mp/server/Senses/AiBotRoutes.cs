using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>An <see cref="AiVector"/> as <c>[x, y, z]</c>, rounded to a tenth.</summary>
public sealed class AiVectorJson : JsonConverter<AiVector>
{
    public override AiVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, AiVector value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(MathF.Round(value.X, 1));
        writer.WriteNumberValue(MathF.Round(value.Y, 1));
        writer.WriteNumberValue(MathF.Round(value.Z, 1));
        writer.WriteEndArray();
    }
}

/// <summary>
/// The bot inspector's routes (plan_hl2mp_ai §7.2, §7.3), web UI only:
/// <c>/ai/bots</c> lists the bots; <c>/ai/bot/&lt;slot&gt;</c> answers the
/// <see cref="BotInspectRecord"/> (memory and skills) and the perception
/// part, and <c>/ai/bot/&lt;slot&gt;/memory|skills|perception</c> one part;
/// <c>/ai/counters</c> the senses' cost (<c>ai.sense.us</c>). Read on the game
/// thread when asked; nothing about a bot's mind is networked to a client.
/// The binding's one <see cref="IDebugRouteProvider"/>
/// (<c>Nav.AiDebugRoutes</c>, a mod registers a contract once) serves them.
/// </summary>
[SideService(Side.Server)]
public sealed class AiBotRoutes(Hl2mpBots bots, Hl2mpSenses senses, VisionPass vision, IEngineFrame frame, IEngineEntities entities) 
{
    private readonly JsonSerializerOptions _json = Options();

    /// <summary>The inspector's JSON: camel case, vectors as arrays, NaN spelled out.</summary>
    public static JsonSerializerOptions Options() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new AiVectorJson() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", "/ai/bots", "the bots: slot, serial, name, skill label"),
        new("GET", "/ai/bot/", "one bot's inspector: /ai/bot/<slot>[/memory|skills|perception|trace]"),
        new("GET", "/ai/counters", "the AI's main-thread counters: ai.sense.us (300-tick mean and worst), ai.sense.traces"),
    ];

    /// <summary>Whether <paramref name="path"/> is one of these routes'.</summary>
    public static bool Serves(string path) => path is "/ai/bots" or "/ai/counters" || path.StartsWith("/ai/bot/", StringComparison.Ordinal);

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        if (path == "/ai/bots")
            return new DebugRouteReply(200, JsonSerializer.Serialize(bots.Bots.Select(b => new
            {
                slot = b.Id.Slot, serial = b.Id.Serial, name = b.Name, profile = b.Skills.Name, spent = b.Skills.Spent,
            }), _json));
        if (path == "/ai/counters")
            return new DebugRouteReply(200, string.Create(CultureInfo.InvariantCulture,
                $"{{\"tick\":{frame.TickCount()},\"ai.sense.us\":{senses.SenseMeanUs:F1},\"ai.sense.us.max\":{senses.SenseMaxUs:F1},\"ai.publish.us\":{senses.PublishMeanUs:F1},\"ai.sense.ticks\":{Math.Min(senses.SenseTicks, Hl2mpSenses.Window)},\"ai.sense.traces\":{vision.TracesLastTick},\"ai.sense.traces.total\":{vision.Traces},\"ai.brain.us\":{senses.Session.Counters.BrainMicrosMean:F1},\"ai.brain.us.max\":{senses.Session.Counters.BrainMicrosMax:F1},\"ai.brain.steps\":{senses.Session.Counters.StepsRun},\"ai.follow.builds\":{bots.Driver.Builds},\"bots\":{bots.Bots.Count}}}"));

        string[] parts = path["/ai/bot/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
            || bots.Bots.FirstOrDefault(b => b.Id.Slot == slot) is not { Id.IsValid: true } bot)
            return new DebugRouteReply(404, "{\"error\":\"no bot in that slot: GET /ai/bots\"}");
        if (senses.Session.BrainIn(slot) is not { } brain)
            return new DebugRouteReply(409, "{\"error\":\"the bot has no brain yet (the senses add it on their next tick)\"}");

        string part = parts.Length > 1 ? parts[1] : "all";
        var tick = new Tick(frame.TickCount());
        BotInspectRecord record = BotInspect.Build(brain, bot.Skills, tick);
        return part switch
        {
            "memory" => new DebugRouteReply(200, JsonSerializer.Serialize(record.Memory, _json)),
            "skills" => new DebugRouteReply(200, JsonSerializer.Serialize(record.Skills, _json)),
            "perception" => new DebugRouteReply(200, Perception(bot)),
            "trace" or "doing" => new DebugRouteReply(200, JsonSerializer.Serialize(record.Doing, _json)),
            "all" => new DebugRouteReply(200, $"{{\"record\":{JsonSerializer.Serialize(record, _json)},\"perception\":{Perception(bot)}}}"),
            _ => new DebugRouteReply(404, "{\"error\":\"parts: memory, skills, perception, trace\"}"),
        };
    }

    /// <summary>
    /// What the bot's eyes did this tick (§7.1, perception): its eye, view and
    /// field, every other living player with the stage it reached (range,
    /// cone, pvs, trace, unserved, clear) and its visible share, and where it
    /// last heard each one. The positions are the debugger's, not the bot's.
    /// </summary>
    private string Perception(Hl2mpBot bot)
    {
        var json = new StringBuilder();
        VisionParams v = VisionCurves.For(bot.Skills);
        AiVector eye = vision.ReadOf(bot.Id.Slot)?.Eye ?? default;
        EntityHandle player = bots.Applier.PlayerOf(bot.Id);
        Vector ang = player == EntityHandle.None ? default : entities.GetEyeAngles(player);
        json.Append(CultureInfo.InvariantCulture, $"{{\"slot\":{bot.Id.Slot},\"eye\":[{eye.X:F1},{eye.Y:F1},{eye.Z:F1}],\"yaw\":{ang.Y:F1},\"pitch\":{ang.X:F1},")
            .Append(CultureInfo.InvariantCulture, $"\"fov\":{v.FovDeg:F0},\"range\":{v.SightRange:F0},\"candidates\":[");
        SightTracker? sight = vision.SightOf(bot.Id.Slot);
        bool first = true;
        for (int t = 1; t < VisionPass.Slots; t++)
        {
            if (t == bot.Id.Slot || vision.ReadOf(t) is not { } them)
                continue;
            AiVector c = VisionPass.Centre(them);
            json.Append(first ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"slot\":{t},\"at\":[{c.X:F1},{c.Y:F1},{c.Z:F1}],\"stage\":\"{vision.StageOf(bot.Id.Slot, t).ToString().ToLowerInvariant()}\",")
                .Append(CultureInfo.InvariantCulture, $"\"fraction\":{sight?.FractionOf(t) ?? 0f:F2},\"sees\":{(sight?.Sees(t) == true ? "true" : "false")}}}");
            first = false;
        }

        json.Append("],\"heard\":[");
        first = true;
        if (senses.Session.BrainIn(bot.Id.Slot)?.Memory is { } m)
        {
            for (int s = 0; s < m.Board.Slots; s++)
            {
                KnownActor a = m.Board.Actor(s);
                if (!a.Known || !a.EverHeard)
                    continue;
                json.Append(first ? "" : ",").Append(CultureInfo.InvariantCulture,
                    $"{{\"slot\":{s},\"at\":[{a.LastHeardPos.X:F1},{a.LastHeardPos.Y:F1},{a.LastHeardPos.Z:F1}],\"tick\":{a.LastHeardTick.Value},\"kind\":\"{a.LastHeardKind}\"}}");
                first = false;
            }
        }

        return json.Append("]}").ToString();
    }
}
