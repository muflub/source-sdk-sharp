namespace SourceSharp.Tests;

/// <summary>The cross-side tests' members of <see cref="ProcessWide"/>.</summary>
public sealed partial class ProcessWide
{
    private static partial Dictionary<Type, string> Rows() => new()
    {
        [typeof(ReloadPinTests)]                = Unload,
    };
}
