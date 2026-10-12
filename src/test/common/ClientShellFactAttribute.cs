using System;
using System.IO;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for a fact that reads
/// <c>src/sourcesharp/game/client/shell_standalone_client.cpp</c>, and skips —
/// visibly — when the compat farm does not have it.
///
/// <para>
/// The shape this replaces is
/// <c>if (code is null) { Assert.True(true, "…skipped…"); return; }</c>, which
/// reports a PASS. Same shape and same argument as
/// <see cref="SharedSourceFactAttribute"/>: xUnit never prints the message of an
/// assertion that held, so a deployed tree with no sources beside it and a tree
/// whose write-site census ran and found nothing produce byte-identical output —
/// and the whole value of a census is the difference between those two.
/// </para>
///
/// <para>
/// SEVEN OF THESE GUARDS WERE IN <c>StandaloneMirrorTests</c> ALONE, which is
/// what makes an attribute worth having rather than a fourth copy of the
/// pattern. Step 6b converted them while it was rewriting two of the censuses
/// they guard; <c>tools/vacuous-assert-audit</c>'s pin drops by the same seven.
/// </para>
///
/// <para>
/// xUnit 2.9.3 has no runtime <c>Assert.Skip</c> — that arrived in v3 — so the
/// decision is made at discovery, which is what a Skip-setting attribute is for.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ClientShellFactAttribute : FactAttribute
{
    public ClientShellFactAttribute()
    {
        if (ClientShellSource.Path is null)
        {
            Skip = "sourcesharp/game/client/shell_standalone_client.cpp is not in the "
                 + "compat farm — the census needs the source it reads";
        }
    }
}

/// <summary>
/// Where the client shell's source is, found once. Shared by
/// <see cref="ClientShellFactAttribute"/>, which runs at discovery, and the
/// facts themselves, which run after it.
/// </summary>
public static class ClientShellSource
{
    /// <summary>The file, or null when the compat farm does not have it.</summary>
    public static string? Path { get; } = RepoLayout.FindSdkFile(
        System.IO.Path.Combine("game", "client", "shell_standalone_client.cpp"));
}
