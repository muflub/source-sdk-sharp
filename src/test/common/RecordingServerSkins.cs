using System.Collections.Generic;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>An <see cref="IServerSkins"/> that records what it was asked.</summary>
public sealed class RecordingServerSkins : IServerSkins
{
    public List<(EntityHandle Entity, int Skin)> Set { get; } = new();

    public void SetSkin(EntityHandle entity, int skin) => Set.Add((entity, skin));
}
