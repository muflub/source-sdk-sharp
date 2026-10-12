using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// A string table for facts: an index per added string, or no table at all
/// (<see cref="Present"/> false answers the engine's "not there": id -1,
/// count -1). Counts its reads, so a fact can see a cache at work.
/// </summary>
internal sealed class FakeStringTable(string name = "SsUserMessages") : IEngineStringTable
{
    public List<string> Strings { get; } = new();
    public bool Present { get; set; } = true;
    public int Reads { get; private set; }

    public string Name { get; } = name;
    public int Id => Present ? 19 : -1;
    public int Count => Present ? Strings.Count : -1;

    public int AddString(string value)
    {
        if (!Present)
            return -1;
        int at = Strings.IndexOf(value);
        if (at >= 0)
            return at;
        Strings.Add(value);
        return Strings.Count - 1;
    }

    public int FindStringIndex(string value) => Present ? Strings.IndexOf(value) : -1;

    public string? GetString(int stringIndex)
    {
        Reads++;
        return Present && stringIndex >= 0 && stringIndex < Strings.Count ? Strings[stringIndex] : null;
    }
}
