using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Everything a declaration can now say at once: bounds the engine clamps to,
/// and flags that were unreachable from C# until it could name them.
/// </summary>
[ConVar("cs_test_bounded", "50", Help = "a bounded test convar",
        Flags = ConVarFlags.Hidden | ConVarFlags.NotConnected | ConVarFlags.Protected,
        Min = 0f, Max = 100f)]
public sealed class BoundedTestConVar : FloatConVar
{
}

/// <summary>The other half of the pair: no bounds stated, so none held.</summary>
[ConVar("cs_test_unbounded", "1", Help = "an unbounded test convar")]
public sealed class UnboundedTestConVar : FloatConVar
{
}
