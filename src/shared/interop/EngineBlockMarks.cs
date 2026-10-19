using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// HOW A BLOCK WRITE IS ANNOUNCED TO THE ENGINE, stated once
/// (plan_entity.md step 1b, plan.md §10).
///
/// <para>
/// The engine has two ways of being told that an entity changed and they are
/// not two spellings of one thing. <c>edict_t::StateChanged()</c> sets
/// <c>FL_FULL_EDICT_CHANGED</c>, which makes the local network backdoor copy
/// EVERY prop in the entity's table; <c>edict_t::StateChanged(offset)</c>
/// records one offset, and <c>LocalTransfer_TransferEntity</c> maps it back to
/// a single prop through <c>CSendTablePrecalc::m_PropOffsetToIndexMap</c> and
/// copies that prop alone.
/// </para>
///
/// <para>
/// The difference is invisible to every functional test that could be written,
/// because both deliver the right VALUE. What the full form costs is bandwidth
/// and delta work, every frame, for the life of the build. So the rule is
/// written down HERE, on the side that owns the block's layout, and the facts
/// in <c>EngineBlockMarkTests</c> hold the native shell to it.
/// </para>
///
/// <para>
/// ONE OFFSET PER FIELD, NOT A RANGE, and that is the engine's arithmetic
/// rather than a saving. <c>MapPropOffsetsToIndices</c> matches a recorded
/// offset against a SendProp's offset EXACTLY, and a Vector is ONE SendProp of
/// type <c>DPT_Vector</c> at the vector's own offset -- not three. Marking
/// <c>Origin + 4</c> as well would name no typed prop at all; it would name
/// whichever generic payload word overlies those bytes. And
/// <c>MAX_CHANGE_OFFSETS</c> is NINETEEN: a move that marked the origin, the
/// angles and the twelve words of the world transform would spend eighteen of
/// the nineteen on one call, and the next thing to change anything would tip
/// the edict into <c>FL_FULL_EDICT_CHANGED</c> anyway -- which is the exact
/// outcome the rule exists to avoid.
/// </para>
/// </summary>
public static class EngineBlockMarks
{
    /// <summary>
    /// What a full-edict mark would look like if this API could express one.
    ///
    /// <para>
    /// It cannot, and the constant exists so a fact can say so: no value this
    /// class returns is ever this. A test that merely asserted "the offsets are
    /// right" would still pass on the day somebody added a widen-to-everything
    /// path beside them.
    /// </para>
    /// </summary>
    public const int FullEdictSentinel = -1;

    /// <summary>
    /// The head fields a shell setter writes, in declaration order. Taken from
    /// the struct rather than listed, so a field added to the block appears
    /// here without anybody remembering to add it.
    /// </summary>
    public static IReadOnlyList<string> HeadFields { get; } = BuildHeadFields();

    /// <summary>
    /// The change offsets a write to one head field must record, measured from
    /// the object base -- which is what <c>edict_t::StateChanged</c> takes and
    /// what a SendProp's own offset is measured from.
    ///
    /// <para>
    /// Exactly one, always. The list shape is deliberate: it is what lets a
    /// fact assert the COUNT, so widening the rule to a byte range is a red
    /// test rather than a silently bigger set.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> MarksForWrite(string headFieldName, int blockBase)
    {
        if (blockBase < 0)
            throw new ArgumentOutOfRangeException(nameof(blockBase));

        return new[] { blockBase + OffsetInBlockOf(headFieldName) };
    }

    /// <summary>
    /// Whether announcing a write to this field marks the whole edict. Always
    /// false, and it is a method rather than a constant so a fact can ask it of
    /// every field rather than of the idea.
    /// </summary>
    public static bool MarksWholeEdict(string headFieldName)
    {
        _ = OffsetInBlockOf(headFieldName);
        return false;
    }

    /// <summary>
    /// One head field's offset inside the block, which is its offset inside
    /// <c>EngineBlockHead</c> plus the head's own offset inside
    /// <c>EngineBlock</c> -- zero today, and written out because it is the
    /// thing that would move if anything were ever put ahead of the head.
    /// </summary>
    /// <summary>
    /// The offset inside the block of a field of the client tail
    /// (<see cref="EngineBlockClientTail"/>), which both sides' blocks carry:
    /// the skin is the one the server writes and marks.
    /// </summary>
    public static int OffsetInBlockOfClientTail(string tailFieldName)
    {
        if (string.IsNullOrEmpty(tailFieldName))
            throw new ArgumentException("a tail field name is required", nameof(tailFieldName));

        return (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
             + (int)Marshal.OffsetOf<EngineBlockClientTail>(tailFieldName);
    }

    public static int OffsetInBlockOf(string headFieldName)
    {
        if (string.IsNullOrEmpty(headFieldName))
            throw new ArgumentException("a head field name is required", nameof(headFieldName));

        return (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
             + (int)Marshal.OffsetOf<EngineBlockHead>(headFieldName);
    }

    /// <summary>
    /// The exact C++ the shell must contain to announce a write to this field.
    ///
    /// <para>
    /// The specification of the native side, spelled out rather than described,
    /// so the facts that hold the shell to it compare text and not intent. If
    /// this string and <c>shell_standalone_base.cpp</c> part company, one of the
    /// two moved and the suite says which.
    /// </para>
    /// </summary>
    public static string NativeMarkExpression(string headFieldName)
        => $"MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( {Named(headFieldName)} ) )";

    /// <summary>
    /// The exact C++ a SendProp addressing this field must be built from.
    ///
    /// <para>
    /// THE SAME ARITHMETIC AS THE MARK, and that is the whole reason this lives
    /// beside it. The engine matches a recorded change offset against a
    /// SendProp's offset exactly, so a prop built one way and a mark computed
    /// another leaves every partial update on the local backdoor matching
    /// nothing -- with the right value still arriving, by the slow path, for
    /// ever.
    /// </para>
    /// </summary>
    public static string NativeBlockBaseExpression => "CStandaloneEntity::BlockBase()";

    /// <summary>
    /// The offset-inside-the-block half of that arithmetic, as the shell and
    /// the prop builder both spell it.
    /// </summary>
    public static string NativeOffsetExpression(string headFieldName)
        => $"SS_BLOCK_HEAD_OFFSET( {Named(headFieldName)} )";

    private static string Named(string headFieldName)
    {
        _ = OffsetInBlockOf(headFieldName);
        return headFieldName;
    }

    private static string[] BuildHeadFields()
    {
        var fields = EngineBlock.FieldsOf(typeof(EngineBlockHead));
        var names = new string[fields.Length];

        for (int i = 0; i < fields.Length; i++)
            names[i] = fields[i].Name;

        return names;
    }
}
