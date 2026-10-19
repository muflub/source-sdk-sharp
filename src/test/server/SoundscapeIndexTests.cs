using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.4: the server's soundscape index is the shared
/// C# library's, built at level init, and no longer the native registry's.
/// The traversal itself is SoundscapeLibraryTests'; these pin that the service
/// builds it for the level and answers from it.
/// </summary>
public class SoundscapeIndexTests
{
    private sealed class Files : IEngineFiles
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public byte[]? ReadFileOnPath(string path, string pathId) => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;

        public readonly Dictionary<string, string> Text = new();
        public readonly List<string> Asked = new();

        public int GameFileSize(string path) => Text.TryGetValue(path, out var t) ? t.Length : -1;

        public string? ReadGameFile(string path)
        {
            Asked.Add(path);
            return Text.TryGetValue(path, out var t) ? t : null;
        }

        public byte[]? ReadGameFileBytes(string path)
            => ReadGameFile(path) is { } t ? System.Text.Encoding.UTF8.GetBytes(t) : null;
    }

    private static (Soundscapes Service, Files Files) Build()
    {
        var files = new Files();
        files.Text["scripts/soundscapes_manifest.txt"] =
            "\"soundscapes_manifest\"\n{\n  \"file\" \"scripts/soundscapes_a.txt\"\n}\n";
        files.Text["scripts/soundscapes_a.txt"] =
            "\"first\" { \"dsp\" \"1\" }\n\"empty\" { }\n\"second\" { \"dsp\" \"2\" }\n";
        files.Text["scripts/soundscapes_mymap.txt"] = "\"mapped\" { \"dsp\" \"3\" }\n";

        var net = Fake();
        return (new Soundscapes(new RecordingLog(), net, net, Fake(), Fake(), files), files);
    }

    [Fact]
    public void BeforeAnyLevelEveryNameIsUnknown()
        => Assert.Equal(-1, Build().Service.IndexOf("first"));

    [Fact]
    public void LevelInitBuildsTheLibraryAndTheIndexComesFromIt()
    {
        var (service, files) = Build();

        ((IGameLifecycle)service).OnLevelInitPreEntity("mymap");

        Assert.Equal(0, service.IndexOf("first"));
        Assert.Equal(1, service.IndexOf("second"));   // "empty" has no subkey: not counted
        Assert.Equal(2, service.IndexOf("mapped"));   // the map's own file, appended
        Assert.Equal(-1, service.IndexOf("empty"));
        Assert.Contains("scripts/soundscapes_mymap.txt", files.Asked);
    }

    [Fact]
    public void EachLevelRebuildsTheLibraryForItsOwnMap()
    {
        var (service, _) = Build();

        ((IGameLifecycle)service).OnLevelInitPreEntity("mymap");
        Assert.Equal(2, service.IndexOf("mapped"));

        ((IGameLifecycle)service).OnLevelInitPreEntity("othermap");
        Assert.Equal(-1, service.IndexOf("mapped"));
        Assert.Equal(1, service.IndexOf("second"));
    }
}

/// <summary>sv_soundscape_printdebuginfo's text, now printed from the C# library.</summary>
public class SoundscapePrintDebugInfoTests
{
    [Fact]
    public void TheTextIsStocksHeaderOneLinePerSoundscapeAndTheRule()
        => Assert.Equal("\n------- SERVER SOUNDSCAPES -------\n- 0: first\n- 1: second\n" +
                        "----------------------------------\n\n",
                        SourceSharp.Server.Entities.Soundscape.SoundscapePrintDebugInfoCommand.Format(
                            new[] { (0, "first"), (1, "second") }));
}
