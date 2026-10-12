using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// Compiles a side's own methods on a background thread while the game starts,
/// so the first map load does not JIT them on the main thread (plan_map_load P5).
///
/// <para>
/// <b>Why not ReadyToRun.</b> A side's assemblies load into a collectible
/// <c>GameLoadContext</c> so they can hot-reload, and the runtime ignores
/// ReadyToRun code in a collectible context. Measured in a test library:
/// 207 methods JIT-compiled with or without R2R. The context's multicore-JIT
/// profile is recorded but not replayed there either (201 of 207). A background
/// <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> pass took
/// the main thread's count from 207 to 6, and the context still unloaded.
/// <c>perf</c> on the first loads at 96d57d1 put <c>libclrjit</c> at
/// 0.22–0.27 s of main-thread time.
/// </para>
///
/// <para>
/// <b>It must not hold an unload up.</b> A thread still inside a collectible
/// context's code keeps that context alive, so <see cref="Dispose"/> cancels the
/// pass and joins the thread, and each module disposes this first in Shutdown.
/// </para>
/// </summary>
public sealed class JitWarmup(ILog log) : IDisposable
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>The methods compiled so far (for the log line and the tests).</summary>
    public int Prepared { get; private set; }

    /// <summary>The types of every assembly in <paramref name="owner"/>'s load context.</summary>
    public IEnumerable<Type> TypesOfContextOf(Assembly owner)
    {
        var context = AssemblyLoadContext.GetLoadContext(owner) ?? AssemblyLoadContext.Default;
        foreach (var assembly in context.Assemblies.ToArray())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = [.. e.Types.Where(t => t is not null)!]; }
            foreach (var type in types)
                yield return type;
        }
    }

    /// <summary>Starts the pass over <paramref name="types"/>; a second call is ignored.</summary>
    public void Start(string side, IEnumerable<Type> types)
    {
        if (_thread is not null)
            return;
        _thread = new Thread(() => Run(side, types))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = $"SourceSharp JIT warm-up ({side})",
        };
        _thread.Start();
    }

    /// <summary>Waits for the pass to finish; true when it did within <paramref name="timeout"/>.</summary>
    public bool Wait(TimeSpan timeout) => _thread is null || _thread.Join(timeout);

    private void Run(string side, IEnumerable<Type> types)
    {
        var watch = Stopwatch.StartNew();
        int prepared = 0;
        foreach (var type in types)
        {
            if (_stop)
                break;
            if (type.ContainsGenericParameters)
                continue;
            foreach (var method in type.GetMethods(Declared))
            {
                if (_stop)
                    break;
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                    continue;
                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    prepared++;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // A method the JIT refuses ahead of time (an
                    // [UnmanagedCallersOnly] export, a body it cannot verify)
                    // is compiled when it is first called instead, as before.
                }
            }
            Prepared = prepared;
        }
        Prepared = prepared;
        log.Msg($"[SourceSharp] JIT warm-up ({side}): {prepared} method(s) in {watch.ElapsedMilliseconds} ms{(_stop ? ", stopped early" : "")}\n");
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join();
        _thread = null;
    }
}
