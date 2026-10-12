using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The fog a viewer sees, as ten numbers and a validity bit.
///
/// <para>
/// A record struct so that "has anything moved" is <c>==</c> rather than ten
/// comparisons written out, which is the operation <c>WorldFog</c> runs
/// once a frame and the one a hand-written comparison would eventually get one
/// field wrong in.
/// </para>
/// </summary>
public readonly record struct FogState(
    bool Valid,
    bool Enable,
    bool Blend,
    bool Radial,
    Vector DirPrimary,
    uint ColorPrimary,
    uint ColorSecondary,
    float Start,
    float End,
    float FarZ,
    float MaxDensity)
{
    /// <summary>No master fog controller. NOT the same as fog switched off.</summary>
    public static readonly FogState None = default;

    /// <summary>Into a player's networked state, which is a view into its blob.</summary>
    public void WriteTo(PlayerNetState state)
    {
        state.FogValid = Valid;
        state.FogEnable = Enable;
        state.FogBlend = Blend;
        state.FogRadial = Radial;
        state.FogDirPrimary = DirPrimary;
        state.FogColorPrimary = ColorPrimary;
        state.FogColorSecondary = ColorSecondary;
        state.FogStart = Start;
        state.FogEnd = End;
        state.FogFarZ = FarZ;
        state.FogMaxDensity = MaxDensity;
    }

    /// <summary>What a player's networked state currently carries. For the client and for tests.</summary>
    public static FogState From(PlayerNetState state) => new(
        Valid: state.FogValid,
        Enable: state.FogEnable,
        Blend: state.FogBlend,
        Radial: state.FogRadial,
        DirPrimary: state.FogDirPrimary,
        ColorPrimary: state.FogColorPrimary,
        ColorSecondary: state.FogColorSecondary,
        Start: state.FogStart,
        End: state.FogEnd,
        FarZ: state.FogFarZ,
        MaxDensity: state.FogMaxDensity);
}
