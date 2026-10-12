#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Server;

//=============================================================================
// THE SERVER SKELETONS' TWO INSTRUMENTS (plan physics_bones_native_zero step
// 1.3, the B3 cost gate). Debug builds only, like every diagnostic command.
//
//   cs_sv_bonecost [reset]   what the managed skeletons have done since the
//                            last reset: asks, builds, the time inside them,
//                            the engine's hitbox traces, per server tick.
//
//   cs_sv_bonecompare [n]    every standalone entity's skeleton built BOTH
//                            ways -- native through IEngineEntityRender, the
//                            way every caller asked before the flip, and
//                            managed through ServerSkeletons -- compared
//                            matrix for matrix, then each rebuilt n times
//                            with the time per rebuild. Native answers only
//                            while the route is empty (before the flip);
//                            after it, the native half reports "routed".
//=============================================================================

/// <summary><c>cs_sv_bonecost</c>: the managed skeletons' running cost.</summary>
[ConCommand("cs_sv_bonecost",
    Help = "SourceSharp: the managed server skeletons' cost since the last `reset` -- asks, builds, microseconds, hitbox traces, per tick")]
public sealed class SkeletonCostCommand(ServerSkeletons skeletons, IServerTicks ticks, IEngineConsole console) : IConCommand
{
    private int _ticksAtReset;

    public void Execute(string args)
    {
        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            skeletons.ResetCost();
            _ticksAtReset = ticks.Count;
            console.Msg("[SourceSharp] bone cost reset\n");
            return;
        }

        int frames = Math.Max(1, ticks.Count - _ticksAtReset);
        double us = skeletons.AskTicks * 1e6 / Stopwatch.Frequency;

        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSBONECOST\tticks\t{frames}\tasks\t{skeletons.Asks}\tbuilds\t{skeletons.Builds}" +
            $"\thitboxtraces\t{skeletons.HitboxTraces}\tus\t{us:F1}\tus_per_tick\t{us / frames:F3}" +
            $"\tbuilds_per_tick\t{(double)skeletons.Builds / frames:F3}\tstates\t{skeletons.States.Count}" +
            $"\tmodels\t{skeletons.Models.Count}\timages\t{skeletons.Models.ImagesRead}\tbytes\t{skeletons.Models.BytesRead}\n"));
    }
}

/// <summary>
/// <c>cs_sv_boneprobe</c>: the server skeleton fixture's two drivers, managed
/// since the skeleton is (plan physics_bones_native_zero 1.2). Every standalone
/// entity wearing a studio model plays sequence n from its start, or is worn
/// at scale f -- shell_standalone_hierarchy_debug.cpp's `seq` and `scale`
/// arms, which tools/live-gate's skinned-model step drives. The native
/// `dump` and `replay` arms were an oracle for bone_library.cpp, which the
/// server no longer links; they went with it.
/// </summary>
[ConCommand("cs_sv_boneprobe",
    Help = "SourceSharp: the server skeleton fixture -- `seq <n>`, `scale <f>`")]
public sealed class SkeletonProbeCommand(ServerSkeletons skeletons, IEngineEntityRender render,
                                         IEntityDirectory directory, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 2)
        {
            console.Msg("usage: cs_sv_boneprobe seq <n> | scale <f>\n");
            return;
        }

        int touched = 0;

        foreach (EntityHandle entity in SkeletonCompareCommand.StandaloneHandles(directory))
        {
            if (skeletons.StudioName(entity).Length == 0)
                continue;

            if (words[0].Equals("seq", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence))
            {
                skeletons.SetSequence(entity, sequence, 1.0f, restart: true);
                touched++;
            }
            else if (words[0].Equals("scale", StringComparison.OrdinalIgnoreCase)
                     && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            {
                render.SetModelScale(entity, scale);
                touched++;
            }
            else
            {
                console.Msg("usage: cs_sv_boneprobe seq <n> | scale <f>\n");
                return;
            }
        }

        console.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp] boneprobe {words[0]} {words[1]}: {touched} skeleton(s)\n"));
    }
}

/// <summary><c>cs_sv_bonecompare</c>: native against managed, skeleton for skeleton.</summary>
[ConCommand("cs_sv_bonecompare",
    Help = "SourceSharp: every standalone skeleton built natively and in managed code, compared, then rebuilt n times each way and timed")]
public sealed class SkeletonCompareCommand(ServerSkeletons skeletons, StandaloneSkeletonRoute route,
                                           IEngineEntityRender render, IEntityDirectory directory,
                                           IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        int reps = int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 20;
        bool nativeAnswers = route.Skeletons is null;

        int entities = 0, compared = 0, exact = 0, differ = 0, nativeOnly = 0, managedOnly = 0;
        int attachmentsCompared = 0, attachmentsDiffer = 0;
        double worst = 0;
        long nativeTicks = 0, managedTicks = 0, invalidateTicks = 0;
        int nativeBuilds = 0, managedBuilds = 0;
        var models = new Dictionary<string, Tally>(StringComparer.Ordinal);

        foreach (EntityHandle entity in Handles())
        {
            if (!skeletons.IsPoseable(entity) && skeletons.BoneMatrices(entity) is null)
                continue;

            entities++;

            // Fresh managed state, seeded from the block as a first ask is:
            // the clock the native skeleton runs on.
            skeletons.Forget(entity);
            StandaloneBones? managed = skeletons.BoneMatrices(entity);
            StandaloneBones? native = nativeAnswers ? render.BoneMatrices(entity) : null;

            string name = skeletons.StudioName(entity);
            if (!models.TryGetValue(name, out Tally? tally))
                models.Add(name, tally = new Tally());

            tally.Count++;
            tally.Bones = managed?.Count ?? 0;

            if (nativeAnswers)
            {
                if (managed is null || native is null)
                {
                    if (managed is null && native is not null) nativeOnly++;
                    if (native is null && managed is not null) managedOnly++;
                }
                else
                {
                    compared++;
                    double d = Diff(native, managed);
                    worst = Math.Max(worst, d);

                    if (d == 0 && native.Count == managed.Count && Same(native.WrittenBits, managed.WrittenBits))
                        exact++;
                    else
                    {
                        differ++;
                        if (differ <= 8)
                            console.Msg(string.Create(CultureInfo.InvariantCulture,
                                $"[SourceSharp] bonecompare: {name} ({entity}) native {native.Count} managed {managed.Count} maxdiff {d:G6}\n"));
                    }

                    for (int a = 1; a <= 16; a++)
                    {
                        bool nf = render.GetAttachment(entity, a, out Vector no, out Vector na);
                        bool mf = skeletons.GetAttachment(entity, a, out Vector mo, out QAngle ma, out _);

                        if (!nf && !mf)
                            break;

                        attachmentsCompared++;
                        if (nf != mf || Far(no, mo) || Far(na, new Vector(ma.Pitch, ma.Yaw, ma.Roll)))
                            attachmentsDiffer++;
                    }
                }
            }

            // The rebuild cost, each way. Native's cache is invalidated by a
            // scale change (two crossings, timed alone and subtracted); the
            // managed state's directly.
            if (nativeAnswers && native is not null)
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < reps; i++)
                {
                    render.SetModelScale(entity, 1.0001f);
                    render.SetModelScale(entity, 1.0f);
                }
                long invalidate = Stopwatch.GetTimestamp() - t0;
                invalidateTicks += invalidate;

                t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < reps; i++)
                {
                    render.SetModelScale(entity, 1.0001f);
                    render.SetModelScale(entity, 1.0f);
                    _ = render.BoneMatrices(entity);
                }
                long spent = Stopwatch.GetTimestamp() - t0;
                nativeTicks += spent;
                nativeBuilds += reps;
                tally.NativeTicks += spent - invalidate;
                tally.NativeBuilds += reps;
            }

            if (managed is not null && skeletons.States.TryGet(entity, out var state) && state is not null)
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < reps; i++)
                {
                    state.InvalidateBoneCache();
                    _ = skeletons.BoneMatrices(entity);
                }
                long spent = Stopwatch.GetTimestamp() - t0;
                managedTicks += spent;
                managedBuilds += reps;
                tally.ManagedTicks += spent;
                tally.ManagedBuilds += reps;
            }
        }

        double nativeUs = (nativeTicks - invalidateTicks) * 1e6 / Stopwatch.Frequency / Math.Max(1, nativeBuilds);
        double managedUs = managedTicks * 1e6 / Stopwatch.Frequency / Math.Max(1, managedBuilds);

        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSBONECOMPARE\tnative\t{(nativeAnswers ? "answers" : "routed")}\tentities\t{entities}\tcompared\t{compared}" +
            $"\texact\t{exact}\tdiffer\t{differ}\tnativeonly\t{nativeOnly}\tmanagedonly\t{managedOnly}\tmaxdiff\t{worst:G6}" +
            $"\tattachments\t{attachmentsCompared}\tattachmentsdiffer\t{attachmentsDiffer}" +
            $"\tus_per_build_native\t{nativeUs:F2}\tus_per_build_managed\t{managedUs:F2}\treps\t{reps}\n"));

        foreach (var (model, t) in models)
            console.Msg(string.Create(CultureInfo.InvariantCulture,
                $"SSBONEMODEL\t{model}\tentities\t{t.Count}\tbones\t{t.Bones}" +
                $"\tus_native\t{Us(t.NativeTicks, t.NativeBuilds):F2}\tus_managed\t{Us(t.ManagedTicks, t.ManagedBuilds):F2}\n"));
    }

    private sealed class Tally
    {
        public int Count;
        public int Bones;
        public long NativeTicks;
        public int NativeBuilds;
        public long ManagedTicks;
        public int ManagedBuilds;
    }

    private static double Us(long ticks, int builds) => ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, builds);

    private IEnumerable<EntityHandle> Handles() => StandaloneHandles(directory);

    /// <summary>Every scripted entity's handle, once (the block read tells a
    /// standalone one from the rest).</summary>
    internal static IEnumerable<EntityHandle> StandaloneHandles(IEntityDirectory directory)
    {
        if (directory is not ScriptedEntityRegistry registry)
            yield break;

        var seen = new HashSet<EntityHandle>();

        foreach (string className in new List<string>(registry.ClassNames))
            foreach (ScriptedEntity e in new List<ScriptedEntity>(registry.OfClass(className)))
                if (seen.Add(e.Handle))
                    yield return e.Handle;
    }

    private static double Diff(StandaloneBones a, StandaloneBones b)
    {
        double worst = 0;
        int n = Math.Min(a.BoneToWorld.Length, b.BoneToWorld.Length);

        for (int i = 0; i < n; i++)
            worst = Math.Max(worst, Math.Abs((double)a.BoneToWorld[i] - b.BoneToWorld[i]));

        return a.Count == b.Count ? worst : double.PositiveInfinity;
    }

    private static bool Same(uint[] a, uint[] b) => a.AsSpan().SequenceEqual(b);

    private static bool Far(Vector a, Vector b) => (a - b).Length > 1e-3f;
}
#endif
