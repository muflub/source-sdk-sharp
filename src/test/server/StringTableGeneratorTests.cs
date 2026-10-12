using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SourceSharp.Abi;
using SourceSharp.Generators;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The string-table generator, driven over source text.
///
/// <para>
/// It cannot be exercised by declaring its bad cases in this assembly, and that
/// is the generator working rather than a limitation: every rule below is an
/// ERROR, so a malformed <c>[StringTable]</c> anywhere in the tree stops the
/// build. The declarations therefore live in strings, the generator is run as a
/// library over them, and what it wrote — or refused to accept — is read back.
/// </para>
///
/// <para>
/// Each rule comes as a PAIR: the declaration that must be diagnosed and the
/// near-identical one that must not. A check that only ever sees one outcome is
/// as weak as one that cannot fail.
/// </para>
/// </summary>
public class StringTableGeneratorTests
{
    private const string Good = """
        [StringTable("Example", 255)]
        public interface ExampleStringTable : IEngineStringTable { }
        """;

    // ---- what it writes ----------------------------------------------------

    [Fact]
    public void ADeclarationGetsAClassThatImplementsIt()
    {
        string written = Generated(Good);

        Assert.Contains("class ExampleStringTable__StringTable : ExampleStringTable", written);
    }

    [Fact]
    public void TheClassForwardsRatherThanDecidingAnything()
    {
        // The policy -- find the table by name on every use, refuse an absent
        // one once and loudly, refuse a client-side add -- lives in
        // StringTableHandle and is written once. A generator that emitted it
        // would emit a copy per declaration, and the copies are what drift.
        string written = Generated(Good);

        Assert.Contains("public int AddString(string value) => _table.AddString(value);", written);
        Assert.Contains("public string? GetString(int stringIndex) => _table.GetString(stringIndex);", written);
        Assert.DoesNotContain("Find(", written);
    }

    /// <summary>
    /// The round trip: the snippet plus what the generator wrote for it,
    /// compiled, instantiated over a handle, and asked. Reading the text and
    /// asserting it says what this test expects is the same answer derived
    /// twice; running it is what says the forwarder forwards.
    /// </summary>
    [Fact]
    public void TheGeneratedClassReallyForwardsToTheHandleItIsGiven()
    {
        Type written = Compiled(Good, "ExampleStringTable__StringTable");

        var engine = TestServices.Mock();
        engine.Returns("StringTables.Find", 7);
        engine.Returns("StringTables.AddString", 3);

        var table = (IEngineStringTable)Activator.CreateInstance(
            written, new StringTableHandle((IEngineStringTables)engine, "Example", new StringTableWriter((IEngineStringTables)engine), new RecordingLog()))!;

        Assert.Equal(3, table.AddString("a string"));
        Assert.Equal(7, engine.AllOf("StringTables.AddString").Single().Args[0]);
    }

    // ---- what it refuses ---------------------------------------------------

    [Fact]
    public void ADeclarationThatDoesNotExtendTheTableInterfaceIsRefused()
    {
        Assert.Contains("SSTAB001", Diagnostics("""
            [StringTable("Example", 255)]
            public interface ExampleStringTable { }
            """));
    }

    [Fact]
    public void AndOneThatDoesIsNot()
        => Assert.DoesNotContain("SSTAB001", Diagnostics(Good));

    [Fact]
    public void ATableWithNoRoomInItIsRefused()
    {
        // The engine sizes a table when it creates it and there is no resize,
        // so a zero-sized table is one that can never be written to -- and the
        // symptom would be an AddString answering -1 forever with nothing to
        // say why.
        Assert.Contains("SSTAB002", Diagnostics("""
            [StringTable("Example", 0)]
            public interface ExampleStringTable : IEngineStringTable { }
            """));
    }

    [Fact]
    public void AndOneWithRoomIsNot()
        => Assert.DoesNotContain("SSTAB002", Diagnostics(Good));

    [Fact]
    public void ADeclarationThatAddsMembersOfItsOwnIsRefused()
    {
        // A declaration is a NAME for a table. The class that answers for it is
        // written here, so a member the author added has no body coming -- and
        // the error the author would otherwise get is "does not implement", on
        // the generated file, which is the file they cannot edit.
        Assert.Contains("SSTAB003", Diagnostics("""
            [StringTable("Example", 255)]
            public interface ExampleStringTable : IEngineStringTable
            {
                int Extra { get; }
            }
            """));
    }

    [Fact]
    public void AndAnEmptyOneIsNot()
        => Assert.DoesNotContain("SSTAB003", Diagnostics(Good));

    [Fact]
    public void ANestedDeclarationIsRefused()
    {
        Assert.Contains("SSTAB004", Diagnostics("""
            public static class Holder
            {
                [StringTable("Example", 255)]
                public interface ExampleStringTable : IEngineStringTable { }
            }
            """));
    }

    [Fact]
    public void AndATopLevelOneIsNot()
        => Assert.DoesNotContain("SSTAB004", Diagnostics(Good));

    //-------------------------------------------------------------------------

    private static List<string> Diagnostics(string source)
        => Drive(source).result.Diagnostics.Select(d => d.Id).ToList();

    private static string Generated(string source)
        => string.Join("\n", Drive(source).result.GeneratedSources.Select(s => s.SourceText.ToString()));

    private static Type Compiled(string source, string typeName)
    {
        var (compilation, _) = Drive(source);

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);

        Assert.True(emitted.Success,
            "the snippet plus its generated half must compile:\n"
            + string.Join("\n", emitted.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        return assembly.GetTypes().Single(t => t.Name == typeName);
    }

    private static (Compilation compilation, GeneratorRunResult result) Drive(string source)
    {
        var tree = CSharpSyntaxTree.ParseText("using System;\nusing SourceSharp.Abi;\n" + source);

        var compilation = CSharpCompilation.Create(
            "StringTableGeneratorTests.Subject." + Guid.NewGuid().ToString("N"),
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new StringTableGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _)
            .GetRunResult();

        var driver = CSharpGeneratorDriver.Create(new StringTableGenerator())
                                          .RunGenerators(compilation);

        return (updated, driver.GetRunResult().Results.Single());
    }

    private static readonly MetadataReference[] References =
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(StringTableAttribute).Assembly.Location))
            .Distinct()
            .ToArray();
}
