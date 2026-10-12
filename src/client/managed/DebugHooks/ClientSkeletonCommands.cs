#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

//=============================================================================
// THE CLIENT SKELETONS' INSTRUMENTS (plan physics_bones_native_zero step 1.3,
// the B3 cost gate, client half). Debug builds only, like every diagnostic
// command; the twins of the server's cs_sv_bonecost / cs_sv_bonecompare.
//
//   cs_cl_bonecost [reset]   what the managed skeletons have done since the
//                            last reset: every ask, the builds among them, the
//                            engine's SetupBones asks alone and their time,
//                            the hitbox traces, the frame's batches (the
//                            skeletons posed ahead, their time, the asks
//                            native answered from them); per client frame.
//   cs_cl_bonebench [reps]   every client skeleton rebuilt `reps` times at
//                            this frame's clock, timed, per model (sources
//                            first, so a merging follower reads a standing
//                            source cache, as it does in a frame), with the
//                            bytes each rebuild allocated.
//   cs_cl_bonebatch [0|1]    the frame's batch off or on (an A/B in one
//                            process: off, every SetupBones ask crosses), and
//                            its table (the address native holds, whether this
//                            build adopted it across cs_cl_reload, the tables
//                            allocated and freed) and the ragdoll flips (those
//                            that dropped an entry, the stale answers after
//                            one, the flip frame's asks answered the solver's).
//   cs_cl_transitions [filter]
//                            each skeleton's sequence transition queue
//                            (SequenceTransitioner) as its last build left
//                            it: the playing sequence and its start count,
//                            then every queued sequence with its weight,
//                            fade time and clock. Studio names containing
//                            the filter; only non-empty queues without one.
//
//   cs_cl_bonedump <file> [time]
//                            every skeleton's history forgotten, then each
//                            built at one clock and frame and written to
//                            <file>, one matrix a line with the cache key it
//                            was built under: two builds of the
//                            code compare skeleton for skeleton across a
//                            cs_cl_reload (the perf pass's parity check).
//
// cs_cl_bonecompare (native against managed, skeleton for skeleton) measured
// the baseline before the flip and left with the native skeleton and the
// StandaloneBoneMatrices slot it read (its numbers are in the commit that
// added it, and the flip's).
//=============================================================================

/// <summary><c>cs_cl_bonecost</c>: the managed client skeletons' running cost.</summary>
[ConCommand("cs_cl_bonecost",
    Help = "SourceSharp: the managed client skeletons' cost since the last `reset` -- asks, builds, the engine's SetupBones asks and their microseconds, per frame")]
public sealed class ClientSkeletonCostCommand(ClientSkeletons skeletons, IClientFrameStats stats, IEngineConsole console)
    : IConCommand
{
    private int _framesAtReset;

    public void Execute(string args)
    {
        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            skeletons.ResetCost();
            _framesAtReset = stats.FrameCount;
            console.Msg("[SourceSharp] client bone cost reset\n");
            return;
        }

        int frames = Math.Max(1, stats.FrameCount - _framesAtReset);
        double us = skeletons.AskTicks * 1e6 / Stopwatch.Frequency;
        double setupUs = skeletons.EngineSetupBonesTicks * 1e6 / Stopwatch.Frequency;

        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSCLBONECOST\tframes\t{frames}\tasks\t{skeletons.Asks}\tbuilds\t{skeletons.Builds}" +
            $"\tsetupbones\t{skeletons.EngineSetupBones}\tsetupbones_us\t{setupUs:F1}" +
            $"\tus_per_setupbones\t{setupUs / Math.Max(1, skeletons.EngineSetupBones):F3}" +
            $"\tsetupbones_us_per_frame\t{setupUs / frames:F3}\tsetupbones_per_frame\t{(double)skeletons.EngineSetupBones / frames:F2}" +
            $"\tus\t{us:F1}\tus_per_frame\t{us / frames:F3}\tbuilds_per_frame\t{(double)skeletons.Builds / frames:F3}" +
            $"\thitboxtraces\t{skeletons.HitboxTraces}\tstates\t{skeletons.States.Count}" +
            $"\tbatches\t{skeletons.Batches}\tbatch_entries_per_frame\t{(double)skeletons.BatchEntries / frames:F2}" +
            $"\tbatch_us_per_frame\t{skeletons.BatchTicks * 1e6 / Stopwatch.Frequency / frames:F3}" +
            $"\tbatch_answered_per_frame\t{(double)skeletons.BatchAnswered / frames:F2}" +
            $"\tmodels\t{skeletons.Models.Count}\timages\t{skeletons.Models.ImagesRead}\tbytes\t{skeletons.Models.BytesRead}\n"));

        foreach (var (model, cost) in skeletons.SetupBonesByModel.OrderByDescending(p => p.Value.Ticks).Take(12))
            console.Msg(string.Create(CultureInfo.InvariantCulture,
                $"SSCLBONECOSTMODEL\t{model}\t{skeletons.Models.StudioName(model)}\tasks_per_frame\t{(double)cost.Asks / frames:F2}" +
                $"\tkeychanges_per_frame\t{(double)cost.Builds / frames:F2}\tus_per_ask\t{cost.Ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, cost.Asks):F2}" +
                $"\tus_per_frame\t{cost.Ticks * 1e6 / Stopwatch.Frequency / frames:F2}\n"));
    }
}

/// <summary><c>cs_cl_bonebench</c>: every client skeleton rebuilt and timed, per model.</summary>
[ConCommand("cs_cl_bonebench",
    Help = "SourceSharp: every client skeleton rebuilt `reps` times (default 200) at this frame's clock, microseconds per rebuild per model")]
public sealed class ClientSkeletonBenchCommand(ClientSkeletons skeletons, IClientStandalones standalones,
                                               IEngineEnvironment environment, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        int reps = int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 200;
        float time = environment.Globals.IsAvailable ? environment.Globals.CurTime : 0f;
        int frame = environment.Globals.IsAvailable ? environment.Globals.FrameCount : 0;
        var rows = new SortedDictionary<string, (int Entities, int Bones, long Ticks, int Builds, long Bytes)>(StringComparer.Ordinal);

        foreach (EntityHandle entity in ClientSkeletonDumpCommand.SourcesFirst(skeletons, standalones))
        {
            // Warmed past the JIT's tiering before the clock starts.
            for (int i = 0; i < 50; i++)
                skeletons.Rebuild(entity, time, frame);

            long b0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            int bones = 0;
            for (int i = 0; i < reps; i++)
                bones = skeletons.Rebuild(entity, time, frame);
            long spent = Stopwatch.GetTimestamp() - t0;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;

            if (bones <= 0)
                continue;

            string key = skeletons.StudioName(entity) + (skeletons.MergeSourceOf(entity) >= 0 ? " (merged)" : "");
            rows.TryGetValue(key, out var row);
            rows[key] = (row.Entities + 1, bones, row.Ticks + spent, row.Builds + reps, row.Bytes + bytes);
        }

        foreach (var (model, row) in rows)
            console.Msg(string.Create(CultureInfo.InvariantCulture,
                $"SSCLBONEBENCH\t{model}\tentities\t{row.Entities}\tbones\t{row.Bones}" +
                $"\tus_per_build\t{row.Ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, row.Builds):F3}\treps\t{reps}" +
                $"\tbytes_per_build\t{row.Bytes / Math.Max(1, row.Builds)}\n"));
    }
}

/// <summary><c>cs_cl_bonedump</c>: every skeleton at one clock, written for a parity diff.</summary>
[ConCommand("cs_cl_bonedump",
    Help = "SourceSharp: every client skeleton, history forgotten, built at one clock and written to a file -- `<file> [time]`")]
public sealed class ClientSkeletonDumpCommand(ClientSkeletons skeletons, IClientStandalones standalones,
                                              IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 1)
        {
            console.Msg("usage: cs_cl_bonedump <file> [time]\n");
            return;
        }

        float time = words.Length > 1 && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float t) ? t : 1000f;
        const int Frame = 1000;
        var text = new StringBuilder();
        int skeletonsWritten = 0;

        skeletons.ForgetHistory();

        foreach (EntityHandle entity in SourcesFirst(skeletons, standalones))
        {
            if (skeletons.Rebuild(entity, time, Frame) <= 0)
                continue;

            ReadOnlySpan<float> m = System.Runtime.InteropServices.MemoryMarshal.Cast<SourceSharp.Shared.Bones.BoneMatrix3x4, float>(
                skeletons.CachedBones(entity));
            skeletonsWritten++;
            string key = skeletons.CacheKeyText(entity);

            for (int bone = 0; bone < m.Length / 12; bone++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{entity.Value}\t{skeletons.StudioName(entity)}\t{bone}\t{key}");
                for (int k = 0; k < 12; k++)
                    text.Append('\t').Append(m[bone * 12 + k].ToString("R", CultureInfo.InvariantCulture));
                text.Append('\n');
            }
        }

        // The builds ran at the dump's clock: a transition queued from one
        // would fade from that clock, not the game's. Forget them again.
        skeletons.ForgetHistory();

        File.WriteAllText(words[0], text.ToString());
        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp] bonedump: {skeletonsWritten} skeleton(s) at time {time} to {words[0]}\n"));
    }

    /// <summary>Every live client standalone and every skeleton held (a prop with no managed class
    /// has one), merge sources before their followers, by handle.</summary>
    public static List<EntityHandle> SourcesFirst(ClientSkeletons skeletons, IClientStandalones standalones)
    {
        var seen = new HashSet<EntityHandle>();
        foreach (Standalone.ClientStandaloneEntity e in standalones.Live())
            seen.Add(e.Handle);
        foreach ((EntityHandle handle, _) in skeletons.States.All())
            seen.Add(handle);
        var all = new List<EntityHandle>(seen);

        all.Sort((a, b) =>
        {
            int ma = skeletons.MergeSourceOf(a) >= 0 ? 1 : 0, mb = skeletons.MergeSourceOf(b) >= 0 ? 1 : 0;
            return ma != mb ? ma.CompareTo(mb) : a.Value.CompareTo(b.Value);
        });
        return all;
    }
}

/// <summary><c>cs_cl_transitions [filter]</c>: each skeleton's transition queue, for the live blend check.</summary>
[ConCommand("cs_cl_transitions",
    Help = "SourceSharp: each client skeleton's sequence transition queue as its last build left it -- `[studio-name filter]`")]
public sealed class ClientSkeletonTransitionsCommand(ClientSkeletons skeletons, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        string filter = args.Trim();
        var text = new StringBuilder();

        foreach ((EntityHandle handle, SourceSharp.Shared.Bones.BoneEntityState s) in skeletons.States.All())
        {
            SourceSharp.Shared.Bones.SequenceTransitioner t = s.Transitions;
            string studio = skeletons.StudioName(handle);

            if (filter.Length > 0 ? !studio.Contains(filter, StringComparison.OrdinalIgnoreCase) : t.Count <= 1)
                continue;

            text.Append(CultureInfo.InvariantCulture,
                $"SSTRANSITION	{handle.Value}	{studio}	seq	{s.Sequence}	starts	{s.SequenceStarts}	queue	{t.Count}");

            for (int i = 0; i < t.Count; i++)
            {
                SourceSharp.Shared.Bones.TransitionBlend b = t[i];
                text.Append(CultureInfo.InvariantCulture,
                    $"	[{b.Sequence} w={b.Weight:F4} fade={b.LayerFadeOuttime:F3} at={b.LayerAnimtime:F4} cycle={b.Cycle:F3}]");
            }

            text.Append('\n');
        }

        console.Msg(text.Length > 0 ? text.ToString() : "SSTRANSITION	none\n");
    }
}

/// <summary><c>cs_cl_bonebatch 0|1</c>: the frame's batch off or on, for an A/B within one process.</summary>
[ConCommand("cs_cl_bonebatch",
    Help = "SourceSharp: the frame's skeleton batch off (0: every SetupBones ask crosses) or on (1), and its state")]
public sealed class ClientSkeletonBatchCommand(ClientSkeletons skeletons, ClientBoneBatchCarry carry, IEngineConsole console)
    : IConCommand
{
    public void Execute(string args)
    {
        string word = args.Trim();

        if (word == "0" || word == "1")
            skeletons.BatchEnabled = word == "1";

        ClientBoneBatch batch = skeletons.Batch;
        console.Msg($"[SourceSharp] bone batch {(skeletons.BatchEnabled ? "on" : "off")}: table 0x{batch.Address:x} " +
                    $"handed {(batch.Handed ? 1 : 0)} adopted {(carry.Adopted ? 1 : 0)} " +
                    $"allocated {batch.TablesAllocated} freed {batch.TablesFreed} (0x{batch.FreedTable:x}); " +
                    $"ragdoll flips {skeletons.RagdollFlips} with an entry {skeletons.RagdollFlipEntries} " +
                    $"stale answers {skeletons.RagdollFlipStaleAnswers} flip-frame solver asks {skeletons.RagdollFlipFrameAsks}\n");
    }
}

#endif
