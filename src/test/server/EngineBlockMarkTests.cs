using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE MARK, NOT THE VALUE (plan_entity.md step 1b, plan.md §10).
///
/// <para>
/// Every other test in this tree about an entity's position asks whether the
/// right number arrived. None of them can fail on the thing this file is about:
/// a shell that announced a position write as <c>StateChanged()</c> rather than
/// <c>StateChanged(offset)</c> delivers exactly the same origin to exactly the
/// same client. What it also delivers is every other prop in the table, on the
/// local backdoor, every frame something moves -- 128 int32 props at the
/// smallest tier this hierarchy can be allocated at.
/// </para>
///
/// <para>
/// So the checks here are about the ANNOUNCEMENT. Two of them state the rule on
/// the managed side, where the block's layout is owned; the rest hold
/// <c>shell_standalone_base.cpp</c> and <c>shell_standalone.cpp</c> to it by
/// reading them, the same way <c>EngineBlockTests</c> holds
/// <c>engine_block_generated.h</c> to the C# declaration.
/// </para>
/// </summary>
public sealed class EngineBlockMarkTests
{
    /// <summary>
    /// A blob base that is not zero and not a round number, so an assertion
    /// that accidentally dropped it would not still pass. The live one is
    /// <c>ALIGN_VALUE(MAX(sizeof(CStandaloneEntity), sizeof(CStandalonePlayer)), 16)</c>
    /// and is printed by the server at tier registration.
    /// </summary>
    private const int SampleBlockBase = 1952;

    [Fact]
    public void WritingOriginMarksExactlyTheOriginOffset()
    {
        IReadOnlyList<int> marks =
            EngineBlockMarks.MarksForWrite(nameof(EngineBlockHead.Origin), SampleBlockBase);

        Assert.Equal(new[] { SampleBlockBase + 16 }, marks);
    }

    [Fact]
    public void WritingOriginMarksOneOffsetAndNotItsThreeFloats()
    {
        IReadOnlyList<int> marks =
            EngineBlockMarks.MarksForWrite(nameof(EngineBlockHead.Origin), SampleBlockBase);

        // A Vector is one SendProp of type DPT_Vector at the vector's own
        // offset. Marking +4 and +8 as well would match no typed prop, and
        // would spend two more of the edict's nineteen change offsets.
        Assert.Single(marks);
        Assert.DoesNotContain(SampleBlockBase + 20, marks);
        Assert.DoesNotContain(SampleBlockBase + 24, marks);
    }

    [Fact]
    public void WritingOriginNeverMarksTheWholeEdict()
    {
        Assert.False(EngineBlockMarks.MarksWholeEdict(nameof(EngineBlockHead.Origin)));

        Assert.DoesNotContain(
            EngineBlockMarks.FullEdictSentinel,
            EngineBlockMarks.MarksForWrite(nameof(EngineBlockHead.Origin), SampleBlockBase));
    }

    [Fact]
    public void NoHeadFieldWriteMarksTheWholeEdict()
    {
        foreach (string field in EngineBlockMarks.HeadFields)
        {
            Assert.False(EngineBlockMarks.MarksWholeEdict(field), field);

            Assert.DoesNotContain(
                EngineBlockMarks.FullEdictSentinel,
                EngineBlockMarks.MarksForWrite(field, SampleBlockBase));
        }
    }

    [Fact]
    public void TheOriginMarkAndTheAnglesMarkAreDifferentOffsets()
    {
        int origin = EngineBlockMarks.MarksForWrite(nameof(EngineBlockHead.Origin), 0).Single();
        int angles = EngineBlockMarks.MarksForWrite(nameof(EngineBlockHead.Angles), 0).Single();

        Assert.NotEqual(origin, angles);
    }

    [Fact]
    public void EveryHeadFieldMarksAnOffsetInsideTheBlock()
    {
        foreach (string field in EngineBlockMarks.HeadFields)
        {
            int mark = EngineBlockMarks.MarksForWrite(field, SampleBlockBase).Single();

            Assert.InRange(mark, SampleBlockBase, SampleBlockBase + EngineBlock.SizeBytes - 1);
        }
    }

    [Fact]
    public void TheHeadFieldListIsTheStructsAndNotAHandWrittenCopy()
    {
        // Non-vacuity for the two facts above: a list that had gone empty would
        // pass both of them by iterating nothing.
        Assert.Equal(21, EngineBlockMarks.HeadFields.Count);
        Assert.Contains(nameof(EngineBlockHead.Origin), EngineBlockMarks.HeadFields);
        Assert.Contains(nameof(EngineBlockHead.SolidFlags), EngineBlockMarks.HeadFields);
    }

    [Fact]
    public void AnUnknownFieldNameIsRefusedRatherThanMarkedAtZero()
    {
        // Offset zero is EngineBlockHead.Handle, so a typo that answered a
        // default would mark a real field -- the wrong one -- and look right.
        Assert.ThrowsAny<Exception>(
            () => EngineBlockMarks.MarksForWrite("m_vecOrigin", SampleBlockBase));
    }

    //-------------------------------------------------------------------------
    // The native side, held to the rule above.
    //-------------------------------------------------------------------------

    private const string ShellBase = "src/sourcesharp/game/server/shell_standalone_base.cpp";
    private const string ShellTables = "src/sourcesharp/game/server/shell_standalone.cpp";

    //-------------------------------------------------------------------------
    // THE MARKS MOVED, THE RULE DID NOT (plan_entity.md step 1f).
    //
    // The tail of SetAbsTransform -- partition, the two marks, the children,
    // the triggers -- is CStandaloneEntity::OnPositionChanged now, because
    // managed code that writes the block itself needs to be able to say "I
    // moved it" without going through a setter that would write the position
    // again (ENTITY_OP_POSITION_CHANGED). It is not a copy: SetAbsTransform
    // calls it, so the op path and the native path are one piece of code.
    //
    // So the function these facts read moved, and the fact below asserts the
    // delegation rather than trusting it -- otherwise a future edit could put
    // an unmarked write back into SetAbsTransform and these would still pass.
    //-------------------------------------------------------------------------
    private const string PositionWrite = "void CStandaloneEntity::OnPositionChanged(";

    [Fact]
    public void TheNativeOriginWriteAnnouncesTheOriginOffset()
    {
        string body = FunctionBody(Read(ShellBase), PositionWrite);

        Assert.Contains(
            EngineBlockMarks.NativeMarkExpression(nameof(EngineBlockHead.Origin)),
            body);
        Assert.Contains(
            EngineBlockMarks.NativeMarkExpression(nameof(EngineBlockHead.Angles)),
            body);
    }

    [Fact]
    public void TheNativeOriginWriteNeverMarksTheWholeEdict()
    {
        string body = FunctionBody(Read(ShellBase), PositionWrite);

        Assert.Equal(0, CountOutsideComments(body, "m_pEdict, -1)"));
    }

    /// <summary>
    /// SetAbsTransform writes the two fields and then hands the whole of the
    /// announcing to <c>OnPositionChanged</c>. Without this fact the two above
    /// could go on passing while SetAbsTransform grew an unannounced write of
    /// its own — they read a different function now.
    /// </summary>
    [Fact]
    public void TheTransformSetterDelegatesItsAnnouncingToOnePlace()
    {
        string body = FunctionBody(Read(ShellBase), "void CStandaloneEntity::SetAbsTransform(");

        Assert.Contains("OnPositionChanged(vecPrevOrigin)", body);

        // It announces NOTHING itself. Two announcing sites for one move is two
        // things to keep in step, and the op path would only run one of them.
        Assert.Equal(0, CountOutsideComments(body, "MarkBlockChanged("));
        Assert.Equal(0, CountOutsideComments(body, "m_pEdict, -1)"));
    }

    [Fact]
    public void NoBlockSetterMarksTheWholeEdict()
    {
        string file = Read(ShellBase);

        string[] setters =
        {
            PositionWrite,
            "void CStandaloneEntity::SetModelName(",
            "void CStandaloneEntity::SetVisible(",
            "void CStandaloneEntity::SetSolidity(",
            "void CStandaloneEntity::SetCollisionBounds(",
            "void CStandaloneEntity::SetCollisionGroup(",
            "void CStandaloneEntity::SetModelIndex(",
        };

        foreach (string setter in setters)
        {
            string body = FunctionBody(file, setter);

            Assert.True(body.Contains("MarkBlockChanged("), $"{setter} announces nothing");
            Assert.Equal(0, CountOutsideComments(body, "m_pEdict, -1)"));
        }
    }

    [Fact]
    public void TheMarkHelperRecordsOneOffsetAndDoesNotWalkARange()
    {
        string body = FunctionBody(Read(ShellBase), "void CStandaloneEntity::MarkBlockChanged(");

        // Exactly one edict mark, and it takes the field's offset (MarkEdictChanged with
        // -1 is the whole edict, CBaseEdict::StateChanged() as it was).
        Assert.Equal(1, CountOutsideComments(body, "MarkEdictChanged(Context(), m_pEdict, BlockBase() + nBlockOffset)"));
        Assert.Equal(0, CountOutsideComments(body, "m_pEdict, -1)"));

        // And it is not a loop. A helper that walked the field's bytes in
        // four-byte words would still mark "the origin offset" -- and two more
        // besides, which is the widening this file exists to refuse.
        Assert.Equal(0, CountOutsideComments(body, "for ("));
        Assert.Equal(0, CountOutsideComments(body, "while ("));
    }

    [Fact]
    public void TheMarkAndTheSendPropAreBuiltFromTheSameArithmetic()
    {
        string mark = FunctionBody(Read(ShellBase), "void CStandaloneEntity::MarkBlockChanged(");
        Assert.Contains(EngineBlockMarks.NativeBlockBaseExpression.Replace(
                            "CStandaloneEntity::", ""), mark);

        string props = FunctionBody(Read(ShellTables), "static void BuildStandaloneProps(");

        Assert.Contains(EngineBlockMarks.NativeBlockBaseExpression, props);
        Assert.Contains(EngineBlockMarks.NativeOffsetExpression(nameof(EngineBlockHead.Origin)),
                        props);
        Assert.Contains(EngineBlockMarks.NativeOffsetExpression(nameof(EngineBlockHead.Angles)),
                        props);
        Assert.Contains(EngineBlockMarks.NativeOffsetExpression(nameof(EngineBlockHead.ModelIndex)),
                        props);
    }

    [Fact]
    public void TheSendPropsNoLongerAddressShellMembers()
    {
        string props = FunctionBody(Read(ShellTables), "static void BuildStandaloneProps(");

        // The old form, and it is the specific thing step 1b deleted: an offset
        // taken against a member of the shell rather than against the block.
        Assert.DoesNotContain("pNull->m_vecOrigin", props);
        Assert.DoesNotContain("pNull->m_angRotation", props);
        Assert.DoesNotContain("pNull->m_nModelIndex", props);
    }

    //-------------------------------------------------------------------------

    private static string Read(string relative) => File.ReadAllText(Locate(relative));

    /// <summary>
    /// The braced body of one function, brace-counted with comments and string
    /// literals skipped -- a <c>{</c> inside either is not a brace.
    /// </summary>
    private static string FunctionBody(string file, string signature)
    {
        int start = file.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' is not in the file; this check measures nothing");

        int open = file.IndexOf('{', start);
        Assert.True(open >= 0, $"'{signature}' has no body");

        int depth = 0;
        bool block = false, line = false, str = false, chr = false;

        for (int i = open; i < file.Length; i++)
        {
            char c = file[i];
            char n = i + 1 < file.Length ? file[i + 1] : '\0';

            if (block) { if (c == '*' && n == '/') { block = false; i++; } continue; }
            if (line) { if (c == '\n') line = false; continue; }
            if (str) { if (c == '\\') i++; else if (c == '"') str = false; continue; }
            if (chr) { if (c == '\\') i++; else if (c == '\'') chr = false; continue; }

            if (c == '/' && n == '*') { block = true; i++; continue; }
            if (c == '/' && n == '/') { line = true; i++; continue; }
            if (c == '"') { str = true; continue; }
            if (c == '\'') { chr = true; continue; }

            if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
                return file.Substring(open, i - open + 1);
        }

        Assert.Fail($"'{signature}' body never closed");
        return "";
    }

    /// <summary>
    /// Occurrences of a needle in code, ignoring the ones inside comments.
    ///
    /// <para>
    /// This file's needles are written about in the comments right beside the
    /// code they check -- "no path calls StateChanged() any more" is a sentence
    /// that contains its own needle -- so a raw count would report the prose.
    /// </para>
    /// </summary>
    private static int CountOutsideComments(string body, string needle)
        => Occurrences(StripComments(body), needle);

    private static string StripComments(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool block = false, line = false, str = false, chr = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char n = i + 1 < text.Length ? text[i + 1] : '\0';

            if (block) { if (c == '*' && n == '/') { block = false; i++; } continue; }
            if (line) { if (c == '\n') { line = false; sb.Append(c); } continue; }

            if (!str && !chr)
            {
                if (c == '/' && n == '*') { block = true; i++; continue; }
                if (c == '/' && n == '/') { line = true; i++; continue; }
            }

            if (str) { if (c == '\\') { sb.Append(c); i++; if (i < text.Length) sb.Append(text[i]); continue; } if (c == '"') str = false; }
            else if (chr) { if (c == '\\') { sb.Append(c); i++; if (i < text.Length) sb.Append(text[i]); continue; } if (c == '\'') chr = false; }
            else if (c == '"') str = true;
            else if (c == '\'') chr = true;

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }
        return n;
    }

    private static string Locate(string relative)
    {
        string? candidate = RepoLayout.FindSdkFile(relative);
        if (candidate is not null)
            return candidate;

        Assert.Fail($"{relative} was not found in {RepoLayout.SdkTree}; a scan that "
                    + "read nothing would pass every check that looks inside it");
        return "";
    }
}
