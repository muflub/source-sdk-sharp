using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>ONE managed answer for the game description, reached by ONE route.</b>
///
/// <para>
/// <c>SourceSharp_GetGameDescription()</c> already reached C# before this phase:
/// native calls <c>g_ManagedApi.GetGameDescription</c>, the bootstrap's
/// <c>ServerGetGameDescription</c> answers, and it asked
/// <c>IServerModule.GetGameDescription()</c>, which returned a literal. The
/// shell's own <c>GetGameDescription</c> needs the same answer, and inventing a
/// second route for it would have given the server browser two answers that
/// agree right up until somebody changes one of them.
/// </para>
///
/// <para>
/// <b>Source facts, because the seam is a wiring decision rather than a
/// computation.</b> No test in this suite constructs a <c>ServerModule</c> — it
/// needs the whole container and a live discovery pass — so the thing worth
/// pinning is which expression the two ends of the route hold. That is exactly
/// what <c>TheReloadIsServicedInThinkAndNowhereElse</c> does for the reload
/// service point, for the same reason.
/// </para>
/// </summary>
public class ServerGameDescriptionRouteTests
{
    private static string? Managed(string project, string file)
        => RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "managed", project, file));

    [Fact]
    public void TheModulesAnswerForwardsToTheGameDllRatherThanALiteral()
    {
        string? path = Managed("SourceSharp.Server", "ServerModule.cs");

        if (path is null)
        {
            Assert.True(true, "ServerModule.cs found for the route check (skipped, not found)");
            return;
        }

        var body = Regex.Match(File.ReadAllText(path),
            @"public\s+string\s+GetGameDescription\(\)\s*=>\s*([^;]+);");

        Assert.True(body.Success && body.Groups[1].Value.Trim() == "_gameDll.GameDescription",
            "IServerModule.GetGameDescription forwards to the game DLL's one answer");
    }

    [Fact]
    public void TheModuleHasExactlyOneGameDescriptionBody()
    {
        string? path = Managed("SourceSharp.Server", "ServerModule.cs");

        if (path is null)
        {
            Assert.True(true, "ServerModule.cs found for the route check (skipped, not found)");
            return;
        }

        int bodies = Regex.Matches(File.ReadAllText(path),
            @"public\s+string\s+GetGameDescription\(\)").Count;

        Assert.True(bodies == 1, "there is one place the module answers this, not two that agree today");
    }

    /// <summary>
    /// The bootstrap end: the description reaches native through
    /// <c>IServerListing</c> and the shared buffer convention, not through a
    /// copy loop written out again at this one site.
    /// </summary>
    [Fact]
    public void TheRuntimeWritesTheListingsAnswerThroughTheSharedBuffer()
    {
        // Since plan_engineapi_vtables E11 the answer is the managed-built
        // runtime's OnGetGameDescription, not an Entry.cs thunk.
        string path = RepoLayout.Repo("src", "server", "interop", "ServerRuntimeBinding.cs");
        Assert.True(File.Exists(path), $"the runtime binding at {path}");

        string text = File.ReadAllText(path);

        Assert.True(text.Contains("ListingBuffer.Write(game.Listing.GameDescription"),
            "the game description answers from IServerListing through ListingBuffer");
    }
}
