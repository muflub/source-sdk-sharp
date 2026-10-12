using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The crossing counters' report as the server's cs_sv_crossings asks for it,
/// managed-built (plan_engineapi_vtables E14). It is the Entry.cs thunk's body,
/// moved. The counters are process-wide (SourceSharp.Abi is in the default
/// ALC), so this answers the same table the client's slot does. It counts no
/// crossing and marks no side, as the thunk did not. A refusal answers -1 and
/// the generated catch would answer 0 ("an empty report"), so this keeps its
/// own catch.
/// </summary>
public sealed unsafe class DiagnosticsBinding(Crossings crossings, ILog log) : IManagedDiagnostics
{
    public int CrossingsReport(byte* a0, int a1, int a2)
    {
        try
        {
            return crossings.WriteReport(a0 == null || a1 <= 0 ? Span<byte>.Empty : new Span<byte>(a0, a1), a2 != 0);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedDiagnostics.CrossingsReport", e);
            return -1;
        }
    }
}
