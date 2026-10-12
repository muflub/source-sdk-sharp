using SourceSharp.Abi;
using SourceSharp.Shared;
using static SourceSharp.Tests.TestServices;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>What the fixture entities ask for.</summary>
public interface ITestSystem { int Answer { get; } }

public sealed class TestSystem : ITestSystem
{
    public TestSystem(int answer) { Answer = answer; }
    public int Answer { get; }
}

[ConVar("ss_test_entity_reach", "3.5")]
public sealed class EntityReachConVar : FloatConVar { }
