using System;
using System.Buffers;
using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// NameMatches: every entity answering to a name as an allocation-free snapshot
/// for foreach, safe while the loop body changes the entity set (the contract
/// written on the type).
/// </summary>
public class NameMatchesTests
{
    private const string ShellLessClass = "logic_relay";

    private sealed class Probe : ScriptedEntity
    {
    }

    private static readonly Func<ScriptedEntity, bool> IsShellLess = e => e.ClassName == ShellLessClass;

    private static EntityHandle H(int entry) => new((uint)entry | (1u << 16));

    private static EntityHandle Add(EntityDirectory d, int entry, string className, string name)
    {
        EntityHandle h = H(entry);
        Assert.True(d.Bind(h, new Probe { ClassName = className }));
        d.SetName(h, name);
        return h;
    }

    private static NameMatches Named(EntityDirectory d, string query, Func<EntityHandle, bool> alive, int limit = 512)
    {
        EntityHandle[] buffer = ArrayPool<EntityHandle>.Shared.Rent(16);
        int count = d.CollectByName(query, IsShellLess, ref buffer, limit);
        return new NameMatches(buffer, count, alive);
    }

    private static Func<EntityHandle, bool> AliveIn(EntityDirectory d) => h => d.Resolve(h) is not null;

    [Fact]
    public void TheWalkIsInNameLookupOrder()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle relay = Add(d, 7811, ShellLessClass, "lights");
        EntityHandle first = Add(d, 100, "info_target", "lights");
        EntityHandle second = Add(d, 900, "info_target", "lights");

        var seen = new List<EntityHandle>();
        foreach (EntityHandle h in Named(d, "lights", AliveIn(d)))
            seen.Add(h);

        var expected = new List<EntityHandle>();
        d.AllByName("lights", IsShellLess, expected, 512);

        Assert.Equal(new[] { first, second, relay }, seen);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void AMatchRemovedBeforeTheLoopReachesItIsSkipped()
    {
        // The usual caller fires an input at each match, and an input can kill
        // the next one.
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle a = Add(d, 10, "info_target", "target");
        EntityHandle b = Add(d, 11, "info_target", "target");
        EntityHandle c = Add(d, 12, "info_target", "target");

        var seen = new List<EntityHandle>();
        foreach (EntityHandle h in Named(d, "target", AliveIn(d)))
        {
            seen.Add(h);
            if (h == a)
                Assert.True(d.Unbind(b));
        }

        Assert.Equal(new[] { a, c }, seen);
    }

    [Fact]
    public void AnEntitySpawnedDuringTheLoopIsNotVisited()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle a = Add(d, 10, "info_target", "target");
        EntityHandle spawned = EntityHandle.None;

        var seen = new List<EntityHandle>();
        foreach (EntityHandle h in Named(d, "target", AliveIn(d)))
        {
            seen.Add(h);
            if (spawned == EntityHandle.None)
                spawned = Add(d, 5, "info_target", "target");
        }

        Assert.Equal(new[] { a }, seen);
        Assert.NotEqual(EntityHandle.None, spawned);
    }

    [Fact]
    public void AMatchRenamedDuringTheLoopIsStillVisited()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle a = Add(d, 10, "info_target", "target");
        EntityHandle b = Add(d, 11, "info_target", "target");

        var seen = new List<EntityHandle>();
        foreach (EntityHandle h in Named(d, "target", AliveIn(d)))
        {
            seen.Add(h);
            if (h == a)
                d.SetName(b, "other");
        }

        Assert.Equal(new[] { a, b }, seen);
    }

    [Fact]
    public void CountIsHowManyMatchedWhenAsked()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 10, "info_target", "x");
        Add(d, 11, "info_target", "x");

        NameMatches matches = Named(d, "x", AliveIn(d));
        Assert.Equal(2, matches.Count);
        matches.Dispose();
    }

    [Fact]
    public void NoMatchesAndNoQueryWalkNothing()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 10, "info_target", "x");

        int n = 0;
        foreach (EntityHandle _ in Named(d, "y", AliveIn(d))) n++;
        foreach (EntityHandle _ in Named(d, "", AliveIn(d))) n++;
        foreach (EntityHandle _ in NameMatches.None) n++;

        Assert.Equal(0, n);
    }

    [Fact]
    public void DisposingTwiceIsSafeAndUsingAroundAForeachReturnsTheBufferOnce()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 10, "info_target", "x");

        NameMatches matches = Named(d, "x", AliveIn(d));
        int n = 0;
        using (matches)
        {
            foreach (EntityHandle _ in matches)
                n++;
        }
        matches.Dispose();

        Assert.Equal(1, n);
    }

    [Fact]
    public void AWalkAllocatesNothing()
    {
        var d = new EntityDirectory(new RecordingLog());
        for (int i = 0; i < 40; i++)
            Add(d, 100 + i, i % 2 == 0 ? "info_target" : ShellLessClass, "lamp" + (i % 4));
        Func<EntityHandle, bool> alive = AliveIn(d);

        static int Walk(EntityDirectory d, Func<EntityHandle, bool> alive)
        {
            int n = 0;
            foreach (EntityHandle _ in Named(d, "lamp*", alive))
                n++;
            foreach (EntityHandle _ in Named(d, "lamp1", alive))
                n++;
            return n;
        }

        Walk(d, alive);   // warm the pools and the JIT

        long before = GC.GetAllocatedBytesForCurrentThread();
        int visited = Walk(d, alive);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(50, visited);
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void AProceduralNameWalksNothing()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 10, "info_target", "!self");

        int n = 0;
        foreach (EntityHandle _ in Named(d, "!self", AliveIn(d))) n++;

        Assert.Equal(0, n);
    }
}
