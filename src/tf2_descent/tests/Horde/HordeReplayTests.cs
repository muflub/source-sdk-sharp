using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Nav;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// plan_descent_logic 3.10's determinism gate for the horde's session (HO4): a
/// session recorded through the core's <see cref="AiRecorder"/> while robots
/// see, lose, hear and are hit by a hero walking the level replays through a
/// fresh session to the same intent stream, byte for byte, inline and on a
/// pool of 1, 2 and 4 worker threads (as the core's ThreadDeterminismTests).
/// </summary>
public sealed class HordeReplayTests
{
    private const int Ticks = 900;

    private static (byte[] Recording, HordeLevel Level, int Intents) Record()
    {
        var level = Row(4, markers: 0);
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, HordeBrains.Header(level));
        recorder.World(HordeBrains.RecordedWorld(0.015f));
        var log = new EventLog();
        var rig = new BrainRig(level, sink: new Both(recorder, log));
        var robots = new List<TF2.Descent.Ai.Roster.RobotId>();
        for (int r = 0; r < 6; r++)
        {
            var id = rig.Robot(Centre(r % 4) + At(-60 + 25 * r, 40 * (r % 3) - 40), seed: (ulong)(31 + r));
            robots.Add(id);
            rig.Lend(id, yaw: 60f * r);
        }

        for (int t = 0; t < Ticks; t++)
        {
            // The hero walks room 0 to room 3 and back; walls block every line while it is in room 2.
            float x = 256f + 1536f * MathF.Abs(MathF.Sin(t * 0.004f));
            rig.Hero(1, At(x, 300));
            rig.Lines.Clear = (from, to) => rig.Level.RoomAt(to with { Z = 0 }) != 2;
            if (t % 97 == 40)
                rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, At(x, 300), 1500f, 1, rig.Level.RoomAt(At(x, 300))));
            if (t % 151 == 75)
            {
                var hit = robots[t % robots.Count];
                if (rig.Roster.Body(hit.Index) is var b && b != 0)
                    rig.Brains.Hit(rig.Now + 1, hit, 1, 12f, false, rig.Poses.Position(b));
            }

            rig.Tick();
        }

        rig.Brains.Session.Drain(wait: true);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SightGained);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SightLost);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SoundHeard);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.Damaged);
        Assert.Contains(log.Intents, i => i.Move.Kind == MoveKind.Follow);
        return (bytes.ToArray(), level, log.Intents.Count);
    }

    private static AiReplayResult Replay(byte[] recording, HordeLevel level, IAiTransport transport, bool families = false, IHordePaths? paths = null) =>
        AiReplayer.ReplayWithRecordedWorld(new MemoryStream(recording), transport, _ => (null, null),
                                           families ? HordeFamilies.Deciders(level, TheRules, paths).Make : HordeDeciders.For(level, paths).Make);

    /// <summary>The tick of the walk on the depth 1 seed 1 level at which the server's graph becomes ready.</summary>
    private const int PathsReadyAt = 300;

    /// <summary>
    /// The families on the depth 1 seed 1 level (the committed fixture and its derived graph), the
    /// graph made ready part way through, as the server's derivation finishes during a level: six
    /// robots in the drift south of the arrival and its alcove, a hero walking from the arrival
    /// down the drift and back, a gunshot and hits now and then. Before the graph is ready the
    /// robots go by the room route; after, by its paths. Every body's arrival is told to its brain
    /// (Arrived), and a hit robot's pack-mates are told (AllyDamaged).
    /// </summary>
    private static (byte[] Recording, HordeLevel Level, int Intents, HordePaths Paths) RecordWithPaths()
    {
        var level = HordeNavDataTests.FixtureLevel;
        var paths = new HordePaths();
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, HordeBrains.Header(level));
        recorder.World(HordeBrains.RecordedWorld(0.015f));
        var log = new EventLog();
        var rig = new BrainRig(level, sink: new Both(recorder, log), register: d => HordeFamilies.Register(d, FamilyNumbers.For(TheRules)), paths: paths)
        {
            TellArrivals = true,
        };
        var robots = new List<TF2.Descent.Ai.Roster.RobotId>();
        string[] templates = ["soldier", "spy", "scout", "soldier", "medic", "spy"];
        for (int r = 0; r < 6; r++)
        {
            var at = r < 3 ? At(2380 + 40 * r, 3560 + 50 * r, 16) : At(2560, 3200 + 150 * r, 16);
            var id = rig.Robot(at, templates[r], seed: (ulong)(61 + r), joinLastPack: r == 4);
            robots.Add(id);
            rig.Lend(id, yaw: 90f);
        }

        long searchesBefore = -1;
        for (int t = 0; t < Ticks; t++)
        {
            if (t == PathsReadyAt)
            {
                searchesBefore = paths.Searches;
                paths.Ready(HordeNavDataTests.Nav, level);
            }

            float y = 4900f - 1500f * MathF.Abs(MathF.Sin(t * 0.004f));
            rig.Hero(1, At(2560, y, 16));
            if (t % 97 == 40)
                rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, At(2560, y, 16), 1500f, 1, level.RoomAt(At(2560, y, 16))));
            if (t % 151 == 75)
            {
                var hit = robots[t % robots.Count];
                if (rig.Roster.Body(hit.Index) is var b && b != 0)
                    rig.Brains.Hit(rig.Now + 1, hit, 1, 12f, false, rig.Poses.Position(b));
            }

            rig.Tick();
        }

        rig.Brains.Session.Drain(wait: true);
        Assert.Equal(0, searchesBefore);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.Arrived);
        Assert.Contains(log.Events, e => e.Kind is AiEventKind.AllyDamaged or AiEventKind.AllyDied);
        Assert.True(paths.Found > 0, $"the control: no path was found after the graph was ready ({paths.Searches} searches)");
        Assert.Contains(log.Intents, i => i.FromTick.Value < rig.Now - Ticks + PathsReadyAt && i.Move.Kind == MoveKind.Follow);
        return (bytes.ToArray(), level, log.Intents.Count, paths);
    }

    [Fact]
    public void A_recording_with_paths_coming_ready_replays_to_the_same_intents_inline_and_at_one_two_and_four_threads()
    {
        var (recording, level, intents, recorded) = RecordWithPaths();
        using var paths = new HordePaths();
        paths.Ready(HordeNavDataTests.Nav, level);

        var inline = Replay(recording, level, new InlineTransport(), families: true, paths: paths);
        Assert.True(inline.RecordedIntentCount == intents && intents >= 5 * (Ticks / DecideSchedule.Period),
                    $"the control: {inline.RecordedIntentCount} intents recorded, {intents} made");
        Assert.True(paths.Found > 0, "the control: the replay planned no path");
        Assert.True(inline.Identical, "inline: the intent stream differs from the recording's");
        recorded.Dispose();

        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            var pooled = Replay(recording, level, new PooledTransport(pool), families: true, paths: paths);
            Assert.Equal(inline.ReplayedIntentCount, pooled.ReplayedIntentCount);
            Assert.True(pooled.Identical, $"{threads} thread(s): the intent stream differs from the recording's");
        }
    }

    /// <summary>
    /// Homes in a recording (format 8's BotAdded home): robots rolled at their markers, lent away
    /// from them, walk home with nothing to hunt; the replay's brains get the recorded homes, so
    /// they walk to the same places.
    /// </summary>
    [Fact]
    public void A_recording_of_robots_walking_home_to_their_markers_replays_to_the_same_intents()
    {
        var level = Row(4, markers: 2);
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, HordeBrains.Header(level));
        recorder.World(HordeBrains.RecordedWorld(0.015f));
        var log = new EventLog();
        var rig = new BrainRig(level, sink: new Both(recorder, log));
        for (int r = 0; r < Math.Min(4, rig.Roster.Count); r++)
        {
            var id = rig.Roster.Id(r);
            rig.Roster.WakeRoom(rig.Roster.HomeRoom(r));
            int body = rig.Lend(id);
            rig.Poses.Set(body, rig.Poses.Position(body) + At(0, 250), 0f);
        }

        rig.Tick(600);
        rig.Brains.Session.Drain(wait: true);
        Assert.Contains(log.Intents, i => i.Move.Kind == MoveKind.Follow);

        var replay = Replay(bytes.ToArray(), level, new InlineTransport());

        Assert.True(replay.RecordedIntentCount > 0 && replay.Identical, "the replay of robots walking home differs from the recording");
    }

    [Fact]
    public void A_recording_with_paths_replayed_over_another_graph_differs()
    {
        var (recording, level, _, recorded) = RecordWithPaths();
        recorded.Dispose();
        var blocks = new byte[HordeNavDataTests.Nav.Graph.CellCount];
        blocks[0] = SourceSharp.Ai.NavCellBlocks.Blocked;
        using var other = new HordePaths();
        other.Ready(HordeNavDataTests.Nav.WithBlocks(blocks), level);
        Assert.NotEqual(HordePaths.EpochOf(HordeNavDataTests.Nav.Identity), other.Epoch);

        var replay = Replay(recording, level, new InlineTransport(), families: true, paths: other);

        Assert.False(replay.Identical, "a replay over another graph made the same intents: the graph's epoch is not read");
    }

    /// <summary>
    /// A fight (HO6, HO7): a rusher, a rocket soldier and its medic, and a scout (a family still
    /// hunting), over the families as the game registers them, while a hero runs across the
    /// rooms and the soldier is hurt for its medic; every attack and heal they ask is kept.
    /// </summary>
    private static (byte[] Recording, HordeLevel Level, int Intents) RecordFight()
    {
        var level = Row(4, markers: 0);
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, HordeBrains.Header(level));
        recorder.World(HordeBrains.RecordedWorld(0.015f));
        var log = new EventLog();
        var rig = new BrainRig(level, sink: new Both(recorder, log), register: d => HordeFamilies.Register(d, FamilyNumbers.For(TheRules)));
        var rusher = rig.Robot(Centre(1) + At(-100, -60), "spy", seed: 51);
        var soldier = rig.Robot(Centre(1) + At(-150, 60), "soldier", seed: 52);
        var medic = rig.Robot(Centre(1) + At(-200, 0), "medic", seed: 53, joinLastPack: true);
        var scout = rig.Robot(Centre(2), "scout", seed: 54);
        var lent = new List<TF2.Descent.Ai.Roster.RobotId> { rusher, soldier, medic, scout };
        // HO12: every other family fights in the same recording.
        string[] rest = ["pyro", "demoman", "heavy", "engineer", "sniper", "sentry_buster", "giant_heavy"];
        for (int k = 0; k < rest.Length; k++)
            lent.Add(rig.Robot(Centre(k % 2 == 0 ? 2 : 3) + At(-120 + 40 * k, 150 - 50 * (k % 3)), rest[k], seed: (ulong)(60 + k)));
        foreach (var id in lent)
            rig.Lend(id, yaw: lent.IndexOf(id) < 4 ? 0f : 180f);   // the new ones east of the run face it
        rig.Roster.Hurt(soldier.Index, rig.Roster.Stats(soldier.Index).Life * 0.6);

        for (int t = 0; t < Ticks; t++)
        {
            float x = 300f + 1300f * MathF.Abs(MathF.Sin(t * 0.005f));
            rig.Hero(1, At(x, 250 + 60 * MathF.Sin(t * 0.03f)));
            rig.Aim.Look = b => new AiAngles(-5f + (b * 7 + t) % 10, rig.Poses.Yaw(b), 0f);
            rig.Tick();
        }

        rig.Brains.Session.Drain(wait: true);
        Assert.Contains(log.Intents, i => i.Action.Kind == ActionKind.Fire);
        Assert.Contains(log.Intents, i => i.Action.Kind == ActionKind.Ability && i.Action.WeaponSlot == HordeAbilities.Heal);
        Assert.Contains(log.Intents, i => i.Move.Kind == MoveKind.Follow);
        // HO12: the other families fought in it too (the stimulus for the replay fact over them).
        var nodes = log.Intents.Select(i => (FamilyNode)i.Reason.NodeId).ToHashSet();
        string seen = string.Join(", ", nodes.Order());
        Assert.True(nodes.Contains(FamilyNode.Suppress), $"the heavies held and fired: {seen}");
        Assert.True(nodes.Overlaps([FamilyNode.Stalk, FamilyNode.Flank, FamilyNode.Stab]), $"the spy fought: {seen}");
        Assert.True(nodes.Overlaps([FamilyNode.Aim, FamilyNode.Relocate]), $"the sniper aimed or ran: {seen}");
        Assert.True(nodes.Overlaps([FamilyNode.Burn, FamilyNode.Circle, FamilyNode.Lob]), $"the pyro, the scout or the demoman fought: {seen}");
        Assert.True(nodes.Overlaps([FamilyNode.Run, FamilyNode.Detonate]), $"the buster ran: {seen}");
        return (bytes.ToArray(), level, log.Intents.Count);
    }

    [Fact]
    public void A_recorded_fight_replays_to_the_same_intents_inline_and_at_one_two_and_four_threads()
    {
        var (recording, level, intents) = RecordFight();

        var inline = Replay(recording, level, new InlineTransport(), families: true);
        Assert.True(inline.RecordedIntentCount == intents && intents >= 4 * (Ticks / DecideSchedule.Period) - 8,
                    $"the control: {inline.RecordedIntentCount} intents recorded, {intents} made");
        Assert.True(inline.Identical, "inline: the fight's intent stream differs from the recording's");

        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            var pooled = Replay(recording, level, new PooledTransport(pool), families: true);
            Assert.Equal(inline.ReplayedIntentCount, pooled.ReplayedIntentCount);
            Assert.True(pooled.Identical, $"{threads} thread(s): the fight's intent stream differs from the recording's");
        }
    }

    /// <summary>
    /// Record format 8 (2026-11-11): the horde's session runs with its team layer off, its header
    /// says so, and the replayer's own path reads it and runs its session the same way.
    /// </summary>
    [Fact]
    public void A_horde_recording_says_its_team_layer_was_off_and_replays_so()
    {
        var (recording, level, _) = Record();

        var replay = Replay(recording, level, new InlineTransport());

        Assert.Equal(AiRecordingHeader.TeamLayerOff, replay.Header.Flags);
        Assert.False(replay.Header.TeamCallouts);
        Assert.True(replay.Identical, "the replay with the team layer off differs from the recording");
    }

    [Fact]
    public void A_recorded_horde_session_replays_to_the_same_intents_inline_and_at_one_two_and_four_threads()
    {
        var (recording, level, intents) = Record();

        var inline = Replay(recording, level, new InlineTransport());
        Assert.True(inline.RecordedIntentCount == intents && intents >= 6 * (Ticks / DecideSchedule.Period) - 12,
                    $"the control: {inline.RecordedIntentCount} intents recorded, {intents} made");
        Assert.True(inline.Identical, "inline: the intent stream differs from the recording's");

        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            var pooled = Replay(recording, level, new PooledTransport(pool));
            Assert.Equal(inline.ReplayedIntentCount, pooled.ReplayedIntentCount);
            Assert.True(pooled.Identical, $"{threads} thread(s): the intent stream differs from the recording's");
        }
    }

    /// <summary>Hands every record to two sinks.</summary>
    private sealed class Both(IAiRecordSink a, IAiRecordSink b) : IAiRecordSink
    {
        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels)
        {
            a.BotAdded(bot, seed, skillLevels);
            b.BotAdded(bot, seed, skillLevels);
        }

        /// <summary>A bot with its home (format 8): without this the interface's default drops the home on its way to the recorder.</summary>
        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels, AiVector? home)
        {
            a.BotAdded(bot, seed, skillLevels, home);
            b.BotAdded(bot, seed, skillLevels, home);
        }

        public void BotRemoved(ActorId bot)
        {
            a.BotRemoved(bot);
            b.BotRemoved(bot);
        }

        public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches)
        {
            a.DecisionTree(names, branches);
            b.DecisionTree(names, branches);
        }

        public void Event(in AiEvent e)
        {
            a.Event(e);
            b.Event(e);
        }

        public void Snapshot(AiSnapshot snapshot)
        {
            a.Snapshot(snapshot);
            b.Snapshot(snapshot);
        }

        public void Intent(in IntentCommand intent)
        {
            a.Intent(intent);
            b.Intent(intent);
        }

        public void UserCmd(in AiUserCmd cmd) => a.UserCmd(cmd);

        public void Frame(in AiActorFrame frame) => a.Frame(frame);

        public void Observed(in AiEvent e) => a.Observed(e);
    }
}
