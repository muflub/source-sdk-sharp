extern alias client;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Abi;
using SourceSharp.Shared;
using static SourceSharp.Tests.TestServices;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The client half, which builds the same way.</summary>
[ClientStandaloneEntity("test_injected")]
public sealed class TestInjectedClientEntity : ClientStandaloneEntity
{
    public TestInjectedClientEntity(ITestSystem system, IEngineEntityRender render)
        : base(render)
    {
        System = system;
    }

    public ITestSystem System { get; }
    public override string ClassName => "test_injected";
}

[ClientStandaloneEntity("test_injection_plain")]
public sealed class TestPlainClientEntity : ClientStandaloneEntity
{
    public TestPlainClientEntity(IEngineEntityRender render) : base(render) { }

    public override string ClassName => "test_injection_plain";
}

[ClientStandaloneEntity("test_injection_missing")]
public sealed class TestMissingClientEntity : ClientStandaloneEntity
{
    public TestMissingClientEntity(IDisposable nobodyRegistersThis, IEngineEntityRender render)
        : base(render)
    {
        _ = nobodyRegistersThis;
    }

    public override string ClassName => "test_injection_missing";
}
