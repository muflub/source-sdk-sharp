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
/// sky_camera: where the 3D skybox is filmed from.
///
/// <para>
/// A Source map's horizon is a second, miniature copy of the world built off to
/// one side, and this entity is the camera pointed at it. The client renders
/// that miniature first, translated so this origin sits at the player's eye and
/// scaled up by <c>scale</c>, and then draws the real world on top. Everything
/// distant in a Source map — a skyline, a bridge, a dam — is that trick.
/// </para>
///
/// <para>
/// So there is nothing here that DRAWS, and there was never any question of
/// moving that. What this entity is, is a block of parameters plus three
/// decisions: which visleaf area the camera occupies, whether the fog direction
/// comes from the entity's angles or from a vector the mapper typed, and
/// whether the fog is radial. All three are now C#'s.
/// </para>
///
/// <para>
/// The one thing it cannot do for itself is DELIVERY. The parameters land in
/// <c>CPlayerLocalData::m_skybox3d</c>, a networked embedded struct on the
/// player, and stock reaches it by walking a linked list of <c>CSkyCamera</c>
/// that a managed entity is not in. So this pushes the block through
/// <see cref="IEngineVisibility.SetSkyboxParams"/> and six guarded lines in
/// <c>playerlocaldata.cpp</c> read it — the whole native cost of the port.
/// </para>
///
/// <para>
/// One per map. Stock's own comment says "UNDONE: Support multiple sky
/// cameras?" and takes the head of its list; this takes the last one to spawn,
/// which is the same arbitrary choice made the other way round.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SkyCamera)]
public sealed class SkyCamera : ScriptedEntity
{
    public SkyCamera(IEngineVisibility visibility, IEngineWorldQuery world)
    {
        Visibility = visibility;
        World = world;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>What a map may set on this entity. See <see cref="SkyCameraVars"/>.</summary>
    [ScriptVars] public SkyCameraVars Vars = new();

    private uint _fogColour = 0xFFFFFFFFu;
    private uint _fogColour2 = 0xFFFFFFFFu;

    /// <summary>What was last pushed, for tests and for debug-api.</summary>
    public SkyboxParams Params { get; private set; }

    /// <summary>
    /// The two colour keys, which are the one thing a a <c>[ScriptVars]</c> key
    /// cannot express: they are RGBA bytes, and the closest field type is a
    /// <see cref="Vector"/> of three floats that would drop the alpha and round
    /// the rest twice.
    /// </summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (key.Equals("fogcolor", StringComparison.OrdinalIgnoreCase))
        {
            _fogColour = ParseColour(value);
            return true;
        }

        if (key.Equals("fogcolor2", StringComparison.OrdinalIgnoreCase))
        {
            _fogColour2 = ParseColour(value);
            return true;
        }

        return false;
    }

    public override void OnSpawn()
    {
        // The fog direction, which is the only arithmetic in the entity. Stock
        // negates the forward vector, and the sign is not cosmetic: the blend
        // downstream is a dot product against the view direction, so a flipped
        // vector makes the sky brightest where it should be darkest.
        Vector dir = Vars.UseAngles
            ? Angles.Forward(Entities.GetAngles(Handle)) * -1f
            : Vars.FogDir;

        Vector origin = Origin;

        Params = new SkyboxParams
        {
            OriginX = origin.X,
            OriginY = origin.Y,
            OriginZ = origin.Z,
            Scale   = Vars.Scale,

            // Asked here rather than remembered from the map, because an area
            // is a property of the compiled visibility data and the entity only
            // knows where it is.
            Area = World.GetArea(origin),

            FogEnable = Vars.FogEnable ? 1 : 0,
            FogBlend  = Vars.FogBlend ? 1 : 0,
            FogRadial = Vars.FogRadial ? 1 : 0,

            FogDirX = dir.X,
            FogDirY = dir.Y,
            FogDirZ = dir.Z,

            FogColorPrimary   = _fogColour,
            FogColorSecondary = _fogColour2,

            FogStart      = Vars.FogStart,
            FogEnd        = Vars.FogEnd,
            FogMaxDensity = Vars.FogMaxDensity,
        };

        Visibility.SetSkyboxParams(Params);

        // Said out loud once per map, because the one way this entity fails is
        // silent and the number that says so is right here. Area 255 is the
        // engine's "nowhere": a sky camera placed outside the sealed world, or
        // in a map compiled without vis, gets one and the client then draws no
        // 3D skybox at all — which reads as a missing skybox rather than as a
        // misplaced camera.
        Log.Msg($"[SourceSharp] sky_camera at {origin.X:F0} {origin.Y:F0} {origin.Z:F0}: "
                 + $"area {Params.Area}, scale {Params.Scale}, fog {(Vars.FogEnable ? "on" : "off")}\n");
    }

    /// <summary>
    /// A map with no sky camera must not inherit the last one's. Cleared here
    /// as well as at level shutdown, because a mapper can delete the entity at
    /// runtime and the client would otherwise keep rendering a miniature world
    /// that nothing is filming.
    /// </summary>
    public override void OnRemoved() => Visibility.ClearSkyboxParams();

    /// <summary>
    /// "R G B" or "R G B A" into a packed word, saturating rather than wrapping.
    ///
    /// <para>
    /// Alpha defaults to 255 rather than 0, which matters: the three-component
    /// form is what Hammer writes for a fog colour, and a zero alpha would make
    /// every such map's fog vanish. Stock's UTIL_StringToColor32 does the same.
    /// </para>
    /// </summary>
    public static uint ParseColour(string value)
    {
        Span<int> parts = stackalloc int[4] { 0, 0, 0, 255 };
        int found = 0;

        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (found >= 4)
                break;

            parts[found++] = int.TryParse(token, out int n) ? Math.Clamp(n, 0, 255) : 0;
        }

        return (uint)parts[0]
             | ((uint)parts[1] << 8)
             | ((uint)parts[2] << 16)
             | ((uint)parts[3] << 24);
    }
}

/// <summary>
/// The map keys <see cref="SkyCamera"/> declares.
/// </summary>
[ScriptVars]
public partial class SkyCameraVars
{
    /// <summary>
    /// How many times smaller the miniature world is. 16 is the usual choice,
    /// so a 16-unit crate in the skybox reads as a 256-unit building.
    /// </summary>
    public int Scale { get; set; } = 16;

    /// <summary>
    /// Take the fog direction from where this entity POINTS rather than from
    /// the <c>fogdir</c> key. A mapper who has already aimed the camera should
    /// not have to type the same direction twice.
    /// </summary>
    [VarName("use_angles")]
    public bool UseAngles { get; set; }

    public bool FogEnable { get; set; }

    public bool FogBlend { get; set; }

    public bool FogRadial { get; set; }

    public Vector FogDir { get; set; } = new(1f, 0f, 0f);

    public float FogStart { get; set; }

    public float FogEnd { get; set; }

    /// <summary>
    /// The thickest the fog gets, as a fraction. One by default, which is stock's
    /// constructor rather than stock's keyvalue — a map that omits the key wants
    /// fog that can reach full opacity, not fog that never appears.
    /// </summary>
    public float FogMaxDensity { get; set; } = 1f;
}
