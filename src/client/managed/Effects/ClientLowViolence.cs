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
/// as stock asks it. A convar the console does not answer reads as off, as the
/// native reference read it.
/// </summary>
public sealed class ClientLowViolence(IEngineConsole console, IViolenceSettings engine) : IClientLowViolence
{
    public bool IsLowViolence
        => RagdollViolencePolicy.AnyLegacyConVarOff(On("violence_hblood"), On("violence_ablood"),
                                                    On("violence_hgibs"), On("violence_agibs"))
           || engine.EngineSaysLowViolence;

    private bool On(string name)
    {
        string? value = console.FindConVarValue(name);
        return value is not null && (int)VariantParse.Atof(value) != 0;
    }
}
