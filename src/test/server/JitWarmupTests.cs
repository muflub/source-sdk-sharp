using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_map_load P5: a side's methods are compiled on a background thread at
/// start-up, so the first map load does not JIT them on the main thread.
/// </summary>
public class JitWarmupTests
{
    private sealed class ListLog : ILog
    {
        public readonly List<string> Lines = [];
        public void Msg(string message) { lock (Lines) Lines.Add(message); }
        public void Warning(string message) { lock (Lines) Lines.Add(message); }
    }

    // Each class's methods are called by exactly one fact, so their first call
    // here is their first call in the process.
    private static class Warmed
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static int A(int x) => x * 3 + 1;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int B(int x) => x ^ 0x55;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int C(int x) => x - 7;
    }

    private static class Cold
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static int A(int x) => x * 5 + 2;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int B(int x) => x ^ 0x33;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int C(int x) => x - 11;
    }

    private static long JittedHere(Func<int> calls)
    {
        // The lambda is new code too: compile it first, so only what it calls counts.
        RuntimeHelpers.PrepareMethod(calls.Method.MethodHandle);
        long before = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true);
        calls();
        return System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true) - before;
    }

    [Fact]
    public void A_warmed_type_is_not_compiled_again_by_its_first_caller()
    {
        var log = new ListLog();
        using var warmup = new JitWarmup(log);
        warmup.Start("test", [typeof(Warmed)]);
        Assert.True(warmup.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(3, warmup.Prepared);

        Assert.Equal(0, JittedHere(() => Warmed.A(1) + Warmed.B(2) + Warmed.C(3)));
        Assert.Contains(log.Lines, l => l.StartsWith("[SourceSharp] JIT warm-up (test): 3 method(s)", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_it_the_first_caller_compiles_them()
        => Assert.True(JittedHere(() => Cold.A(1) + Cold.B(2) + Cold.C(3)) >= 3,
                       "the measurement itself must see a cold method compile, or the warmed fact proves nothing");

    [Fact]
    public void Dispose_stops_a_running_pass_and_joins_it()
    {
        var log = new ListLog();
        var warmup = new JitWarmup(log);
        var types = warmup.TypesOfContextOf(typeof(JitWarmupTests).Assembly).ToList();
        Assert.True(types.Count > 1000, $"the stop needs a pass long enough to interrupt; this context has {types.Count} types");

        warmup.Start("test", types);
        warmup.Dispose();

        Assert.True(warmup.Wait(TimeSpan.Zero), "Dispose returned with the warm-up thread still running");
        Assert.Contains(log.Lines, l => l.Contains("stopped early", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_modules_start_it_last_in_Initialize_and_stop_it_first_in_Shutdown()
    {
        foreach (var (path, side, cls) in new[] { (RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"), "client", "ClientModule"),
                                                 (RepoLayout.Repo("src", "server", "managed", "ServerModule.cs"), "server", "ServerModule") })
        {
            Assert.True(File.Exists(path), $"{path} was not found; a scan that read nothing would pass");
            string text = File.ReadAllText(path);
            Assert.Matches($@"_jitWarmup\.Start\(""{side}"", _jitWarmup\.TypesOfContextOf\(typeof\({cls}\)\.Assembly\)\);\s*\}}", text);
            Assert.Matches(@"public void Shutdown\(\)\s*\{\s*(//[^\n]*\s*)*_jitWarmup\?\.Dispose\(\);", text);
        }
    }
}

/// <summary>
/// The warm-up runs inside a side's collectible context (each side compiles its
/// own copy of src/shared/managed/JitWarmup.cs), so it must walk that side's
/// context only and must not keep it alive past a reload. A second copy of
/// SourceSharp.Server, loaded the way GameLoadContext loads the real one, runs
/// its OWN JitWarmup here.
/// </summary>
[Collection(ProcessWide.Name)]
public class JitWarmupUnloadTests
{
    private static (SideContext Context, Assembly Server, object Warmup, Type WarmupType) LoadServerCopy()
    {
        var context = new SideContext(collectible: true);
        Assembly? server = context.FromBin("SourceSharp.Server");
        Assert.True(server is not null, "SourceSharp.Server must be a file beside the tests for a side copy");
        Assert.NotSame(typeof(SourceSharp.Server.ServerModule).Assembly, server);

        Type type = server!.GetType("SourceSharp.Abi.JitWarmup", throwOnError: true)!;
        Assert.NotSame(typeof(JitWarmup), type);   // the copy's own, not the default context's
        object warmup = Activator.CreateInstance(type, new RecordingLog())!;
        return (context, server, warmup, type);
    }

    private static IEnumerable<Type> TypesOfContextOf(Type warmupType, object warmup, Assembly owner)
        => (IEnumerable<Type>)warmupType.GetMethod("TypesOfContextOf")!.Invoke(warmup, [owner])!;

    [Fact]
    public void It_walks_its_own_sides_context_and_nothing_else()
    {
        var (context, server, warmup, type) = LoadServerCopy();
        try
        {
            Type[] types = TypesOfContextOf(type, warmup, server).ToArray();

            Assert.Contains(types, t => t.FullName == "SourceSharp.Server.ServerModule");   // the walk reached its side
            Assert.All(types, t => Assert.Same(context, AssemblyLoadContext.GetLoadContext(t.Assembly)));
            Assert.DoesNotContain(types, t => t.Assembly == typeof(ILog).Assembly);              // not the shared Abi
            Assert.DoesNotContain(types, t => t.Assembly.GetName().Name == "SourceSharp.Client"); // not the other side
        }
        finally
        {
            ((IDisposable)warmup).Dispose();
            context.Unload();
        }
    }

    [Fact]
    public void A_disposed_warmup_leaves_its_context_collectible()
    {
        var (weak, prepared) = StartDisposeAndUnload();

        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.True(prepared > 0, "the warm-up compiled nothing before Dispose; the fact would not have exercised a running pass");
        Assert.False(weak.IsAlive, "a side's context outlived its JIT warm-up's Dispose");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, int) StartDisposeAndUnload()
    {
        var (context, server, warmup, type) = LoadServerCopy();
        var weak = new WeakReference(context, trackResurrection: true);

        type.GetMethod("Start")!.Invoke(warmup, ["server", TypesOfContextOf(type, warmup, server)]);

        // Let the pass get going, so Dispose stops a running thread rather than
        // one that has not been scheduled yet.
        var prepared = type.GetProperty("Prepared")!;
        for (int i = 0; i < 200 && (int)prepared.GetValue(warmup)! == 0; i++)
            Thread.Sleep(5);

        ((IDisposable)warmup).Dispose();
        int count = (int)prepared.GetValue(warmup)!;
        context.Unload();
        return (weak, count);
    }
}
