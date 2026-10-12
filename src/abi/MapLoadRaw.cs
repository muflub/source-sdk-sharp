using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One map load, flattened. Mirrors <c>SourceSharpMapLoad</c>.
///
/// <para>
/// THE LAYOUT, NOT THE API. <c>MapLoad</c> is the API — a <c>ref struct</c> that
/// cannot outlive the call (MapLoad.cs) — and this is the raw four-pointer
/// record the engine actually hands across, named the way
/// <see cref="CGlobalVarsRaw"/> is named beside <see cref="Globals"/>. Nothing
/// outside the bootstrap's thunks should hold one.
/// </para>
///
/// <para>
/// Crosses BY POINTER, like <c>BreakPiece</c> and <c>GibShot</c>: four pointers
/// is small enough to pass either way, but a struct passed by value across this
/// boundary has an ABI of its own that neither side states.
/// </para>
///
/// <para>
/// The strings are the ENGINE's and are valid only for the duration of the
/// call. <see cref="MapEntities"/> in particular is the whole entity lump;
/// managed code parses it inside the call and does not keep it.
/// <see cref="OldLevel"/> and <see cref="LandmarkName"/> are null on a load that
/// is not a transition.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapLoadRaw
{
    public byte* MapName;
    public byte* MapEntities;
    public byte* OldLevel;
    public byte* LandmarkName;
}
