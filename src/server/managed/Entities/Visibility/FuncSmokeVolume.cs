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
/// func_smokevolume: a volume full of slowly turning smoke.
///
/// <para>
/// The server entity is a brush and ten keys — stock's <c>Spawn</c> clears the
/// material buffer and binds the model, and its <c>Activate</c> copies the
/// material name across. The client divides the brush into a 3D grid of cells
/// and holds a puff per cell, trading them between neighbours as the smoke
/// drifts, lighting each from the world and rotating it. That is the renderer.
/// </para>
///
/// <para>
/// One thing here IS a decision a map drives at runtime, and it is the reason
/// this entity has inputs at all: <c>SetDensity</c>, <c>SetRotationSpeed</c> and
/// <c>SetMovementSpeed</c>. Stock spells them as <c>DEFINE_INPUT</c> straight
/// onto the networked members, which is the shortest possible way to say "the
/// map writes this and the client reads it" — and exactly what a
/// <c>[ScriptedEvent]</c> writing into networked state says here.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncSmokevolume, ScriptedEntityKind.Brush)]
public sealed class FuncSmokeVolume : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="FuncSmokeVolumeVars"/>.</summary>
    [ScriptVars] public FuncSmokeVolumeVars Vars = new();

    [NetState] public SmokeVolumeNetState State = new();

    public override void OnSpawn()
    {
        // Passable and undrawn, but still SOLID_BSP underneath — which is what
        // BrushSolidity leaves it as. The client reads the brush's own bounds to
        // work out how many cells of smoke fit in it.
        SetSolidity(BrushSolidity.None);
        SetVisible(false);

        State.Red1   = ToByte(Vars.Color1.X);
        State.Green1 = ToByte(Vars.Color1.Y);
        State.Blue1  = ToByte(Vars.Color1.Z);

        State.Red2   = ToByte(Vars.Color2.X);
        State.Green2 = ToByte(Vars.Color2.Y);
        State.Blue2  = ToByte(Vars.Color2.Z);

        State.Material                = Vars.Material;
        State.ParticleDrawWidth       = Vars.ParticleDrawWidth;
        State.ParticleSpacingDistance = Vars.ParticleSpacing;
        State.DensityRampSpeed        = Vars.DensityRampSpeed;
        State.RotationSpeed           = Vars.RotationSpeed;
        State.MovementSpeed           = Vars.MovementSpeed;
        State.Density                 = Vars.Density;
        State.SpawnFlags              = SpawnFlags;
    }

    //-------------------------------------------------------------------------
    // The three inputs, which stock declares as DEFINE_INPUT straight onto the
    // networked members. Writing into networked state from an input handler is
    // the same statement said in this language.
    //
    // The DENSITY is the one that matters and the one the ramp speed exists for:
    // a map fades smoke in and out by driving this, and the client eases toward
    // whatever it last heard rather than snapping.
    //-------------------------------------------------------------------------
    [ScriptedEvent("SetDensity")]       private void OnSetDensity(float v)       => State.Density = v;
    [ScriptedEvent("SetRotationSpeed")] private void OnSetRotationSpeed(float v) => State.RotationSpeed = v;
    [ScriptedEvent("SetMovementSpeed")] private void OnSetMovementSpeed(float v) => State.MovementSpeed = v;

    private static byte ToByte(float value) => (byte)Math.Clamp((int)value, 0, 255);
}

/// <summary>
/// The map keys <see cref="FuncSmokeVolume"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncSmokeVolumeVars
{
    public Vector Color1 { get; set; } = new(255f, 255f, 255f);

    public Vector Color2 { get; set; } = new(255f, 255f, 255f);

    public string Material { get; set; } = string.Empty;

    public float ParticleDrawWidth { get; set; }

    [VarName("ParticleSpacingDistance")]
    public float ParticleSpacing { get; set; }

    public float DensityRampSpeed { get; set; }

    public float RotationSpeed { get; set; }

    public float MovementSpeed { get; set; }

    /// <summary>Stock's constructor defaults this to 1, before any key is read.</summary>
    public float Density { get; set; } = 1f;
}
