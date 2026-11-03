using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Tests;

/// <summary>A player in the lag compensation's fake world.</summary>
public sealed class LagPlayer(EntityHandle handle)
{
    public EntityHandle Handle { get; } = handle;
    public Vector Origin;
    public QAngle Angles;
    public Vector Mins = new(-16f, -16f, 0f);
    public Vector Maxs = new(16f, 16f, 72f);
    public bool Alive = true;
    public bool Bot;
    public bool Observer;
    public bool WantsLagCompensation = true;
    public float LerpTime;
    public float? Latency;
    public float MaxSpeed = 320f;
    public float SimulationTime;
    public int Sequence = 3;
    public float Cycle;
    public float? HeldCycle;
    public float[] Pose = new float[24];
}

/// <summary>
/// The lag compensation's world over plain records: where each player is,
/// what each gate answers, and every move the compensation made.
/// </summary>
public sealed class LagWorld : ILagCompensationWorld
{
    public const float Tick = 0.015f;

    public readonly Dictionary<int, LagPlayer> Players = new();
    public readonly List<(EntityHandle Player, Vector Origin, QAngle Angles)> Relinks = [];
    public readonly List<(EntityHandle Player, Vector Origin)> TouchingMoves = [];
    public readonly List<(EntityHandle Player, Vector Mins, Vector Maxs)> Sizes = [];
    public readonly List<EntityHandle> Flushes = [];

    /// <summary>The box each restore sweep was made with.</summary>
    public readonly List<(Vector Mins, Vector Maxs)> SweptBoxes = [];

    /// <summary>Points a player's box does not fit at (the restore's traces).</summary>
    public Func<Vector, bool> Solid { get; set; } = _ => false;

    public float CurTime { get; set; }
    public int TickCount { get; set; }
    public float TickInterval => Tick;
    public int MaxClients { get; set; } = 4;

    public LagPlayer Add(int index)
        => Players[index] = new LagPlayer(new EntityHandle((uint)(0x0001_0000 | index)));

    private LagPlayer? Of(EntityHandle h) => Players.Values.FirstOrDefault(p => p.Handle == h);

    private LagPlayer Must(EntityHandle h) => Of(h) ?? throw new InvalidOperationException($"no player {h}");

    /// <summary>The server's frame at <paramref name="tick"/>: the clock, and every player's simulation time.</summary>
    public void At(int tick)
    {
        TickCount = tick;
        CurTime = tick * Tick;
        foreach (LagPlayer p in Players.Values)
            p.SimulationTime = CurTime;
    }

    public EntityHandle PlayerByIndex(int index)
        => Players.TryGetValue(index, out LagPlayer? p) ? p.Handle : EntityHandle.None;

    public bool IsAlive(EntityHandle player) => Must(player).Alive;
    public bool IsBot(EntityHandle player) => Must(player).Bot;
    public bool IsObserver(EntityHandle player) => Must(player).Observer;
    public bool WantsLagCompensation(EntityHandle player) => Must(player).WantsLagCompensation;
    public float LerpTime(EntityHandle player) => Must(player).LerpTime;
    public float? Latency(EntityHandle player) => Must(player).Latency;
    public float MaxSpeed(EntityHandle player) => Must(player).MaxSpeed;
    public float GetSimulationTime(EntityHandle player) => Must(player).SimulationTime;
    public void SetSimulationTime(EntityHandle player, float time) => Must(player).SimulationTime = time;

    public bool TryGetPlacement(EntityHandle player, out EntityPlacement placement)
    {
        if (Of(player) is not { } p)
        {
            placement = default;
            return false;
        }

        placement = new EntityPlacement(p.Origin, p.Angles, p.Mins, p.Maxs);
        return true;
    }

    public void Relink(EntityHandle player, Vector origin, QAngle angles)
    {
        Relinks.Add((player, origin, angles));
        LagPlayer p = Must(player);
        p.Origin = origin;
        p.Angles = angles;
    }

    public void SetSize(EntityHandle player, Vector mins, Vector maxs)
    {
        Sizes.Add((player, mins, maxs));
        LagPlayer p = Must(player);
        p.Mins = mins;
        p.Maxs = maxs;
    }

    public void SetOrigin(EntityHandle player, Vector origin)
    {
        TouchingMoves.Add((player, origin));
        Must(player).Origin = origin;
    }

    public EntityHullTrace TraceEntity(EntityHandle player, Vector start, Vector end, Vector mins, Vector maxs)
    {
        SweptBoxes.Add((mins, maxs));

        if (Solid(start))
            return new EntityHullTrace(start, true, true);

        // A sweep stops half way into a solid end.
        return Solid(end)
            ? new EntityHullTrace(start + (end - start) * 0.5f, false, false, default, 0.5f)
            : new EntityHullTrace(end, false, false, default, 1f);
    }

    public bool CaptureAnimation(EntityHandle player, SkeletonAnimation into)
    {
        LagPlayer p = Must(player);
        into.Sequence = p.Sequence;
        into.Cycle = p.HeldCycle ?? p.Cycle;
        into.HeldCycle = p.HeldCycle;
        p.Pose.AsSpan().CopyTo(into.PoseParameters);
        return true;
    }

    public void PoseAnimation(EntityHandle player, int sequence, float cycle, ReadOnlySpan<float> poseParameters)
    {
        LagPlayer p = Must(player);
        p.Sequence = sequence;
        p.HeldCycle = cycle;
        poseParameters[..p.Pose.Length].CopyTo(p.Pose);
    }

    public void RestoreAnimation(EntityHandle player, SkeletonAnimation saved)
    {
        LagPlayer p = Must(player);
        p.Sequence = saved.Sequence;
        p.HeldCycle = saved.HeldCycle;
        saved.PoseParameters.AsSpan(0, p.Pose.Length).CopyTo(p.Pose);
    }

    public void InvalidateBoneCache(EntityHandle player) => Flushes.Add(player);
}

/// <summary>The four convars, as plain values.</summary>
public sealed class LagSettings : ILagCompensationSettings
{
    public bool Unlag { get; set; } = true;
    public float MaxUnlag { get; set; } = 1f;
    public bool FlushBoneCache { get; set; } = true;
    public float TeleportDistance { get; set; } = 64f;
}

/// <summary>A hook list that is a list.</summary>
public sealed class LagHooks<T>(params T[] all) : IHooks<T> where T : class
{
    public IReadOnlyList<T> All => all;

    public void Each(string what, Action<T> call)
    {
        foreach (T hook in all)
            call(hook);
    }
}
