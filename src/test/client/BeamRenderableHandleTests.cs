using System.IO;
using System.Text.RegularExpressions;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A pooled beam renderable leaves the leaf system with no handle, even when
/// the leaf system was purged first (level shutdown). Found live by
/// tools/live-gate's phys-convert step, 2026-10-12: two NewRenderable asserts
/// per visit to ss_sandbox after the first, 0 with the reset (3 passes each).
/// The pool and the leaf system need an engine, so this reads the code.
/// </summary>
public class BeamRenderableHandleTests
{
    private static string Body(string signature)
    {
        string code = Regex.Replace(File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_beam_render.cpp")),
                                    @"//[^\n]*", "");
        int at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature + " is not in shell_beam_render.cpp");
        int end = code.IndexOf("\n}", at, StringComparison.Ordinal);
        Assert.True(end > at);
        return code[at..end];
    }

    [Fact]
    public void ARemovedBeamRenderableForgetsItsHandleBeforeItIsPooled()
    {
        string body = Body("void SourceSharp_BeamRenderableRemove(");

        int removed = body.IndexOf("ClientLeafSystem()->RemoveRenderable( p->RenderHandle() );", StringComparison.Ordinal);
        int reset = body.IndexOf("p->RenderHandle() = INVALID_CLIENT_RENDER_HANDLE;", StringComparison.Ordinal);
        int pooled = body.IndexOf("g_FreeBeamRenderables.AddToTail( nRenderable );", StringComparison.Ordinal);

        Assert.True(removed >= 0, "the remove still asks the leaf system");
        Assert.True(reset > removed, "and then resets the handle itself");
        Assert.True(pooled > reset, "before the renderable goes back to the pool");
    }
}
