using System;
using System.IO;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A fact that needs one or more files from the repository next to the binary,
/// and is genuinely SKIPPED — not passed — when they are not there.
///
/// <para>
/// <b>WHY THIS EXISTS.</b> The shape it replaces is
/// <c>Assert.True(true, "…(skipped, not found)")</c> followed by a
/// <c>return</c>. That reports <b>PASSED</b>, not SKIPPED. A suite run from a
/// directory with no source tree beside it then reports a row of green for
/// checks that examined nothing, and the run is indistinguishable from one
/// where every parity check really held. <c>tools/vacuous-assert-audit</c>
/// exists to catch exactly that and caught five of them in one file.
/// </para>
///
/// <para>
/// <see cref="SharedSourceFactAttribute"/> is the same idea for
/// <c>src/game/shared</c> specifically. This one takes REPO-RELATIVE paths, so
/// it reaches <c>src/public</c>, <c>src/sourcesharp/game/server</c> and
/// anywhere else, and takes several because a fact that reads two files must
/// skip if either is missing rather than half-run.
/// </para>
///
/// <para>
/// The paths are looked up in the compat farm (<see cref="RepoLayout.SdkTree"/>),
/// which is where the facts themselves read their inputs — so the attribute
/// and the body cannot disagree about whether the file is present.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RepoSourceFactAttribute : FactAttribute
{
    /// <param name="relativePaths">
    /// Repo-relative paths, e.g. <c>src/public/const.h</c>. Forward slashes;
    /// they are split so the attribute reads the same on any platform.
    /// </param>
    public RepoSourceFactAttribute(params string[] relativePaths)
    {
        foreach (string relative in relativePaths)
        {
            if (Find(relative) is null)
            {
                Skip = $"{relative} is not in {RepoLayout.SdkTree} — this fact reads the "
                     + "source tree it is a parity check against";
                return;
            }
        }
    }

    /// <summary>The path if the farm has the file, else null.</summary>
    public static string? Find(string relative)
    {
        string path = RepoLayout.Sdk(relative.Split('/', '\\'));

        return File.Exists(path) ? path : null;
    }
}
