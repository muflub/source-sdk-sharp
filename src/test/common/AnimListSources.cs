using System.Text;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Finding the C++ the tables mirror, and reading an enum out of it.
/// </summary>
internal static class AnimListSources
{
    /// <summary>
    /// The named file under <c>src/game/shared</c> in the compat farm, or null
    /// when the farm has no such file.
    /// </summary>
    internal static string? Find(string name)
    {
        // ANCHORED, NOT WALKED. This used to walk up from the test binary
        // looking for the FILE, and in a git worktree that escapes into the
        // main checkout above it, so a branch that DELETED one of these would
        // silently read the other tree's copy and report on a repository
        // nobody is editing. plan_shim.md Phase 2d deletes
        // game/shared/animation.cpp from this very directory, so the hazard is
        // live rather than theoretical. The farm (RepoLayout.SdkTree) is the
        // one tree these tests read, and an evicted file has no link there.
        string candidate = RepoLayout.Sdk("src", "game", "shared", name);

        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// The member names of a <c>typedef enum { … } Name;</c>, in order.
    ///
    /// <para>
    /// Comments are stripped first, and both tables need it: <c>ai_activity.h</c>
    /// is a third comment by line and <c>eventlist.h</c> carries an old
    /// <c>#define</c> in a trailing comment on nearly every member.
    /// </para>
    /// </summary>
    internal static List<string> EnumMembers(string source, string typeName)
    {
        Match body = Regex.Match(source, @"typedef enum\s*\{(.*?)\}\s*" + typeName + @"\s*;",
                                 RegexOptions.Singleline);

        Assert.True(body.Success, $"the {typeName} enum was found in the header");

        string text = Regex.Replace(body.Groups[1].Value, @"/\*.*?\*/", string.Empty,
                                    RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", string.Empty);

        return [.. text.Split(',')
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 0)
                       .Select(t => t.Split('=')[0].Trim())];
    }
}
