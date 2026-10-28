namespace SourceSharp.Client.Interop;

/// <summary>
/// Where the interpolation pass's admitted set goes: the shell's PreRender
/// walk, which re-transforms exactly the entities the pass may write.
/// </summary>
public interface IInterpolationAdmission
{
    /// <summary>The admitted entities' handles, replacing the last set.</summary>
    void Admitted(ReadOnlySpan<uint> handles);
}
