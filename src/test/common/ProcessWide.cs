using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE ONE SERIAL COLLECTION: test classes that share a process-wide resource
/// with some other test and so must not run beside anything.
///
/// <para>
/// Everything else runs in parallel (AssemblyInfo.cs). xUnit runs this
/// collection's classes one at a time AFTER every parallel collection has
/// finished, so a member never overlaps any other test; it can still see what
/// an earlier test left behind, exactly as it could when the whole suite ran
/// serially.
/// </para>
///
/// <para>
/// MEMBERSHIP IS A LIST, NOT A HABIT. A class joins with
/// <c>[Collection(ProcessWide.Name)]</c> AND a row in <see cref="Members"/>
/// naming what it shares; the build fails (SS0060-SS0062, ProcessWideAnalyzer) when
/// the two disagree. Found by running the suite in parallel repeatedly and by
/// a census of the static fields the runtime assemblies declare. The way out
/// is to inject what the class shares, not to widen this list.
/// </para>
///
/// <para>
/// NOT A SEPARATE PROCESS, measured 2026-10-04 (test-speed-2, trx): the
/// serial tail is 0.67 s of the server project's 13.1 s (seven classes, the
/// slowest ManagedVtableTests at 0.20 s) and ReloadPinTests' 0.68 s in the
/// shared project, against a second test process's 2-3 s start. The Unload
/// classes and ManagedVtableTests stay here; DebugApiTests and
/// CMakeGraphFixtureTests left once what they shared was injected.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed partial class ProcessWide
{
    public const string Name = "ProcessWide";

    // What each member shares. Keep the reasons specific: the static, the
    // variable or the path.
    internal const string Unload =
        "unloads collectible load contexts and forgets ManagedCaches' process-wide routes and " +
        "registries; the unload is judged by GC, which any concurrent test's references defeat";

    /// <summary>
    /// This test project's members (plan_side_isolation P6: one list per
    /// project, ProcessWide.Members.cs beside its facts, because a collection
    /// is per test assembly and so is the type each row names).
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> Members = Rows();

    private static partial Dictionary<Type, string> Rows();
}
