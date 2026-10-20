using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real ones, over the api.</summary>
internal sealed unsafe class ClientEffectRegistrations(IFxClientArea fxClient) : IClientEffectRegistrations
{
    private const int NameBytes = 256;

    public int Count => Math.Max(fxClient.EffectRegistrationCount(), 0);

    public string? Name(int index)
    {
        byte* buffer = stackalloc byte[NameBytes];
        buffer[0] = 0;
        if (fxClient.EffectRegistrationName(index, buffer, NameBytes) < 0)
            return null;
        return new string((sbyte*)buffer);
    }

    public void Run(int index, in EffectData data)
    {
        fixed (EffectData* p = &data)
            fxClient.EffectRegistrationRun(index, p);
    }
}
