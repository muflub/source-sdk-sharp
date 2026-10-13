using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The one switch for facts that cannot pass on CI's runners: SOURCESHARP_CI=1,
/// which .github/workflows/ci.yml sets for every test step and nothing else
/// does. Read once per attribute, at discovery.
///
/// <para>
/// WHAT GOES UNDER IT, and nothing else (user ruling 2026-10-13, "skip non ci
/// friendly tests"): a fact whose answer depends on an input a runner does not
/// have, never on this tree's code. A golden recorded bit for bit on the
/// owner's machine (another CPU or C library rounds the last bit
/// differently), a fact that reads the fork's own edits or comments (a runner
/// has the public patch series, which carries neither), and a fact that needs
/// a Steam game install. Each names its reason, and the reason is printed
/// with the skip.
/// </para>
///
/// <para>
/// ON EVERY OTHER MACHINE THESE FACTS RUN AND FAIL AS BEFORE. The skip is CI's
/// alone, so `make check` on the owner's machine stays the gate it was, and a
/// missing input there is still red, as the repo's rules require.
/// </para>
/// </summary>
public static class NotOnCi
{
    /// <summary>The variable CI sets.</summary>
    public const string Variable = "SOURCESHARP_CI";

    /// <summary>True when <paramref name="value"/> is CI's setting: exactly "1".</summary>
    public static bool IsCi(string? value) => value == "1";

    /// <summary>The skip text for a reason: says it is CI's skip, and why.</summary>
    public static string SkipText(string reason) => "not on CI: " + reason;
}

/// <summary>A <see cref="FactAttribute"/> that skips on CI (<see cref="NotOnCi"/>), with its reason.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NotOnCiFactAttribute : FactAttribute
{
    /// <summary>Skips when this process runs under CI.</summary>
    public NotOnCiFactAttribute(string reason)
        : this(reason, Environment.GetEnvironmentVariable(NotOnCi.Variable))
    {
    }

    /// <summary>Any setting of the variable, so both branches are testable.</summary>
    internal NotOnCiFactAttribute(string reason, string? ciValue)
    {
        Reason = reason;
        if (NotOnCi.IsCi(ciValue))
            Skip = NotOnCi.SkipText(reason);
    }

    /// <summary>Why the fact cannot pass on CI.</summary>
    public string Reason { get; }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips on CI (<see cref="NotOnCi"/>), with its reason.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NotOnCiTheoryAttribute : TheoryAttribute
{
    /// <summary>Skips when this process runs under CI.</summary>
    public NotOnCiTheoryAttribute(string reason)
        : this(reason, Environment.GetEnvironmentVariable(NotOnCi.Variable))
    {
    }

    /// <summary>Any setting of the variable, so both branches are testable.</summary>
    internal NotOnCiTheoryAttribute(string reason, string? ciValue)
    {
        Reason = reason;
        if (NotOnCi.IsCi(ciValue))
            Skip = NotOnCi.SkipText(reason);
    }

    /// <summary>Why the theory cannot pass on CI.</summary>
    public string Reason { get; }
}
