namespace SourceSharp.Abi;

internal sealed unsafe class ClientConVarSlots(IConVarsClientArea conVarsClient) : IClientConVarSlots, IClientConVarFlags
{
    public bool SetValue(string name, float value)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return conVarsClient.ConVarsClientSetValue(p, value) != 0;
    }

    public bool AddFlags(string name, ConVarFlags flags)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return conVarsClient.ConVarsClientAddFlags(p, (int)flags) != 0;
    }
}
