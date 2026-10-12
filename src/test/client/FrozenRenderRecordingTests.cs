using System.Globalization;
using System.IO.Compression;

using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The stock render recordings frozen by C7 step 3 (user ruling 2026-10-12,
/// "freeze recordings": the stock path leaves and these are the reference for
/// good). Each must carry what a later port reads by name, since no stock
/// frame can be recorded again: every convar and the engine's answers.
/// </summary>
public class FrozenRenderRecordingTests
{
    private static string Dir(string name) => RepoLayout.Repo("src", "test", "client", "Fixtures", name);

    private static IEnumerable<(string Name, string Text)> Recordings()
    {
        foreach (string path in Directory.GetFiles(Dir("render-stock"), "*.txt").Order(StringComparer.Ordinal))
            yield return (Path.GetFileName(path), File.ReadAllText(path));
        foreach (string path in Directory.GetFiles(Dir("post-stock"), "*.txt.gz").Order(StringComparer.Ordinal))
        {
            using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            using var reader = new StreamReader(gz);
            yield return (Path.GetFileName(path), reader.ReadToEnd());
        }
    }

    private static RenderTrace Post(string name) =>
        RenderTrace.Parse(Recordings().Single(r => r.Name == name + ".txt.gz").Text);

    [Fact]
    public void EnvironmentAndConvarLinesAreReadByNameAndAreNotCalls()
    {
        const string text = "frame\tpath=0 framecount=7 rect=0,0,640,480\nenv\thdrtype\t1\ncvar\tmat_bloomscale\t0.5\n" +
                            "ctx.Flush\nq\tmaterial.var\tdev/x\t$a\tf 1\n";
        RenderTrace t = RenderTrace.Parse(text);
        Assert.Equal(["ctx.Flush"], t.Calls);
        Assert.Equal("1", t.Environment["hdrtype"]);
        Assert.Equal("0.5", t.ConVars["MAT_BLOOMSCALE"]);
        Assert.Single(t.Queries);
    }

    [Fact]
    public void EveryRecordingIsAStockFrameWithItsConvarsAndEnvironment()
    {
        int n = 0;
        foreach ((string name, string text) in Recordings())
        {
            n++;
            RenderTrace t = RenderTrace.Parse(text);
            Assert.True(t.PlanNote == null && text.Contains("frame\tpath=0 ", StringComparison.Ordinal), $"{name} is not a stock frame");
            Assert.True(t.ConVars.Count > 1000, $"{name}: {t.ConVars.Count} convars");
            foreach (string key in new[] { "realtime", "hdrtype", "dxlevel", "maphashdrlighting", "tonemapscalelinear", "viewport", "customtonemap" })
                Assert.True(t.Environment.ContainsKey(key), $"{name}: no env {key}");
            Assert.True(t.Calls.Count > 100, $"{name}: {t.Calls.Count} calls");
        }
        Assert.Equal(5 + 20, n);
    }

    [Fact]
    public void TheSandboxTonemapsAndBloomsAndLockdownDoesNot()
    {
        RenderTrace sandbox = Post("post-sandbox-default"), lockdown = Post("post-lockdown-default");
        Assert.Equal("1", sandbox.Environment["hdrtype"]);
        Assert.Equal("0", lockdown.Environment["hdrtype"]);
        Assert.Contains(sandbox.Calls, c => c.StartsWith("ctx.DrawScreenSpaceRectangle\tmaterial=dev/bloomadd ", StringComparison.Ordinal));
        Assert.DoesNotContain(lockdown.Calls, c => c.StartsWith("ctx.DrawScreenSpaceRectangle", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMotionBlurFramesHoldTheValuesTheProxyBound()
    {
        foreach (string name in new[] { "post-sandbox-motionblur", "post-lockdown-motionblur" })
        {
            RenderTrace t = Post(name);
            Assert.Equal("1", t.ConVars["mat_motion_blur_enabled"]);
            string line = t.Queries.Select(q => q.Text).Single(q => q.StartsWith("material.var\tdev/motion_blur\t$motionblurinternal\t", StringComparison.Ordinal));
            float[] v = line[(line.LastIndexOf(' ') + 1)..].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            Assert.True(v.Length == 4 && v.Any(x => x != 0f), $"{name}: {line}");
        }
    }

    [Fact]
    public void ThePostRecordingsReadmeNamesEachFile()
    {
        string readme = File.ReadAllText(Path.Combine(Dir("post-stock"), "README.md"));
        foreach (string path in Directory.GetFiles(Dir("post-stock"), "*.txt.gz"))
            Assert.Contains("`" + Path.GetFileName(path)[..^".txt.gz".Length] + "`", readme, StringComparison.Ordinal);
    }

    // RenderInputs may grow at its end; a frozen frame's shorter bytes still read.
    [Fact]
    public void AnOlderShorterInputsLayoutReadsWithItsMissingTailAsZero()
    {
        string text = File.ReadAllText(Path.Combine(Dir("render-stock"), "dm_lockdown-pose1.txt"));
        RenderTrace whole = RenderTrace.Parse(text);
        Assert.NotNull(whole.Inputs());
        string hex = text.Split('\n').Single(l => l.StartsWith("inbytes\t", StringComparison.Ordinal))["inbytes\t".Length..].TrimEnd('\r');
        string cut = hex[..^(64 * 4 * 2)];
        RenderInputs? shorter = RenderTrace.Parse("inbytes\t" + cut + "\n").Inputs();
        Assert.NotNull(shorter);
        RenderInputs w = whole.Inputs()!.Value, s = shorter!.Value;
        Assert.Equal(w.FrameCount, s.FrameCount);
        Assert.Equal(w.FlashlightOn, s.FlashlightOn);
        Assert.NotEqual(0f, w.Cvar(RenderCvar.r_skybox));
        Assert.Equal(0f, s.Cvar(RenderCvar.r_skybox));
        string overlong = new string('0', 2 * (System.Runtime.InteropServices.Marshal.SizeOf<RenderInputs>() + 1));
        Assert.Null(RenderTrace.Parse("inbytes\t" + overlong + "\n").Inputs());
    }

    // C7 step 3: a live plan frame is held to a frozen frame from another
    // session, whose entities the map numbered apart. Shifting every entity
    // index of a frozen frame changes nothing across sessions, and the plain
    // compare still sees the shift.
    [Fact]
    public void AFrameWithItsEntitiesRenumberedIsTheSameFrameAcrossSessions()
    {
        (_, string recorded) = Recordings().First(r => r.Name == "dm_lockdown-pose1.txt");
        // The calls alone: the scenes number their entries' entities first, and
        // shifting those means rewriting their bytes.
        string text = string.Join('\n', recorded.Split('\n').Where(l => !l.StartsWith("scene", StringComparison.Ordinal)));
        RenderTrace frozen = RenderTrace.Parse(text);
        Assert.Contains(frozen.Calls, c => c.Contains("\tent=", StringComparison.Ordinal));
        string shifted = System.Text.RegularExpressions.Regex.Replace(text, @"(model\.\w+\tent=)(\d+)",
            m => m.Groups[1].Value + (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) is int e && e > 0 ? e + 12 : 0));
        RenderTrace live = RenderTrace.Parse(shifted);
        Assert.NotNull(RenderTrace.Compare(frozen, live));
        Assert.Null(RenderTrace.CompareAcrossSessions(frozen, live));
    }

    // Two entities that trade places are not the same frame, whatever their numbers.
    [Fact]
    public void TwoEntitiesTradingDrawsDifferAcrossSessions()
    {
        const string a = "frame\tpath=0 framecount=1 rect=0,0,8,8\nmodel.DrawModelEx\tent=5 model=x.mdl\nmodel.DrawModelEx\tent=9 model=y.mdl\n";
        const string b = "frame\tpath=1 framecount=1 rect=0,0,8,8\nmodel.DrawModelEx\tent=17 model=y.mdl\nmodel.DrawModelEx\tent=21 model=x.mdl\n";
        const string c = "frame\tpath=1 framecount=1 rect=0,0,8,8\nmodel.DrawModelEx\tent=17 model=x.mdl\nmodel.DrawModelEx\tent=21 model=y.mdl\n";
        Assert.NotNull(RenderTrace.CompareAcrossSessions(RenderTrace.Parse(a), RenderTrace.Parse(b)));
        Assert.Null(RenderTrace.CompareAcrossSessions(RenderTrace.Parse(a), RenderTrace.Parse(c)));
    }

    private const string Push = "ctx.SetFrameBufferCopyTexture\ttexture=_rt_poweroftwofb index=0\n";
    private const string Draw = "model.DrawModelEx\tent=5 model=x.mdl\n";

    private static RenderTrace CopyFrame(string start, string restore, string tail = "") =>
        RenderTrace.Parse("env\tfbcopy0\t" + start + "\n" + "frame\tpath=1 framecount=1 rect=0,0,8,8\n" + Push + Draw
                          + "ctx.SetFrameBufferCopyTexture\ttexture=" + restore + " index=0\n" + tail);

    // The copy texture a frame starts with is the session's history (ss_sandbox's
    // refraction leaves _rt_fullframefb behind): a frame restoring its own start
    // is the same frame as one restoring another start.
    [Fact]
    public void RestoringEachSessionsStartingCopyTextureIsTheSameFrameAcrossSessions()
    {
        RenderTrace fresh = CopyFrame("-", "-"), afterSandbox = CopyFrame("_rt_fullframefb", "_rt_fullframefb");
        Assert.NotNull(RenderTrace.Compare(fresh, afterSandbox));
        Assert.Null(RenderTrace.CompareAcrossSessions(fresh, afterSandbox));
    }

    // A restore of anything but the frame's own start is a different call.
    [Fact]
    public void RestoringATextureTheFrameDidNotStartWithDiffersAcrossSessions()
    {
        Assert.NotNull(RenderTrace.CompareAcrossSessions(CopyFrame("-", "-"), CopyFrame("-", "_rt_fullframefb")));
    }

    // A copy set while the frame's start texture is already in force is a copy
    // of its own (post-processing's) and keeps its name.
    [Fact]
    public void ACopyOfTheStartTextureWhileItIsInForceKeepsItsName()
    {
        const string post = "ctx.SetFrameBufferCopyTexture\ttexture=_rt_fullframefb index=0\n";
        RenderTrace fresh = CopyFrame("-", "-", post), afterSandbox = CopyFrame("_rt_fullframefb", "_rt_fullframefb", post);
        Assert.Null(RenderTrace.CompareAcrossSessions(fresh, afterSandbox));
        Assert.NotNull(RenderTrace.CompareAcrossSessions(fresh, CopyFrame("_rt_fullframefb", "_rt_fullframefb")));
    }

    // A recording without the note answers "-" only when it restores none:
    // the three dm_lockdown poses the gate compares; the others cannot say.
    [Fact]
    public void AFrozenFrameNamesItsStartingCopyTextureOnlyWhenItRestoresNone()
    {
        var starts = Recordings().ToDictionary(r => r.Name, r => RenderTrace.Parse(r.Text).FrameStartCopyName);
        Assert.Equal(25, starts.Count);
        foreach ((string name, string? start) in starts)
            Assert.Equal(name.StartsWith("dm_lockdown-pose", StringComparison.Ordinal) ? "-" : null, start);
    }

    // The note, when a frame has it, answers for the frame.
    [Fact]
    public void TheNoteNamesTheStartingCopyTexture()
    {
        Assert.Equal("_rt_fullframefb", CopyFrame("_rt_fullframefb", "_rt_fullframefb").FrameStartCopyName);
        Assert.Null(RenderTrace.Parse("frame\tpath=1 framecount=1 rect=0,0,8,8\n" + Push + Draw).FrameStartCopyName);
    }
}
