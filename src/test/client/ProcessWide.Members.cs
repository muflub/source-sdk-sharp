namespace SourceSharp.Tests;

/// <summary>The client tests' members of <see cref="ProcessWide"/>.</summary>
public sealed partial class ProcessWide
{
    private static partial Dictionary<Type, string> Rows() => new()
    {
        [typeof(Ui.UiAllocationTests)]          =
            "measures GC.GetAllocatedBytesForCurrentThread, which a collection on any thread moves; a concurrent test's GC spoils its windows",
        [typeof(UiLoaderTests)]                 = Unload,
    };
}
