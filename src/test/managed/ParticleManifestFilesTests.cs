using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The particle manifests' file lists (SourceSharp.Shared.ParticleManifestFiles,
/// plan_client_novalve C9), as particle_parse.cpp's GetParticleManifest and the
/// map half of ParseParticleEffectsMap read them.
/// </summary>
public class ParticleManifestFilesTests
{
    private sealed class Log : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add(message);
    }

    private static Func<string, string, byte[]?> Files(Dictionary<(string, string), string> files)
        => (path, id) => files.TryGetValue((path, id), out string? t) ? System.Text.Encoding.UTF8.GetBytes(t) : null;

    [Fact]
    public void TheGameManifestsFileEntriesInOrderAndABogusOneIsSaid()
    {
        var log = new Log();
        var reader = new ParticleManifestFiles(log, Files(new()
        {
            [("particles/particles_manifest.txt", "GAME")] =
                "particles_manifest { \"file\" \"particles/a.pcf\" \"other\" \"x\" \"FILE\" \"!particles/b.pcf\" }",
        }));
        Assert.Equal(new[] { "particles/a.pcf", "!particles/b.pcf" }, reader.Game());
        Assert.Contains(log.Lines, l => l.Contains("bogus file type 'other'"));
    }

    [Fact]
    public void AMapManifestComesFromTheBspFirstAndKeepsOnlyTheParticlesDirectory()
    {
        var log = new Log();
        var reader = new ParticleManifestFiles(log, Files(new()
        {
            [("maps/m_particles.txt", "BSP")] =
                "m { \"file\" \"particles/a.pcf\" \"file\" \"!particles/b.pcf\" \"file\" \"materials/c.pcf\" }",
            [("maps/m_particles.txt", "GAME")] = "m { \"file\" \"particles/ignored.pcf\" }",
        }));
        Assert.Equal(new[] { "particles/a.pcf", "!particles/b.pcf" }, reader.Map("m"));
        Assert.Contains(log.Lines, l => l.Contains("'materials/c.pcf' that's not under the particles directory"));
        Assert.Null(reader.Map("nomanifest"));
        Assert.Null(reader.Map(""));
    }

    [Fact]
    public void AMapManifestStopsAtSixtyFourEntries()
    {
        var entries = string.Concat(Enumerable.Range(0, 70).Select(i => $"\"file\" \"particles/p{i}.pcf\" "));
        var log = new Log();
        var reader = new ParticleManifestFiles(log, Files(new() { [("particles.txt", "BSP")] = $"m {{ {entries} }}" }));
        Assert.Equal(ParticleManifestFiles.MaxMapEntries, reader.Map("m")!.Count);
        Assert.Contains(log.Lines, l => l.Contains("specifies more than 64 entries"));
    }
}
