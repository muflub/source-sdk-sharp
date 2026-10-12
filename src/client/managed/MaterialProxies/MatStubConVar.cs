using SourceSharp.Abi;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// <c>mat_stub</c> (cl_mat_stub.cpp's): while on, each frame the view renders
/// through the material system stub (CMatStubHandler swaps it in and out
/// around the render). A cheat, as stock declared it; the engine reads it by
/// name too.
/// </summary>
[ConVar("mat_stub", "0", Flags = ConVarFlags.Cheat, Help = "")]
public sealed class MatStubConVar : BoolConVar
{
}
