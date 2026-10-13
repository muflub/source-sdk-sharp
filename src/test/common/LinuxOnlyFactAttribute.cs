using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts whose subject exists only on Linux: they skip on any other OS, naming
/// why, and run everywhere Linux is.
///
/// <para>
/// WHAT GOES UNDER IT, and nothing else: a fact that reads something only a
/// Linux build or a Linux host has. The Linux build's CMake graph (the
/// gcc/clang build tree's compile database and ninja file, which a Windows
/// checkout never configures), <c>/proc</c>, a bash or Python tool run as a
/// program, Unix file modes, a golden recorded bit for bit against glibc's
/// libm (the Windows C runtime rounds the last bit of sin, cos and pow
/// differently), and pixel goldens rendered with Linux's fonts. It is the
/// managed counterpart of the native suite's <c>LINUX_ONLY</c>
/// (src/test/native/CMakeLists.txt), and each use names its reason.
/// </para>
///
/// <para>
/// A FACT THAT FAILS ON WINDOWS FOR ANY OTHER REASON IS A BUG, not a candidate:
/// a path spelled with '/', a separator compared literally, a Linux library
/// name where the platform's belongs. Those are fixed, as ModsConfig.IsWithin
/// and tools/setup's game_dir keys were.
/// </para>
/// </summary>
public static class LinuxOnly
{
    /// <summary>The skip text for a reason: says it is a platform skip, and why.</summary>
    public static string SkipText(string reason) => "Linux only: " + reason;
}

/// <summary>A <see cref="FactAttribute"/> that skips off Linux (<see cref="LinuxOnly"/>), with its reason.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LinuxOnlyFactAttribute : FactAttribute
{
    /// <summary>Skips unless this process runs on Linux.</summary>
    public LinuxOnlyFactAttribute(string reason)
        : this(reason, OperatingSystem.IsLinux())
    {
    }

    /// <summary>Either platform, so both branches are testable.</summary>
    internal LinuxOnlyFactAttribute(string reason, bool onLinux)
    {
        Reason = reason;
        if (!onLinux)
            Skip = LinuxOnly.SkipText(reason);
    }

    /// <summary>Why the fact has no subject off Linux.</summary>
    public string Reason { get; }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips off Linux (<see cref="LinuxOnly"/>), with its reason.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LinuxOnlyTheoryAttribute : TheoryAttribute
{
    /// <summary>Skips unless this process runs on Linux.</summary>
    public LinuxOnlyTheoryAttribute(string reason)
        : this(reason, OperatingSystem.IsLinux())
    {
    }

    /// <summary>Either platform, so both branches are testable.</summary>
    internal LinuxOnlyTheoryAttribute(string reason, bool onLinux)
    {
        Reason = reason;
        if (!onLinux)
            Skip = LinuxOnly.SkipText(reason);
    }

    /// <summary>Why the theory has no subject off Linux.</summary>
    public string Reason { get; }
}

/// <summary>
/// A Linux-only fact (<see cref="LinuxOnly"/>) that also reads the debug
/// build's native output: <c>bin/debug/bin/linux64/server.so</c> and the game
/// directory around it.
///
/// <para>
/// CI runs the managed legs BEFORE the native build (user ruling 2026-10-13:
/// "run managed tests after the managed build (before native build)"), so in
/// that step the output is not there yet. Under CI, and only while it is
/// absent, the fact skips, saying so; the workflow's later step "Test (managed
/// facts that read the native build)" runs the facts carrying
/// <see cref="Trait"/> again once <c>make check</c> has built debug. Anywhere
/// else a missing build stays a failure: the fact's own message says to build.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LinuxDebugBuildFactAttribute : FactAttribute
{
    /// <summary>The trait the later CI step filters on: <c>Needs=DebugBuild</c>.</summary>
    public const string Trait = "Needs";

    /// <summary>The trait's value.</summary>
    public const string TraitValue = "DebugBuild";

    /// <summary>The file whose absence means the native build has not run.</summary>
    public static string Witness => RepoLayout.Repo("bin", "debug", "bin", "linux64", "server.so");

    /// <summary>Skips off Linux, and on CI before the native build.</summary>
    public LinuxDebugBuildFactAttribute(string reason)
        : this(reason, OperatingSystem.IsLinux(),
               NotOnCi.IsCi(Environment.GetEnvironmentVariable(NotOnCi.Variable)),
               () => File.Exists(Witness))
    {
    }

    /// <summary>Every input injectable, so each branch is testable.</summary>
    internal LinuxDebugBuildFactAttribute(string reason, bool onLinux, bool onCi, Func<bool> built)
    {
        Reason = reason;
        if (!onLinux)
            Skip = LinuxOnly.SkipText(reason);
        else if (onCi && !built())
            Skip = BeforeTheBuild;
    }

    /// <summary>The skip text on CI before the native build.</summary>
    public const string BeforeTheBuild =
        "not built yet: CI runs the managed legs before the native build; " +
        "the step \"Test (managed facts that read the native build)\" runs this after make check";

    /// <summary>Why the fact has no subject off Linux.</summary>
    public string Reason { get; }
}
