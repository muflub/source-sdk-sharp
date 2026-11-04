using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceSharp.Abi;
using SourceSharp.Generators;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TempEntGenerator"/>: its diagnostics, each a refused declaration beside the nearly
/// identical accepted one, what it writes for each member shape, and the registry's startup
/// check that what it wrote agrees with the schema.
/// </summary>
public partial class TempEntGeneratorTests
{
    //-------------------------------------------------------------------------
    // Driving the generator over source text.
    //-------------------------------------------------------------------------

    private static readonly MetadataReference[] References =
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => a.Location)
            .Append(typeof(TempEntState).Assembly.Location)
            .Append(typeof(Vector).Assembly.Location)
            .Distinct()
            .Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))
            .ToArray();

    private static CSharpCompilation Compilation(string source)
        => CSharpCompilation.Create(
            "TempEntGeneratorTests.Subject",
            [CSharpSyntaxTree.ParseText("using System.Runtime.CompilerServices;\nusing SourceSharp.Abi;\n" + source,
                                        new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    /// <summary>The ids the generator reports for a snippet.</summary>
    private static List<string> Ids(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new TempEntGenerator()).RunGenerators(Compilation(source));

        return driver.GetRunResult().Results.Single().Diagnostics.Select(d => d.Id).ToList();
    }

    /// <summary>
    /// An accepted snippet: no diagnostic, and the snippet with what was generated compiles with
    /// no error, which is the whole of what a generated override has to do to be one.
    /// </summary>
    private static void AssertAccepted(string source)
    {
        CSharpGeneratorDriver.Create(new TempEntGenerator())
            .RunGeneratorsAndUpdateCompilation(Compilation(source), out Compilation after, out var diagnostics);

        Assert.Empty(diagnostics.Select(d => d.ToString()));

        Assert.Empty(after.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
    }

    private static void AssertRefused(string id, string source, string named)
    {
        var driver = CSharpGeneratorDriver.Create(new TempEntGenerator()).RunGenerators(Compilation(source));
        var run = driver.GetRunResult().Results.Single();

        Assert.Equal([id], run.Diagnostics.Select(d => d.Id));
        Assert.Contains(named, run.Diagnostics[0].GetMessage());
    }

    [Fact]
    public void A_whole_declaration_is_accepted_and_compiles()
    {
        AssertAccepted("""
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState
            {
                [TeProp] public int Count;
            }
            """);
    }

    [Fact]
    public void A_class_that_is_not_partial_is_refused_by_name()
    {
        AssertRefused("SSTE001", """
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public class Probe : TempEntState
            {
                [TeProp] public int Count;
            }
            """, "Probe");
    }

    [Fact]
    public void A_struct_that_is_not_partial_is_refused_and_a_partial_one_accepted()
    {
        AssertRefused("SSTE001", """
            [TempEntity("CEffectProbe", "DT_EffectProbe")]
            public struct Probe { [TeProp] public int Count; }
            """, "Probe");

        AssertAccepted("""
            [TempEntity("CEffectProbe", "DT_EffectProbe")]
            public partial struct Probe { [TeProp] public int Count; }
            """);
    }

    [Fact]
    public void A_type_holding_a_declaration_must_be_partial_too()
    {
        AssertRefused("SSTE001", """
            public class Holder
            {
                [TempEntity("CTEProbe", "DT_TEProbe")]
                public partial class Probe : TempEntState { [TeProp] public int Count; }
            }
            """, "Holder");

        AssertAccepted("""
            public partial class Holder
            {
                [TempEntity("CTEProbe", "DT_TEProbe")]
                private sealed partial class Probe : TempEntState { [TeProp] public int Count; }
            }
            """);
    }

    [Fact]
    public void A_generic_declaration_is_refused()
    {
        AssertRefused("SSTE002", """
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe<T> : TempEntState { [TeProp] public int Count; }
            """, "Probe");
    }

    [Fact]
    public void A_class_outside_a_chain_of_declarations_is_refused()
    {
        AssertRefused("SSTE003", """
            public class Plain : TempEntState
            {
                public override string ClassName => "";
                public override int ReadMembers(in TempEntReader te) => 0;
                public override int WriteMembers(ref TempEntWriter writer) => 0;
            }

            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : Plain { [TeProp] public int Count; }
            """, "Probe");

        AssertRefused("SSTE003", """
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe { [TeProp] public int Count; }
            """, "Probe");

        AssertAccepted("""
            [TempEntity("CTEBaseProbe", "DT_BaseProbe")]
            public partial class BaseProbe : TempEntState { [TeProp] public int First; }

            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : BaseProbe { [TeProp] public int Count; }
            """);
    }

    [Fact]
    public void A_name_that_cannot_be_derived_is_refused_and_one_that_can_is_accepted()
    {
        AssertRefused("SSTE004", """
            [TempEntity]
            public partial class Probe : TempEntState { [TeProp] public int Count; }
            """, "Probe");

        AssertAccepted("""
            [TempEntity]
            public partial class TeProbe : TempEntState { [TeProp] public int Count; }
            """);
    }

    [Theory]
    [InlineData("public int Count { get; set; }")]
    [InlineData("internal int Count;")]
    [InlineData("public static int Count;")]
    [InlineData("public readonly int Count;")]
    public void A_member_that_is_not_a_public_instance_field_is_refused_naming_it(string member)
    {
        AssertRefused("SSTE005", $$"""
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState { [TeProp] {{member}} }
            """, "Probe.Count");
    }

    [Theory]
    [InlineData("uint")]
    [InlineData("ushort")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("string")]
    [InlineData("int[]")]
    [InlineData("LongFlags")]
    [InlineData("Run")]
    public void A_type_with_no_wire_form_is_refused_naming_the_member(string type)
    {
        string refused = type == "Run" ? "SSTE008" : "SSTE006";

        AssertRefused(refused, $$"""
            public enum LongFlags : long { None }
            [InlineArray(4)] public struct Run { private Vector _element0; }
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState { [TeProp] public {{type}} Count; }
            """, "Probe.Count");
    }

    [Theory]
    [InlineData("Vector")]
    [InlineData("QAngle")]
    [InlineData("Color32")]
    [InlineData("float")]
    [InlineData("bool")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("int")]
    [InlineData("EntityHandle")]
    [InlineData("Small")]
    public void Every_type_with_a_wire_form_is_accepted(string type)
    {
        AssertAccepted($$"""
            public enum Small : byte { None }
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState { [TeProp] public {{type}} Count; }
            """);
    }

    [Theory]
    [InlineData("[TeProp(TePropKind.DataTable)] public Inner Count;")]
    [InlineData("[TeProp(TePropKind.DataTable, SubTable = \"DT_Inner\")] public Plain Count;")]
    [InlineData("[TeProp(TePropKind.DataTable, SubTable = \"DT_Inner\")] public int Count;")]
    public void A_nested_table_must_be_a_declared_struct_naming_its_table(string member)
    {
        AssertRefused("SSTE007", $$"""
            public struct Plain { public int X; }
            [TempEntity("CInner", "DT_Inner")] public partial struct Inner { [TeProp] public int X; }
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState { {{member}} }
            """, "Probe.Count");
    }

    [Fact]
    public void A_nested_table_is_accepted_when_it_is_one()
    {
        AssertAccepted("""
            [TempEntity("CInner", "DT_Inner")] public partial struct Inner { [TeProp] public int X; }
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState
            {
                [TeProp(TePropKind.DataTable, SubTable = "DT_Inner")] public Inner Count;
            }
            """);
    }

    [Theory]
    [InlineData("[TeProp(TePropKind.Array, Elems = 3, ElemStride = 12)] public Run Count;")]
    [InlineData("[TeProp(TePropKind.Array, Elems = 4, ElemStride = 4)] public Floats Count;")]
    [InlineData("[TeProp(TePropKind.Array, Elems = 4, ElemStride = 12)] public Vector Count;")]
    [InlineData("[TeProp(TePropKind.Vector, Elems = 4, ElemStride = 12)] public Run Count;")]
    public void An_array_must_be_an_inline_run_of_vectors_of_its_stated_length(string member)
    {
        AssertRefused("SSTE008", $$"""
            [InlineArray(4)] public struct Run { private Vector _element0; }
            [InlineArray(4)] public struct Floats { private float _element0; }
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState { {{member}} }
            """, "Probe.Count");
    }

    [Fact]
    public void An_inline_run_of_vectors_is_accepted()
    {
        AssertAccepted("""
            [InlineArray(4)] public struct Run { private Vector _element0; }
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial class Probe : TempEntState
            {
                [TeProp(TePropKind.Array, Elems = 4, ElemStride = 12)] public Run Count;
            }
            """);
    }

    private static string Nesting(int depth)
    {
        var text = new System.Text.StringBuilder("""[TempEntity("CL0", "DT_L0")] public partial struct L0 { [TeProp] public int X; }""");

        for (int i = 1; i <= depth; i++)
            text.Append($$"""

                [TempEntity("CL{{i}}", "DT_L{{i}}")] public partial struct L{{i}}
                {
                    [TeProp(TePropKind.DataTable, SubTable = "DT_L{{i - 1}}")] public L{{i - 1}} Count;
                }
                """);

        return text.ToString();
    }

    [Fact]
    public void Tables_nested_more_than_eight_deep_are_refused_and_eight_is_accepted()
    {
        Assert.Contains("SSTE009", Ids(Nesting(9)));

        AssertAccepted(Nesting(8));
    }

    [Fact]
    public void A_prop_on_a_type_with_no_declaration_is_refused_naming_it()
    {
        AssertRefused("SSTE010", """
            public partial class Probe { [TeProp] public int Count; }
            """, "Probe.Count");
    }

    //-------------------------------------------------------------------------
    // The shapes the inventory does not use, through what was generated in this project.
    //-------------------------------------------------------------------------

    private enum SmallKind : byte { Low = 1, High = 200 }

    private enum WideKind { Negative = -5, Large = 1 << 20 }

    [InlineArray(3)]
    private struct ThreePoints
    {
        private Vector _element0;
    }

    [TempEntity("CInnerProbe", "DT_InnerProbe", RegistersClass = false)]
    private partial struct InnerProbe
    {
        [TeProp(WireName = "m_iCount", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte Count;

        [TeProp(Bits = 16)]
        public short Signed;
    }

    [TempEntity("CTEBaseProbe", "DT_TEBaseProbe")]
    private partial class BaseProbe : TempEntState
    {
        [TeProp(WireName = "m_vecOrigin", Bits = -1, Flags = TeFlags.Coord)]
        public Vector Origin;
    }

    [TempEntity("CTEShapesProbe", "DT_TEShapesProbe")]
    private sealed partial class ShapesProbe : BaseProbe
    {
        [TeProp(Bits = 13)] public QAngle Facing;
        [TeProp] public Color32 Tint;
        [TeProp(WireName = "m_clrOther")] public Color32 Other;
        [TeProp(Bits = 1, Flags = TeFlags.Unsigned)] public bool Flag;
        [TeProp(Bits = 8, Flags = TeFlags.Unsigned)] public byte Unsigned8;
        [TeProp(Bits = 8)] public byte Signed8;
        [TeProp(Bits = 16, Flags = TeFlags.Unsigned)] public short Unsigned16;
        [TeProp(Bits = 16)] public short Signed16;
        [TeProp(Bits = 32)] public int Whole;
        [TeProp(BitsFrom = TeBits.NetworkedEHandle, Flags = TeFlags.Unsigned)] public EntityHandle Owner;
        [TeProp(BitsFrom = TeBits.NetworkedEHandle, Flags = TeFlags.Unsigned)] public EntityHandle Nobody;
        [TeProp(Bits = 8, Flags = TeFlags.Unsigned)] public SmallKind Small;
        [TeProp(Bits = 32)] public WideKind Wide;
        [TeProp(TePropKind.Array, Bits = -1, Flags = TeFlags.Coord, Elems = 3, ElemStride = 12)] public ThreePoints Points;
        [TeProp(TePropKind.DataTable, SubTable = "DT_InnerProbe")] public InnerProbe Inner;
        [TeProp(Bits = 0, Flags = TeFlags.NoScale)] public float Trailer;
    }

    private static TempEntType Shapes(RecordingLog? log = null)
    {
        var engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(log ?? new RecordingLog());
        registry.Register("Test", [typeof(BaseProbe), typeof(InnerProbe), typeof(ShapesProbe)], engine);

        return registry.Find("CTEShapesProbe")!;
    }

    private static ShapesProbe Filled()
    {
        var sent = new ShapesProbe
        {
            Origin = new Vector(1f, -2f, 3.5f),
            Facing = new QAngle(10f, 20f, 30f),
            Tint = new Color32(200, 201, 202, 203),
            Other = new Color32(1, 2, 3, 255),
            Flag = true,
            Unsigned8 = 200,
            Signed8 = 201,
            Unsigned16 = -2,
            Signed16 = -3,
            Whole = int.MinValue + 7,
            Owner = new EntityHandle(9u | (5u << 16)),
            Nobody = EntityHandle.None,
            Small = SmallKind.High,
            Wide = WideKind.Negative,
            Inner = new InnerProbe { Count = 250, Signed = -300 },
            Trailer = 0.25f,
        };

        sent.Points[0] = new Vector(1f, 2f, 3f);
        sent.Points[2] = new Vector(7f, 8f, 9f);

        return sent;
    }

    [Fact]
    public void Every_shape_comes_back_as_it_went_in()
    {
        TempEntType type = Shapes();
        ShapesProbe sent = Filled();

        ShapesProbe back = ShapesProbe.ReadFrom(new TempEntReader(type, sent.ToBlob(type)));

        Assert.Equal(sent.Origin, back.Origin);
        Assert.Equal(sent.Facing, back.Facing);
        Assert.Equal(sent.Tint, back.Tint);
        Assert.Equal(sent.Other, back.Other);
        Assert.True(back.Flag);
        Assert.Equal(200, back.Unsigned8);
        Assert.Equal(201, back.Signed8);
        Assert.Equal(-2, back.Unsigned16);
        Assert.Equal(-3, back.Signed16);
        Assert.Equal(int.MinValue + 7, back.Whole);
        Assert.Equal(sent.Owner, back.Owner);
        Assert.Equal(EntityHandle.None, back.Nobody);
        Assert.Equal(SmallKind.High, back.Small);
        Assert.Equal(WideKind.Negative, back.Wide);
        Assert.Equal(new Vector(1f, 2f, 3f), back.Points[0]);
        Assert.Equal(Vector.Zero, back.Points[1]);
        Assert.Equal(new Vector(7f, 8f, 9f), back.Points[2]);
        Assert.Equal(250, back.Inner.Count);
        Assert.Equal(-300, back.Inner.Signed);
        Assert.Equal(0.25f, back.Trailer);
    }

    [Fact]
    public void The_wire_values_are_the_literal_bytes_each_field_owns()
    {
        TempEntType type = Shapes();
        byte[] blob = Filled().ToBlob(type);

        int At(string wire) => type.Fields[wire].Offset;

        // The renamed member under its wire name, and the expanded colour under its own.
        Assert.Equal(1f, BitConverter.ToSingle(blob, At("m_vecOrigin")));
        Assert.Equal(200, blob[At("TintR")]);
        Assert.Equal(203, blob[At("TintA")]);
        Assert.Equal(255, blob[At("m_clrOtherA")]);

        Assert.Equal(1, blob[At("Flag")]);
        Assert.Equal(200, blob[At("Unsigned8")]);
        Assert.Equal(unchecked((short)-3), BitConverter.ToInt16(blob, At("Signed16")));
        Assert.Equal(9 | (5 << 11), BitConverter.ToInt32(blob, At("Owner")));
        Assert.Equal((1 << 21) - 1, BitConverter.ToInt32(blob, At("Nobody")));
        Assert.Equal(200, blob[At("Small")]);
        Assert.Equal(-5, BitConverter.ToInt32(blob, At("Wide")));
        Assert.Equal(7f, BitConverter.ToSingle(blob, At("Points") + 24));
        Assert.Equal(250, blob[At("m_iCount")]);
    }

    [Fact]
    public void The_generated_position_of_each_member_is_its_place_in_the_schema()
    {
        TempEntType type = Shapes();

        Assert.Equal(type.Slots.Select(s => s.Name), typeof(ShapesProbe).GetCustomAttribute<TeGeneratedAttribute>()!.Slots);

        // The stimulus: the shape the schema's list has to match is the one this class has.
        Assert.Equal(["m_vecOrigin", "Facing", "TintR"], type.Slots.Take(3).Select(s => s.Name));
        Assert.Equal(["Inner", "m_iCount", "Signed", "Trailer"], type.Slots.TakeLast(4).Select(s => s.Name));
    }

    [Fact]
    public void A_read_and_a_write_answer_how_many_members_they_handled()
    {
        TempEntType type = Shapes();
        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);
        ShapesProbe sent = Filled();

        // Origin, the fourteen of its own that are one member each, Trailer, and the nested
        // table's two in place of the table.
        const int Members = 1 + 14 + 1 + 2;

        Assert.Equal(Members, sent.WriteMembers(ref writer));
        Assert.Equal(Members, new ShapesProbe().ReadMembers(new TempEntReader(type, blob)));
    }

    [Fact]
    public void A_class_says_the_class_name_the_schema_pairs_on()
    {
        Assert.Equal("CTEShapesProbe", new ShapesProbe().ClassName);
        Assert.Equal("CTEBaseProbe", new BaseProbe().ClassName);
        Assert.Equal("CInnerProbe", InnerProbe.WireClassName);
    }

    //-------------------------------------------------------------------------
    // The startup check.
    //-------------------------------------------------------------------------

    private static TempEntRegistry EmptyRegistry(out RecordingEngine engine)
    {
        engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        return new TempEntRegistry(new RecordingLog());
    }

    /// <summary>
    /// A snippet built with the generator and loaded on its own, where no RegisterAll's scan of
    /// this process finds it: a declaration here that fails the check would fail every registry.
    /// </summary>
    private static Assembly Generated(string source)
    {
        CSharpGeneratorDriver.Create(new TempEntGenerator())
            .RunGeneratorsAndUpdateCompilation(Compilation(source), out Compilation after, out _);

        using var image = new MemoryStream();
        Assert.True(after.Emit(image).Success);

        return Assembly.Load(image.ToArray());
    }

    private const string OtherBase = """
        [TempEntity("CTEOtherBase", "DT_TEOtherBase", RegistersClass = false)]
        public partial struct OtherBase { [TeProp(Bits = 8)] public int InTheBase; }

        """;

    [Fact]
    public void A_schema_the_generated_positions_disagree_with_fails_at_registration_naming_both()
    {
        // The attribute names a base the C# chain does not have, so the two lists differ.
        Assembly built = Generated(OtherBase + """
            [TempEntity("CTEDisagrees", "DT_TEDisagrees", BaseTable = "DT_TEOtherBase")]
            public partial class Disagrees : TempEntState { [TeProp(Bits = 8)] public int Own; }
            """);

        TempEntRegistry registry = EmptyRegistry(out var engine);

        var refused = Assert.Throws<InvalidOperationException>(
            () => registry.Register("Test", [built.GetType("OtherBase")!, built.GetType("Disagrees")!], engine));

        Assert.Contains("CTEDisagrees", refused.Message);
        Assert.Contains("[Own]", refused.Message);
        Assert.Contains("[InTheBase, Own]", refused.Message);
    }

    [Fact]
    public void A_schema_they_agree_with_registers()
    {
        Assembly built = Generated(OtherBase + """
            [TempEntity("CTEAgrees", "DT_TEAgrees")]
            public partial class Agrees : TempEntState { [TeProp(Bits = 8)] public int Own; }
            """);

        TempEntRegistry registry = EmptyRegistry(out var engine);

        registry.Register("Test", [built.GetType("OtherBase")!, built.GetType("Agrees")!], engine);

        Assert.True(registry.Find("CTEAgrees") is { Registered: true });
    }

    [Fact]
    public void A_declaration_built_with_no_generator_fails_at_registration_saying_so()
    {
        var compilation = Compilation("""
            [TempEntity("CTEProbe", "DT_TEProbe")]
            public partial struct Probe { [TeProp] public int Count; }
            """);

        using var image = new MemoryStream();
        Assert.True(compilation.Emit(image).Success);

        Type probe = Assembly.Load(image.ToArray()).GetType("Probe")!;
        TempEntRegistry registry = EmptyRegistry(out var engine);

        var refused = Assert.Throws<InvalidOperationException>(() => registry.Register("Test", [probe], engine));

        Assert.Contains("CTEProbe", refused.Message);
        Assert.Contains("SourceSharp.Generators", refused.Message);
    }
}
