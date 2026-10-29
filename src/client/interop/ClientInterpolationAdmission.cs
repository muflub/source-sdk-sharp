using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The standalone area's admitted-set slot
/// (<c>SourceSharp_ClientInterpAdmitted</c>), one crossing per pass.
/// </summary>
internal sealed unsafe class ClientInterpolationAdmission(IStandaloneClientArea standalone) : IInterpolationAdmission
{
    public void Admitted(ReadOnlySpan<uint> handles)
    {
        fixed (uint* p = handles)
            standalone.ClientInterpAdmitted(p, handles.Length);
    }
}
