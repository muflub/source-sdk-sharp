using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceSharp.Abi;
using SourceSharp.Generators;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The payload generator, driven over source text.
///
/// <para>
/// The subjects live in strings for the reason the old packer's did: a
/// <c>[UserMessage]</c> class anywhere the registry can reach takes a wire id.
/// The generator runs as a library over each snippet, and the snippet plus what
/// it wrote is compiled and RUN, so a round trip is Write and Read agreeing for
/// real rather than the generated text matching what this file expects.
/// </para>
///
/// <para>
/// Each rule is a PAIR: the declaration that must produce something and the
/// near-identical one that must not.
/// </para>
/// </summary>
public class UserMessagePayloadGeneratorTests
{
    //-------------------------------------------------------------------------
    // What opts in.
    //-------------------------------------------------------------------------

    [Fact]
    public void AHandlerNamingAPayloadGetsItsWireCode()
    {
        string written = Generated("""
            public partial struct Probe { public partial int Id { get; set; } }

            [UserMessage("Probe")]
            public sealed class ProbeMessage : IUserMessage<Probe>
            {
                public void OnReceived(Probe message) { }
            }
            """);

        Assert.Contains("public int Write(global::System.Span<byte> into)", written);
        Assert.Contains("public static Probe Read(ref global::SourceSharp.Abi.UserMessageReader reader)", written);
        Assert.Contains("Handler => typeof(global::ProbeMessage)", written);
    }

    [Fact]
    public void ADeclarationWithNoPayloadGetsNothing()
    {
        // A message only native code reads: no IUserMessage<T>, no payload,
        // nothing generated and nothing diagnosed, whatever it holds.
        var (written, ids) = Run("""
            [UserMessage("Probe")]
            public sealed partial class ProbeMessage
            {
                public System.Collections.Generic.List<int> Contacts { get; set; } = new();
            }
            """);

        Assert.True(written.Length == 0, "no payload, no code; got:\n" + written);
        Assert.Empty(ids);
    }

    //-------------------------------------------------------------------------
    // Every member kind, written and read back for real.
    //-------------------------------------------------------------------------

    [Fact]
    public void EverySupportedMemberTypeRoundTrips()
    {
        var probe = Compiled("""
            public partial struct EveryKind
            {
                public partial bool    Flag    { get; set; }
                public partial byte    Small   { get; set; }
                public partial short   Medium  { get; set; }
                public partial ushort  UMedium { get; set; }
                public partial int     Large   { get; set; }
                public partial uint    ULarge  { get; set; }
                public partial float   Real    { get; set; }
                public partial string  Text    { get; set; }
                public partial Vector  Where   { get; set; }
                public partial Color32 Tint    { get; set; }
                [UserMessageArray(MaxCount = 4)]
                public partial string[] Lines  { get; set; }
                [UserMessageArray(MaxCount = 4)]
                public partial byte[]  Bytes   { get; set; }
            }

            [UserMessage("EveryKind")]
            public sealed class EveryKindMessage : IUserMessage<EveryKind>
            {
                public void OnReceived(EveryKind message) { }
            }
            """, "EveryKind");

        object sent = Activator.CreateInstance(probe)!;
        sent = With(sent, "Flag", true);
        sent = With(sent, "Small", (byte)200);
        sent = With(sent, "Medium", (short)-1234);
        sent = With(sent, "UMedium", (ushort)60000);
        sent = With(sent, "Large", 1234567);
        sent = With(sent, "ULarge", 0xDEADBEEFu);
        sent = With(sent, "Real", 0.5f);
        sent = With(sent, "Text", "hello");
        sent = With(sent, "Where", new Vector(1f, -2f, 3f));
        sent = With(sent, "Tint", new Color32(1, 2, 3, 4));
        sent = With(sent, "Lines", new[] { "a", "bc" });
        sent = With(sent, "Bytes", new byte[] { 9, 8, 7 });

        byte[] payload = Write(sent);
        Assert.Equal(1 + 1 + 2 + 2 + 4 + 4 + 4 + 6 + 12 + 4 + (1 + 2 + 3) + (1 + 3), payload.Length);

        var (got, overran) = Read(probe, payload);
        Assert.False(overran, "a payload the same build wrote reads whole");

        Assert.True((bool)Get(got, "Flag"));
        Assert.Equal((byte)200, Get(got, "Small"));
        Assert.Equal((short)-1234, Get(got, "Medium"));
        Assert.Equal((ushort)60000, Get(got, "UMedium"));
        Assert.Equal(1234567, Get(got, "Large"));
        Assert.Equal(0xDEADBEEFu, Get(got, "ULarge"));
        Assert.Equal(0.5f, Get(got, "Real"));
        Assert.Equal("hello", Get(got, "Text"));
        Assert.Equal(new Vector(1f, -2f, 3f), Get(got, "Where"));
        Assert.Equal(new Color32(1, 2, 3, 4), Get(got, "Tint"));
        Assert.Equal(new[] { "a", "bc" }, (string[])Get(got, "Lines"));
        Assert.Equal(new byte[] { 9, 8, 7 }, (byte[])Get(got, "Bytes"));
    }

    [Fact]
    public void AClassPayloadRoundTripsAndStartsEmpty()
    {
        var probe = Compiled("""
            public partial class Line
            {
                public partial string Text { get; set; }
                public partial int    Id   { get; private set; }
            }

            [UserMessage("Line")]
            public sealed class LineMessage : IUserMessage<Line>
            {
                public void OnReceived(Line message) { }
            }
            """, "Line");

        object empty = Activator.CreateInstance(probe)!;
        Assert.Equal("", Get(empty, "Text"));

        object sent = With(Activator.CreateInstance(probe)!, "Text", "x");
        var (got, overran) = Read(probe, Write(sent));
        Assert.False(overran);
        Assert.Equal("x", Get(got, "Text"));
    }

    [Fact]
    public void TheLayoutIsDeclarationOrder()
    {
        // Two payloads whose members differ only in ORDER: the same length,
        // so what must differ is which byte is which.
        byte[] straight = Write(With(With(Activator.CreateInstance(Compiled(Ordered("First", "Second"), "Order"))!,
                                          "First", (byte)0xAA), "Second", (byte)0xBB));
        byte[] swapped  = Write(With(With(Activator.CreateInstance(Compiled(Ordered("Second", "First"), "Order"))!,
                                          "First", (byte)0xAA), "Second", (byte)0xBB));

        Assert.Equal(new byte[] { 0xAA, 0xBB }, straight);
        Assert.Equal(new byte[] { 0xBB, 0xAA }, swapped);
    }

    private static string Ordered(string a, string b) => $$"""
        public partial struct Order
        {
            public partial byte {{a}} { get; set; }
            public partial byte {{b}} { get; set; }
        }

        [UserMessage("Order")]
        public sealed class OrderMessage : IUserMessage<Order>
        {
            public void OnReceived(Order message) { }
        }
        """;

    //-------------------------------------------------------------------------
    // The limits.
    //-------------------------------------------------------------------------

    [Fact]
    public void AStringOverItsMaxLengthIsCutOnWrite()
    {
        var probe = Compiled("""
            public partial struct Capped
            {
                [UserMessageString(MaxLength = 4)]
                public partial string Text { get; set; }
                public partial string Free { get; set; }
            }

            [UserMessage("Capped")]
            public sealed class CappedMessage : IUserMessage<Capped>
            {
                public void OnReceived(Capped message) { }
            }
            """, "Capped");

        object sent = With(With(Activator.CreateInstance(probe)!, "Text", "abcdefgh"), "Free", "abcdefgh");
        var (got, _) = Read(probe, Write(sent));

        Assert.Equal("abcd", Get(got, "Text"));
        Assert.Equal("abcdefgh", Get(got, "Free"));

        object exact = With(Activator.CreateInstance(probe)!, "Text", "abcd");
        Assert.Equal("abcd", Get(Read(probe, Write(exact)).Message, "Text"));
    }

    [Fact]
    public void AnArrayOverItsMaxCountIsCutOnWriteAndRefusedOnRead()
    {
        var probe = Compiled("""
            public partial struct Listed
            {
                [UserMessageArray(MaxCount = 2)]
                public partial byte[] Items { get; set; }
            }

            [UserMessage("Listed")]
            public sealed class ListedMessage : IUserMessage<Listed>
            {
                public void OnReceived(Listed message) { }
            }
            """, "Listed");

        byte[] cut = Write(With(Activator.CreateInstance(probe)!, "Items", new byte[] { 1, 2, 3 }));
        Assert.Equal(new byte[] { 2, 1, 2 }, cut);

        Assert.False(Read(probe, [2, 1, 2]).Overran, "a count at the most reads");
        Assert.True(Read(probe, [3, 1, 2, 3]).Overran, "a count over the most is refused");
    }

    [Fact]
    public void AShortPayloadOverruns()
    {
        var probe = Compiled(Ordered("First", "Second"), "Order");
        Assert.False(Read(probe, [1, 2]).Overran);
        Assert.True(Read(probe, [1]).Overran);
    }

    //-------------------------------------------------------------------------
    // The bridge: IUserMessage<T>'s dispatch.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheBridgeDispatchesAWholePayloadAndRefusesAnOverrun()
    {
        var handlerType = Compiled("""
            public partial struct Pair
            {
                public partial int A { get; set; }
                public partial int B { get; set; }
            }

            [UserMessage("Pair")]
            public sealed class PairMessage : IUserMessage<Pair>
            {
                public int Calls;
                public int LastB;
                public void OnReceived(Pair message) { Calls++; LastB = message.B; }
            }
            """, "PairMessage");

        var handler = (IUserMessage)Activator.CreateInstance(handlerType)!;
        int Calls() => (int)handlerType.GetField("Calls")!.GetValue(handler)!;

        handler.OnReceived(new UserMessageReader(new byte[] { 1, 0, 0, 0, 2, 0 }));
        Assert.Equal(0, Calls());

        handler.OnReceived(new UserMessageReader(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 }));
        Assert.Equal(1, Calls());
        Assert.Equal(2, (int)handlerType.GetField("LastB")!.GetValue(handler)!);
    }

    //-------------------------------------------------------------------------
    // The diagnostics, each paired with the declaration that is accepted.
    //-------------------------------------------------------------------------

    private static string WithHandler(string payloadBody, string attribute = "[UserMessage(\"Probe\")]") => $$"""
        public partial struct Probe
        {
            {{payloadBody}}
        }

        {{attribute}}
        public sealed class ProbeMessage : IUserMessage<Probe>
        {
            public void OnReceived(Probe message) { }
        }
        """;

    [Fact]
    public void AnUnsupportedMemberTypeIsRefused()
    {
        Assert.Contains("SSMSG001", Diagnostics(WithHandler("public partial System.Collections.Generic.List<int> Contacts { get; set; }")));
        Assert.Contains("SSMSG001", Diagnostics(WithHandler("public partial long Big { get; set; }")));
        Assert.Empty(Diagnostics(WithHandler("public partial int Contacts { get; set; }")));
    }

    [Fact]
    public void AMemberWithOnlyAGetterIsRefused()
    {
        Assert.Contains("SSMSG002", Diagnostics(WithHandler("public partial int Contacts { get; }")));
        Assert.Empty(Diagnostics(WithHandler("public partial int Contacts { get; private set; }")));
    }

    [Fact]
    public void TwoHandlersForOnePayloadAreRefused()
    {
        string one = """
            public partial struct Probe { public partial int Id { get; set; } }

            [UserMessage("A")]
            public sealed class AMessage : IUserMessage<Probe> { public void OnReceived(Probe message) { } }
            """;

        Assert.Empty(Diagnostics(one));
        Assert.Contains("SSMSG003", Diagnostics(one + """

            [UserMessage("B")]
            public sealed class BMessage : IUserMessage<Probe> { public void OnReceived(Probe message) { } }
            """));
    }

    [Fact]
    public void AHandlerWithTwoPayloadsIsRefused()
    {
        Assert.Contains("SSMSG004", Diagnostics("""
            public partial struct P1 { public partial int Id { get; set; } }
            public partial struct P2 { public partial int Id { get; set; } }

            [UserMessage("Both")]
            public sealed class BothMessage : IUserMessage<P1>, IUserMessage<P2>
            {
                public void OnReceived(P1 message) { }
                public void OnReceived(P2 message) { }
            }
            """));
    }

    [Fact]
    public void APayloadFromAnotherAssemblyIsRefused()
    {
        // JalopyRadar is SourceSharp.Shared's, already generated there.
        Assert.Contains("SSMSG005", Diagnostics("""
            [UserMessage("Foreign")]
            public sealed class ForeignMessage : IUserMessage<SourceSharp.Shared.JalopyRadar>
            {
                public void OnReceived(SourceSharp.Shared.JalopyRadar message) { }
            }
            """));
    }

    [Fact]
    public void APayloadThatIsNotPartialIsRefused()
    {
        string declared = """
            public {0}struct Probe {{ }}

            [UserMessage("Probe")]
            public sealed class ProbeMessage : IUserMessage<Probe> {{ public void OnReceived(Probe message) {{ }} }}
            """;

        Assert.Contains("SSMSG006", Diagnostics(string.Format(declared, "")));
        Assert.DoesNotContain("SSMSG006", Diagnostics(string.Format(declared, "partial ")));
    }

    [Fact]
    public void AnArrayWithoutItsMostIsRefused()
    {
        Assert.Contains("SSMSG007", Diagnostics(WithHandler("public partial int[] Items { get; set; }")));
        Assert.Contains("SSMSG007", Diagnostics(WithHandler("[UserMessageArray(MaxCount = 256)] public partial int[] Items { get; set; }")));
        Assert.Empty(Diagnostics(WithHandler("[UserMessageArray(MaxCount = 254)] public partial byte[] Items { get; set; }")));
    }

    [Fact]
    public void APayloadOverTheCapIsRefused()
    {
        // 21 Vectors are 252 bytes, 22 are 264: the engine's limit is 255.
        static string Vectors(int n) => string.Join("\n", Enumerable.Range(0, n).Select(i => $"public partial Vector V{i} {{ get; set; }}"));

        Assert.Empty(Diagnostics(WithHandler(Vectors(21))));
        Assert.Contains("SSMSG008", Diagnostics(WithHandler(Vectors(22))));

        // An array counts at its most.
        Assert.Contains("SSMSG008", Diagnostics(WithHandler("[UserMessageArray(MaxCount = 64)] public partial int[] Items { get; set; }")));
    }

    [Fact]
    public void AFixedSizeThatDisagreesWithThePayloadIsRefused()
    {
        string body = "public partial byte Command { get; set; }\npublic partial float Amplitude { get; set; }";

        Assert.Empty(Diagnostics(WithHandler(body, "[UserMessage(\"Probe\", Size = 5)]")));
        Assert.Contains("SSMSG009", Diagnostics(WithHandler(body, "[UserMessage(\"Probe\", Size = 4)]")));
        Assert.Contains("SSMSG009", Diagnostics(WithHandler("public partial string Text { get; set; }", "[UserMessage(\"Probe\", Size = 1)]")));
    }

    [Fact]
    public void SettableStateThatIsNotAMemberIsRefused()
    {
        Assert.Contains("SSMSG010", Diagnostics(WithHandler("public int Contacts { get; set; }")));
        Assert.Contains("SSMSG010", Diagnostics(WithHandler("public int Contacts;")));
        Assert.Empty(Diagnostics(WithHandler("public partial int Contacts { get; set; }\npublic int Twice => Contacts * 2;")));
    }

    //-------------------------------------------------------------------------
    // Driving the generator and the code it wrote.
    //-------------------------------------------------------------------------

    private static object With(object target, string property, object value)
    {
        target.GetType().GetProperty(property)!.SetValue(target, value);
        return target;
    }

    private static object Get(object target, string property)
        => target.GetType().GetProperty(property)!.GetValue(target)!;

    private static byte[] Write(object payload)
        => (byte[])typeof(UserMessagePayloadGeneratorTests).GetMethod(nameof(WriteVia), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
               .MakeGenericMethod(payload.GetType()).Invoke(null, [payload])!;

    private static (object Message, bool Overran) Read(Type payload, byte[] bytes)
        => ((object, bool))typeof(UserMessagePayloadGeneratorTests).GetMethod(nameof(ReadVia), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
               .MakeGenericMethod(payload).Invoke(null, [bytes])!;

    private static byte[] WriteVia<T>(T payload) where T : IUserMessagePayload<T>
    {
        var buffer = new byte[512];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the probe payload fits 512 bytes");
        return buffer[..n];
    }

    private static (object, bool) ReadVia<T>(byte[] bytes) where T : IUserMessagePayload<T>
    {
        var reader = new UserMessageReader(bytes);
        T message = T.Read(ref reader);
        return (message!, reader.Overran);
    }

    private static List<string> Diagnostics(string source) => Run(source).Ids;

    private static string Generated(string source) => Run(source).Written;

    private static (string Written, List<string> Ids) Run(string source)
    {
        var (_, result) = Drive(source);
        return (string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString())),
                result.Diagnostics.Select(d => d.Id).ToList());
    }

    /// <summary>The snippet plus what the generator wrote for it, compiled and loaded.</summary>
    private static Type Compiled(string source, string typeName)
    {
        var (compilation, result) = Drive(source);
        Assert.Empty(result.Diagnostics);

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
            "UserMessagePayloadTests.Subject." + Guid.NewGuid().ToString("N"),
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new UserMessagePayloadGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        return (updated, driver.GetRunResult().Results.Single());
    }

    private static readonly MetadataReference[] References =
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(UserMessageAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SourceSharp.Shared.JalopyRadar).Assembly.Location))
            .Distinct()
            .ToArray();
}
