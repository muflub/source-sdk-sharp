using System.Text.Json;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_tdm's scenario (plan_bots_teams_tf2 B.8), from its committed recording: two bots a
/// side of Teamwork 10 under team deathmatch, brains deciding and nobody acting. A teammate
/// stands and walks across a bot's line of fire to a sighted enemy; a spotter sees an enemy
/// its teammate cannot; a teammate is killed in sight by an enemy in sight, ten times. The
/// recording replays with the game's trees over this tree's ss_ai_tdm nav, byte for byte,
/// inline and at 1, 2 and 4 threads, and the replay says what each brain's team layer chose.
/// </summary>
public sealed class SsAiTdmScenarioTests
{
    private const string Map = "ss_ai_tdm";

    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load(Map));

    private static readonly Lazy<(NavGraph Graph, NavSight Sight)> Nav = new(() =>
    {
        NavLoadResult l = NavLoader.Load(Map, p => File.ReadAllBytes(p.EndsWith(".nav3d", StringComparison.Ordinal) ? PerTreeNav.NavPath(Map) : PerTreeNav.BspPath(Map)), null);
        Assert.True(l.Nav is not null, l.Refusal);
        return (l.Nav!.Nav.Graph, NavSight.Build(l.Nav.Nav.Graph, new Nav3dVoxelSpace(l.Nav.Reader)));
    });

    /// <summary>
    /// The inline replay with this tree's deciders: every intent they made, and each bot's team
    /// role after every tick. The facts read what the replay decided, so a change to the trees
    /// is judged here and not only against what the recording's trees did.
    /// </summary>
    private static readonly Lazy<(AiReplayResult Result, List<IntentCommand> Intents, Dictionary<int, SortedList<int, string>> Roles)> Replayed = new(() =>
    {
        var deciders = new Dictionary<int, Hl2mpDecider>();
        var roles = new Dictionary<int, SortedList<int, string>>();
        var intents = new List<IntentCommand>();
        AiReplayResult r = Replay(new InlineTransport(), setup =>
        {
            var d = new Hl2mpDecider(setup);
            deciders[setup.Bot.Slot] = d;
            roles[setup.Bot.Slot] = [];
            return new Capturing(d, intents);
        }, (_, tick) =>
        {
            foreach ((int slot, Hl2mpDecider d) in deciders)
                roles[slot][tick.Value] = d.TeamRole;
        });
        return (r, intents, roles);
    });

    /// <summary>A decider that keeps what the one it wraps decided.</summary>
    private sealed class Capturing(Hl2mpDecider inner, List<IntentCommand> sink) : IDecider
    {
        public BtTrace Trace => inner.Trace;

        public DoingRecord Doing => inner.Doing;

        public IReadOnlyList<string> NodeNames => inner.NodeNames;

        public IReadOnlyList<int> NodeBranches => inner.NodeBranches;

        public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
        {
            IntentCommand i = inner.Decide(tick, view, seq);
            sink.Add(i);
            return i;
        }
    }

    private static AiReplayResult Replay(IAiTransport transport, Func<BrainSetup, IDecider?> deciders, Action<AiSession, Tick>? published = null)
    {
        using FileStream f = File.OpenRead(ScenarioRecording.Path(Map + ".airec"));
        return AiReplayer.ReplayWithRecordedWorld(f, transport, _ => (Nav.Value.Graph, Nav.Value.Sight), deciders, published);
    }

    private static int Slot(string who) => Recording.Value.Summary.GetProperty(who).GetInt32();

    private static int TeamOf(int slot) => Recording.Value.Summary.GetProperty("teams").TryGetProperty(slot.ToString(System.Globalization.CultureInfo.InvariantCulture), out JsonElement t) ? t.GetInt32() : 0;

    private static (int From, int To) Scene(string name)
    {
        JsonElement s = Recording.Value.Summary.GetProperty("scenes").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        return (s.GetProperty("from").GetInt32(), s.GetProperty("to").GetInt32());
    }

    private static bool InScenes(int tick, string prefix)
        => Recording.Value.Summary.GetProperty("scenes").EnumerateArray()
            .Any(e => e.GetProperty("name").GetString()!.StartsWith(prefix, StringComparison.Ordinal)
                      && tick >= e.GetProperty("from").GetInt32() && tick <= e.GetProperty("to").GetInt32());

    [PerTreeNavFact(Map)]
    public void TheScenarioReplaysInlineToItsIntents()
    {
        AiReplayResult r = Replayed.Value.Result;
        Assert.StartsWith(Map, r.Header.Map);
        Assert.True(r.RecordedIntentCount > 1000, $"the control: {r.RecordedIntentCount} intents recorded");
        Assert.Equal(r.RecordedIntentCount, r.ReplayedIntentCount);
        Assert.True(r.Identical, "inline: the intents differ from the recording's");
    }

    /// <summary>B.8's determinism with callouts: the callouts are re-published by the replay's own session, in slot order, at every pool size.</summary>
    [PerTreeNavFact(Map)]
    public void TheScenarioReplaysAtOneTwoAndFourThreadsToItsIntents()
    {
        Assert.Contains(Recording.Value.Events, e => e.Kind == AiEventKind.Callout);
        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            AiReplayResult r = Replay(new PooledTransport(pool), setup => new Hl2mpDecider(setup));
            Assert.Equal(r.RecordedIntentCount, r.ReplayedIntentCount);
            Assert.True(r.Identical, $"{threads} thread(s): the intents differ from the recording's");
        }
    }

    // ------------------------------------------------------------------ the friend gate

    private const float EyeHeight = 64f;

    /// <summary>The view a decide at <paramref name="tick"/> read: the bot's newest published view at or before it.</summary>
    private static (SelfState Self, ActorFacts[] Sighted)? ViewAt(int slot, int tick)
    {
        ScenarioRecording r = Recording.Value;
        for (int t = tick; t > tick - 8; t--)
        {
            if (r.Views.TryGetValue((t, slot), out ActorFacts[]? seen) && r.Selves.TryGetValue((t, slot), out SelfState self))
                return (self, seen);
        }

        return null;
    }

    /// <summary>The angle from a line along <paramref name="dir"/> to a standing body, at the shooter's height on it (AimCone's rule).</summary>
    private static double AngleToBody(AiVector dir, AiVector eye, AiVector origin)
    {
        AiVector axis = new(0f, 0f, EyeHeight);
        float u = Math.Clamp((eye - origin).Dot(axis) / axis.LengthSquared, 0f, 1f);
        AiVector to = (origin + axis * u - eye).Normalised();
        return Math.Acos(Math.Clamp(dir.Dot(to), -1f, 1f)) * (180.0 / Math.PI);
    }

    /// <summary>
    /// B.8's measure, from the recording alone: a teammate the bot sees is nearer than where it
    /// aims and within <see cref="AimCone"/> of the line to it, or of the bot's view.
    /// </summary>
    private static bool FriendOnTheLine(in IntentCommand i, out bool enemySighted)
    {
        enemySighted = false;
        if (ViewAt(i.Bot.Slot, i.FromTick.Value) is not { } v)
            return false;
        int team = TeamOf(i.Bot.Slot);
        AiVector eye = v.Self.Origin + new AiVector(0f, 0f, EyeHeight);
        AiVector viewDir = VisionMath.Forward(v.Self.Eye);
        bool aimed = i.Look.Kind is LookKind.Actor or LookKind.CornerCheck or LookKind.Point;
        AiVector aimAt = aimed ? i.Look.Point : eye + viewDir * 2048f;
        float reach = (aimAt - eye).Length;
        AiVector aimDir = reach > 0f ? (aimAt - eye) * (1f / reach) : viewDir;
        bool friend = false;
        foreach (ActorFacts f in v.Sighted)
        {
            if (TeamOf(f.Id.Slot) != team)
            {
                enemySighted |= TeamOf(f.Id.Slot) != 0;
                continue;
            }

            float d = (f.Origin - eye).Length;
            friend |= d < reach && (AimCone.Holds(AngleToBody(aimDir, eye, f.Origin), d) || AimCone.Holds(AngleToBody(viewDir, eye, f.Origin), d));
        }

        return friend;
    }

    /// <summary>The shooter's decides while its teammate stands on or walks across its line, each with how many ticks it held.</summary>
    private static List<(IntentCommand Intent, int Ticks, bool OnLine, bool EnemySighted)> ShooterDecides(string prefix)
    {
        int shooter = Slot("shooter");
        IntentCommand[] mine = [.. Replayed.Value.Intents.Where(i => i.Bot.Slot == shooter).OrderBy(i => i.FromTick.Value)];
        var out1 = new List<(IntentCommand, int, bool, bool)>();
        for (int k = 0; k < mine.Length; k++)
        {
            if (!InScenes(mine[k].FromTick.Value, prefix))
                continue;
            int ticks = k + 1 < mine.Length ? Math.Min(mine[k + 1].FromTick.Value - mine[k].FromTick.Value, IntentCommand.LifetimeTicks) : 6;
            bool on = FriendOnTheLine(mine[k], out bool enemy);
            out1.Add((mine[k], ticks, on, enemy));
        }

        return out1;
    }

    [PerTreeNavFact(Map)]
    public void ABotNeverFiresWithASightedTeammateNearerOnItsLine()
    {
        var decides = ShooterDecides("in line").Concat(ShooterDecides("crossing")).ToList();
        int onLineTicks = decides.Where(x => x.OnLine && x.EnemySighted).Sum(x => x.Ticks);
        Assert.True(onLineTicks > 100, $"the stimulus: {onLineTicks} ticks with a teammate on the line and the enemy in sight");
        Assert.DoesNotContain(decides, x => x.OnLine && x.Intent.Action.Kind is ActionKind.Fire or ActionKind.AltFire);
    }

    /// <summary>The control: with the line clear, the same bot at the same enemy fires.</summary>
    [PerTreeNavFact(Map)]
    public void WithTheLineClearTheBotFires()
    {
        var clear = ShooterDecides("clear");
        Assert.All(clear, x => Assert.False(x.OnLine, "the clear scene's line is clear"));
        Assert.Contains(clear, x => x.Intent.Action.Kind == ActionKind.Fire);
    }

    // ------------------------------------------------------------------ callouts

    [PerTreeNavFact(Map)]
    public void CalloutsReachTeammatesAndNoEnemy()
    {
        AiEvent[] heard = [.. Recording.Value.Events.Where(e => e.Kind == AiEventKind.Callout && e.CalloutKind == CalloutKind.Enemy)];
        Assert.True(heard.Length > 0, "the stimulus: no callout was heard");
        Assert.All(heard, e => Assert.Equal(TeamOf(e.Other.Slot), TeamOf(e.Bot.Slot)));
        // The control: the enemies were in the game, hearing other things, all along.
        int foe = Slot("foe");
        Assert.Contains(Recording.Value.Events, e => e.Bot.Slot == foe && e.Kind == AiEventKind.SightGained);
    }

    /// <summary>B.4: a callout arrives 400-1,200 ms after the speaker's brain said it (its calloutDelayMs, at least the session's floor).</summary>
    [PerTreeNavFact(Map)]
    public void ACalloutArrivesAfterTheSpeakersDelay()
    {
        ScenarioRecording r = Recording.Value;
        AiEvent[] heard = [.. r.Events.Where(e => e.Kind == AiEventKind.Callout && e.CalloutKind == CalloutKind.Enemy)];
        Assert.NotEmpty(heard);
        foreach (AiEvent e in heard)
        {
            IntentCommand said = r.Intents.Last(i => i.Bot.Slot == e.Other.Slot && i.Say.Kind == CalloutKind.Enemy && i.FromTick.Value <= e.Tick.Value);
            float ms = (e.Tick.Value - said.FromTick.Value) * 0.015f * 1000f;
            Assert.True(ms is >= 400f and <= 1200f, $"heard {ms:F0} ms after it was said (tick {said.FromTick.Value} to {e.Tick.Value})");
        }
    }

    // ------------------------------------------------------------------ Trade

    /// <summary>A decide every six ticks: within one of the kill feed is within this many ticks of it.</summary>
    private const int OneDecide = 12;

    [PerTreeNavFact(Map)]
    public void ATeammateKilledInSightIsTradedWithinOneDecide()
    {
        ScenarioRecording r = Recording.Value;
        var roles = Replayed.Value.Roles;
        int foe = Slot("foe"), trials = 0, traded = 0;
        var lines = new List<string>();
        foreach (JsonElement t in r.Summary.GetProperty("trades").EnumerateArray())
        {
            (int from, int to) = Scene(t.GetProperty("scene").GetString()!);
            int watcher = t.GetProperty("watcher").GetInt32(), victim = t.GetProperty("victim").GetInt32();
            AiEvent kill = r.Events.First(e => e.Kind == AiEventKind.KillFeed && e.Bot.Slot == watcher && e.Other.Slot == victim && e.Tick.Value >= from && e.Tick.Value <= to);
            Assert.Equal(foe, kill.Killer.Slot);
            trials++;
            bool yes = roles[watcher].Any(kv => kv.Key >= kill.Tick.Value && kv.Key <= kill.Tick.Value + OneDecide && kv.Value == "trade");
            traded += yes ? 1 : 0;
            lines.Add($"kill at {kill.Tick.Value}: {(yes ? "traded" : "not")}");
        }

        Assert.Equal(10, trials);
        // tradeWeight 0.9 at Teamwork 10: each chance is drawn, so some trials are declined (under 6 of 10 is 1 in 600).
        Assert.True(traded >= 6, $"{traded} of {trials} traded: {string.Join("; ", lines)}");
    }

    /// <summary>The control: in each trial's seconds before the kill, with the same enemy in sight, nobody trades.</summary>
    [PerTreeNavFact(Map)]
    public void BeforeTheKillNobodyTrades()
    {
        var roles = Replayed.Value.Roles;
        foreach (JsonElement t in Recording.Value.Summary.GetProperty("trades").EnumerateArray())
        {
            string trial = t.GetProperty("scene").GetString()!;
            (int from, int to) = Scene(trial.Replace("trade ", "trade sight ", StringComparison.Ordinal));
            int watcher = t.GetProperty("watcher").GetInt32();
            Assert.DoesNotContain(roles[watcher], kv => kv.Key >= from && kv.Key <= to && kv.Value == "trade");
        }
    }
}
