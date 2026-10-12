using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Guards the scanning that <see cref="ManagedInitFatalTests"/> leans on.
///
/// <para>
/// Every one of those checks reads a slice of C++ and counts something in it,
/// and each scanner can fail in the direction that PASSES: a body matcher that
/// stops at the first inner brace returns a short slice with no returns in it
/// and the "no way out" check is vacuous; a call counter that misses a call
/// makes a "must be fatal on all three paths" check read zero and — if the
/// comparison were the wrong way round — pass. So each one is driven here
/// against a literal snippet, in both directions: what must be found, and what
/// must not.
/// </para>
///
/// <para>
/// The snippets are shaped like the real thing on purpose — nested braces, an
/// <c>OVERRIDE</c> between the parens and the brace, a call inside a comment —
/// because those are the three ways a naive matcher goes wrong here.
/// </para>
/// </summary>
public class ManagedInitFatalScanTests
{
    // ------------------------------------------------------------------
    // BodyAfter
    // ------------------------------------------------------------------

    [Fact]
    public void BodyAfterMatchesASignatureWithTrailingTokens()
    {
        // virtual bool Init() OVERRIDE — the closing paren and the brace have
        // a word between them, which is what defeats the parity file's matcher.
        string? body = ManagedInitFatalTests.BodyAfter(
            "class C {\n\tvirtual bool Init() OVERRIDE\n\t{\n\t\treturn true;\n\t}\n};\n",
            "virtual bool Init()");

        Assert.True(body is not null, "the body of Init() must be found");
        Assert.Contains("return true;", body!, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyAfterKeepsNestedBraces()
    {
        string? body = ManagedInitFatalTests.BodyAfter(
            "void F()\n{\n\tif ( a )\n\t{\n\t\tinner();\n\t}\n\ttail();\n}\n", "void F()");

        Assert.True(body is not null);
        Assert.Contains("inner();", body!, StringComparison.Ordinal);
        Assert.Contains("tail();", body!, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyAfterStopsAtTheMatchingBrace()
    {
        string? body = ManagedInitFatalTests.BodyAfter(
            "void F()\n{\n\tmine();\n}\n\nvoid G()\n{\n\ttheirs();\n}\n", "void F()");

        Assert.True(body is not null);
        Assert.Contains("mine();", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("theirs();", body!, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyAfterAnswersNullForAFunctionThatIsNotThere()
    {
        Assert.Null(ManagedInitFatalTests.BodyAfter("void F() {}\n", "void Missing()"));
    }

    // ------------------------------------------------------------------
    // CallCount
    // ------------------------------------------------------------------

    [Fact]
    public void CallCountCountsEveryCall()
    {
        Assert.Equal(3, ManagedInitFatalTests.CallCount(
            "\tX( 1 );\n\tX( 2 );\n\tif ( y ) X( 3 );\n", "X"));
    }

    [Fact]
    public void CallCountIgnoresACommentedOutCall()
    {
        // A path someone commented out is not a path.
        Assert.Equal(1, ManagedInitFatalTests.CallCount(
            "\t// X( 1 );\n\t/* X( 2 ); */\n\tX( 3 );\n", "X"));
    }

    [Fact]
    public void CallCountDoesNotMatchANameItIsTheTailOf()
    {
        // The boundary that matters is the one in FRONT of the name, because
        // that is the side the guard is on: `\bError\s*\(`. A snippet like
        // `SetErrorState( 1 )` does NOT exercise it — "Error" there is not
        // followed by a paren, so the match fails for an unrelated reason and
        // the check passes whether or not the guard is present. `MyError( 1 )`
        // is the input that separates them.
        Assert.Equal(0, ManagedInitFatalTests.CallCount("\tMyError( 1 );\n", "Error"));

        // ...and the same name standing alone is still found, so the check
        // above cannot pass by matching nothing at all.
        Assert.Equal(1, ManagedInitFatalTests.CallCount("\tError( 1 );\n", "Error"));
    }

    // ------------------------------------------------------------------
    // BetweenGateAndClose
    // ------------------------------------------------------------------

    [Fact]
    public void BetweenGateAndCloseSkipsTheGatesOwnReturn()
    {
        // The gate's `return true;` is the idempotent no-op, not a degrade
        // path, so it must fall OUTSIDE the slice — otherwise the "no returns"
        // check could never pass and would be rewritten until it did.
        string? middle = ManagedInitFatalTests.BetweenGateAndClose(
            "\tif ( !gate.ShouldRun() )\n\t\treturn true;\n\n\twork();\n\n\tgate.MarkSucceeded();\n\treturn true;\n");

        Assert.True(middle is not null);
        Assert.Contains("work();", middle!, StringComparison.Ordinal);
        Assert.Equal(0, ManagedInitFatalTests.ReturnCount(middle!));
    }

    [Fact]
    public void BetweenGateAndCloseSeesADegradePath()
    {
        // The old shape, restored: a warning and a `return true` in the middle.
        string? middle = ManagedInitFatalTests.BetweenGateAndClose(
            "\tif ( !gate.ShouldRun() )\n\t\treturn true;\n\n"
            + "\tif ( !Acquire() )\n\t{\n\t\tWarning( \"nope\\n\" );\n\t\treturn true;\n\t}\n\n"
            + "\tgate.MarkSucceeded();\n\treturn true;\n");

        Assert.True(middle is not null);
        Assert.Equal(1, ManagedInitFatalTests.ReturnCount(middle!));
    }

    [Fact]
    public void BetweenGateAndCloseAnswersNullWhenTheGateClosesFirst()
    {
        // MarkSucceeded above ShouldRun is the original bug's shape — the flag
        // set on entry. There is no slice to scan and the caller must be told,
        // not handed an empty string that reads as clean.
        Assert.Null(ManagedInitFatalTests.BetweenGateAndClose(
            "\tgate.MarkSucceeded();\n\tif ( !gate.ShouldRun() )\n\t\treturn true;\n"));
    }

    [Fact]
    public void BetweenGateAndCloseAnswersNullWithNoGate()
    {
        Assert.Null(ManagedInitFatalTests.BetweenGateAndClose("\twork();\n\treturn true;\n"));
    }

    // ------------------------------------------------------------------
    // ReturnCount
    // ------------------------------------------------------------------

    [Fact]
    public void ReturnCountCountsEveryFlavourOfReturn()
    {
        Assert.Equal(3, ManagedInitFatalTests.ReturnCount(
            "\treturn true;\n\treturn false;\n\treturn;\n"));
    }

    [Fact]
    public void ReturnCountIgnoresACommentedOutReturn()
    {
        Assert.Equal(0, ManagedInitFatalTests.ReturnCount("\t// return true;\n"));
    }

    [Fact]
    public void ReturnCountDoesNotMatchAnIdentifierThatContainsReturn()
    {
        // Both boundaries, and both in lower case. `bNoReturn` does not
        // exercise either one — the regex is case-sensitive, so it fails to
        // match for a reason that has nothing to do with the \b guards, and the
        // check would pass with them removed.
        Assert.Equal(0, ManagedInitFatalTests.ReturnCount("\tint myreturn = 1;\n"));
        Assert.Equal(0, ManagedInitFatalTests.ReturnCount("\treturnValue = 1;\n"));

        // ...and a real return is still counted, so neither line above can be
        // passing by matching nothing at all.
        Assert.Equal(1, ManagedInitFatalTests.ReturnCount("\treturn value;\n"));
    }
}
