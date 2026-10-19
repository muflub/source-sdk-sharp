namespace SourceSharp.Abi;

internal sealed unsafe class ClientConVarSlots(ConVarsClientArea? conVarsClient) : IClientConVarSlots
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private ConVarsClientArea ConVarsClient => conVarsClient ?? throw ShimAreaGuard.NotAttached(nameof(ConVarsClient));

    /// <summary>shell_convars_client.h's SOURCESHARP_CONVAR_WATCH_ID_BASE: clear of the registry's ids.</summary>
    public const int WatchIdBase = 0x10000;

    public int Watch(string name)
    {
        if (conVarsClient is null)
            return -1;
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return ConVarsClient.ConVarsClientWatch(p);
    }

    public bool SetValue(string name, float value)
    {
        if (conVarsClient is null)
            return false;
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return ConVarsClient.ConVarsClientSetValue(p, value) != 0;
    }
}
