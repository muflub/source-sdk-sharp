using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>Whether this client runs with gore turned down (<c>UTIL_IsLowViolence</c>).</summary>
public interface IClientLowViolence
{
    bool IsLowViolence { get; }
}

/// <summary>
/// <c>UTIL_IsLowViolence</c> (util_shared.cpp): low when any of the four
/// legacy violence_* convars is off, else the engine's answer, asked only then
/// as stock asks it.
/// </summary>
public sealed class ClientLowViolence(ViolenceHBloodConVar humanBlood, ViolenceABloodConVar alienBlood,
                                      ViolenceHGibsConVar humanGibs, ViolenceAGibsConVar alienGibs,
                                      IViolenceSettings engine) : IClientLowViolence
{
    public bool IsLowViolence
        => RagdollViolencePolicy.AnyLegacyConVarOff(humanBlood.Value, alienBlood.Value, humanGibs.Value, alienGibs.Value)
           || engine.EngineSaysLowViolence;
}
