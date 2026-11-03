using System.Globalization;
using System.Text.RegularExpressions;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The stock-enum oracle's parser and its mirror assertion, for the rules'
/// and the modes' enums: an enum body read out of a pristine header (comments
/// and preprocessor lines dropped, C's implicit numbering), and a C# enum held
/// to it member for member and value for value.
/// </summary>
internal static class StockEnums
{
    public static void AssertMirrors<TEnum>(IReadOnlyList<(string Name, int Value)> stock, string prefix, int expected,
                                            Func<string, string?> renames)
        where TEnum : struct, Enum
    {
        Assert.Equal(expected, stock.Count);
        Assert.Equal(stock.Count, Enum.GetValues<TEnum>().Length);

        foreach (var (stockName, value) in stock)
        {
            string member = renames(stockName) ?? Pascal(Strip(stockName, prefix));
            Assert.True(Enum.TryParse<TEnum>(member, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed),
                        $"{stockName} has no {typeof(TEnum).Name}.{member}");
            int mine = Convert.ToInt32(parsed, CultureInfo.InvariantCulture);
            Assert.True(mine == value, $"{typeof(TEnum).Name}.{member} is {mine}, stock {stockName} is {value}");
        }
    }

    private static string Strip(string name, string prefix)
    {
        Assert.True(name.StartsWith(prefix, StringComparison.Ordinal), $"{name} lacks the prefix {prefix}");
        return name[prefix.Length..];
    }

    private static string Pascal(string upperSnake)
        => string.Concat(upperSnake.Split('_').Select(w => w[..1] + w[1..].ToLowerInvariant()));

    /// <summary>
    /// The enumerators of the enum body that <paramref name="anchor"/> opens
    /// (<c>enum X</c>) or sits in (an enumerator of an anonymous enum), in
    /// order, with C's implicit numbering; comments and preprocessor lines are
    /// dropped. Only literal values are accepted; anything else throws.
    /// </summary>
    public static IReadOnlyList<(string Name, int Value)> Enumerators(string text, string anchor)
    {
        int at = text.IndexOf(anchor, StringComparison.Ordinal);
        int open = at < 0 ? -1
            : anchor.StartsWith("enum ", StringComparison.Ordinal) ? text.IndexOf('{', at)
            : text.LastIndexOf('{', at);
        if (open < 0)
            throw new InvalidDataException($"no '{anchor}' enum in the header");
        int close = text.IndexOf('}', open);

        string body = Regex.Replace(text[(open + 1)..close], @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        body = Regex.Replace(body, @"^\s*#[^\n]*$", "", RegexOptions.Multiline);

        var result = new List<(string, int)>();
        int next = 0;
        foreach (string raw in body.Split(','))
        {
            string item = raw.Trim();
            if (item.Length == 0)
                continue;

            string name = item;
            int value = next;
            int eq = item.IndexOf('=');
            if (eq >= 0)
            {
                name = item[..eq].Trim();
                value = int.TryParse(item[(eq + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                    ? v
                    : throw new InvalidDataException($"'{item}' is not a literal enumerator");
            }

            result.Add((name, value));
            next = value + 1;
        }

        if (result.Count == 0)
            throw new InvalidDataException($"the '{anchor}' enum is empty");
        return result;
    }
}
