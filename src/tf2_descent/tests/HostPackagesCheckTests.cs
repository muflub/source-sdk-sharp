using System.Diagnostics;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// src/tf2_descent/Directory.Build.props refuses to restore without the host's
/// packages, and says which make target builds them (plan_host_sdk §2.5).
/// </summary>
public sealed class HostPackagesCheckTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SourceSharp.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("SourceSharp.slnx not found above " + AppContext.BaseDirectory);
    }

    private static (int ExitCode, string Output) CheckHostPackages(string hostRoot)
    {
        var dotnet = Environment.ProcessPath is { } p && Path.GetFileNameWithoutExtension(p) == "dotnet"
            ? p
            : "dotnet";
        var psi = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepoRoot(),
        };
        foreach (var arg in new[]
                 {
                     "msbuild", "src/tf2_descent/server/TF2.Descent.Server.csproj", "-t:CheckHostPackages",
                     "-nologo", "-nodeReuse:false", $"-p:HostRoot={hostRoot}",
                 })
            psi.ArgumentList.Add(arg);
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    [Fact]
    public void A_host_root_without_packages_fails_naming_make_host_pack()
    {
        var empty = Directory.CreateTempSubdirectory("tf2_descent-hostroot-").FullName + "/";
        try
        {
            var (exit, output) = CheckHostPackages(empty);

            Assert.NotEqual(0, exit);
            Assert.Contains("make host-pack", output);
            Assert.Contains(empty + "bin/nupkg", output);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void The_real_host_root_passes_the_check()
    {
        // The positive arm: the refusal above means nothing if the check
        // refuses every root. This one is the root the build itself restored
        // from, so its packages are there.
        var hostRoot = Path.GetFullPath(Path.Combine(RepoRoot(), "../source-sdk-sharp-host/"));
        var hostRootFromEnv = Environment.GetEnvironmentVariable("HOST_ROOT");
        if (!string.IsNullOrEmpty(hostRootFromEnv))
            hostRoot = Path.TrimEndingDirectorySeparator(hostRootFromEnv) + "/";

        var (exit, output) = CheckHostPackages(hostRoot);

        Assert.True(exit == 0, output);
        Assert.DoesNotContain("make host-pack", output);
    }
}
