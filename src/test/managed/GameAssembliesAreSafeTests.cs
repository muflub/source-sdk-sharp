extern alias client;
using System.Reflection;
using System.Security;
using System.Xml.Linq;

using client::SourceSharp.Client;
using client::SourceSharp.Client.Interop;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// SourceSharp.Server and SourceSharp.Client, which compile the shared source
/// (plan_side_isolation P7), never allow
/// unsafe code (the user, 2026-10-01). Function-pointer interop lives in each
/// side's interop assembly or in SourceSharp.Abi. A binding, a generated area or
/// a raw pointer that drifts back into a game assembly needs /unsafe turned on
/// again, and these facts say so.
/// </summary>
public class GameAssembliesAreSafeTests
{
    private static readonly string[] GameProjects =
    {
        "src/server/managed/SourceSharp.Server.csproj",
        "src/client/managed/SourceSharp.Client.csproj",
    };

    private static readonly string[] InteropProjects =
    {
        "src/server/interop/SourceSharp.Server.Interop.csproj",
        "src/client/interop/SourceSharp.Client.Interop.csproj",
    };

    /// <summary>
    /// Whether an MSBuild file sets AllowUnsafeBlocks to true. A missing file is
    /// a failure, not a "no": a reader that found nothing would pass.
    /// </summary>
    private static bool SetsUnsafe(string relative)
    {
        string path = RepoLayout.Repo(relative.Split('/'));
        Assert.True(File.Exists(path), $"{relative} was not found; a check that read nothing would pass");
        return XDocument.Load(path).Descendants()
            .Any(e => e.Name.LocalName == "AllowUnsafeBlocks"
                   && string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheGameProjectsDoNotTurnOnUnsafe()
    {
        foreach (string project in GameProjects)
            Assert.False(SetsUnsafe(project), $"{project} sets AllowUnsafeBlocks; its pointer code belongs in an interop assembly or SourceSharp.Abi");
    }

    [Fact]
    public void TheSharedBuildPropsDoNotTurnOnUnsafe()
        => Assert.False(SetsUnsafe("Directory.Build.props"),
                        "Directory.Build.props sets AllowUnsafeBlocks, which turns it on for every game assembly");

    /// <summary>The control: the same reader sees the flag where it is set.</summary>
    [Fact]
    public void TheInteropProjectsDoTurnOnUnsafe()
    {
        foreach (string project in InteropProjects)
            Assert.True(SetsUnsafe(project), $"{project} no longer sets AllowUnsafeBlocks, so the project reader is not seeing it");
    }

    private static bool CompiledUnsafe(Type inAssembly)
        => inAssembly.Module.GetCustomAttribute<UnverifiableCodeAttribute>() is not null;

    /// <summary>
    /// The built modules, not only the project text: the compiler stamps
    /// UnverifiableCode on a module built with /unsafe, whatever set it.
    /// </summary>
    [Fact]
    public void TheGameModulesWereNotCompiledUnsafe()
    {
        Assert.False(CompiledUnsafe(typeof(ServerModule)), "SourceSharp.Server was compiled with /unsafe");
        Assert.False(CompiledUnsafe(typeof(WeaponScriptDatabase)), "the shared source (in SourceSharp.Server) was compiled with /unsafe");
        Assert.False(CompiledUnsafe(typeof(client::SourceSharp.Shared.WeaponScriptDatabase)), "the shared source (in SourceSharp.Client) was compiled with /unsafe");
        Assert.False(CompiledUnsafe(typeof(ClientModule)), "SourceSharp.Client was compiled with /unsafe");
    }

    [Fact]
    public void TheInteropModulesWereCompiledUnsafe()
    {
        Assert.True(CompiledUnsafe(typeof(EntitiesBinding)), "SourceSharp.Server.Interop carries no UnverifiableCode, so the module check cannot see /unsafe");
        Assert.True(CompiledUnsafe(typeof(ClientRuntimeBinding)), "SourceSharp.Client.Interop carries no UnverifiableCode, so the module check cannot see /unsafe");
    }
}
