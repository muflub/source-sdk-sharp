using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real ones, over the api.</summary>
internal sealed unsafe class ClientEffectRegistrations(EngineSide api) : IClientEffectRegistrations
{
    private const int NameBytes = 256;

    public int Count => api.ClientAttached ? Math.Max(api.ClientShims.EffectRegistrationCount(), 0) : 0;

    public string? Name(int index)
    {
        if (!api.ClientAttached)
            return null;
        byte* buffer = stackalloc byte[NameBytes];
        buffer[0] = 0;
        if (api.ClientShims.EffectRegistrationName(index, buffer, NameBytes) < 0)
            return null;
        return new string((sbyte*)buffer);
    }

    public void Run(int index, in EffectData data)
    {
        if (!api.ClientAttached)
            return;
        fixed (EffectData* p = &data)
            api.ClientShims.EffectRegistrationRun(index, p);
    }
}
