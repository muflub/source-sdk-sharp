using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's crossing report and user-command seed, managed-built
/// (plan_client_vtables CE9a): the bodies of the Entry.cs thunks
/// CrossingsReport and ClientMd5PseudoRandom, moved, as the server's
/// DiagnosticsBinding took its report (plan_engineapi_vtables E14). The
/// generated thunk has already made the client the active side and catches what
/// a body throws.
/// </summary>
public sealed unsafe class DiagnosticsClientBinding(Crossings crossings, ILog log) : IManagedDiagnosticsClient
{
    /// <summary>
    /// The process-wide counters as text (SourceSharp.Abi is in the default ALC,
    /// so cs_cl_crossings and cs_sv_crossings print one table). NOT COUNTED: this
    /// reads the counters, and counting it would put the act of measuring into
    /// the measurement (CrossingsAuditTests.CrossingsReportIsNotCounted). A
    /// refusal answers -1 and the generated catch would answer 0 ("an empty
    /// report"), so this keeps its own catch.
    /// </summary>
    public int CrossingsReport(byte* a0, int a1, int a2)
    {
        try
        {
            return crossings.WriteReport(a0 == null || a1 <= 0 ? Span<byte>.Empty : new Span<byte>(a0, a1), a2 != 0);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedDiagnosticsClient.CrossingsReport", e);
            return -1;
        }
    }

    /// <summary>
    /// A new user command's random seed for the client's CInput::CreateMove: the
    /// value the server's decode computes (UserCmdCodec), so prediction's shared
    /// random numbers agree. Pure; nothing in it throws.
    /// </summary>
    public uint Md5PseudoRandom(uint a0)
    {
        crossings.Count(CrossingOp.ClientMd5PseudoRandom);
        return UserCmdCodec.Md5PseudoRandom(a0);
    }
}
