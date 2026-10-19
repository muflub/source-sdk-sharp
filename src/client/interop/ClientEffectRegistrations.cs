using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real ones, over the api.</summary>
internal sealed unsafe class ClientEffectRegistrations(FxClientArea? fxClient) : IClientEffectRegistrations
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private FxClientArea FxClient => fxClient ?? throw ShimAreaGuard.NotAttached(nameof(FxClient));

    private const int NameBytes = 256;

    public int Count => (fxClient is not null) ? Math.Max(FxClient.EffectRegistrationCount(), 0) : 0;

    public string? Name(int index)
    {
        if (fxClient is null)
            return null;
        byte* buffer = stackalloc byte[NameBytes];
        buffer[0] = 0;
        if (FxClient.EffectRegistrationName(index, buffer, NameBytes) < 0)
            return null;
        return new string((sbyte*)buffer);
    }

    public void Run(int index, in EffectData data)
    {
        if (fxClient is null)
            return;
        fixed (EffectData* p = &data)
            FxClient.EffectRegistrationRun(index, p);
    }
}
