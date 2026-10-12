using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The 3D skybox a viewer is being shown, as twelve numbers and a validity bit.
///
/// <para>
/// <see cref="FogState"/>'s twin, deliberately: the same record struct so that
/// "has anything moved" is <c>==</c> rather than twelve comparisons written
/// out, the same <c>WriteTo</c>/<c>From</c> pair over
/// <see cref="PlayerNetState"/>, and the same reason for living in
/// <c>SourceSharp.Shared</c> — the server builds one and the client reads one
/// out of the same blob, and neither assembly should have to reference the
/// other to do it.
/// </para>
///
/// <para>
/// <b>VALID IS NOT "AREA 255".</b> The two mean different things and collapsing
/// them is the bug this type is shaped to avoid. <see cref="Valid"/> false is
/// "no sky camera has been heard from" and makes
/// <c>SourceSharp_ViewerSkyboxParams</c> answer NULL, which is what stock's
/// <c>CSkyboxView</c> gets for a player with no sky camera. Area 255 is a sky
/// camera that IS there and sits in the engine's "nowhere" — outside the sealed
/// world, or in a map compiled without vis — and <c>PreRender3dSkyboxWorld</c>
/// refuses it on its own line. A zeroed struct would say neither: area 0 is a
/// REAL visleaf area, so it would send the renderer at a camera that is not
/// there. That is step 7c's objection and it is why this carries a bit rather
/// than relying on a sentinel.
/// </para>
/// </summary>
public readonly record struct SkyState(
    bool Valid,
    Vector Origin,
    int Scale,
    int Area,
    bool FogEnable,
    bool FogBlend,
    bool FogRadial,
    Vector FogDirPrimary,
    uint FogColorPrimary,
    uint FogColorSecondary,
    float FogStart,
    float FogEnd,
    float FogMaxDensity)
{
    /// <summary>No sky camera. NOT the same as a sky camera in area 255.</summary>
    public static readonly SkyState None = default;

    /// <summary>Into a player's networked state, which is a view into its blob.</summary>
    public void WriteTo(PlayerNetState state)
    {
        state.SkyValid = Valid;
        state.SkyOrigin = Origin;
        state.SkyScale = Scale;
        state.SkyArea = Area;
        state.SkyFogEnable = FogEnable;
        state.SkyFogBlend = FogBlend;
        state.SkyFogRadial = FogRadial;
        state.SkyFogDirPrimary = FogDirPrimary;
        state.SkyFogColorPrimary = FogColorPrimary;
        state.SkyFogColorSecondary = FogColorSecondary;
        state.SkyFogStart = FogStart;
        state.SkyFogEnd = FogEnd;
        state.SkyFogMaxDensity = FogMaxDensity;
    }

    /// <summary>What a player's networked state currently carries. For the client and for tests.</summary>
    public static SkyState From(PlayerNetState state) => new(
        Valid: state.SkyValid,
        Origin: state.SkyOrigin,
        Scale: state.SkyScale,
        Area: state.SkyArea,
        FogEnable: state.SkyFogEnable,
        FogBlend: state.SkyFogBlend,
        FogRadial: state.SkyFogRadial,
        FogDirPrimary: state.SkyFogDirPrimary,
        FogColorPrimary: state.SkyFogColorPrimary,
        FogColorSecondary: state.SkyFogColorSecondary,
        FogStart: state.SkyFogStart,
        FogEnd: state.SkyFogEnd,
        FogMaxDensity: state.SkyFogMaxDensity);
}
