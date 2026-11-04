using System.Globalization;
using System.Text;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp.Senses;

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
public sealed class AiBotRoutes(Hl2mpBots bots, Hl2mpRoster roster, Hl2mpSenses senses, VisionPass vision, IEngineFrame frame, IEngineEntities entities,
                                Hl2mpAiSession ai, Nav.IHl2mpNavigation nav, AimMeter aim, Chat.Hl2mpBotChat? chat = null, Nav.Hl2mpNav? navField = null, IEngineEntityRender? modelNames = null,
                                AiMainBudget? budget = null)
{
    private AiWorkerPool Pool => ai.Pool;

    /// <summary>
    /// The AI's worker threads the kernel lists now, by name (§2.5's gate:
    /// after a level change it equals the pool's count, the old workers
    /// joined), or -1 off Linux.
    /// </summary>
    public static int WorkerThreads()
    {
        if (!Directory.Exists("/proc/self/task"))
            return -1;
        int n = 0;
        foreach (string task in Directory.GetDirectories("/proc/self/task"))
        {
            try
            {
                if (File.ReadAllText(Path.Combine(task, "comm")).StartsWith("ai-worker", StringComparison.Ordinal))
                    n++;
            }
            catch (IOException)
            {
                // a thread that ended while listed
            }
        }

        return n;
    }

    /// <summary><c>/ai/bots</c>: each bot's slot, serial, name, profile and points spent.</summary>
    public static string BotsJson(IReadOnlyList<RosterBot> list)
        => AiJson.Write(list.Select(b => new
        {
            slot = b.Active ? b.Actor.Slot : -1,
            serial = b.Actor.Serial,
            name = b.Name,
            profile = b.Skills.Name,
            spent = b.Skills.Spent,
            tag = BotSkillLabel.For(b.Skills),
            style = b.Skills.StyleName,
            levels = b.Skills.Levels.ToArray(),
            state = b.Active ? "active" : "benched",
            reason = b.Reason,
        }));

    /// <summary>The process's threads now (the gate's baseline across map loads, §2.5).</summary>
    public static int ProcessThreads()
    {
        try
        {
            return Directory.Exists("/proc/self/task")
                ? Directory.GetDirectories("/proc/self/task").Length
                : System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
        }
        catch (IOException)
        {
            return -1;
        }
    }

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", "/ai/bots", "the roster: each wanted bot's slot (-1 benched), serial, name, skills, drawn levels, state and reason"),
        new("GET", "/ai/chat", "bot chat (ai_chat): the endpoint and model, each bot's last lines, the filter's refusals by reason"),
        new("GET", "/ai/bot/", "one bot's inspector: /ai/bot/<slot>[/memory|skills|perception|trace]"),
        new("GET", "/ai/aim", "the aim meter: each bot's account of each player it saw, shot at or hit (in-sight ticks, firing, crosshair error, first shot, shots, hits by weapon and band, leaves); ?reset=1 starts afresh"),
        new("GET", "/ai/steps", "how far each bot went each tick, all bots together: a histogram of steps, the runs of ticks it stood between steps, and ?reset=1 to start afresh"),
        new("GET", "/ai/counters", "the AI's counters: ai.sense.us, ai.publish.us, ai.apply.us, ai.main.us (300-tick means), ai.brain.us per decide, ai.pool.* (threads, busy µs a second), ai.steps.* (skipped, faulted, in flight), process.threads"),
    ];

    /// <summary>Whether <paramref name="path"/> is one of these routes'.</summary>
    public static bool Serves(string path) => path is "/ai/bots" or "/ai/counters" or "/ai/aim" or "/ai/chat" or "/ai/steps" || path.StartsWith("/ai/bot/", StringComparison.Ordinal);

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        if (path == "/ai/bots")
            return new DebugRouteReply(200, BotsJson(roster.Bots));
        if (path == "/ai/aim")
        {
            string json = aim.Json();
            if (query.ContainsKey("reset"))
                aim.Reset();
            return new DebugRouteReply(200, json);
        }

        if (path == "/ai/steps")
        {
            string json = senses.Steps.Json();
            if (query.ContainsKey("reset"))
                senses.Steps.Reset();
            return new DebugRouteReply(200, json);
        }

        if (path == "/ai/chat")
            return new DebugRouteReply(200, chat?.Json() ?? "{}");
        if (path == "/ai/counters")
            return new DebugRouteReply(200, string.Create(CultureInfo.InvariantCulture,
                $"{{\"tick\":{frame.TickCount()},\"ai.sense.us\":{senses.SenseMeanUs:F1},\"ai.sense.us.max\":{senses.SenseMaxUs:F1},\"ai.publish.us\":{senses.PublishMeanUs:F1},\"ai.sense.ticks\":{Math.Min(senses.SenseTicks, Hl2mpSenses.Window)},\"ai.sense.traces\":{vision.TracesLastTick},\"ai.sense.traces.total\":{vision.Traces},\"ai.brain.us\":{senses.Session.Counters.BrainMicrosMean:F1},\"ai.brain.us.max\":{senses.Session.Counters.BrainMicrosMax:F1},\"ai.brain.cpu.us\":{senses.Session.Counters.BrainCpuMicrosMean:F1},\"ai.brain.steps\":{senses.Session.Counters.StepsRun},\"ai.follow.builds\":{ai.Drives.Totals.Builds},\"ai.follow.builds.main\":{ai.Drives.Totals.BuildsOnMain},\"ai.drive.steps\":{ai.Drives.Steps},\"ai.drive.main\":{ai.Drives.StepsOnMain},\"ai.drive.late\":{ai.Drives.Late},\"ai.drive.repeats\":{bots.Applier.Repeats},\"ai.drive.refused\":{ai.Drives.Refused},\"ai.drive.faults\":{ai.Drives.Faults},\"ai.drive.us\":{(ai.Drives.Steps == 0 ? 0 : ai.Drives.Micros / ai.Drives.Steps):F1},\"ai.drive.us.max\":{ai.Drives.MicrosMax:F1},\"bots\":{bots.Bots.Count},"
                + $"\"ai.apply.us\":{bots.ApplyMeanUs:F1},\"ai.run.us\":{bots.RunMeanUs:F1},"
                + $"\"ai.bots.added\":{bots.Added},\"ai.bots.adopted\":{bots.Adopted},\"ai.punch.peak_cdeg\":{(int)(bots.Applier.PunchPeakDeg * 100f)},\"ai.streaks.hot\":{bots.HotStreaks},\"ai.streaks.cold\":{bots.ColdStreaks},\"ai.breaks\":{ai.Drives.Totals.Breaks},\"ai.breakables\":{navField?.Field?.BreakableBodies ?? 0},\"ai.breaks.given_up\":{ai.Drives.Totals.BreaksGivenUp},\"ai.bots.models\":\"{string.Join('|', bots.Bots.Select(b => modelNames?.GetModelName(bots.Applier.PlayerOf(b.Id))))}\","
                + $"\"ai.obstacles.us\":{navField?.Field?.MeanUs ?? 0:F1},\"ai.obstacles.bodies\":{navField?.Field?.Bodies ?? 0},\"ai.obstacles.moved\":{navField?.Field?.Moved ?? 0},"
                + $"\"ai.obstacles.learned\":{navField?.Field?.Learned ?? 0},\"ai.obstacles.published\":{navField?.Field?.Published ?? 0},"
                + $"\"ai.obstacles.blocked\":{navField?.Field?.TruthCells.Blocked ?? 0},\"ai.obstacles.costly\":{navField?.Field?.TruthCells.Costly ?? 0},"
                + $"\"ai.refused.stale\":{bots.Applier.Refused(IntentRefusal.StaleSerial)},\"ai.refused.unknown\":{bots.Applier.Refused(IntentRefusal.UnknownBot)},"
                + $"\"ai.refused.expired\":{bots.Applier.Refused(IntentRefusal.Expired)},\"ai.main.us\":{senses.SenseMeanUs + senses.PublishMeanUs + bots.ApplyMeanUs:F1},"
                + $"\"ai.sense.cpu.us\":{senses.SenseCpuMeanUs:F1},\"ai.main.cpu.us\":{senses.SenseCpuMeanUs + senses.PublishCpuMeanUs + bots.ApplyCpuMeanUs:F1},\"ai.pool.cpu.us\":{senses.PoolCpuUsPerSecond:F1},"
                + $"\"ai.sync\":{(senses.Session.Transport.Inline ? 1 : 0)},\"ai.pool.threads\":{Pool.Threads},\"ai.pool.alive\":{Pool.Alive},\"ai.pool.os_threads\":{WorkerThreads()},\"ai.pool.us\":{senses.PoolUsPerSecond:F1},"
                + $"\"ai.pool.ran\":{Pool.Ran},\"ai.pool.faults\":{Pool.Faults},\"ai.steps.skipped\":{senses.Session.Counters.StepsSkipped},\"ai.steps.faulted\":{senses.Session.Counters.StepsFaulted},\"ai.steps.held\":{senses.Session.Counters.StepsHeld},"
                + $"\"ai.steps.inflight\":{senses.Session.Counters.InFlight},\"ai.steps.inflight.max\":{senses.Session.Counters.InFlightMax},\"process.threads\":{ProcessThreads()},"
                + $"{BudgetCounters()}{ChatCounters()}"
                + $"\"nav.map\":{AiJson.Quote(nav.Current?.Map ?? "")},\"nav.cells\":{nav.Current?.Nav.Graph.CellCount ?? 0},\"nav.refusal\":{AiJson.Quote(nav.Refusal)}}}"));

        string[] parts = path["/ai/bot/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
            || bots.Bots.FirstOrDefault(b => b.Id.Slot == slot) is not { Id.IsValid: true } bot)
            return new DebugRouteReply(404, "{\"error\":\"no bot in that slot: GET /ai/bots\"}");
        if (senses.Session.BrainIn(slot) is null)
            return new DebugRouteReply(409, "{\"error\":\"the bot has no brain yet (the senses add it on their next tick)\"}");

        string part = parts.Length > 1 ? parts[1] : "all";
        var tick = new Tick(frame.TickCount());
        // Read only while none of its steps runs on a worker; never waited for.
        if (!senses.Session.TryReadBrain(slot, b => BotInspect.Build(b, bot.Skills, tick), out BotInspectRecord record))
            return new DebugRouteReply(503, "{\"error\":\"the bot's brain is stepping on a worker: ask again\"}");
        return part switch
        {
            "memory" => new DebugRouteReply(200, AiJson.Write(record.Memory)),
            "skills" => new DebugRouteReply(200, AiJson.Write(record.Skills)),
            "perception" => new DebugRouteReply(200, Perception(bot)),
            "trace" or "doing" => new DebugRouteReply(200, AiJson.Write(record.Doing)),
            "all" => new DebugRouteReply(200, $"{{\"record\":{AiJson.Write(record)},\"perception\":{Perception(bot)}}}"),
            _ => new DebugRouteReply(404, "{\"error\":\"parts: memory, skills, perception, trace\"}"),
        };
    }

    /// <summary>The main-thread budget's part of <c>/ai/counters</c>, with its comma: what the bots used of what they were allowed, over the last frames.</summary>
    private string BudgetCounters()
    {
        if (budget is null)
            return "";
        MainThreadBudget b = budget.Measure;
        BudgetStats s = b.FrameStats();
        return string.Create(CultureInfo.InvariantCulture,
            $"\"ai.budget.on\":{(b.Capped ? 1 : 0)},\"ai.budget.target.us\":{b.TargetUs:F0},\"ai.budget.nonbot.us\":{b.NonBotEstimateUs:F0},\"ai.budget.allowance.us\":{s.AllowedMean:F0},\"ai.budget.used.us\":{s.UsedMean:F1},\"ai.budget.used.us.p99\":{s.UsedP99:F0},\"ai.budget.used.us.max\":{s.UsedMax:F0},"
            + $"\"ai.budget.deferrals\":{b.Deferrals},\"ai.budget.sense.deferrals\":{vision.LooksDeferred},\"ai.budget.forced\":{b.Forced},\"ai.budget.frames\":{b.Frames},\"ai.budget.frames.over\":{b.FramesOver},\"ai.budget.frames.late\":{b.FramesLate},"
            + $"\"ai.frame.us.p50\":{s.PeriodP50:F0},\"ai.frame.us.p99\":{s.PeriodP99:F0},\"ai.frame.us.max\":{s.PeriodMax:F0},");
    }

    /// <summary>Bot chat's part of <c>/ai/counters</c> (§3.6), with its comma; nothing without chat in the composition.</summary>
    private string ChatCounters()
    {
        if (chat?.Counters is not { } c)
            return "";
        return string.Create(CultureInfo.InvariantCulture,
            $"\"ai.chat.questions\":{c.Questions},\"ai.chat.answers.model\":{c.AnswersModel},\"ai.chat.answers.template\":{c.AnswersTemplate},\"ai.chat.answers.unverified\":{c.AnswersUnverified},\"ai.chat.answers.dropped\":{c.AnswersDropped},\"ai.chat.shrugs\":{c.Shrugs},\"ai.chat.marks\":{c.Marks},"
            + $"\"ai.chat.triggers\":{c.Triggers},\"ai.chat.chance_missed\":{c.ChanceMissed},\"ai.chat.limit.interval\":{c.LimitedBy(ChatLimit.Interval)},\"ai.chat.limit.cooldown\":{c.LimitedBy(ChatLimit.Cooldown)},\"ai.chat.limit.fight\":{c.LimitedBy(ChatLimit.Fight)},\"ai.chat.limit.reply\":{c.LimitedBy(ChatLimit.Reply)},\"ai.chat.limit.paused\":{c.LimitedBy(ChatLimit.Paused)},\"ai.chat.limit.queue\":{c.LimitedBy(ChatLimit.Queue)},"
            + $"\"ai.chat.requests\":{c.Requests},\"ai.chat.answered\":{c.Answered},\"ai.chat.said\":{c.Said},\"ai.chat.replies\":{c.Replies},\"ai.chat.latency.ms\":{c.LatencyMeanMs:F1},\"ai.chat.latency.ms.max\":{c.LatencyMaxMs:F1},"
            + $"\"ai.chat.timeouts\":{c.TimedOut},\"ai.chat.server_down\":{c.ServerDown},\"ai.chat.refused\":{c.Refused},\"ai.chat.rate_limited\":{c.RateLimited},\"ai.chat.stale\":{c.Stale},\"ai.chat.main.us\":{c.MainMeanUs:F2},\"ai.chat.main.us.max\":{c.MainMaxUs:F1},");
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
        senses.Session.TryReadBrain(bot.Id.Slot, b =>
        {
            if (b.Memory is not { } m)
                return 0;
            for (int s = 0; s < m.Board.Slots; s++)
            {
                KnownActor a = m.Board.Actor(s);
                if (!a.Known || !a.EverHeard)
                    continue;
                json.Append(first ? "" : ",").Append(CultureInfo.InvariantCulture,
                    $"{{\"slot\":{s},\"at\":[{a.LastHeardPos.X:F1},{a.LastHeardPos.Y:F1},{a.LastHeardPos.Z:F1}],\"tick\":{a.LastHeardTick.Value},\"kind\":\"{a.LastHeardKind}\"}}");
                first = false;
            }

            return 0;
        }, out int _);

        return json.Append("]}").ToString();
    }
}
