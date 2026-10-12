using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's decal tables, managed-built (plan_client_novalve cluster 18,
/// lane c2-sims): what <c>shell_decals_client.cpp</c>'s <c>decalsystem</c>
/// forwards to, so <c>game/shared/decals.cpp</c> leaves client.so. Each body
/// counts its own crossing first. A name answer is NUL-terminated UTF-8 into
/// native's buffer, or −1 for no answer or one that does not fit (the server's
/// <c>NameBuffer</c> contract: refused, never truncated, because half a decal
/// name names no decal); the generated catch would answer 0, a length, so the
/// three questions keep their own.
/// </summary>
public sealed unsafe class DecalsClientBinding(Crossings crossings, ILog log, IClientDecals decals) : IManagedDecalsClient
{
    private const int NoAnswer = -1;

    public void Init()
    {
        crossings.Count(CrossingOp.ClientDecalsInit);
        decals.Init();
    }

    public void LevelInitPreEntity()
    {
        crossings.Count(CrossingOp.ClientDecalsLevelInitPreEntity);
        decals.LevelInitPreEntity();
    }

    public void Shutdown()
    {
        crossings.Count(CrossingOp.ClientDecalsShutdown);
        decals.Shutdown();
    }

    public int IndexForName(byte* a0)
    {
        crossings.Count(CrossingOp.ClientDecalIndexForName);
        try { return decals.IndexForName(Utf8(a0)); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDecalsClient.IndexForName", e); return -1; }
    }

    public int NameForIndex(int a0, byte* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientDecalNameForIndex);
        try
        {
            if (a1 == null || a2 <= 0) return NoAnswer;
            return Write(decals.NameForIndex(a0), new Span<byte>(a1, a2));
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDecalsClient.NameForIndex", e); return NoAnswer; }
    }

    public int TranslateForGameMaterial(byte* a0, int a1, byte* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientDecalTranslate);
        try
        {
            if (a0 == null || a2 == null || a3 <= 0) return NoAnswer;
            return Write(decals.TranslateForGameMaterial(Utf8(a0), a1), new Span<byte>(a2, a3));
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedDecalsClient.TranslateForGameMaterial", e); return NoAnswer; }
    }

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    /// <summary>The byte count written (0 for ""), or −1 for null or an answer that does not fit.</summary>
    internal static int Write(string? value, Span<byte> into)
    {
        if (value is null || into.Length < 1)
            return NoAnswer;

        into[0] = 0;
        if (value.Length == 0)
            return 0;

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > into.Length - 1)
            return NoAnswer;

        bytes.CopyTo(into);
        into[bytes.Length] = 0;
        return bytes.Length;
    }
}
