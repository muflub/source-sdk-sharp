using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IManagedMaterialProxyClient"/>, managed-built (lane c5-proxies):
/// native's material proxy trampoline asks each Init, OnBind and Release here.
/// Every body counts its crossing; a failure answers what the generated
/// thunk's catch answers (0: Init refused, no setter calls).
/// </summary>
public sealed unsafe class MaterialProxyClientBinding(Crossings crossings, ILog log, IClientMaterialProxies proxies)
    : IManagedMaterialProxyClient
{
    private readonly ProxyBindFrame _frame = new();

    public int ProxyInit(int a0, byte* a1, byte* a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientMaterialProxyInit);
        try
        {
            string name = Marshal.PtrToStringUTF8((nint)a1) ?? string.Empty;
            string material = Marshal.PtrToStringUTF8((nint)a2) ?? string.Empty;
            var keys = new List<ProxyKeyData>(Math.Max(a4, 0));
            var raw = (ProxyKeyRaw*)a3;
            for (int i = 0; raw != null && i < a4; i++)
            {
                ProxyKeyRaw k = raw[i];
                keys.Add(new ProxyKeyData(Marshal.PtrToStringUTF8((nint)k.Name) ?? string.Empty, k.Type,
                                          Marshal.PtrToStringUTF8((nint)k.String) ?? string.Empty, k.Float, k.Int,
                                          k.ScanCount, k.ScanValue, k.Atof, k.Atoi));
            }
            return proxies.Init(a0, name, material, keys) ? 1 : 0;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedMaterialProxyClient.ProxyInit", e); return 0; }
    }

    public int ProxyBind(int a0, void* a1, void* a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientMaterialProxyBind);
        if (a1 == null || a3 == null || a4 <= 0 || _frame.Active)
            return 0;
        try
        {
            _frame.Begin((ProxyBindIn*)a1, (ProxyVar*)a2, (ProxyOp*)a3, a4);
            proxies.Bind(a0, _frame);
            return _frame.OpCount;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedMaterialProxyClient.ProxyBind", e); return 0; }
        finally { _frame.End(); }
    }

    public void ProxyRelease(int a0)
    {
        crossings.Count(CrossingOp.ClientMaterialProxyRelease);
        try { proxies.Release(a0); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedMaterialProxyClient.ProxyRelease", e); }
    }

    public int MatStubEnabled()
    {
        crossings.Count(CrossingOp.ClientMaterialProxyMatStub);
        try { return proxies.MatStubEnabled() ? 1 : 0; }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedMaterialProxyClient.MatStubEnabled", e); return 0; }
    }
}
