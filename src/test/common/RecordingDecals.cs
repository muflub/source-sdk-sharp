using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// The decal system as a test sees it: each group name answers the index a
/// test arranged for it, and every name asked for is recorded. An unarranged
/// name answers -1, which is the real system's "do not decal".
/// </summary>
public sealed class RecordingDecals : IServerDecals
{
    private readonly Dictionary<string, int> _indices = new();

    /// <summary>Every group name asked for, in order.</summary>
    public List<string> Asked { get; } = new();

    public RecordingDecals Answers(string name, int index)
    {
        _indices[name] = index;
        return this;
    }

    public int IndexForName(string name)
    {
        Asked.Add(name);
        return _indices.TryGetValue(name, out int index) ? index : -1;
    }

    public string? NameForIndex(int index) => null;

    public string? TranslateForGameMaterial(string decalName, int gameMaterial) => null;
}
