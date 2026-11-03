using System.Runtime.Loader;
using System.Text.Json;

using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dev API's /preview/live (Debug.Api's UiMockups): the client's UI
/// assemblies loaded straight from the managed directory into a context of
/// their own, answering the live mock-ups as the preview tool does.
/// </summary>
public sealed class UiMockupsHostTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-mockups-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static UiMockups OverTestBin(string mod = "") => new(() => AppContext.BaseDirectory, () => mod);

    [Fact]
    public void The_UI_assemblies_beside_the_dev_API_list_their_segments()
    {
        using var host = OverTestBin();
        var reply = host.Answer("""{"op":"list"}""");
        Assert.Equal(200, reply.Status);
        var ids = JsonDocument.Parse(reply.Body).RootElement.GetProperty("segments").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()).ToList();
        Assert.Contains("screen:scoreboard", ids);
        Assert.Contains(ids, id => id!.StartsWith("panel:SourceSharp.Ai.Hl2mp.Ui.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_UI_assemblies_load_into_a_context_of_their_own()
    {
        using var host = OverTestBin();
        host.Answer("""{"op":"list"}""");
        var own = AssemblyLoadContext.All.SingleOrDefault(c => c.Name == "UiMockups");
        Assert.NotNull(own);
        Assert.Contains(own.Assemblies, a => a.GetName().Name == UiMockups.Toolkit);
    }

    [Fact]
    public void An_empty_body_answers_the_list()
    {
        using var host = OverTestBin();
        Assert.Contains("\"segments\"", host.Answer("").Body);
    }

    [Fact]
    public void A_render_answers_the_screen_s_svg()
    {
        using var host = OverTestBin();
        var reply = host.Answer("""{"op":"render","segment":"screen:scoreboard","preset":"auto","w":1280,"h":720}""");
        Assert.Contains("<svg", JsonDocument.Parse(reply.Body).RootElement.GetProperty("svg").GetString());
    }

    [Fact]
    public void A_directory_without_the_toolkit_answers_503_naming_it()
    {
        using var host = new UiMockups(() => _dir, () => "");
        var reply = host.Answer("""{"op":"list"}""");
        Assert.Equal(503, reply.Status);
        Assert.Contains($"no {UiMockups.Toolkit}.dll in {_dir}", reply.Body);
    }

    [Fact]
    public void After_dispose_a_request_is_refused()
    {
        var host = OverTestBin();
        host.Answer("""{"op":"list"}""");
        Assert.True(host.Loaded);
        host.Dispose();
        Assert.False(host.Loaded);
        Assert.Equal(503, host.Answer("""{"op":"list"}""").Status);
    }

    [Fact]
    public void The_UI_assemblies_are_the_toolkit_the_core_s_screens_then_each_game_s_by_name()
    {
        foreach (var n in new[] { "TF2.Client.Ui", "Other.Thing", "SourceSharp.Client.Ui", UiMockups.Toolkit, "A.Ui", "SourceSharp.Client.Ui.Host" })
            File.WriteAllText(Path.Combine(_dir, n + ".dll"), "");
        Assert.Equal([UiMockups.Toolkit, "SourceSharp.Client.Ui", "A.Ui", "TF2.Client.Ui"],
            UiMockups.UiAssemblies(_dir).Select(Path.GetFileNameWithoutExtension));
    }

    [Fact]
    public void The_copy_is_the_mod_s_own_files_then_each_sibling_mod_s()
    {
        foreach (var (mod, file) in new[] { ("tf2", "tf_english.txt"), ("test", "test_english.txt"), ("test", "a_english.txt"), ("descent", "d_english.txt") })
        {
            Directory.CreateDirectory(Path.Combine(_dir, mod, "resource"));
            File.WriteAllText(Path.Combine(_dir, mod, "resource", file), "");
        }
        Assert.Equal(["a_english.txt", "test_english.txt", "d_english.txt", "tf_english.txt"],
            UiMockups.LanguageFiles(Path.Combine(_dir, "test")).Select(Path.GetFileName));
    }
}
