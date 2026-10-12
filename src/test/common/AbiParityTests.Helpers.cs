using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="AbiParityTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class AbiParityTests
{
    /// <summary>
    /// The engine slots a gamesystem source actually assigns.
    ///
    /// CODE ONLY: a commented-out "// engineApi.Foo = ..." must not count as
    /// wired. Against raw text it did, and the slot it names then stays a null
    /// pointer that every guarded call in Engine.cs skips in silence.
    ///
    /// Split out from the file read so the stripping can be exercised on a
    /// literal snippet — see AbiParityScanTests. Reading the real file is the
    /// only part that cannot be, and it is now the only part not covered.
    /// </summary>
    internal static HashSet<string> AssignedEngineSlots(string wiringSource)
        => Regex.Matches(CodeOnly(wiringSource), @"engineApi\.(\w+)\s*=")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Just the function pointers of a struct in the header, in declaration
    /// order — without the two plain fields, whose header and C# spellings
    /// differ on purpose.
    /// </summary>
    internal static List<string> PointerNamesOnly(string text, string structName)
    {
        // CODE ONLY, again, and here rather than only at the caller: a
        // commented-out "// void (*Foo)( ... );" in the header would otherwise
        // count as a declared slot, and the wiring check would then demand
        // wiring for a slot that does not exist. CodeOnly is idempotent, so
        // stripping text the caller already stripped costs a pass and nothing
        // else — and every entry point is safe on its own.
        text = CodeOnly(text);

        var names = new List<string>();

        // The header declares these as anonymous "typedef struct { ... } Name;",
        // so the name is at the END of the declaration and the body is found by
        // scanning back from it.
        int end = text.IndexOf($"}} {structName};", StringComparison.Ordinal);
        if (end < 0)
            return names;

        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);
        if (start < 0)
            return names;

        foreach (Match m in Regex.Matches(text[start..end], @"\(\s*\*\s*(\w+)\s*\)\s*\("))
            names.Add(m.Groups[1].Value);

        return names;
    }

    /// <summary>
    /// C/C++ source with its comments removed, so that a scan matches code and
    /// only code.
    ///
    /// This is not tidiness. Every regex in this file used to run over raw file
    /// text, and a commented-out line counted as a live one:
    ///
    ///     // engineApi.ExplosionSetMagnitude = ...
    ///
    /// satisfied "every declared slot is wired" while leaving that slot null.
    /// The check could not fail for the single input it was built to reject,
    /// and the ABI-bump decision leans on it. The version #define and the
    /// header's function-pointer scan had the same exposure, so the strip
    /// happens once at read time and every later match inherits it.
    ///
    /// Newlines inside a block comment are preserved, so any line number in a
    /// failure message still lines up with the file on disk. String and
    /// character literals are copied whole, so a "//" inside a literal is not
    /// mistaken for the start of a comment.
    ///
    /// Precedent in the native tests: SlurpCodeFromSourceRoot / CodeOnly in
    /// sdk_player_cast_test.cpp, and the StripComments helpers throughout
    /// src/sourcesharp/test.
    /// </summary>
    internal static string CodeOnly(string source)
    {
        var code = new System.Text.StringBuilder(source.Length);

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            // Line comment: the rest of THIS line goes, the newline stays.
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;

                if (i < source.Length)
                    code.Append('\n');

                continue;
            }

            // Block comment: everything but the newlines goes.
            if (c == '/' && next == '*')
            {
                i += 2;

                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                        code.Append('\n');

                    i++;
                }

                // Land on the '/' of "*/"; the loop's own i++ steps past it. An
                // unterminated comment runs to the end, which is what a
                // compiler would see too.
                i = Math.Min(i + 1, source.Length - 1);
                continue;
            }

            // A literal is copied whole, escapes included, so neither a "//"
            // inside it nor an escaped quote can confuse the scan.
            if (c == '"' || c == '\'')
            {
                char quote = c;
                code.Append(c);
                i++;

                while (i < source.Length && source[i] != quote)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        code.Append(source[i]);
                        i++;
                    }

                    code.Append(source[i]);
                    i++;
                }

                if (i < source.Length)
                    code.Append(source[i]);

                continue;
            }

            code.Append(c);
        }

        return code.ToString();
    }
}
