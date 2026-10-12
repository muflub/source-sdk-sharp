using System;
using System.Collections.Generic;

namespace SourceSharp.Analyzers;

/// <summary>One row of the waiver file: the field it names, why it stays, and the 1-based line it is on.</summary>
public sealed class StaticStateWaiver
{
    public StaticStateWaiver(string key, string reason, int line)
    {
        Key = key;
        Reason = reason;
        Line = line;
    }

    /// <summary><c>Assembly: Namespace.Type.field</c>, as the CLR names the field.</summary>
    public string Key { get; }

    /// <summary><see cref="StaticStateWaiverFile.Table"/> or <see cref="StaticStateWaiverFile.NullObject"/>.</summary>
    public string Reason { get; }

    public int Line { get; }

    /// <summary>The assembly the row is for: the key up to its first ": ".</summary>
    public string Assembly => Key.Substring(0, Key.IndexOf(": ", StringComparison.Ordinal));
}

/// <summary>
/// The parsed waiver file (<c>src/analyzers/static-state-waivers.txt</c>), the
/// one list both <see cref="StaticStateAnalyzer"/> and StaticStateCensusTests
/// read. The format is in the file's own header.
/// </summary>
public sealed class StaticStateWaiverFile
{
    public const string FileName = "static-state-waivers.txt";

    /// <summary>A constant table: written once by its initialiser and only read.</summary>
    public const string Table = "TABLE";

    /// <summary>A stateless null object or default: an instance with no state that changes.</summary>
    public const string NullObject = "NULL-OBJECT";

    private StaticStateWaiverFile(List<StaticStateWaiver> rows, List<(int Line, string Error)> errors)
    {
        Rows = rows;
        Errors = errors;
    }

    public IReadOnlyList<StaticStateWaiver> Rows { get; }

    /// <summary>Each malformed line, 1-based, with what is wrong with it. A file with any is not trusted.</summary>
    public IReadOnlyList<(int Line, string Error)> Errors { get; }

    public static StaticStateWaiverFile Parse(string text)
    {
        var rows = new List<StaticStateWaiver>();
        var errors = new List<(int, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            int number = i + 1;

            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;

            string[] cells = line.Split('\t');

            if (cells.Length != 2)
            {
                errors.Add((number, "a row is \"<Assembly>: <Type>.<field><TAB><reason>\""));
                continue;
            }

            string key = cells[0];
            string reason = cells[1];
            int colon = key.IndexOf(": ", StringComparison.Ordinal);

            if (colon <= 0 || key.LastIndexOf('.') < colon || key.Trim() != key)
            {
                errors.Add((number, $"'{key}' is not \"<Assembly>: <Type>.<field>\""));
                continue;
            }

            if (reason != Table && reason != NullObject)
            {
                errors.Add((number, $"the reason '{reason}' is neither {Table} nor {NullObject}: a static that is not a constant table or a stateless null object is converted, not waived"));
                continue;
            }

            if (!seen.Add(key))
            {
                errors.Add((number, $"'{key}' is waived twice"));
                continue;
            }

            rows.Add(new StaticStateWaiver(key, reason, number));
        }

        return new StaticStateWaiverFile(rows, errors);
    }
}
