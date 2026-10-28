using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceSharp.Abi;
using SourceSharp.Generators;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The generators' diagnostics, driven over source text.
///
/// <para>
/// WHY THIS EXISTS AND WHY IT LOOKS LIKE THIS. Every other check in this runner
/// declares the thing it is checking and asks a question about it. A build-time
/// diagnostic cannot be checked that way: the code that provokes it does not
/// compile, so there would be no test binary to run it from. The result is that
/// this tree has shipped eleven generator diagnostics — SSVAR001 through
/// SSNET006 — none of which has ever been seen to fire, and four checks
/// elsewhere in this project have turned out to be incapable of failing. A rule
/// nobody has watched fail is a comment.
/// </para>
///
/// <para>
/// So the generator is run as a library over source text held in strings, and
/// the diagnostics it reports are compared against what the text deserves. Each
/// case comes in a PAIR: the declaration that must be refused, and the nearly
/// identical one that must be accepted. A test that only ever sees red is as
/// weak as one that only ever sees green.
/// </para>
///
/// <para>
/// This is the one file in the runner that needs Roslyn — see the .csproj for
/// the trade that was made and why the alternative did not survive it.
/// </para>
/// </summary>
public class GeneratorDiagnosticTests
{

    //-------------------------------------------------------------------------

    /// <summary>
    /// THE CONTROL, and it goes first. Everything below is worth nothing if the
    /// harness cannot observe a diagnostic, or reports one for clean source. Two
    /// declarations that differ in one word, against a rule that predates this
    /// step and is known to work.
    /// </summary>
    [Fact]
    public void TheHarnessCanSeeADiagnosticAtAll()
    {
        var clean = VarsDiagnostics("""
            [ScriptVars]
            public partial class CleanVars
            {
                public int Speed { get; set; }
            }
            """);

        Assert.True(clean.Count == 0, "a clean var set reports nothing at all");

        // A map value cannot be read as a Dictionary, and SSVAR002 has said so
        // since before this step.
        var bad = VarsDiagnostics("""
            [ScriptVars]
            public partial class BadVars
            {
                public System.Collections.Generic.List<int> Speed { get; set; } = new();
            }
            """);

        Assert.True(bad.Contains("SSVAR002"), "and an unsupported key type reports SSVAR002, so the harness works");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// The rule Step 12 is for: an enum used as a map key must number every
    /// member explicitly, because a map key is a NUMBER A MAPPER TYPED and
    /// implicit ordinals mean inserting a member silently repoints every
    /// existing map.
    /// </summary>
    [Fact]
    public void TheExplicitValueRuleFires()
    {
        // Nothing numbered at all: the shape somebody writes without thinking
        // about it, and the one that is quietly wrong.
        var implicitAll = VarsDiagnostics("""
            public enum Weather { Rain, Snow, Ash, Snowfall }

            [ScriptVars]
            public partial class WeatherVars
            {
                public Weather PrecipType { get; set; }
            }
            """);

        Assert.True(implicitAll.Contains("SSVAR012"), "an enum with no member numbered is refused (SSVAR012)");

        // THE SNEAKY ONE, and the reason the rule is per member rather than per
        // enum. The first member is numbered, so the declaration LOOKS
        // deliberate -- and every member after it still moves when one is
        // inserted above it.
        var implicitTail = VarsDiagnostics("""
            public enum Weather { Rain = 0, Snow, Ash, Snowfall }

            [ScriptVars]
            public partial class WeatherVars
            {
                public Weather PrecipType { get; set; }
            }
            """);

        Assert.True(implicitTail.Contains("SSVAR012"), "and so is one where only the first member is numbered");

        // The message has to name the property, or a set of twenty keys sends
        // the reader to a diff tool.
        Assert.True(
            VarsMessages("""
                public enum Weather { Rain, Snow }

                [ScriptVars]
                public partial class WeatherVars
                {
                    public Weather PrecipType { get; set; }
                }
                """).Any(m => m.Contains("PrecipType") && m.Contains("Weather")),
            "the message names the key that caused it");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// The other half of the pair. A rule that fires on everything is a rule
    /// that will be turned off.
    /// </summary>
    [Fact]
    public void AndDoesNotFireOnWhatItShouldNot()
    {
        var explicitAll = VarsDiagnostics("""
            public enum Weather { Rain = 0, Snow = 1, Ash = 2, Snowfall = 3 }

            [ScriptVars]
            public partial class WeatherVars
            {
                public Weather PrecipType { get; set; }
            }
            """);

        Assert.True(explicitAll.Count == 0, "an enum numbering every member is accepted, and reports nothing");

        // A flags enum, which is what the five fields this step converted are.
        var flags = VarsDiagnostics("""
            [System.Flags]
            public enum Ignoring
            {
                None      = 0b0000_0000,
                Players   = 0b0000_0001,
                NPCs      = 0b0000_0010,
                Pushables = 0b0000_0100,
            }

            [ScriptVars]
            public partial class BrushVars
            {
                public Ignoring InputFilter { get; set; }
            }
            """);

        Assert.True(flags.Count == 0, "nor does a flags enum whose bits are all written out");

        // An ALIAS is written as a reference to another member, which is an
        // explicit value -- it just is not a literal. The rule must not read
        // "explicit" as "a number".
        var aliased = VarsDiagnostics("""
            public enum Weather { Rain = 0, None = Rain, Snow = 1 }

            [ScriptVars]
            public partial class WeatherVars
            {
                public Weather PrecipType { get; set; }
            }
            """);

        Assert.True(aliased.Count == 0, "nor an alias, which is an explicit value that is not a literal");

        // AND NOT ON AN ENUM THAT IS NOT A MAP KEY. The rule is about what a
        // mapper types, so an enum declared beside a var set and used nowhere in
        // it is nobody's business.
        var elsewhere = VarsDiagnostics("""
            public enum Weather { Rain, Snow }

            [ScriptVars]
            public partial class WeatherVars
            {
                public int PrecipType { get; set; }
            }
            """);

        Assert.True(elsewhere.Count == 0, "nor an enum in the same file that no key uses");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// What the seam actually emits, because a diagnostic that fires and a
    /// dispatch that does not call the enum parse would be half the step.
    /// </summary>
    [Fact]
    public void TheEnumSeamIsWhatGetsGenerated()
    {
        string generated = VarsSource("""
            public enum Weather { Rain = 0, Snow = 1 }

            [ScriptVars]
            public partial class WeatherVars
            {
                public Weather PrecipType { get; set; }
            }
            """);

        Assert.True(generated.Contains("ScriptedVarParse.Enum<Weather>(value, out var parsed)"), "the dispatch parses the key through the generic enum parse");

        Assert.True(generated.Contains("case \"preciptype\":"), "and the case label is the lowered key, as for every other kind");

        // The unreadable path still names the type the author declared, because
        // that line is read by a person looking at a map that will not load.
        Assert.True(generated.Contains("ReportUnreadable(\"PrecipType\", value, \"Weather\")"), "and an unreadable value is reported as the enum, not as an int");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// SSVAR005, the rule written after a <c>func_button</c> shipped with an
    /// <c>OnDamaged</c> output that could never fire. The field hid
    /// <c>ScriptedEntity.OnDamaged</c>, the damage virtual, so the entity could
    /// not override the handler and nothing ever reached the output.
    /// </summary>
    [Fact]
    public void AnOutputThatHidesABaseMemberIsRefused()
    {
        var collides = VarsDiagnostics("""
            [ScriptedEntity("test_button")]
            public class RedButton : ScriptedEntity
            {
                public readonly Output OnDamaged = new();
            }
            """);

        Assert.True(collides.Contains("SSVAR005"),
            "an output named for a base virtual is refused (SSVAR005)");

        // The message has to name both halves and the way out, because the
        // reader is looking at a line that looks completely ordinary.
        Assert.True(
            VarsMessages("""
                [ScriptedEntity("test_button")]
                public class RedButton : ScriptedEntity
                {
                    public readonly Output OnDamaged = new();
                }
                """).Any(m => m.Contains("OnDamaged")
                              && m.Contains("ScriptedEntity")
                              && m.Contains("[OutputName(\"OnDamaged\")]")),
            "the message names the output, what it hides, and the one-word fix");
    }

    /// <summary>
    /// THE REASON THIS IS NOT LEFT TO CS0108. The compiler reports the same
    /// collision, as a warning, and offers <c>new</c> as the fix — which keeps
    /// the collision and removes the only sign of it. SSVAR005 has to survive
    /// the keyword that silences the compiler, or it adds nothing.
    /// </summary>
    [Fact]
    public void AndTheNewKeywordDoesNotSilenceIt()
    {
        var silenced = VarsDiagnostics("""
            [ScriptedEntity("test_button")]
            public class RedButton : ScriptedEntity
            {
                public new readonly Output OnDamaged = new();
            }
            """);

        Assert.True(silenced.Contains("SSVAR005"),
            "the new keyword silences CS0108 and not this, because it is the wrong fix");
    }

    /// <summary>
    /// The other half of the pair. The fix the message asks for has to be
    /// accepted, or the rule sends people somewhere it will not let them go.
    /// </summary>
    [Fact]
    public void AndAcceptsTheOutputThatNamesItself()
    {
        // What FuncButton, FuncPhysbox and FuncBreakableSurf all do: the field
        // takes a free name, the MAP-FACING name is unchanged.
        var named = VarsDiagnostics("""
            [ScriptedEntity("test_button")]
            public class GreenButton : ScriptedEntity
            {
                [OutputName("OnDamaged")] public readonly Output Damaged = new();
            }
            """);

        Assert.True(!named.Contains("SSVAR005"),
            "an output that names itself and leaves the C# name free is fine");

        // And the ordinary case, which is most of them: a name nothing else has.
        var ordinary = VarsDiagnostics("""
            [ScriptedEntity("test_button")]
            public class GreenButton : ScriptedEntity
            {
                public readonly Output OnPressed = new();
            }
            """);

        Assert.True(ordinary.Count == 0, "and an output whose name nothing has taken reports nothing at all");
    }

    //-------------------------------------------------------------------------
    // Driving the generator.
    //-------------------------------------------------------------------------

    /// <summary>The diagnostic IDs the var-set generator reports for a snippet.</summary>
    private static List<string> VarsDiagnostics(string source)
        => Drive(source).Diagnostics.Select(d => d.Id).ToList();

    private static List<string> VarsMessages(string source)
        => Drive(source).Diagnostics.Select(d => d.GetMessage()).ToList();

    /// <summary>Everything the var-set generator wrote for a snippet, as one string.</summary>
    private static string VarsSource(string source)
        => string.Join("\n", Drive(source).GeneratedSources.Select(s => s.SourceText.ToString()));

    private static GeneratorRunResult Drive(string source)
    {
        // `using SourceSharp.Abi;` on every snippet, so the cases above are the
        // declaration and nothing else.
        var tree = CSharpSyntaxTree.ParseText("using SourceSharp.Abi;\n" + source);

        var compilation = CSharpCompilation.Create(
            "GeneratorDiagnosticTests.Subject",
            new[] { tree },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The COMPILATION's own errors are not checked and must not be: a
        // snippet declaring a partial class the generator has not completed yet
        // does not compile by construction, which is the whole reason this file
        // drives the generator instead of declaring the code.
        var driver = CSharpGeneratorDriver.Create(new ScriptedVarsGenerator())
                                          .RunGenerators(compilation);

        return driver.GetRunResult().Results.Single();
    }

    /// <summary>
    /// Every assembly already loaded that has a file behind it. Blunt, and right
    /// for the job: what a snippet has to resolve is <c>[ScriptVars]</c> and the
    /// framework, and both are in this process already.
    /// </summary>
    private static readonly MetadataReference[] References =
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ScriptVarsAttribute).Assembly.Location))
            .Distinct()
            .ToArray();
}
