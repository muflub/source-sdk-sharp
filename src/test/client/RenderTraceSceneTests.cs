using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="RenderTrace.SceneDifference"/>: the live compare's way to tell a
/// plan that parted from stock from two adjacent frames that were asked about
/// different lists (C7 step 2; an entity that entered a view between the stock
/// frame and the plan frame).
/// </summary>
public class RenderTraceSceneTests
{
    private static string Fixture(string name)
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "render-stock", name);
        Assert.True(File.Exists(path), "the fixture is missing: " + path);
        return File.ReadAllText(path);
    }

    // The fixture with its first scene's last entry dropped (the header's
    // count and its group's count lowered to match).
    private static string WithoutLastEntry(string text)
    {
        var lines = text.Split('\n').ToList();
        int at = lines.FindIndex(l => l.StartsWith("scenebytes\t", StringComparison.Ordinal));
        Assert.True(at >= 0, "the fixture has a scene");
        byte[] bytes = Convert.FromHexString(lines[at]["scenebytes\t".Length..].TrimEnd('\r'));
        ViewSceneData scene = ViewSceneData.FromBytes(bytes)!;
        int last = scene.Entries.Length - 1;
        Assert.True(last >= 0, "the scene has entries");
        int group = scene.Entries[last].Group;
        var header = scene.Header;
        header.EntryCount--;
        header.SetGroupCount(group, header.GroupCountOf(group) - 1);
        var cut = new byte[bytes.Length - System.Runtime.InteropServices.Marshal.SizeOf<SourceSharp.Client.Interop.ViewSceneEntry>()];
        Array.Copy(bytes, cut, cut.Length);
        System.Runtime.InteropServices.MemoryMarshal.Write(cut, in header);
        lines[at] = "scenebytes\t" + Convert.ToHexString(cut).ToLowerInvariant();
        return string.Join('\n', lines);
    }

    [Fact]
    public void AFrameHasTheSameScenesAsItself()
    {
        string text = Fixture("dm_lockdown-pose3.txt");
        Assert.Null(RenderTrace.SceneDifference(RenderTrace.Parse(text), RenderTrace.Parse(text)));
    }

    [Fact]
    public void AnEntityMissingFromOneFramesListIsADifference()
    {
        string text = Fixture("dm_lockdown-pose3.txt");
        string? why = RenderTrace.SceneDifference(RenderTrace.Parse(text), RenderTrace.Parse(WithoutLastEntry(text)));
        Assert.NotNull(why);
        Assert.Contains("entries against", why);
    }

    [Fact]
    public void ADifferentNumberOfViewsIsADifference()
    {
        string a = Fixture("ss_sandbox-underwater.txt");
        string b = string.Join('\n', a.Split('\n').Where(l => !l.StartsWith("scenebytes\t", StringComparison.Ordinal)));
        Assert.Equal("2 scenes against 0", RenderTrace.SceneDifference(RenderTrace.Parse(a), RenderTrace.Parse(b)));
    }
}
