using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A declaration with no handler, for the registry facts about one.
///
/// <para>
/// Every production message has a payload and a handler since the payload
/// model (user ruling 2026-10-04: all user messages in managed code), but the
/// registry still accepts a bare declaration and must still give it an id and
/// no handler. The fixture is compiled into an assembly of its own and handed
/// to <c>UserMessageRegistry.Register</c> beside the production types, rather
/// than declared in this assembly, where every registry a test builds would
/// take it.
/// </para>
/// </summary>
internal static class UserMessageFixtures
{
    /// <summary>The production declarations, then the bare fixture.</summary>
    public static (List<Type> Types, Type Bare) WithBareDeclaration()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            namespace UserMessageFixture;

            [SourceSharp.Abi.UserMessage("Bare")]
            public sealed class BareMessage;
            """);

        // The side whose [UserMessage] the fixture is written against, and not
        // the other: a process that loads both sides holds two copies of the
        // shared source (plan_side_isolation P7), and the compiler would find
        // every shared name twice.
        string? otherSide = LoadedAssemblies.OtherSideSuffix(typeof(UserMessageAttribute).Assembly);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Where(a => otherSide is null || !(a.GetName().Name ?? "").EndsWith(otherSide, StringComparison.Ordinal))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(UserMessageAttribute).Assembly.Location))
            .Distinct()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "UserMessageFixture." + Guid.NewGuid().ToString("N"), [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        Type bare = System.Reflection.Assembly.Load(stream.ToArray()).GetType("UserMessageFixture.BareMessage")!;

        var types = typeof(SourceSharp.Shared.FadeMessage).Assembly.GetTypes().ToList();
        types.Add(bare);
        return (types, bare);
    }
}
