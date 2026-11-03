using SourceSharp.Client;
using SourceSharp.Client.Effects;

namespace SourceSharp.Shared;

/// <summary>
/// The client's half of the shared <see cref="UniformStream"/>: the particle
/// sims draw through <see cref="IParticleRandom"/>, and the beam system keeps
/// its stream across a managed reload.
/// </summary>
public sealed partial class UniformStream : IParticleRandom
{
    /// <summary>The same state through a managed reload's writer (the beam system's stream).</summary>
    public void Save(ReloadWriter writer)
    {
        foreach (int v in _table)
            writer.Write(v);
        writer.Write(_state);
        writer.Write(_last);
    }

    public void Restore(ReloadReader reader)
    {
        for (int i = 0; i < TableSize; i++)
            _table[i] = reader.ReadInt();
        _state = reader.ReadInt();
        _last = reader.ReadInt();
    }
}
