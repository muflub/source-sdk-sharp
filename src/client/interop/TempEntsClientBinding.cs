using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's temp entities, its temp entity pool, and the impact rules the
/// effect code asks for, managed-built (plan_client_vtables CE6): the bodies of
/// the Entry.cs thunks ClientOnTempEntity, ClientImpactChoice and
/// ClientTempEntPoolUpdate/Clear, moved.
///
/// <para>
/// The generated thunk has already made the client the active side and catches
/// what a body throws. ITS CATCH ANSWERS 0, and for two of these slots 0 is not
/// a refusal: OnImpactChoice answers -1 when it cannot answer (pool slot 0 is a
/// real slot), so it keeps its own catch with the thunk's -1.
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

    /// <summary>
    /// Whether a temp entity the client makes for itself is dropped (c_te.cpp's
    /// SuppressTE). The thunk's catch answers 0, which lets the effect through,
    /// as stock does whenever it can predict and has a recipient.
    /// </summary>
    public int SuppressLocalTempEnt(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientSuppressLocalTempEnt);
        return client.SuppressLocalTempEnt(a0 != 0, a1) ? 1 : 0;
    }
}
