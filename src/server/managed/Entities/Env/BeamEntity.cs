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
/// CBeam's server half (beam_shared.cpp), under env_beam and env_laser: the
/// beam it draws with, the sparks at its ends, the damage along it, a
/// random pick among the entities of a name, and the inputs every beam takes.
/// </summary>
public abstract class BeamEntity : ScriptedEntity
{
    protected BeamEntity(IGameClock clock, IEngineHost host, IEngineDamage damage, IEngineEffects effects,
                         IServerDecals decals, IEngineWorldQuery world, IEngineEntityRender render)
    {
        Clock = clock;
        Host = host;
        Damage = damage;
        Effects = effects;
        Decals = decals;
        World = world;
        Render = render;
    }

    protected readonly IGameClock Clock;
    protected readonly IEngineHost Host;
    private readonly IEngineDamage Damage;
    protected readonly IEngineEffects Effects;
    private readonly IServerDecals Decals;
    protected readonly IEngineWorldQuery World;
    protected readonly IEngineEntityRender Render;

    /// <summary>MAX_BEAM_WIDTH, MAX_BEAM_SCROLLSPEED, MAX_BEAM_NOISEAMPLITUDE (beam_shared.h).</summary>
    public const float MaxWidth = 102.3f, MaxScrollSpeed = 100f, MaxNoiseAmplitude = 64f;

    /// <summary>The SF_BEAM_* spawnflags (beam_shared.h).</summary>
    [Flags]
    public enum SpawnFlag
    {
        None = 0,
        StartOn = 0x0001,
        Toggle = 0x0002,
        Random = 0x0004,
        Ring = 0x0008,
        SparkStart = 0x0010,
        SparkEnd = 0x0020,
        Decals = 0x0040,
        ShadeIn = 0x0080,
        ShadeOut = 0x0100,
        TaperOut = 0x0200,
    }

    protected SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>What a map may set on any beam. See <see cref="BeamVars"/>.</summary>
    [ScriptVars] public BeamVars BeamVars = new();

    /// <summary>The beam drawn, when this beam draws one of its own.</summary>
    protected SsBeam? Beam { get; private set; }

    private EntityHandle _beam = EntityHandle.None;

    /// <summary>m_flFireTime: when damage was last dealt, which the next damage is scaled from.</summary>
    [SaveField] protected float FireTime;

    /// <summary>The beam this draws with, made once.</summary>
    protected bool CreateBeam()
    {
        if (Beam is not null)
            return true;

        _beam = Entities.Create(EntityNames.Beam);
        if (!Entities.IsValid(_beam))
            return false;

        Entities.Spawn(_beam);
        Beam = FindByHandle<SsBeam>(_beam);
        if (Beam is not null)
            Beam.State.HdrColorScale = BeamVars.HdrColorScale;
        return Beam is not null;
    }

    public override void OnRemoved()
    {
        if (Entities.IsValid(_beam))
            Entities.Remove(_beam);
    }

    /// <summary>
    /// CBeam::RandomTargetname: one of the entities of a name, each as likely
    /// as the others (a reservoir draw of one).
    /// </summary>
    protected EntityHandle RandomTargetname(string name)
    {
        if (name.Length == 0)
            return EntityHandle.None;

        int total = 0;
        EntityHandle picked = EntityHandle.None;
        foreach (EntityHandle found in Entities.FindAllByName(name))
        {
            total++;
            if (Host.RandomInt(0, total - 1) < 1)
                picked = found;
        }

        return picked;
    }

    /// <summary>
    /// IsStaticPointEntity (beam_shared.cpp): unparented, and with no model or
    /// one of the point classes a mapper aims beams at.
    /// </summary>
    protected bool IsStaticPoint(EntityHandle entity)
    {
        if (Entities.IsValid(Entities.GetParent(entity)))
            return false;

        if (Render.GetModelName(entity).Length == 0)
            return true;

        string className = Entities.GetClassName(entity);
        return className is EntityNames.InfoTarget or EntityNames.InfoLandmark or EntityNames.PathCorner;
    }

    /// <summary>CBeam::DoSparks: sparks at the start, the end or both, as the spawnflags say.</summary>
    protected void DoSparks(Vector start, Vector end)
    {
        // g_pEffects->Sparks( pos ): magnitude 1, trail 1, no sound, no direction.
        if (Flags.HasFlag(SpawnFlag.SparkStart))
            Effects.SparkEffect(start, 1, 1, makeSound: false);
        if (Flags.HasFlag(SpawnFlag.SparkEnd))
            Effects.SparkEffect(end, 1, 1, makeSound: false);
    }

    /// <summary>CBeam::GetDecalName: the decal a damaging beam paints on a brush it burns.</summary>
    protected virtual string DecalName => "BigShot";

    /// <summary>
    /// CBeam::BeamDamage: what the trace stopped on takes the damage of the
    /// time since the last damage, energy or dissolving as dissolvetype says,
    /// pushed along the beam; a brush is marked with the decal.
    /// </summary>
    protected void BeamDamage(in TraceResult trace, Vector origin)
    {
        if (trace.Fraction != 1f && trace.Entity != EntityHandle.None && Entities.IsValid(trace.Entity))
        {
            Vector dir = (trace.End - origin).Normalized;

            DamageType type = BeamVars.DissolveType switch
            {
                0 => DamageType.Dissolve,
                > 0 => DamageType.Dissolve | DamageType.Shock,
                _ => DamageType.EnergyBeam,
            };

            float amount = BeamVars.Damage * (Clock.Now - FireTime);
            Damage.ApplyAt(trace.Entity, Handle, amount, type, DamageForceRules.MeleeForce(dir, amount), trace.End);

            if (Flags.HasFlag(SpawnFlag.Decals) && IsBspModel(trace.Entity) && DecalName.Length > 0)
            {
                int decal = Decals.IndexForName(DecalName);
                if (decal >= 0)
                    Effects.PaintDecal(decal, trace.End, angles: null, projectDistance: 0f, permanent: false, lowPriority: false);
            }
        }

        FireTime = Clock.Now;
    }

    /// <summary>CBeam::BeamDamageInstant: a whole second's damage at once.</summary>
    protected void BeamDamageInstant(in TraceResult trace, Vector origin)
    {
        FireTime = Clock.Now - 1f;
        BeamDamage(trace, origin);
    }

    /// <summary>IsBSPModel: the world or a brush entity.</summary>
    private bool IsBspModel(EntityHandle entity)
    {
        string model = Render.GetModelName(entity);
        return model.StartsWith('*') || model.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase);
    }

    // CBeam's inputs.

    [ScriptedEvent("Width")]
    private void OnWidth(float width) => Beam?.SetWidth(Math.Min(MaxWidth, width), Math.Min(MaxWidth, width));

    [ScriptedEvent("Noise")]
    private void OnNoise(float noise) => Beam?.SetNoise(noise);

    [ScriptedEvent("ColorRedValue")]
    private void OnColorRed(float value)
    {
        if (Beam is { } b)
            b.SetColour(ColourByte(value), b.State.Green, b.State.Blue);
    }

    [ScriptedEvent("ColorGreenValue")]
    private void OnColorGreen(float value)
    {
        if (Beam is { } b)
            b.SetColour(b.State.Red, ColourByte(value), b.State.Blue);
    }

    [ScriptedEvent("ColorBlueValue")]
    private void OnColorBlue(float value)
    {
        if (Beam is { } b)
            b.SetColour(b.State.Red, b.State.Green, ColourByte(value));
    }

    /// <summary>clamp( FastFloatToSmallInt( value ), 0, 255 ).</summary>
    private static byte ColourByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}

/// <summary>The map keys every <see cref="BeamEntity"/> declares (CBeam's).</summary>
[ScriptVars]
public partial class BeamVars
{
    /// <summary>Damage a second along the beam.</summary>
    public float Damage { get; set; }

    /// <summary>-1 energy, 0 dissolve, above 0 dissolve and shock.</summary>
    public int DissolveType { get; set; } = -1;

    public float HdrColorScale { get; set; } = 1f;
}
