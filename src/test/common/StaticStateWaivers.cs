using SourceSharp.Analyzers;

namespace SourceSharp.Tests;

/// <summary>
/// The mutable statics that stay, each with the reason it stays, read from the
/// ONE list both gates share: <c>src/analyzers/static-state-waivers.txt</c>.
/// The build-time analyzer (SS0001/SS0002/SS0003) is the gate; a row goes when
/// its static does, so the list only shrinks.
/// </summary>
internal static class StaticStateWaivers
{
    internal const string Table =
        "CONSTANT TABLE: written once by its initialiser and only read";

    internal const string NullObject =
        "STATELESS NULL OBJECT / DEFAULT: an instance with no state that changes";

    /// <summary>Where the list is, from the repo root.</summary>
    internal static string FilePath => RepoLayout.Repo("src", "analyzers", StaticStateWaiverFile.FileName);

    /// <summary>
    /// Every row, key to reason. A missing or malformed file throws: an empty
    /// list would read every waived static as a new one, and a stale row as gone.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Reasons = Load();

    private static Dictionary<string, string> Load()
    {
        var file = StaticStateWaiverFile.Parse(File.ReadAllText(FilePath));

        if (file.Errors.Count > 0)
            throw new InvalidDataException($"{FilePath}: " + string.Join("; ", file.Errors.Select(e => $"line {e.Line}: {e.Error}")));

        if (file.Rows.Count == 0)
            throw new InvalidDataException($"{FilePath} has no rows; a waiver list read as empty is not evidence");

        return file.Rows.ToDictionary(r => r.Key, r => r.Reason == StaticStateWaiverFile.Table ? Table : NullObject, StringComparer.Ordinal);
    }
}
