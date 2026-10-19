using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.4: the surface property manifest, read in C#.
/// Held live to the native PhysParseSurfaceData on dm_lockdown before the
/// switch (93 surface properties, every index and name the same); these pin
/// the manifest rules and that each file reaches VPhysics' parser.
/// </summary>
public class SurfacePropertyManifestTests
{
    private sealed class Props : IVtablePhysicsSurfaceProps
    {
        public readonly List<(string File, string Text)> Parsed = new();
        public int ParseSurfaceData(string fileName, string text) { Parsed.Add((fileName, text)); return 0; }
        public int SurfacePropCount() => Parsed.Count;
        public string? PropName(int index) => null;
        public int GameMaterial(int surfaceProps) => 0;
        public MoveSurface Surface(int surfaceProps) => default;
        public string? ScrapeSound(int surfaceProps, bool smooth) => null;
        public int SurfaceIndex(string name) => 0;
    }

    private sealed class Files : IEngineFiles
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public byte[]? ReadFileOnPath(string path, string pathId) => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;

        public readonly Dictionary<string, string> Text = new();
        public int GameFileSize(string path) => Text.TryGetValue(path, out var t) ? t.Length : -1;
        public string? ReadGameFile(string path) => Text.TryGetValue(path, out var t) ? t : null;
        public byte[]? ReadGameFileBytes(string path)
            => ReadGameFile(path) is { } t ? System.Text.Encoding.UTF8.GetBytes(t) : null;
    }

    [Fact]
    public void OnlyTheFirstRootsFileKeysAreFilesAndInOrder()
    {
        var warnings = new List<string>();
        var files = SurfacePropertyManifest.FilesNamed(
            "surfaceprops_manifest { file a.txt FILE b.txt bogus c.txt file d.txt } second { file e.txt }",
            warnings.Add);

        Assert.Equal(new[] { "a.txt", "b.txt", "d.txt" }, files);
        Assert.Single(warnings);
        Assert.Contains("bogus", warnings[0]);
    }

    [Fact]
    public void EachNamedFileIsHandedToVPhysicsWithItsText()
    {
        var files = new Files();
        files.Text[SurfacePropertyManifest.ManifestPath] = "m { file scripts/a.txt file scripts/missing.txt file scripts/b.txt }";
        files.Text["scripts/a.txt"] = "default { density 2000 }";
        files.Text["scripts/b.txt"] = "metal { base default }";
        var props = new Props();

        int parsed = new SurfacePropertyManifest(new RecordingLog(), files).Load(props);

        Assert.Equal(2, parsed);
        Assert.Equal(new[] { ("scripts/a.txt", "default { density 2000 }"), ("scripts/b.txt", "metal { base default }") },
                     props.Parsed);
    }

    [Fact]
    public void AMissingManifestParsesNothing()
    {
        var props = new Props();
        Assert.Equal(0, new SurfacePropertyManifest(new RecordingLog(), new Files()).Load(props));
        Assert.Empty(props.Parsed);
    }
}
