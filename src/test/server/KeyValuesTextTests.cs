using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The KV1 text reader — plan_gamedll_after.md Phase A3.
///
/// <para>
/// <b>Every case here is a shape one of the two real files uses</b>, and the
/// facts say which. A parser tested only against invented input is a parser
/// tested against what its author expected the format to be, and the two files
/// this one exists for are shipped content nobody in this tree controls.
/// </para>
/// </summary>
public class KeyValuesTextTests
{
    [Fact]
    public void ADocumentMayHoldSeveralTopLevelKeys()
    {
        // decals_subrect.txt is exactly this: TranslationData, then one block
        // per decal group, all at the top level. Stock's own loader walks
        // GetNextKey() after LoadFromFile for no other reason, and a parser
        // that returned only the first root would read one group out of fifty.
        var kv = KeyValuesText.Parse("\"A\" { \"x\" \"1\" } \"B\" { \"y\" \"2\" }");

        Assert.True(kv.Children.Count == 2, "both top-level blocks are read");
        Assert.True(kv.Children[0].Name == "A", "the first root keeps its name");
        Assert.True(kv.Children[1].Name == "B", "and so does the second");
    }

    [Fact]
    public void AValueMayBeEmptyAndThatIsNotTheSameAsAbsent()
    {
        // "X" "" in TranslationData means "do not decal this surface". A reader
        // that dropped an empty value would turn that into "this surface has no
        // rule", which is the opposite instruction.
        var kv = KeyValuesText.Parse("\"T\" { \"X\" \"\" \"C\" \"Impact.Concrete\" }");
        var block = kv.Children[0];

        Assert.True(block.Children.Count == 2, "an empty value is still a line");
        Assert.True(block.Children[0].Value.Length == 0, "and its value is empty");
        Assert.True(block.Children[1].Value == "Impact.Concrete", "the next line is unaffected");
    }

    [Fact]
    public void ABackslashIsNotAnEscape()
    {
        // propdata.txt spells its gib models "models\Gibs\wood_gib01e.mdl", and
        // stock loads it with escape handling OFF. A reader that interpreted
        // \G and \w would hand the engine a path to nothing -- and the engine
        // answers a missing model with an index, so the failure is a prop that
        // breaks into invisible pieces.
        var kv = KeyValuesText.Parse("\"g\" { \"models\\Gibs\\wood_gib01e.mdl\" \"1\" }");

        Assert.True(kv.Children[0].Children[0].Name == "models\\Gibs\\wood_gib01e.mdl",
            "a backslash in a model path survives the parse");
    }

    [Fact]
    public void ALineCommentEndsAtTheNewlineAndNotAtTheSlash()
    {
        var kv = KeyValuesText.Parse("// header\n\"A\" { \"x\" \"1\" // trailing\n }");

        Assert.True(kv.Children.Count == 1, "the header comment is not a key");
        Assert.True(kv.Children[0].Children.Count == 1, "the trailing comment is not a key either");
    }

    [Fact]
    public void ASingleSlashIsNotAComment()
    {
        // "decals/concrete/shot1_subrect" is a real entry in decals_subrect.txt.
        // Treating one slash as a comment would truncate every decal name in
        // the file at its first directory separator.
        var quoted = KeyValuesText.Parse("\"g\" { \"decals/concrete/shot1_subrect\" \"1\" }");

        Assert.True(quoted.Children[0].Children[0].Name == "decals/concrete/shot1_subrect",
            "a path separator inside a quoted token is not the start of a comment");

        // AND UNQUOTED, which is the case the quoted one cannot see. Comment
        // skipping happens BETWEEN tokens, so a quoted string is read whole
        // whatever the comment rule is -- a mutation making one slash start a
        // comment left the assertion above green while every bare token in a
        // file would have been truncated at its first separator.
        var bare = KeyValuesText.Parse("g { models/gibs/wood_gib01e.mdl 1 }");

        Assert.True(bare.Children[0].Children[0].Name == "models/gibs/wood_gib01e.mdl",
            "and neither is one in a bare token");
    }

    [Fact]
    public void FindIsCaseInsensitiveLikeTheEnginesOwn()
    {
        var kv = KeyValuesText.Parse("\"PropData.txt\" { \"BreakableModels\" { \"W\" { } } }");

        Assert.True(kv.Children[0].Find("breakablemodels") is not null,
            "FindKey on the engine's KeyValues is case-insensitive and so is this");
    }

    [Fact]
    public void ABlockWithNoChildrenIsStillABlock()
    {
        var kv = KeyValuesText.Parse("\"empty\" { }");

        Assert.True(kv.Children[0].IsBlock, "an empty block is a block");
        Assert.True(kv.Children[0].Children.Count == 0, "and it holds nothing");
    }

    [Fact]
    public void ABlockAndALeafWithAnEmptyValueAreDistinguishable()
    {
        // Both answer "" from Value; only IsBlock tells them apart, and the
        // decal loader depends on it -- it takes a top-level key as a group
        // only if it is a block.
        var kv = KeyValuesText.Parse("\"a\" { } \"b\" \"\"");

        Assert.True(kv.Children[0].IsBlock, "the first is a block");
        Assert.True(!kv.Children[1].IsBlock, "the second is a leaf with an empty value");
    }

    [Fact]
    public void AnUnclosedBlockIsRefusedRatherThanReturningWhatItRead()
    {
        // Half a parse is a table missing its tail, which is silent: the groups
        // that were read still answer and the ones that were not answer -1,
        // which is what a working reader answers for a name that is not there.
        Assert.Throws<KeyValuesTextException>(() => KeyValuesText.Parse("\"a\" { \"x\" \"1\""));
    }

    [Fact]
    public void AnUnterminatedQuotedTokenIsRefusedAtTheLineRatherThanSwallowingTheFile()
    {
        // Stock's tokeniser reads to the next quote wherever it is, so ONE
        // missing quote silently merges the rest of the document into a single
        // token. Refusing at the newline turns that into an error with an
        // offset in it.
        // THE INPUT PARSES CLEANLY WITHOUT THE RULE, and that is what makes this
        // fact about the rule.
        //
        // The first version used a genuinely broken document — a missing quote
        // followed by more content — and it was THE CHECK THAT CANNOT FAIL:
        // with the newline rule disabled the run-on token simply reached the end
        // of the text, so the SAME exception came out of the end-of-input arm
        // and the case passed while its subject was gone. Measured with a
        // transliteration of the parser rather than reasoned about.
        //
        // A value that spans a line is where the two arms disagree: the newline
        // rule refuses it, and without that rule it is an ordinary key whose
        // value happens to contain a newline.
        var e = Assert.Throws<KeyValuesTextException>(
            () => KeyValuesText.Parse("\"a\" { \"x\" \"1\n\" }"));

        Assert.True(e.Message.Contains("unterminated quoted token"),
            "and the refusal is the newline rule's, naming the offset it gave up at");
    }

    [Fact]
    public void AStrayClosingBraceIsRefused()
    {
        Assert.Throws<KeyValuesTextException>(() => KeyValuesText.Parse("\"a\" { } }"));
    }

    [Fact]
    public void TheRealDecalScriptsShapeParses()
    {
        // The literal opening of scripts/decals_subrect.txt, transcribed: the
        // comment header, the translation block with an empty value and a
        // trailing comment on the same line, and a group.
        const string text = """
            // Decal definitions, format is
            //   decalname weight
            "TranslationData"
            {
            	"-"			""  // don't decal this surface

            	"C"			"Impact.Concrete"
            }

            "Impact.Concrete"
            {
            	"decals/concrete/shot1_subrect" "1"
            	"decals/concrete/shot2_subrect" "1"
            }
            """;

        var kv = KeyValuesText.Parse(text);

        Assert.True(kv.Children.Count == 2, "two top-level blocks");
        Assert.True(kv.Children[0].Children.Count == 2, "the translation block has both lines");
        Assert.True(kv.Children[1].Children.Count == 2, "and the group has both decals");
    }
}
