namespace SourceSharp.Abi;

internal sealed unsafe class ClientConVarSlots(IConVarsClientArea conVarsClient) : IClientConVarSlots
{
    public bool SetValue(string name, float value)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return conVarsClient.ConVarsClientSetValue(p, value) != 0;
    }
}
