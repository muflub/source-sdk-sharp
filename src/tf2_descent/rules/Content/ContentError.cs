namespace TF2.Descent.Rules.Content;

/// <summary>What a content load error broke. Each is a rule of plan_tf2_rpg 1b.</summary>
public enum ContentRule
{
    /// <summary>Not JSON, the wrong shape, a field of the wrong kind, a required field missing or one nobody reads.</summary>
    Malformed,
    /// <summary>A table every load needs is not in the source.</summary>
    MissingTable,
    /// <summary>Two rows of one table share an id, or a key the table holds unique across its rows (an item class under two families).</summary>
    DuplicateId,
    /// <summary>A row names a stat that is not in the stat table (nor an attribute).</summary>
    UnknownStat,
    /// <summary>A row names a row of another table that does not exist.</summary>
    DanglingReference,
    /// <summary>A value or a roll range outside what its field or its stat allows.</summary>
    OutOfRange,
    /// <summary>A skill or spell names an item base: a skill is never tied to an item the player finds (R17).</summary>
    ItemBaseReference,
    /// <summary>A skill or spell whose id or display name is an item base's (R17, R12): one name, two things.</summary>
    NameCollision,
    /// <summary>An action (a cast skill or a spell) names a weapon family: actions work whatever is equipped (R17).</summary>
    FamilyOnAction,
    /// <summary>Skills whose prerequisites lead back to themselves: none of them could ever be bought.</summary>
    PrerequisiteCycle,
}

/// <summary>One content load error: which table, which row (null for the table itself), which rule, and why.</summary>
public sealed record ContentError(string Table, string? Row, ContentRule Rule, string Message)
{
    public override string ToString() => $"{Table}{(Row is null ? "" : "[" + Row + "]")}: {Rule}: {Message}";
}

/// <summary>The errors of one load, in the order they were found.</summary>
public sealed class ContentErrors
{
    private readonly List<ContentError> _list = [];

    public IReadOnlyList<ContentError> All => _list;
    public int Count => _list.Count;

    public void Add(string table, string? row, ContentRule rule, string message) =>
        _list.Add(new ContentError(table, row, rule, message));
}

/// <summary>The shipped content does not load: the rules cannot be built from it.</summary>
public sealed class ContentLoadException(IReadOnlyList<ContentError> errors)
    : Exception("the Descent content does not load:\n  " + string.Join("\n  ", errors))
{
    public IReadOnlyList<ContentError> Errors { get; } = errors;
}
