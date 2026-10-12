using SourceSharp.Abi;
using Debug.Api;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="DebugApiGoldenBuildTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class DebugApiGoldenBuildTests
{
    internal static readonly BuildHalf KnownHalf = new(
        new BuildStamp("abc12345", new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), 3),
        new GitProvenance("debug-api-iface", "deadbeef", true),
        "/game/bin/managed",
        false);
}
