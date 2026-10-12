using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's native temp-entity forwards, managed-built (plan_engineapi_vtables
/// E13): SourceSharpTe_Forward (shell_tempents.cpp) asks for the packed fields
/// and the class to send them as. The send stays native, because it keeps the
/// caller's own IRecipientFilter. The server half of the Entry.cs thunk, moved.
/// Every failure answers -1, so native sends nothing rather than a blob it made
/// up. The generated catch would answer 0, so this keeps its own.
/// </summary>
public sealed unsafe class TempEntsBinding(Crossings crossings, ILog log, ITempEntBlobs blobs) : IManagedTempEnts
{
    public int OnNativeTempEntity(int a0, void* a1, int a2, void* a3, int a4, void* a5, int a6, void* a7)
    {
        crossings.Count(CrossingOp.ServerNativeTempEntity);
        try
        {
            if (a5 == null) return -1;

            var floats = a2 > 0 ? new ReadOnlySpan<float>(a1, a2) : default;
            var ints   = a4 > 0 ? new ReadOnlySpan<int>(a3, a4)   : default;

            int bytes = blobs.BuildForwardBlob(a0, floats, ints, new Span<byte>(a5, a6), out int classHandle);

            if (bytes > 0 && a7 != null)
                *(int*)a7 = classHandle;

            return bytes;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedTempEnts.OnNativeTempEntity", e);
            return -1;
        }
    }
}
