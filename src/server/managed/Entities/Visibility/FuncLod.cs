//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// func_lod: a piece of the map that fades out when you get far enough away.
///
/// <para>
/// A real brush, unlike the two entities beside it in this folder — it is drawn,
/// it can be solid, and it is in the world. What makes it a visibility entity is
/// that its ONE decision is about drawing: past DisappearDist it is not there.
/// </para>
///
/// <para>
/// The fade itself is client-side and has to be: it is a distance from the
/// viewer, recomputed as they move, and there is one viewer per client. So the
/// server sends the distance and the client's shell does the arithmetic — see
/// <c>C_StandaloneEntity::ComputeFxBlend</c>, which is the engine's own
/// ComputeDistanceFade, and <see cref="SourceSharp.Client.Standalone.ClientLod"/>,
/// which does nothing but hand the number over.
/// </para>
///
/// <para>
/// The "Solid" key is inverted and stock is not wrong about it, however it
/// reads: <c>CFunc_LOD::KeyValue</c> adds FSOLID_NOT_SOLID when the key is
/// NON-zero. The FGD calls the key "Solid" with 0 meaning solid, so a map that
/// wrote the obvious thing would get the opposite of what it asked for if this
/// were "corrected". Reproduced as stock has it, and recorded in
/// docs/divergences.md as a stock oddity rather than silently improved.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncLod, ScriptedEntityKind.Brush)]
public sealed class FuncLod : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="FuncLodVars"/>.</summary>
    [ScriptVars] public FuncLodVars Vars = new();

    [NetState] public LodNetState State = new();

    public override void OnSpawn()
    {
        SetSolidity(Vars.NotSolid != 0 ? BrushSolidity.None : BrushSolidity.Solid);
        SetVisible(true);

        // Static in the solver, which is CFunc_LOD::CreateVPhysics. A brush that
        // fades out of sight is still a brush a crate can rest on, and stock
        // makes one whether or not the map asked for solidity.
        MakePhysicsStatic();

        State.DisappearDist = Vars.DisappearDistance;
    }
}

/// <summary>
/// The map keys <see cref="FuncLod"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncLodVars
{
    /// <summary>Distance past which it is gone. Stock's client default is 5000.</summary>
    [VarName("DisappearDist")]
    public float DisappearDistance { get; set; } = 5000f;

    /// <summary>Non-zero makes it PASSABLE. See the class comment.</summary>
    [VarName("Solid")]
    public int NotSolid { get; set; }
}
