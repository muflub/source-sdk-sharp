using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for a fact that reads a file out of
/// <c>src/game/shared</c>, and skips — visibly — when the compat farm does not
/// have it.
///
/// <para>
/// The shape this replaces is
/// <c>if (path is null) { Assert.True(true, "…skipped…"); return; }</c>, which
/// reports a PASS. It is the same shape and the same argument as
/// <see cref="SandboxMapFactAttribute"/>, arrived at from the other direction:
/// <c>tools/vacuous-assert-audit</c> refused four new ones and named the
/// alternative. xUnit never prints the message of an assertion that held, so a
/// deployed tree with no sources beside it and a tree whose tables were read
/// and agreed produce byte-identical output — and the whole value of a parity
/// fact is the difference between those two.
/// </para>
///
/// <para>
/// WHY THESE FACTS ARE SKIPPABLE AT ALL, given that they are the only thing
/// standing between the managed activity table and the enum it mirrors: the
/// suite has to run from the deployed managed directory too, where there is no
/// <c>src/</c>. What keeps that from being a hole is that the tables' OWN
/// behaviour — the lookups, the round trip through the packer — is asserted by
/// facts with no file in them, and CI runs from the checkout, where these run.
/// A skip that is invisible would be the hole; a skip the runner prints by name
/// is a reader being told which half ran.
/// </para>
///
/// <para>
/// xUnit 2.9.3 has no runtime <c>Assert.Skip</c> — that arrived in v3 — so the
/// decision is made at discovery, which is what a Skip-setting attribute is
/// for.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SharedSourceFactAttribute : FactAttribute
{
    /// <param name="fileName">
    /// The file's name under <c>src/game/shared</c>, e.g. <c>ai_activity.h</c>.
    /// The fact reads it with <c>AnimListSources.Find</c>, which searches the
    /// same way.
    /// </param>
    public SharedSourceFactAttribute(string fileName)
    {
        if (AnimListSources.Find(fileName) is null)
        {
            Skip = $"src/game/shared/{fileName} is not in the compat farm — "
                 + "the parity check needs the source tree it mirrors";
        }
    }
}
