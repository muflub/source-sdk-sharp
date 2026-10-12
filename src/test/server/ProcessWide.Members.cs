namespace SourceSharp.Tests;

/// <summary>The server tests' members of <see cref="ProcessWide"/>.</summary>
public sealed partial class ProcessWide
{
    private static partial Dictionary<Type, string> Rows() => new()
    {
        [typeof(ReloadTests)]                   = Unload,
        [typeof(UnloadTests)]                   = Unload,
        [typeof(UnloadAfterDiscoverTests)]      = Unload,
        [typeof(JitWarmupUnloadTests)]          = Unload,
        [typeof(GameLoadContextTests)]          = Unload,

        [typeof(ManagedVtableTests)]            =
            "forces full blocking compacting GC collections (plan_engineapi_vtables D6), which every concurrent test would pay for and some time-based facts cannot",

    };
}
