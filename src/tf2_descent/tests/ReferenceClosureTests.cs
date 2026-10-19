using System.Reflection;
using SourceSharp.Host.Sdk;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// Which assemblies each TF2Descent assembly may reference (plan_host_sdk §2.1).
/// The hosting service loads TF2.Descent.Rules alone, in its own process, to replay
/// rolls; the client loads TF2.Descent.Client (the shared source compiled in) and must never
/// pull in the SDK or its gRPC stack.
/// </summary>
public sealed class ReferenceClosureTests
{
    private static readonly string[] HostAssemblies =
        ["SourceSharp.Host.Sdk", "SourceSharp.Host.Contracts", "SourceSharp.Host.Proto", "Grpc.Net.Client", "Google.Protobuf"];

    private static string[] References(string name) =>
        Assembly.Load(new AssemblyName(name)).GetReferencedAssemblies().Select(r => r.Name!).ToArray();

    private static bool IsFramework(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    /// <summary>The references of <paramref name="assembly"/> that are host assemblies.</summary>
    private static string[] HostReferences(string assembly) => References(assembly).Intersect(HostAssemblies).ToArray();

    [Fact]
    public void Rules_references_nothing_but_the_framework_and_host_contracts()
    {
        var others = References("TF2.Descent.Rules").Where(r => !IsFramework(r) && r != "SourceSharp.Host.Contracts");

        Assert.Empty(others);
    }

    [Theory]
    [InlineData("TF2.Descent.Client")]
    public void Client_side_assemblies_never_reference_the_host(string assembly)
    {
        Assert.Empty(HostReferences(assembly));
    }

    [Fact]
    public void The_host_check_sees_a_host_reference()
    {
        // Fail-first arm for the facts above: this assembly references the SDK
        // (the typeof below makes the compiler keep the reference), so the same
        // check must report it. A check that cannot see a reference passes every
        // closure fact vacuously.
        Assert.NotNull(typeof(HostSdk));

        Assert.Contains("SourceSharp.Host.Sdk", HostReferences("TF2.Descent.Tests"));
    }

    [Theory]
    [InlineData("TF2.Server")]
    [InlineData("TF2.Client")]
    public void The_tf2_mod_never_references_tf2_descent_or_the_host(string assembly)
    {
        // TF2 is the base mod; TF2Descent builds on it, never the other way.
        var refs = References(assembly);

        Assert.Empty(HostReferences(assembly));
        Assert.DoesNotContain(refs, r => r.StartsWith("TF2.Descent", StringComparison.Ordinal));
    }
}

