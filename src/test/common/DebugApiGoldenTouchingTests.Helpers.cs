using SourceSharp.Abi;
using Debug.Api;
using Xunit;
using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="DebugApiGoldenTouchingTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class DebugApiGoldenTouchingTests
{
    /// <summary>Two touch links, 5-7 and 9-5, over an engine that answers each handle's index in turn.</summary>
    internal static SourceSharp.Server.Interface.IDebugServerHooks Hooks()
    {
        var engine = DebugApiGoldenWorldTests.Engine();
        var indices = new Queue<int>([5, 7, 9, 5]);
        engine.ReturnsEachCall("Entities.GetIndex", () => indices.Count > 0 ? indices.Dequeue() : -1);
        var pairs = new (EntityHandle, EntityHandle)[]
        {
            (new EntityHandle(0x0001_0005u), new EntityHandle(0x0001_0007u)),
            (new EntityHandle(0x0001_0009u), new EntityHandle(0x0002_0005u)),
        };
        return TestDebugHooks.Server(engine: DebugApiGoldenWorldTests.Container(engine), touching: () => pairs);
    }
}
