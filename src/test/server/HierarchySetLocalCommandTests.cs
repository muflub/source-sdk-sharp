using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>cs_sv_setlocal</c>, ported out of <c>shell_standalone_hierarchy.cpp</c>
/// (plan_shim.md §5 3b, lane 3c-entityshell).
///
/// <para>
/// <b>WHY THE COMMAND EXISTS AT ALL.</b> Parenting's failures are entirely
/// visual — a child in the wrong place, a child that stopped following, a child
/// following the wrong frame — and none of the three prints anything. This is
/// the door that drives one link by hand and prints both ends of it: the local
/// offset that went in, and the world position the composition made of it. A
/// child that moved the same distance as its parent has followed; a child whose
/// LOCAL offset is unchanged has followed correctly, and only the second
/// survives the parent rotating.
/// </para>
///
/// <para>
/// <b>WHY THESE FACTS AND NOT A LIVE RUN.</b> Every one of them is about what
/// the command asks the engine and what it says while doing it. The composition
/// itself is <c>Hierarchy</c>'s and is pinned by <c>HierarchyTests</c> against
/// <c>parent_frame_cases.txt</c>; a fact here that asserted on a resulting
/// position would be testing that instead, through a double that was told the
/// answer.
/// </para>
///
/// <para>
/// <b>The native spelling read its coordinates with <c>atof</c></b>, which
/// answers 0 for anything that is not a number — so <c>cs_sv_setlocal crate x
/// 0 0</c> moved the crate to its parent's origin and reported having done what
/// was asked. A typo in a coordinate is the likeliest thing to go wrong at this
/// console and it was the one thing that produced no complaint. Three of the
/// facts below are about that.
/// </para>
/// </summary>
public class HierarchySetLocalCommandTests
{
    private static readonly EntityHandle Child = new(42u);

    /// <summary>
    /// The command over a recorder, with the console captured.
    ///
    /// <para>
    /// EVERY REFUSAL ASSERTS ON THE LINE, never on the absence of a write.
    /// A refusal that returns in silence writes nothing either, and silence is
    /// what this whole console door is a fix for — so "nothing was written" is
    /// a check that cannot fail for the thing it is meant to catch.
    /// </para>
    /// </summary>
    private static (IReadOnlyList<EngineCall> Calls, string Console) Run(
        string args, EntityHandle? found = null, Vector? origin = null)
    {
        var engine = Fake();

        engine.Returns("Entities.FindByName", found ?? Child)
              .Returns("Entities.GetOrigin", origin ?? new Vector(10f, 20f, 30f))
              .Ignores("Entities.SetLocalTransform")
              .Ignores("Console.Msg");

        var command = new HierarchySetLocalCommand(engine, engine);

        command.Execute(args);

        // THE CONSOLE IS READ OUT OF THE RECORDER, not out of Log.Sink. The
        // command writes through the IEngineConsole it was given — which is
        // what lets a test see its words at all without a game — and a capture
        // of the static log sink would come back empty and read as "it said
        // nothing", which is the exact verdict these facts exist to distinguish
        // from a real silence.
        string console = string.Concat(engine.Calls
            .Where(c => c.Member == "Console.Msg")
            .Select(c => c.ArgAs<string>(0)));

        return (engine.Calls, console);
    }

    private static IEnumerable<EngineCall> Writes(IReadOnlyList<EngineCall> calls)
        => calls.Where(c => c.Member == "Entities.SetLocalTransform");

    //-------------------------------------------------------------------------
    // It is a command, under the name the native one had.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The NAME is load-bearing and is not this port's to choose. A console
    /// door renamed is a console door deleted, from the point of view of anyone
    /// with it in a script or in their fingers.
    /// </summary>
    [Fact]
    public void ItIsRegisteredAsCsSvSetlocal()
    {
        var attributes = typeof(HierarchySetLocalCommand)
            .GetCustomAttributes(typeof(ConCommandAttribute), false);

        Assert.Single(attributes);
        Assert.Equal("cs_sv_setlocal", ((ConCommandAttribute)attributes[0]).Name);
    }

    [Fact]
    public void ItCarriesHelpTextSoTheConsoleCanDescribeIt()
    {
        var attribute = (ConCommandAttribute)typeof(HierarchySetLocalCommand)
            .GetCustomAttributes(typeof(ConCommandAttribute), false)
            .Single();

        Assert.Contains("PARENT", attribute.Help);
    }

    //-------------------------------------------------------------------------
    // The write.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheThreeCoordinatesAreWrittenAsTheChildsLocalOrigin()
    {
        var (calls, _) = Run("crate 1 2 3");

        var write = Assert.Single(Writes(calls));

        Assert.Equal(Child, write.ArgAs<EntityHandle>(0));
        Assert.Equal(new Vector(1f, 2f, 3f), write.ArgAs<Vector?>(1));
    }

    /// <summary>
    /// ANGLES ARE LEFT ALONE, which is the difference between this command and
    /// a teleport. The crossing takes both halves as nullable so that either
    /// can be written without disturbing the other, and a command that passed
    /// a zero rotation would silently un-rotate every child it was used on.
    /// </summary>
    [Fact]
    public void TheAnglesHalfOfTheTransformIsNotWritten()
    {
        var (calls, _) = Run("crate 1 2 3");

        Assert.Null(Assert.Single(Writes(calls)).ArgAs<Vector?>(2));
    }

    /// <summary>
    /// The read-back is taken AFTER the write. The local offset that was
    /// accepted and the world position it produced are two different facts with
    /// the composition between them, and reading first would report the second
    /// as though it were the first — which is precisely the question the
    /// command is typed to answer.
    /// </summary>
    [Fact]
    public void TheWorldPositionIsReadAfterTheWriteAndNotBefore()
    {
        var (calls, _) = Run("crate 1 2 3");

        var names = calls.Select(c => c.Member).ToList();

        int wrote = names.IndexOf("Entities.SetLocalTransform");
        int read = names.IndexOf("Entities.GetOrigin");

        Assert.True(wrote >= 0, "the command wrote the local transform");
        Assert.True(read >= 0, "the command read the world origin back");
        Assert.True(wrote < read,
            $"the read-back must follow the write; order was {string.Join(", ", names)}");
    }

    [Fact]
    public void BothTheLocalOffsetAndTheWorldPositionAreReported()
    {
        var (_, console) = Run("crate 1 2 3", origin: new Vector(11f, 22f, 33f));

        Assert.Contains("local 1.00 2.00 3.00", console);
        Assert.Contains("world 11.00 22.00 33.00", console);
    }

    //-------------------------------------------------------------------------
    // The refusals, each said out loud.
    //-------------------------------------------------------------------------

    [Fact]
    public void TooFewArgumentsPrintTheUsageAndWriteNothing()
    {
        var (calls, console) = Run("crate 1 2");

        Assert.Contains("usage: cs_sv_setlocal", console);
        Assert.Empty(Writes(calls));
    }

    [Fact]
    public void ANameNothingAnswersToIsRefusedByName()
    {
        var (calls, console) = Run("nosuch 1 2 3", found: EntityHandle.None);

        Assert.Contains("nosuch", console);
        Assert.Empty(Writes(calls));
    }

    /// <summary>
    /// THE atof DEFECT, in the position it was most likely to go unnoticed: a
    /// bad FIRST coordinate with two good ones behind it. Under the native
    /// spelling this placed the child at x=0 and said so in the past tense.
    /// </summary>
    [Fact]
    public void ANonNumericFirstCoordinateIsRefusedRatherThanReadAsZero()
    {
        var (calls, console) = Run("crate x 2 3");

        Assert.Contains("'x' is not a number", console);
        Assert.Empty(Writes(calls));
    }

    [Fact]
    public void ANonNumericLastCoordinateIsRefusedToo()
    {
        var (calls, console) = Run("crate 1 2 zzz");

        Assert.Contains("'zzz' is not a number", console);
        Assert.Empty(Writes(calls));
    }

    /// <summary>
    /// A NEGATIVE AND A FRACTION ARE NUMBERS, which is the other half of the
    /// parse being right: a refusal rule strict enough to reject "-0.5" would
    /// have broken the command for every offset anyone actually types.
    /// </summary>
    [Fact]
    public void NegativeAndFractionalCoordinatesAreAccepted()
    {
        var (calls, _) = Run("crate -0.5 2.25 -3");

        Assert.Equal(new Vector(-0.5f, 2.25f, -3f),
                     Assert.Single(Writes(calls)).ArgAs<Vector?>(1));
    }

    /// <summary>
    /// Parsed in the INVARIANT culture, because a console is typed at in ASCII
    /// whatever the machine's locale is. Under a comma-decimal locale a
    /// culture-sensitive parse reads "1.5" as 15 — a ten-fold placement error,
    /// accepted silently, in a command whose entire job is to place something
    /// precisely.
    ///
    /// <para>
    /// Asserted through the parser directly rather than by switching the
    /// thread's culture inside a test: the suite runs xUnit facts in parallel
    /// within a class by default, and a fact that mutates ambient culture is a
    /// fact that can fail a different one.
    /// </para>
    /// </summary>
    [Fact]
    public void ADecimalPointIsADecimalPointWhateverTheLocale()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;

        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            Assert.True(HierarchySetLocalCommand.TryVector(
                ["crate", "1.5", "0", "0"], out Vector parsed, out _));

            Assert.Equal(1.5f, parsed.X);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
