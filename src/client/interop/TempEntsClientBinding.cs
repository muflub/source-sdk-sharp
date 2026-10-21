using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's temp entities, its temp entity pool, and the impact rules the
/// effect code asks for, managed-built (plan_client_vtables CE6): the bodies of
/// the Entry.cs thunks ClientOnTempEntity, ClientNativeTempEntity,
/// ClientImpactChoice,
/// ClientTempEntPoolUpdate/Clear, moved, as
/// the server's TempEntsBinding took its forward (plan_engineapi_vtables E13).
///
/// <para>
/// The generated thunk has already made the client the active side and catches
/// what a body throws. ITS CATCH ANSWERS 0, and for two of these slots 0 is not
/// a refusal: OnNativeTempEntity and OnImpactChoice each
/// answer -1 when they cannot
/// answer (pool slot 0 is a real slot), so each keeps its own catch with the
/// thunk's -1.
/// </para>
///
/// <para>
/// OnTempEntPoolAlloc and OnTempEntEjectBrass left the area in the serialised
/// ABI pass (docs/abi-retirements.md, 2026-10-20). Their only native callers
/// were the SourceSharpTePool_Reserve/Bind/Release/EjectBrass exports, deleted
/// with the tempents cutover; the shell flow goes through
/// IManagedFxClient::TempEntEjectBrass.
/// </para>
/// </summary>
public sealed unsafe class TempEntsClientBinding(Crossings crossings, ILog log, IClientModule client) : IManagedTempEntsClient
{
    public void OnTempEntity(int a0, byte* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientOnTempEntity);
        if (a2 < 0) return;

        client.DispatchTempEntity(a0, new ReadOnlySpan<byte>(a1, a2));
    }

    /// <summary>
    /// A client forward raises the effect locally, so no blob exists at any
    /// point; a call with a blob buffer is the server's (TempEntsBinding) and
    /// answers -1, as does anything else it cannot do.
    /// </summary>
    public int OnNativeTempEntity(int a0, void* a1, int a2, void* a3, int a4, void* a5, int a6, void* a7)
    {
        crossings.Count(CrossingOp.ClientNativeTempEntity);
        try
        {
            if (a5 is not null)
                return -1;

            var floats = a2 > 0 ? new ReadOnlySpan<float>(a1, a2) : default;
            var ints   = a4 > 0 ? new ReadOnlySpan<int>(a3, a4)   : default;

            return client.ForwardTempEntity(a0, floats, ints) ? 0 : -1;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedTempEntsClient.OnNativeTempEntity", e);
            return -1;
        }
    }

    /// <summary>One frame of the pool: CTempEnts::Update's body.</summary>
    public void OnTempEntPoolUpdate(float a0)
    {
        crossings.Count(CrossingOp.ClientTempEntPoolUpdate);
        client.TempEntPoolUpdate(a0);
    }

    /// <summary>Free every live tempent: CTempEnts::Clear's body.</summary>
    public void OnTempEntPoolClear()
    {
        crossings.Count(CrossingOp.ClientTempEntPoolClear);
        client.TempEntPoolClear();
    }

    /// <summary>
    /// Which effect family an impact is. A null name buffer becomes an empty
    /// span: the module's spelling of "the caller did not ask for this one".
    /// </summary>
    public int OnImpactChoice(void* a0, int a1, void* a2, int a3, byte* a4, int a5, byte* a6, int a7)
    {
        crossings.Count(CrossingOp.ClientImpactChoice);
        try
        {
            if (a0 is null || a1 <= 0) return -1;
            if (a2 is null || a3 <= 0) return -1;

            return client.ImpactChoice(
                new ReadOnlySpan<int>(a0, a1),
                new Span<int>(a2, a3),
                a4 is null || a5 <= 0 ? Span<byte>.Empty : new Span<byte>(a4, a5),
                a6 is null || a7 <= 0 ? Span<byte>.Empty : new Span<byte>(a6, a7));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedTempEntsClient.OnImpactChoice", e);
            return -1;
        }
    }
}
