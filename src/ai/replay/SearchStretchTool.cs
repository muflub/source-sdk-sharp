using System.Globalization;
using System.Text;

namespace SourceSharp.Ai.Replay;

/// <summary>One stretch of a bot's decides under a leaf: its ticks, where the bot was, and what it heard meanwhile.</summary>
public sealed record SearchStretch(ActorId Bot, int Team, int From, int To, AiVector Start, AiVector Least, AiVector Most, IReadOnlyList<AiEvent> Heard,
                                   IReadOnlyList<AiActorFrame>? Track = null, IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>>? Everyone = null)
{
    public double Seconds => (To - From) * AiMetrics.TickSeconds;
}

/// <summary>
/// <c>ai-replay searches</c> (lane Gmove, 2026-11-16: a bot looping at a spawn door on "heard
/// something"): every stretch of a bot's intents under the search leaf, from its first intent to the
/// first under another (a death ends one: its Killed event), with where the bot stood (its frames'
/// least and most corners) and the sounds and callouts it was told of meanwhile. Pure.
/// </summary>
public static class SearchStretchTool
{
    /// <summary>The leaf's name in TF2's tree and HL2MP's.</summary>
    public const string Leaf = "search";

    /// <summary>The track of a stretch: the bot's place every second.</summary>
    public const int TrackTicks = 66;

    /// <summary>A stretch this long is a loop (the brief's 15 s).</summary>
    public const double LongSeconds = 15.0;

    /// <summary>Every bot's search stretches in <paramref name="recording"/>, longest first.</summary>
    public static List<SearchStretch> Read(Stream recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        AiRecordFormat.ReadFileHeader(recording);
        var intents = new Dictionary<ActorId, List<IntentCommand>>();
        var frames = new Dictionary<ActorId, SortedList<int, AiActorFrame>>();
        var heard = new Dictionary<ActorId, List<AiEvent>>();
        var deaths = new Dictionary<ActorId, List<int>>();
        var teams = new Dictionary<ActorId, int>();
        AiDecisionTree? tree = null;
        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.DecisionTree:
                    tree = AiRecordFormat.ReadDecisionTree(r);
                    break;
                case AiRecordType.Intent:
                    IntentCommand i = AiRecordFormat.ReadIntent(r);
                    Add(intents, i.Bot, i);
                    break;
                case AiRecordType.Frame:
                    AiActorFrame f = AiRecordFormat.ReadFrame(r);
                    if (!frames.TryGetValue(f.Actor, out SortedList<int, AiActorFrame>? at))
                        frames[f.Actor] = at = new SortedList<int, AiActorFrame>();
                    at[f.Tick.Value] = f;
                    break;
                case AiRecordType.Event:
                    AiEvent e = AiRecordFormat.ReadEvent(r);
                    if (e.Kind is AiEventKind.SoundHeard or AiEventKind.Callout)
                        Add(heard, e.Bot, e);
                    else if (e.Kind == AiEventKind.Killed)
                        Add(deaths, e.Bot, e.Tick.Value);
                    else if (e.Kind == AiEventKind.ActorTeam && e.Other == e.Bot)
                        teams[e.Bot] = e.Code;
                    break;
            }
        }

        return tree is null ? [] : From(tree, intents, frames, heard, deaths, teams);
    }

    /// <summary>
    /// The stretches of <paramref name="intents"/> (each bot's in tick order) under <see cref="Leaf"/> of
    /// <paramref name="tree"/>, ended by another leaf or a death in <paramref name="deaths"/>, longest first.
    /// </summary>
    public static List<SearchStretch> From(AiDecisionTree tree, IReadOnlyDictionary<ActorId, List<IntentCommand>> intents,
                                           IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames, IReadOnlyDictionary<ActorId, List<AiEvent>> heard,
                                           IReadOnlyDictionary<ActorId, List<int>> deaths, IReadOnlyDictionary<ActorId, int> teams)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(heard);
        ArgumentNullException.ThrowIfNull(deaths);
        ArgumentNullException.ThrowIfNull(teams);
        var stretches = new List<SearchStretch>();
        foreach ((ActorId bot, List<IntentCommand> list) in intents)
        {
            List<int> died = deaths.GetValueOrDefault(bot) ?? [];
            int start = -1;
            for (int k = 0; k <= list.Count; k++)
            {
                bool searching = k < list.Count && tree.Name(list[k].Reason.NodeId) == Leaf;
                int t = k < list.Count ? list[k].FromTick.Value : int.MaxValue;
                bool deathBetween = start >= 0 && died.Any(d => d > list[start].FromTick.Value && d <= t);
                if (start >= 0 && (!searching || deathBetween))
                {
                    int end = k < list.Count ? t : list[k - 1].ExpiresTick.Value;
                    if (deathBetween)
                        end = Math.Min(end, died.First(d => d > list[start].FromTick.Value));
                    stretches.Add(Stretch(bot, teams.GetValueOrDefault(bot), list[start].FromTick.Value, end, frames.GetValueOrDefault(bot), heard.GetValueOrDefault(bot))
                                   with { Everyone = frames });
                    start = -1;
                }

                if (searching && start < 0)
                    start = k;
            }
        }

        stretches.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        return stretches;
    }

    private static void Add<T>(Dictionary<ActorId, List<T>> into, ActorId who, T item)
    {
        if (!into.TryGetValue(who, out List<T>? list))
            into[who] = list = new List<T>();
        list.Add(item);
    }

    private static SearchStretch Stretch(ActorId bot, int team, int from, int to, SortedList<int, AiActorFrame>? frames, List<AiEvent>? heard)
    {
        AiVector start = default, least = new(float.MaxValue, float.MaxValue, float.MaxValue), most = new(float.MinValue, float.MinValue, float.MinValue);
        bool any = false;
        var track = new List<AiActorFrame>();
        if (frames is not null)
        {
            foreach ((int t, AiActorFrame f) in frames)
            {
                if (t < from || t > to)
                    continue;
                if (!any)
                    start = f.Origin;
                any = true;
                if (track.Count == 0 || t - track[^1].Tick.Value >= TrackTicks)
                    track.Add(f);
                least = new AiVector(MathF.Min(least.X, f.Origin.X), MathF.Min(least.Y, f.Origin.Y), MathF.Min(least.Z, f.Origin.Z));
                most = new AiVector(MathF.Max(most.X, f.Origin.X), MathF.Max(most.Y, f.Origin.Y), MathF.Max(most.Z, f.Origin.Z));
            }
        }

        if (!any)
            (least, most) = (default, default);
        List<AiEvent> during = heard?.Where(e => e.Tick.Value >= from - 66 && e.Tick.Value <= to).ToList() ?? [];
        return new SearchStretch(bot, team, from, to, start, least, most, during, track);
    }

    /// <summary>The longest stretch's track: the bot's place a second, and each sound or callout in tick order with its distance from the bot then.</summary>
    public static string Track(SearchStretch s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var w = new StringBuilder();
        var rows = new List<(int Tick, string Text)>();
        IReadOnlyList<AiActorFrame> track = s.Track ?? [];
        foreach (AiActorFrame f in track)
            rows.Add((f.Tick.Value, string.Create(CultureInfo.InvariantCulture, $"at ({f.Origin.X:F0} {f.Origin.Y:F0} {f.Origin.Z:F0})")));
        foreach (AiEvent e in s.Heard)
        {
            AiActorFrame? near = track.LastOrDefault(f => f.Tick.Value <= e.Tick.Value);
            string what = e.Kind == AiEventKind.SoundHeard ? ((SoundKind)e.Code).ToString() : $"callout{e.Code & 0xFF}";
            float d = near is { } n ? n.Origin.DistanceTo(e.Position) : float.NaN;
            string nearest = "";
            if (s.Everyone is { } all && e.Kind == AiEventKind.SoundHeard)
            {
                // Who was nearest the heard place then (it carries the hearing's error): for a sound with no player source, who may have made it.
                (ActorId who, float dist) = (ActorId.None, float.MaxValue);
                foreach ((ActorId a, SortedList<int, AiActorFrame> fs) in all)
                {
                    int i = fs.IndexOfKey(e.Tick.Value);
                    if (i >= 0 && fs.Values[i].Origin.DistanceTo(e.Position) < dist)
                        (who, dist) = (a, fs.Values[i].Origin.DistanceTo(e.Position));
                }

                nearest = who.IsValid ? string.Create(CultureInfo.InvariantCulture, $"; nearest player slot {who.Slot} at {dist:F0} u") : "";
            }

            rows.Add((e.Tick.Value, string.Create(CultureInfo.InvariantCulture,
                $"heard {what} from slot {e.Other.Slot} at ({e.Position.X:F0} {e.Position.Y:F0} {e.Position.Z:F0}) loudness {e.Amount:F2}, {d:F0} u from the bot{nearest}")));
        }

        foreach ((int tick, string text) in rows.OrderBy(r => r.Tick))
            w.Append(CultureInfo.InvariantCulture, $"searches: track slot {s.Bot.Slot} tick {tick} {text}\n");
        return w.ToString();
    }

    /// <summary>The report: the longest stretch, the count of loops, and the <paramref name="detail"/> longest with what was heard in each by kind, source and place.</summary>
    public static string Table(IReadOnlyList<SearchStretch> stretches, int detail = 3)
    {
        ArgumentNullException.ThrowIfNull(stretches);
        var w = new StringBuilder();
        double longest = stretches.Count > 0 ? stretches.Max(s => s.Seconds) : 0;
        w.Append(CultureInfo.InvariantCulture,
                 $"searches: {stretches.Count} stretches, longest {longest:F1} s, over {LongSeconds:F0} s {stretches.Count(s => s.Seconds > LongSeconds)}, over 5 s {stretches.Count(s => s.Seconds > 5)}\n");
        foreach (SearchStretch s in stretches.Take(detail))
        {
            w.Append(CultureInfo.InvariantCulture,
                     $"searches: slot {s.Bot.Slot} team {s.Team} ticks {s.From}-{s.To} ({s.Seconds:F1} s) from ({s.Start.X:F0} {s.Start.Y:F0} {s.Start.Z:F0}) within ({s.Least.X:F0} {s.Least.Y:F0})-({s.Most.X:F0} {s.Most.Y:F0})\n");
            foreach (var g in s.Heard.GroupBy(e => (What: e.Kind == AiEventKind.SoundHeard ? ((SoundKind)e.Code).ToString() : $"callout{e.Code & 0xFF}",
                                                    e.Other.Slot, X: (int)MathF.Round(e.Position.X / 32f) * 32, Y: (int)MathF.Round(e.Position.Y / 32f) * 32))
                                     .OrderByDescending(g => g.Count()).Take(6))
            {
                AiEvent last = g.Last();
                w.Append(CultureInfo.InvariantCulture,
                         $"searches:   {g.Key.What} from slot {g.Key.Slot} at ({g.Key.X} {g.Key.Y} {last.Position.Z:F0}) x{g.Count()} first {g.First().Tick.Value} last {last.Tick.Value} loudness {last.Amount:F2}\n");
            }
        }

        return w.ToString();
    }
}
