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
{
    private int _framesAtReset;

    public void Execute([Complete("reset")] string action = "")
    {
        if (action.Equals("reset", StringComparison.OrdinalIgnoreCase))
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
                                               IEngineEnvironment environment, IEngineConsole console)
{
    public void Execute(int reps = 200)
    {
        if (reps <= 0)
            reps = 200;

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
                                              IEngineConsole console)
{
    public void Execute(string file, float time = 1000f)
    {
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

        File.WriteAllText(file, text.ToString());
        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp] bonedump: {skeletonsWritten} skeleton(s) at time {time} to {file}\n"));
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

/// <summary><c>cs_cl_ragdoll_roots</c>: where each body's first element is, for the gate's check that a body was thrown.</summary>
[ConCommand("cs_cl_ragdoll_roots", Help = "SourceSharp: each client ragdoll's first element, as the physics has it")]
public sealed class ClientRagdollRootsCommand(Ragdolls.IClientRagdolls ragdolls, IClientStandalones standalones, IEngineConsole console)
{
    public void Execute()
    {
        foreach (Standalone.ClientStandaloneEntity entity in standalones.Live())
        {
            if (ragdolls.TryRoot(entity.Handle, out Vector at))
                console.Msg(string.Create(CultureInfo.InvariantCulture, $"SSRAGDOLLROOT\t{entity.Handle.Value}\t{at.X:F1}\t{at.Y:F1}\t{at.Z:F1}\n"));
        }

        console.Msg("SSRAGDOLLROOT\tend\n");
    }
}

/// <summary><c>cs_cl_transitions [filter]</c>: each skeleton's transition queue, for the live blend check.</summary>
[ConCommand("cs_cl_transitions",
    Help = "SourceSharp: each client skeleton's sequence transition queue as its last build left it -- `[studio-name filter]`")]
public sealed class ClientSkeletonTransitionsCommand(ClientSkeletons skeletons, IEngineConsole console)
{
    public void Execute(string filter = "")
    {
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
{
    public void Execute(bool? batching = null)
    {
        if (batching is { } wanted)
            skeletons.BatchEnabled = wanted;

        ClientBoneBatch batch = skeletons.Batch;
        console.Msg($"[SourceSharp] bone batch {(skeletons.BatchEnabled ? "on" : "off")}: table 0x{batch.Address:x} " +
                    $"handed {(batch.Handed ? 1 : 0)} adopted {(carry.Adopted ? 1 : 0)} " +
                    $"allocated {batch.TablesAllocated} freed {batch.TablesFreed} (0x{batch.FreedTable:x}); " +
                    $"ragdoll flips {skeletons.RagdollFlips} with an entry {skeletons.RagdollFlipEntries} " +
                    $"stale answers {skeletons.RagdollFlipStaleAnswers} flip-frame solver asks {skeletons.RagdollFlipFrameAsks}\n");
    }
}

/// <summary>
/// <c>cs_cl_hitbands &lt;entindex&gt; [from-yaw]</c>: the twin of the server's <c>cs_sv_hitbands</c> over the
/// client's own skeleton, the one it draws: level lines, one per unit of height over the entity's feet, from 64 u out
/// at the given world yaw (0 by default) through its origin, each against its hitboxes alone, and each run of heights
/// in one hit group printed as a band; then where the drawn head bone stands about the feet.
/// </summary>
[ConCommand("cs_cl_hitbands",
    Help = "SourceSharp: the heights over an entity's feet where a level line stops on the client's drawn hitboxes, by hit group -- `<entindex> [from-yaw]`")]
public sealed class ClientHitBandsCommand(ClientSkeletons skeletons, IClientStandalones standalones, IEngineEntities entities,
                                          IEngineConsole console)
{
    /// <summary><c>MASK_SHOT</c>.</summary>
    private const int MaskShot = 0x46004003;

    /// <summary>The head bone's name on TF's rig, then on HL2's.</summary>
    private readonly string[] _headBones = ["bip_head", "ValveBiped.Bip01_Head1"];

    /// <summary>The torso dials on TF's rig, then on HL2DM's.</summary>
    private readonly string[] _torsoDials = ["body_pitch", "body_yaw", "aim_pitch", "aim_yaw"];

    public void Execute(int entIndex, float fromYaw = 0f)
    {
        if (standalones.GetByIndex(entIndex) is not { } standalone)
        {
            console.Msg($"[SourceSharp] cs_cl_hitbands: no client entity at {entIndex}\n");
            return;
        }

        EntityHandle entity = standalone.Handle;
        Vector feet = entities.GetOrigin(entity);
        float radians = fromYaw * MathF.PI / 180f;
        var toward = new System.Numerics.Vector3(MathF.Cos(radians), MathF.Sin(radians), 0f);
        var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp] cl hitbands #{entIndex} {skeletons.StudioName(entity)} from yaw {fromYaw:0.#}:"));
        string? band = null;
        int bandFrom = 0;
        for (int h = 0; h <= 96; h++)
        {
            var at = new System.Numerics.Vector3(feet.X, feet.Y, feet.Z + h);
            var tr = new SourceSharp.Shared.Bones.BoneTrace();
            bool tested = skeletons.TestHitboxes(entity, at + toward * 64f, at - toward * 64f, MaskShot, tr);
            string name = !tested ? "untested" : tr.Fraction < 1f ? ((HitGroup)tr.HitGroup).ToString() : "-";
            if (name != band)
            {
                if (band is not null)
                    line.Append(CultureInfo.InvariantCulture, $" | {band} {bandFrom}..{h - 1}");
                band = name;
                bandFrom = h;
            }
        }

        line.Append(CultureInfo.InvariantCulture, $" | {band} {bandFrom}..96");
        // The head bone and the torso dials by the rig's own names: TF's
        // (bip_head, body_pitch / body_yaw), else HL2's (ValveBiped head,
        // aim_pitch / aim_yaw); a rig with neither says so.
        string? headName = _headBones.FirstOrDefault(name => skeletons.LookupBone(entity, name) >= 0);
        if (headName is not null && skeletons.GetBonePosition(entity, skeletons.LookupBone(entity, headName), out Vector bone))
            line.Append(CultureInfo.InvariantCulture, $" | {headName} at ({bone.X - feet.X:0.0} {bone.Y - feet.Y:0.0} {bone.Z - feet.Z:0.0})");
        else
            line.Append(" | no head bone");
        skeletons.SequenceState(entity, out int sequence, out float cycle, out _, out _, out _);
        line.Append(CultureInfo.InvariantCulture, $" | sequence {sequence} cycle {cycle:0.00} yaw {entities.GetAngles(entity).Y:0.0}");
        bool anyDial = false;
        foreach (string dial in _torsoDials)
        {
            int index = skeletons.LookupPoseParameter(entity, dial);
            if (index < 0)
                continue;
            anyDial = true;
            line.Append(CultureInfo.InvariantCulture, $" {dial} {skeletons.GetPoseParameter(entity, index):0.#}");
        }

        if (!anyDial)
            line.Append(" no torso dials");

        console.Msg(line.Append('\n').ToString());
    }
}
#endif
