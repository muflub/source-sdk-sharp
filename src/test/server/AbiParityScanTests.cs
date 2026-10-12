using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Guards the scanning in <see cref="AbiParityTests"/> itself.
///
/// AbiParityTests reads two source files and matches regexes over them. Until
/// this wave it matched the RAW text, so a commented-out line counted as a live
/// one — a commented-out "engineApi.Foo =" read as wired, leaving that slot a
/// null pointer that Engine.cs skips in silence. The ABI-bump decision leans on
/// that check, and for a commented-out assignment it could not fail.
///
/// The fix was a comment stripper, and a stripper is itself logic that can fail
/// quietly: one that returned its input unchanged would restore the original
/// hole with no visible symptom, and one that stripped too much would drop real
/// declarations and pass for the opposite wrong reason. So the stripper and the
/// two scans that depend on it are exercised here against literal snippets,
/// where the right answer is written down next to the input.
/// </summary>
public class AbiParityScanTests
{
    // ---------------------------------------------------------------------
    // The wiring scan: the one that was actually broken.
    // ---------------------------------------------------------------------

    [Fact]
    public void WiringScanFindsALiveAssignment()
    {
        var slots = AbiParityTests.AssignedEngineSlots(
            "engineApi.ExplosionSetMagnitude = &Explosion_SetMagnitude;");

        Assert.Contains("ExplosionSetMagnitude", slots);
    }

    [Fact]
    public void WiringScanIgnoresALineCommentedAssignment()
    {
        // The exact shape of the defect: the slot is declared in the header and
        // its wiring is commented out. Raw-text matching called this wired.
        var slots = AbiParityTests.AssignedEngineSlots(
            "\tengineApi.Live = &A;\n\t// engineApi.Commented = &B;\n");

        Assert.Contains("Live", slots);
        Assert.DoesNotContain("Commented", slots);
    }

    [Fact]
    public void WiringScanIgnoresABlockCommentedAssignment()
    {
        var slots = AbiParityTests.AssignedEngineSlots(
            "engineApi.Live = &A;\n/* engineApi.Commented = &B; */\n");

        Assert.Contains("Live", slots);
        Assert.DoesNotContain("Commented", slots);
    }

    [Fact]
    public void WiringScanIgnoresAnAssignmentInsideAMultiLineBlockComment()
    {
        // The way a batch of slots actually gets disabled: one /* ... */ over
        // several lines, not a comment marker per line.
        var slots = AbiParityTests.AssignedEngineSlots(
            "engineApi.Live = &A;\n"
            + "/*\n"
            + "    engineApi.OffOne = &B;\n"
            + "    engineApi.OffTwo = &C;\n"
            + "*/\n"
            + "engineApi.AlsoLive = &D;\n");

        Assert.Contains("Live", slots);
        Assert.Contains("AlsoLive", slots);
        Assert.DoesNotContain("OffOne", slots);
        Assert.DoesNotContain("OffTwo", slots);
    }

    [Fact]
    public void WiringScanKeepsCodeThatTRAILSABlockCommentOnOneLine()
    {
        // A block comment that closes mid-line must not swallow the rest of it.
        var slots = AbiParityTests.AssignedEngineSlots(
            "/* off: engineApi.Off = &B; */ engineApi.Live = &A;");

        Assert.Contains("Live", slots);
        Assert.DoesNotContain("Off", slots);
    }

    // ---------------------------------------------------------------------
    // The header scan, which had the same exposure.
    // ---------------------------------------------------------------------

    [Fact]
    public void HeaderScanFindsDeclaredPointers()
    {
        string header =
            "typedef struct\n"
            + "{\n"
            + "\tuint32_t struct_size;\n"
            + "\tvoid (*Alpha)( int32_t n );\n"
            + "\tint32_t (*Beta)( const char *p );\n"
            + "} SourceSharpEngineApi;\n";

        Assert.Equal(new List<string> { "Alpha", "Beta" },
                     AbiParityTests.PointerNamesOnly(header, "SourceSharpEngineApi"));
    }

    [Fact]
    public void HeaderScanIgnoresACommentedOutPointer()
    {
        // A slot commented out in the header used to count as declared, and the
        // wiring check would then demand wiring for a slot that is not there.
        string header =
            "typedef struct\n"
            + "{\n"
            + "\tvoid (*Alpha)( int32_t n );\n"
            + "\t// void (*Ghost)( int32_t n );\n"
            + "\t/* void (*AlsoGhost)( int32_t n ); */\n"
            + "\tint32_t (*Beta)( const char *p );\n"
            + "} SourceSharpEngineApi;\n";

        Assert.Equal(new List<string> { "Alpha", "Beta" },
                     AbiParityTests.PointerNamesOnly(header, "SourceSharpEngineApi"));
    }

    [Fact]
    public void HeaderScanIgnoresAPointerShapeMentionedInProse()
    {
        // Doc comments in sourcesharp.h describe signatures in passing. Any one
        // of them reads as a declaration to a raw scan, and inserts a phantom
        // slot that shifts every name after it.
        string header =
            "typedef struct\n"
            + "{\n"
            + "\t// Superseded by Beta; was void (*Legacy)( int32_t n ).\n"
            + "\tvoid (*Alpha)( int32_t n );\n"
            + "\tint32_t (*Beta)( const char *p );\n"
            + "} SourceSharpEngineApi;\n";

        Assert.Equal(new List<string> { "Alpha", "Beta" },
                     AbiParityTests.PointerNamesOnly(header, "SourceSharpEngineApi"));
    }

    // ---------------------------------------------------------------------
    // The stripper. If this silently became the identity function, everything
    // above would go back to passing for the wrong reason.
    // ---------------------------------------------------------------------

    [Fact]
    public void StripperRemovesALineComment()
    {
        Assert.Equal("int a;\n", AbiParityTests.CodeOnly("int a;// gone\n"));
    }

    [Fact]
    public void StripperRemovesABlockComment()
    {
        Assert.Equal("int a; int b;", AbiParityTests.CodeOnly("int a;/* gone */ int b;"));
    }

    [Fact]
    public void StripperKeepsLineCountSoFailureMessagesStillLineUp()
    {
        // Three source lines in, three out: a block comment's newlines survive
        // even though its text does not.
        string stripped = AbiParityTests.CodeOnly("int a;\n/* one\n   two */\nint b;\n");

        Assert.Equal(4, stripped.Split('\n').Length - 1);
        Assert.Contains("int a;", stripped);
        Assert.Contains("int b;", stripped);
        Assert.DoesNotContain("one", stripped);
        Assert.DoesNotContain("two", stripped);
    }

    [Fact]
    public void StripperKeepsSlashesInsideAStringLiteral()
    {
        // A "//" in a literal is not a comment. Nothing in the two files scanned
        // today has one, which is exactly why it would go unnoticed later.
        const string source = "const char *p = \"a // b\"; int a;";

        Assert.Equal(source, AbiParityTests.CodeOnly(source));
    }

    [Fact]
    public void StripperSurvivesAnEscapedQuoteInsideALiteral()
    {
        const string source = "const char *p = \"a\\\" // b\"; int a;";

        Assert.Equal(source, AbiParityTests.CodeOnly(source));
    }

    [Fact]
    public void StripperSurvivesACharLiteralHoldingASlash()
    {
        const string source = "char c = '/'; int a;";

        Assert.Equal(source, AbiParityTests.CodeOnly(source));
    }

    [Fact]
    public void StripperIsIdempotent()
    {
        // PointerNamesOnly strips text its caller may already have stripped.
        const string source = "int a; // gone\n/* also gone */ int b;\n";

        string once = AbiParityTests.CodeOnly(source);
        Assert.Equal(once, AbiParityTests.CodeOnly(once));
    }

    [Fact]
    public void StripperDoesNotHangOnAnUnterminatedBlockComment()
    {
        // A truncated or malformed header must fail the parity check, not spin
        // forever inside the scanner.
        string stripped = AbiParityTests.CodeOnly("int a;\n/* never closed\nint b;\n");

        Assert.Contains("int a;", stripped);
        Assert.DoesNotContain("int b;", stripped);
    }

    [Fact]
    public void StripperIsNotTheIdentityFunction()
    {
        // The check that catches the fix being quietly reverted: if CodeOnly
        // ever returns its input unchanged, every guard above goes hollow while
        // staying green, and the ABI-bump decision goes back to trusting a scan
        // that reads prose as wiring.
        const string source = "engineApi.Live = &A; // engineApi.Commented = &B;";

        Assert.NotEqual(source, AbiParityTests.CodeOnly(source));
    }
}
