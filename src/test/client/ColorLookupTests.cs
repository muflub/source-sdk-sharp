using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's colour-correction lookups (lane M1-A): the handle table
/// shell_worldrender.cpp kept natively is <see cref="ColorLookupTable"/>, and
/// the frame's reset is managed's, in the same pass as the re-weigh, at
/// FRAME_RENDER_START — stock's <c>OnRenderStart</c> resets the weights and
/// then lets each <c>C_ColorCorrection</c> think. Before this the native reset
/// ran at OnRenderStart after the re-weigh, which ran from the frame's update
/// after the render, so the view always drew with every weight at zero.
/// </summary>
public class ColorLookupTests
{
    //=========================================================================
    // The table.
    //=========================================================================

    [Fact]
    public void HandlesCountUpFromOne()
    {
        var table = new ColorLookupTable();

        Assert.Equal(1, table.Register(0x1000));
        Assert.Equal(2, table.Register(0x2000));
    }

    [Fact]
    public void ALookupThatWouldNotLoadIsNoHandle()
    {
        var table = new ColorLookupTable();

        Assert.Equal(0, table.Register(0));
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void AHandleNamesItsLookup()
    {
        var table = new ColorLookupTable();
        int handle = table.Register(0x1000);

        Assert.True(table.TryGet(handle, out nint lookup));
        Assert.Equal((nint)0x1000, lookup);
    }

    [Fact]
    public void AForgottenHandleNamesNothing()
    {
        var table = new ColorLookupTable();
        int handle = table.Register(0x1000);

        Assert.True(table.Forget(handle, out nint lookup));
        Assert.Equal((nint)0x1000, lookup);
        Assert.False(table.TryGet(handle, out _));
        Assert.False(table.Forget(handle, out _));
    }

    [Fact]
    public void AHandleIsNotReused()
    {
        var table = new ColorLookupTable();
        table.Forget(table.Register(0x1000), out _);

        Assert.Equal(2, table.Register(0x3000));
    }

    //=========================================================================
    // The frame: reset, then re-weigh.
    //=========================================================================

    private sealed class Weighed(Action<Vector> reweigh) : IWeightedCorrection
    {
        public void Reweigh(Vector playerOrigin) => reweigh(playerOrigin);
    }

    [Fact]
    public void TheFrameResetsTheWeightsBeforeItReweighsThem()
    {
        var engine = new RecordingEngine().Returns("Hud.LocalPlayerOrigin", (Vector?)new Vector(1f, 2f, 3f));
        var service = new ColorCorrections(engine, engine);
        service.Add(new Weighed(_ => ((IEngineVisibility)engine).ColorCorrectionSetWeight(1, 0.5f)));

        service.Frame();

        var calls = engine.Calls.Select(c => c.Member).Where(n => n.StartsWith("Visibility.ColorCorrection", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Visibility.ColorCorrectionResetWeights", "Visibility.ColorCorrectionSetWeight"], calls);
    }

    [Fact]
    public void TheFrameResetsWithNothingToWeigh()
    {
        // The plan's "a correction is active" input counts the weights since the
        // reset; with the last correction gone it must fall back to none.
        var engine = new RecordingEngine();

        new ColorCorrections(engine, engine).Frame();

        Assert.Contains(engine.Calls, c => c.Member == "Visibility.ColorCorrectionResetWeights");
    }

    //=========================================================================
    // Where in the frame: FRAME_RENDER_START, and the native reset gone.
    //=========================================================================

    private static string CodeOnly(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    private static string Body(string code, string head)
    {
        int at = code.IndexOf(head, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {head}");
        int open = code.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[open..(i + 1)];
        }
        throw new InvalidOperationException($"{head} has no closing brace");
    }

    [Fact]
    public void TheCorrectionsAreWeighedAtRenderStartAndNotInTheFrame()
    {
        string module = CodeOnly(File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs")));
        string onStage = Body(module, "public void OnStage(ClientFrameStage stage)");
        string renderStart = onStage[onStage.IndexOf("case ClientFrameStage.RenderStart:", StringComparison.Ordinal)..];
        renderStart = renderStart[..renderStart.IndexOf("break;", StringComparison.Ordinal)];

        Assert.Contains("_corrections?.Frame();", renderStart);
        Assert.DoesNotContain("_corrections", Body(module, "public void Frame(float frameTime)"));
    }

}
