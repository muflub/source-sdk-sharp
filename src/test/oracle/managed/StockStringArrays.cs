using System.Text.RegularExpressions;

namespace SourceSharp.OracleTests;

/// <summary>
/// A C string table read out of a stock source file, for an oracle that holds
/// a port's copy of it: <c>static const char *NAME[] = { "a", "b", "" };</c>.
/// The literals come back in order, the empty END marker dropped; a row inside
/// an <c>#ifdef</c>/<c>#if defined(...)</c> arm whose macro is not in
/// <c>defined</c> is dropped too, and the directive lines never count. A file
/// without the table, or a table with no rows, throws.
/// </summary>
internal static class StockStringArrays
{
    public static IReadOnlyList<string> Rows(string source, string name, params string[] defined)
    {
        var head = Regex.Match(source, @"static\s+const\s+char\s*\*\s*" + Regex.Escape(name) + @"\s*\[\s*\]\s*=\s*\{");
        if (!head.Success)
            throw new InvalidDataException($"no 'static const char *{name}[] = {{' in the source");

        int end = source.IndexOf("};", head.Index + head.Length, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidDataException($"{name}[] has no closing '}};'");

        var rows = new List<string>();
        var arms = new Stack<bool>();

        foreach (string raw in source[(head.Index + head.Length)..end].Split('\n'))
        {
            string line = Regex.Replace(raw, @"//.*$", string.Empty).Trim();

            var ifdef = Regex.Match(line, @"^#\s*if(?:def\s+(\w+)|\s+defined\s*\(\s*(\w+)\s*\))");
            if (ifdef.Success)
            {
                string macro = ifdef.Groups[1].Success ? ifdef.Groups[1].Value : ifdef.Groups[2].Value;
                arms.Push(defined.Contains(macro));
                continue;
            }

            if (Regex.IsMatch(line, @"^#\s*else"))
            {
                arms.Push(!arms.Pop());
                continue;
            }

            if (Regex.IsMatch(line, @"^#\s*endif"))
            {
                arms.Pop();
                continue;
            }

            if (line.StartsWith('#'))
                throw new InvalidDataException($"{name}[]: a directive this reader does not know: {line}");

            if (arms.Any(taken => !taken))
                continue;

            foreach (Match literal in Regex.Matches(line, "\"([^\"]*)\""))
            {
                if (literal.Groups[1].Value.Length > 0)
                    rows.Add(literal.Groups[1].Value);
            }
        }

        if (rows.Count == 0)
            throw new InvalidDataException($"{name}[] has no rows");

        return rows;
    }
}
