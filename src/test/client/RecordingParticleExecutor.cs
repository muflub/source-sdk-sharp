using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// An <see cref="IClientParticleExecutor"/> that records each call as a line,
/// for the managed particle sims' entity facts (lane c2-sims).
/// </summary>
internal sealed class RecordingParticleExecutor : IClientParticleExecutor
{
    public readonly List<string> Calls = new();

    public float[] LastLights = [];

    public bool Create(EntityHandle owner, int channel, string material, float cullRadius, bool alwaysSimulate)
    {
        Calls.Add($"create {owner.Value} {channel} {material} {cullRadius} {alwaysSimulate}");
        return true;
    }

    public bool CreateAt(int id, string material, Vector origin, float cullRadius, bool alwaysSimulate)
    {
        Calls.Add($"createat {id} {material} {origin.X} {origin.Y} {origin.Z} {cullRadius} {alwaysSimulate}");
        return material.Length > 0;
    }

    public void Configure(EntityHandle owner, int channel, float cullRadius) => Calls.Add($"configure {owner.Value} {channel} {cullRadius}");

    public void Destroy(EntityHandle owner, int channel) => Calls.Add($"destroy {owner.Value} {channel}");

    public ParticleFollow SetSpace(EntityHandle owner, int channel, uint entity, int attachment, ParticleSpace flags)
    {
        Calls.Add($"space {owner.Value} {channel} {entity} {attachment} {(int)flags}");
        return default;
    }

    public void SetLights(EntityHandle owner, int channel, ReadOnlySpan<float> lights)
    {
        Calls.Add($"lights {owner.Value} {channel} {lights.Length}");
        LastLights = lights.ToArray();
    }

    /// <summary>What the material answers for a var; null: it has none.</summary>
    public int? MaterialVar;

    public bool MaterialVarInt(EntityHandle owner, int channel, string var, out int value)
    {
        Calls.Add($"materialvar {owner.Value} {channel} {var}");
        value = MaterialVar ?? 0;
        return MaterialVar is not null;
    }
}
