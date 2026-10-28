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
/// point_spotlight: a visible cone of light with a bright patch where it lands.
///
/// <para>
/// It is not one thing but three: this entity, which decides; a beam, which is
/// the shaft of light you can see through the dust; and a
/// <see cref="SpotlightEnd"/>, which is the pool on the floor. The first is a
/// point entity nobody can see, and the other two are already managed — the
/// beam is a <c>beam</c>, the end is a C# class with its own client half.
/// </para>
///
/// <para>
/// The interesting decision in it is the OPTIMISATION, which stock calls an
/// "efficient spotlight" and which is worth keeping because most spotlights in
/// most maps qualify: a spotlight that is not parented to anything and has no
/// name will never move and can never be switched, so it is traced once at map
/// load, its beam is left in place, and the entity <b>deletes itself</b>. Four
/// of dm_lockdown's nine do exactly that.
/// </para>
///
/// <para>
/// What it costs is that such a spotlight cannot then be found or driven. That
/// is the trade stock made and it is the right one — the alternative is a think
/// every tenth of a second, forever, for a lamp that is bolted to a ceiling.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointSpotlight)]
public sealed class PointSpotlight : ScriptedEntity
{
    public PointSpotlight(IEnginePrecache precache, IEngineWorldQuery world)
    {
        Precache = precache;
        World = world;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;


    /// <summary>
    /// The widest a beam may be, from the engine's own beam code. A wider one
    /// is not drawn at all, so clamping is what keeps a mapper's mistake
    /// visible rather than invisible.
    /// </summary>
    public const float MaxBeamWidth = 102.3f;

    /// <summary>
    /// How much wider the pool of light is than the beam that made it. Stock's
    /// comment calls it a magic number depending on the sprite's size, which is
    /// exactly what it is.
    /// </summary>
    private const float LightScalePerWidth = 1.8f;

    /// <summary>What a map may set on this entity. See <see cref="PointSpotlightVars"/>.</summary>
    [ScriptVars] public PointSpotlightVars Vars = new();

    private EntityHandle _beam;
    private EntityHandle _end;
    private SsBeam? _managedBeam;
    private SpotlightEnd? _managedEnd;

    private bool _on;

    /// <summary>
    /// The beam's length as it stands. Seeded from <c>MaxLength</c> in
    /// <see cref="OnSpawn"/> and smoothed from there by the think, which a hot
    /// reload does not run again -- so it is <c>[SaveField]</c>. Uncarried, the
    /// rebuilt instance read zero and the beam flashed back at full width
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _currentLength;

    /// <summary>Where the beam last landed. Kept because the end entity's own
    /// origin is not readable from here — a scripted entity's position is its
    /// own business.</summary>
    private Vector _lastTarget;

    /// <summary>
    /// The precached beam/halo model handles, taken once in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so they
    /// are <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>-1</c> and every later <c>SetLook</c> drew nothing
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _haloSprite = -1;

    /// <inheritdoc cref="_haloSprite"/>
    [SaveField] private int _beamSprite = -1;

    /// <summary>Whether the spotlight is currently lit. For tests and debug-api.</summary>
    public bool IsOn => _on;

    /// <summary>The beam's current length, which the fade is smoothed over. For tests.</summary>
    public float CurrentLength => _currentLength;

    /// <summary>
    /// point_spotlight's own spawnflags.
    ///
    /// <para>
    /// Bit 1 is "start on" here, and on env_spark the same idea is bit 64. That is
    /// the ordinary state of spawnflags across entities and the reason each set
    /// gets its own type rather than a shared "StartOn" constant.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        StartOn = 0b0000_0001,
        NoDynamicLight = 0b0000_0010,
    }

    public readonly Output OnLightOn = new("OnLightOn");
    public readonly Output OnLightOff = new("OnLightOff");

    public override void OnSpawn()
    {
        _haloSprite = Precache.PrecacheModel("sprites/light_glow03.vmt");
        _beamSprite = Precache.PrecacheModel("sprites/glow_test02.vmt");

        // Both of these are mapper error and both are silent otherwise: a zero
        // length traces nowhere and a zero width draws nothing.
        if (Vars.MaxLength <= 0f)
        {
            Log.Msg($"[SourceSharp] point_spotlight '{Name}': length must be positive, using 500\n");
            Vars.MaxLength = 500f;
        }

        if (Vars.GoalWidth <= 0f)
        {
            Log.Msg($"[SourceSharp] point_spotlight '{Name}': width must be positive, using 10\n");
            Vars.GoalWidth = 10f;
        }

        if (Vars.GoalWidth > MaxBeamWidth)
        {
            Log.Msg($"[SourceSharp] point_spotlight '{Name}': width {Vars.GoalWidth} "
                     + $"exceeds the maximum {MaxBeamWidth}\n");
            Vars.GoalWidth = MaxBeamWidth;
        }

        _currentLength = Vars.MaxLength;
        _on = Flags.HasFlag(SpawnFlag.StartOn);

        // Deferred one tick, which is stock's Activate: the beam and the end
        // are entities of their own and creating them mid-parse would put them
        // ahead of the map in the spawn order.
        SetNextThink(0f);
    }

    /// <summary>
    /// The one-shot setup, and then either nothing at all or a think forever.
    ///
    /// <para>
    /// The static case is the whole optimisation: an unnamed spotlight can
    /// never be switched, so once its beam exists there is nothing left for the
    /// entity to do and it removes itself. The beam and the end outlive it —
    /// they are ordinary entities and nothing ties their lifetime to this one.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        if (!_built)
        {
            _built = true;

            if (_on)
                Create();

            if (Vars.TargetName.Length == 0 && Name.Length == 0)
            {
                // Nothing can ever reach it again, so there is nothing left to
                // think about. The beam stays where it was put; the end goes
                // with this entity, as stock's CPointSpotlight::Activate kills
                // "ourselves and the end point" (SpotlightBeamTests).
                StopThinking();
                if (Entities.IsValid(_end))
                    Entities.Remove(_end);
                _end = EntityHandle.None;
                _managedEnd = null;
                Entities.Remove(Handle);
                _static = true;
                return;
            }
        }

        SetNextThink(0.1f);
        Update();
    }

    private bool _built;
    private bool _static;

    /// <summary>Whether this spotlight removed itself as a fixed one. For tests.</summary>
    public bool IsStatic => _static;

    /// <summary>
    /// Where the beam ends: along the entity's forward vector, stopped by the
    /// world unless the mapper said to ignore it.
    ///
    /// <para>
    /// Traced to TWICE the maximum length, which looks like a bug and is not:
    /// the beam fades out past its stated length rather than stopping dead, so
    /// the trace has to find the surface even when that surface is further away
    /// than the light officially reaches.
    /// </para>
    /// </summary>
    private Vector CurrentEnd()
    {
        Vector origin = Origin;
        Vector forward = Angles.Forward(Entities.GetAngles(Handle));
        Vector far = origin + forward * (2f * Vars.MaxLength);

        if (Vars.IgnoreSolid)
            return far;

        return World.TraceLine(origin, far, TraceMask.Brushes, Handle).End;
    }

    private void Create()
    {
        if (Entities.IsValid(_end))
            return;

        Vector origin = Origin;
        Vector forward = Angles.Forward(Entities.GetAngles(Handle));

        Vector target = Vars.IgnoreSolid
            ? origin + forward * Vars.MaxLength
            : World.TraceLine(origin, origin + forward * Vars.MaxLength, TraceMask.Brushes, Handle).End;

        _end = Entities.Create(EntityNames.SpotlightEnd);

        if (Entities.IsValid(_end))
        {
            Entities.Spawn(_end);
            _managedEnd = FindByHandle<SpotlightEnd>(_end);
            _managedEnd?.SetColour((byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z);
            _managedEnd?.Place(target, 0f, 255);
        }

        _lastTarget = target;

        _beam = Entities.Create(EntityNames.Beam);

        if (Entities.IsValid(_beam))
        {
            Entities.Spawn(_beam);
            _managedBeam = FindByHandle<SsBeam>(_beam);

            // Brightness 64 rather than 255, which is stock: a spotlight shaft
            // is meant to read as dusty air, not as a solid bar of light.
            _managedBeam?.SetLook(_beamSprite, Vars.GoalWidth, Vars.GoalWidth, 0f, 0f,
                                  (byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z, 64);
            _managedBeam?.SetPoints(origin, target);

            // Stock's CBeam draws whenever it exists; a beam draws only
            // when told to (SsBeam.Visible), and nothing told this one, so no
            // spotlight's shaft was ever drawn (SpotlightBeamTests).
            if (_managedBeam is not null)
                _managedBeam.Visible = true;
        }

        _currentLength = (target - origin).Length;
        ComputeRenderInfo(_currentLength);

        OnLightOn.Fire();
    }

    private void Destroy()
    {
        if (!Entities.IsValid(_beam) && !Entities.IsValid(_end))
            return;

        OnLightOff.Fire();

        if (Entities.IsValid(_beam)) Entities.Remove(_beam);
        if (Entities.IsValid(_end)) Entities.Remove(_end);

        _beam = EntityHandle.None;
        _end = EntityHandle.None;
        _managedBeam = null;
        _managedEnd = null;
    }

    private void Update()
    {
        if (!_on)
        {
            Destroy();
            return;
        }

        if (!Entities.IsValid(_end))
        {
            Create();
            return;
        }

        Vector origin = Origin;
        Vector target = CurrentEnd();
        float length = (target - origin).Length;

        // Smoothed rather than snapped, and stock's exact weighting. Without it
        // a spotlight sweeping across a doorway snaps its length as the trace
        // crosses the edge, which reads as the beam flickering.
        _currentLength = 0.6f * _currentLength + 0.4f * length;

        _lastTarget = target;
        _managedBeam?.SetPoints(origin, target);

        ComputeRenderInfo(_currentLength);
    }

    /// <summary>
    /// How the beam and the pool look at a given length.
    ///
    /// <para>
    /// Two things happen as a spotlight stretches past the length its mapper
    /// gave it: the pool at the end fades out, reaching nothing at twice the
    /// length, and the beam gets WIDER at the far end so that it appears to
    /// stay the same width in the view. Both are stock's arithmetic and both
    /// are the reason a spotlight aimed down a long corridor still looks right.
    /// </para>
    /// </summary>
    private void ComputeRenderInfo(float length)
    {
        byte alpha = (byte)Math.Clamp((int)MathF.Round(EndAlpha(length, Vars.MaxLength) * 255f), 0, 255);
        float width = EndWidth(length, Vars.MaxLength, Vars.GoalWidth);

        _managedBeam?.SetLook(_beamSprite, Vars.GoalWidth, width, 0f, 0f,
                              (byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z, 64);

        // SpotlightCreate's CBeam: shaded out along its length and untiled,
        // with the halo at its source; ComputeRenderInfo's fade length.
        _managedBeam?.SetEffects(BeamNetFlags.ShadeOut | BeamNetFlags.NoTile, FadeLength(length, Vars.MaxLength),
                                 _haloSprite, HaloScale, Vars.HdrColourScale);

        if (_managedEnd is not null)
        {
            float scale = Flags.HasFlag(SpawnFlag.NoDynamicLight)
                ? 0f
                : LightScalePerWidth * width;

            _managedEnd.Place(_lastTarget, scale, alpha);
        }
    }

    /// <summary>SpotlightCreate's SetHaloScale( 60 ).</summary>
    public const float HaloScale = 60f;

    /// <summary>
    /// How far along the beam it fades: its length, but never more than the
    /// stated length (stock's ComputeRenderInfo, all three arms).
    /// </summary>
    public static float FadeLength(float length, float maxLength) => length > maxLength ? maxLength : length;

    /// <summary>
    /// How much of the pool's colour survives at a given length: all of it
    /// within the stated length, fading linearly to none at twice it.
    /// </summary>
    public static float EndAlpha(float length, float maxLength)
    {
        if (maxLength <= 0f)
            return 0f;

        if (length > 2f * maxLength)
            return 0f;

        if (length > maxLength)
            return 1f - (length - maxLength) / maxLength;

        return 1f;
    }

    /// <summary>
    /// How wide the beam is at the far end, so that it looks the same width
    /// from the emitter however far it reaches.
    /// </summary>
    public static float EndWidth(float length, float maxLength, float goalWidth)
    {
        if (maxLength <= 0f)
            return goalWidth;

        return Math.Clamp(goalWidth * (length / maxLength), 0f, MaxBeamWidth);
    }

    [ScriptedEvent("LightOn")]
    private void OnLightOnInput()
    {
        if (_on)
            return;

        _on = true;
        Create();
    }

    [ScriptedEvent("LightOff")]
    private void OnLightOffInput()
    {
        if (!_on)
            return;

        _on = false;
        Destroy();
    }

    /// <summary>
    /// The beam and the end are ours and nothing else will clean them up — they
    /// are ordinary entities the engine has no reason to associate with this
    /// one.
    ///
    /// <para>
    /// Except when this entity removed ITSELF as a fixed spotlight, where the
    /// whole point is that the light outlives it.
    /// </para>
    /// </summary>
    public override void OnRemoved()
    {
        if (_static)
            return;

        if (Entities.IsValid(_beam)) Entities.Remove(_beam);
        if (Entities.IsValid(_end)) Entities.Remove(_end);
    }
}

/// <summary>
/// The map keys <see cref="PointSpotlight"/> declares.
/// </summary>
[ScriptVars]
public partial class PointSpotlightVars
{
    [VarName("SpotlightLength")]
    public float MaxLength { get; set; }

    [VarName("SpotlightWidth")]
    public float GoalWidth { get; set; }

    public bool IgnoreSolid { get; set; }

    [VarName("HDRColorScale")]
    public float HdrColourScale { get; set; } = 1f;

    [VarName("rendercolor")]
    public Vector Colour { get; set; } = new(255f, 255f, 255f);

    public string TargetName { get; set; } = string.Empty;
}
