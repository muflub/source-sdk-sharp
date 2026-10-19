namespace SourceSharp.Abi;

internal sealed unsafe class ClientConVarSlots(ConVarsClientArea conVarsClient) : IClientConVarSlots
{
    /// <summary>shell_convars_client.h's SOURCESHARP_CONVAR_WATCH_ID_BASE: clear of the registry's ids.</summary>
    public const int WatchIdBase = 0x10000;

    public int Watch(string name)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return conVarsClient.ConVarsClientWatch(p);
    }

    public bool SetValue(string name, float value)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return conVarsClient.ConVarsClientSetValue(p, value) != 0;
    }
}
