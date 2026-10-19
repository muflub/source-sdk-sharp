namespace SourceSharp.Abi;

internal sealed unsafe class ClientConVarSlots(EngineSide api) : IClientConVarSlots
{
    /// <summary>shell_convars_client.h's SOURCESHARP_CONVAR_WATCH_ID_BASE: clear of the registry's ids.</summary>
    public const int WatchIdBase = 0x10000;

    public int Watch(string name)
    {
        if (!api.ClientAttached)
            return -1;
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return api.ClientShims.ConVarsClientWatch(p);
    }

    public bool SetValue(string name, float value)
    {
        if (!api.ClientAttached)
            return false;
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return api.ClientShims.ConVarsClientSetValue(p, value) != 0;
    }
}
