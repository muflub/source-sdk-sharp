using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="SourceFileNamesExistTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public sealed partial class SourceFileNamesExistTests
{
    /// <summary>
    /// C# with <c>//</c> and <c>/* */</c> blanked and everything else — string
    /// literals above all — left exactly where it was, so a match's index is
    /// still the real one.
    /// </summary>
    internal static string StripComments(string text) => Strip(text, blankLiterals: false);

    /// <summary>
    /// The same, with the CONTENTS of every string and character literal
    /// blanked too.
    ///
    /// <para>
    /// The vacuous-assertion count needs this and the name scan must not have
    /// it. A test that asserts on an expected message, or quotes an assertion
    /// in a raw-string fixture, puts the token <c>Assert.True(true</c> inside a
    /// literal — and the count went from 108 to 109 the moment this file's own
    /// fixture did exactly that.
    /// </para>
    /// </summary>
    internal static string StripCommentsAndLiterals(string text) => Strip(text, blankLiterals: true);

    private static string Strip(string text, bool blankLiterals)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int j = text.IndexOf('\n', i);
                j = j < 0 ? text.Length : j;
                Blank(sb, text, i, j);
                i = j;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int j = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                j = j < 0 ? text.Length : j + 2;
                Blank(sb, text, i, j);
                i = j;
            }
            else if (c == '"' && Quotes(text, i) >= 3)
            {
                // A RAW STRING LITERAL. The old audit's scanner said this suite
                // used none and did not handle them; it does now, and an
                // unhandled one reads as code.
                int fence = Quotes(text, i);
                int j = i + fence;

                while (j < text.Length && Quotes(text, j) != fence)
                    j++;

                j = j < text.Length ? j + fence : text.Length;
                Emit(sb, text, i, j, blankLiterals, keepEdges: fence);
                i = j;
            }
            else if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                int j = i + 2;
                while (j < text.Length)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }

                        j++;
                        break;
                    }

                    j++;
                }

                Emit(sb, text, i, j, blankLiterals, keepEdges: 1);
                i = j;
            }
            else if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < text.Length)
                {
                    if (text[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }

                    if (text[j] == c || text[j] == '\n')
                    {
                        j++;
                        break;
                    }

                    j++;
                }

                j = Math.Min(j, text.Length);
                Emit(sb, text, i, j, blankLiterals, keepEdges: 1);
                i = j;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>The run of <c>"</c> starting at <paramref name="at"/>.</summary>
    private static int Quotes(string text, int at)
    {
        int n = 0;
        while (at + n < text.Length && text[at + n] == '"')
            n++;

        return n;
    }

    /// <summary>
    /// A literal, kept whole or blanked between its delimiters. The delimiters
    /// themselves survive either way, because the name scan matches on the
    /// quotes around a name.
    /// </summary>
    private static void Emit(StringBuilder sb, string text, int from, int to, bool blank, int keepEdges)
    {
        if (!blank)
        {
            sb.Append(text, from, to - from);
            return;
        }

        int head = Math.Min(from + keepEdges, to);
        int tail = Math.Max(head, to - keepEdges);

        sb.Append(text, from, head - from);
        Blank(sb, text, head, tail);
        sb.Append(text, tail, to - tail);
    }

    private static void Blank(StringBuilder sb, string text, int from, int to)
    {
        for (int k = from; k < to; k++)
            sb.Append(text[k] == '\n' ? '\n' : ' ');
    }

    /// <summary>
    /// The test projects' source directories in the farm. A missing one is a
    /// failure, not an empty scan: a scan that reads nothing passes.
    /// </summary>
    internal static IEnumerable<string> TestDirectories(string root)
    {
        foreach (string project in (string[])["SourceSharp.Tests", "SourceSharp.Server.Tests",
                                              "SourceSharp.Client.Tests", "SourceSharp.Tests.Common"])
        {
            string dir = Path.Combine(root, "src", "sourcesharp", "managed", project);
            Assert.True(Directory.Exists(dir), $"the test sources at {dir}");
            yield return dir;
        }
    }
}
