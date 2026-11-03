using System.Text;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The engine's GAME search path for a fact: the files it was handed, by
/// path, and nothing else (a path it lacks is missing, as on the engine).
/// </summary>
internal sealed class EconTestFiles : IEngineFiles
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Reads { get; } = [];

    public EconTestFiles With(string path, string text)
    {
        _files[path] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    public int GameFileSize(string path) => _files.TryGetValue(path, out var b) ? b.Length : -1;

    public string? ReadGameFile(string path) => ReadGameFileBytes(path) is { } b ? Encoding.UTF8.GetString(b) : null;

    public byte[]? ReadGameFileBytes(string path)
    {
        Reads.Add(path);
        return _files.TryGetValue(path, out var b) ? b : null;
    }

    public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;

    /// <summary>A committed fixture under src/tf2/tests/TestData/items; a missing one fails the fact.</summary>
    public static string Fixture(string name)
    {
        string path = RepoLayout.Repo("src", "tf2", "tests", "TestData", "items", name);
        Assert.True(File.Exists(path), $"fixture {path} is missing");
        return File.ReadAllText(path);
    }

    /// <summary>The trimmed shipped schema, parsed.</summary>
    public static ItemSchemaContents Trimmed() => SchemaParser.Parse(Fixture("items_game.trimmed.txt"));

    /// <summary>A schema service over <paramref name="text"/> at the stock path.</summary>
    public static ItemSchema Schema(string text, RecordingLog? log = null) =>
        new(new EconTestFiles().With(ItemSchema.FilePath, text), log ?? new RecordingLog());

    /// <summary>The service over the trimmed fixture.</summary>
    public static ItemSchema TrimmedSchema(RecordingLog? log = null) => Schema(Fixture("items_game.trimmed.txt"), log);
}
