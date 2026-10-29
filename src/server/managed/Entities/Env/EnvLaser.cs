//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_laser (stock CEnvLaser, EnvLaser.cpp): a beam from where it stands to
/// its target (one of the entities of that name, picked afresh each tick),
/// stopped by what is solid in the way, burning what it stops on every tenth
/// of a second, with an optional glow sprite at its far end.
/// </summary>
[ScriptedEntity(EntityNames.EnvLaser)]
public sealed class EnvLaser : BeamEntity
{
    public EnvLaser(IGameClock clock, IEngineHost host, IEngineDamage damage, IEngineEffects effects,
                    IServerDecals decals, IEngineWorldQuery world, IEngineEntityRender render,
                    IEnginePrecache precache)
        : base(clock, host, damage, effects, decals, world, render)
    {
        Precache = precache;
    }

    private readonly IEnginePrecache Precache;

    /// <summary>What a map may set on this entity. See <see cref="EnvLaserVars"/>.</summary>
    [ScriptVars] public EnvLaserVars Vars = new();

    /// <summary>kRenderGlow.</summary>
    private const int RenderGlow = 3;

    private EntityHandle _sprite = EntityHandle.None;

    /// <summary>GetAbsEndPos: where the beam last ended, which it fires at when the target is gone.</summary>
    [SaveField] private Vector _end;

    private bool _on;

    public override void OnSpawn()
    {
        if (Vars.Sprite.Length == 0)
        {
            // SetThink( SUB_Remove ).
            Remove();
            return;
        }

        // PointsInit( origin, origin ).
        _end = Origin;

        int sprite = Precache.PrecacheModel(Vars.Sprite);
        if (Vars.EndSprite.Length > 0)
            Precache.PrecacheModel(Vars.EndSprite);

        if (!CreateBeam() || Beam is null)
            return;

        float width = Math.Min(MaxWidth, Vars.Width);
        Beam.SetLook(sprite, width, width, Math.Min(MaxNoiseAmplitude, Vars.Noise),
                     Math.Clamp(Vars.ScrollRate, 0, (int)MaxScrollSpeed),
                     (byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z, (byte)Vars.Brightness);
        Beam.SetPoints(Origin, Origin);

        if (Vars.EndSprite.Length > 0)
            CreateEndSprite();

        if (Name.Length > 0 && !Flags.HasFlag(SpawnFlag.StartOn))
            TurnOff();
        else
            TurnOn();
    }

    /// <summary>
    /// CSprite::SpriteCreate at the laser, under its parent, drawn as a glow
    /// in the laser's colour.
    /// </summary>
    private void CreateEndSprite()
    {
        _sprite = Entities.Create(EntityNames.EnvSprite);
        if (_sprite == EntityHandle.None)
            return;

        Entities.SetKeyValue(_sprite, "model", Vars.EndSprite);
        Entities.SetKeyValue(_sprite, "rendermode", RenderGlow.ToString(CultureInfo.InvariantCulture));
        Entities.SetKeyValue(_sprite, "rendercolor", string.Create(CultureInfo.InvariantCulture,
            $"{(int)Vars.Colour.X} {(int)Vars.Colour.Y} {(int)Vars.Colour.Z}"));
        Entities.SetKeyValue(_sprite, "renderamt", Vars.Brightness.ToString(CultureInfo.InvariantCulture));
        Entities.SetKeyValue(_sprite, "renderfx", Vars.RenderFx.ToString(CultureInfo.InvariantCulture));
        Entities.SetOrigin(_sprite, Origin);
        Entities.Spawn(_sprite);

        EntityHandle parent = Entities.GetParent(Handle);
        if (Entities.IsValid(parent))
            Entities.SetParent(_sprite, parent);
    }

    public override void OnRemoved()
    {
        base.OnRemoved();
        if (_sprite != EntityHandle.None && Entities.IsValid(_sprite))
            Entities.Remove(_sprite);
    }

    private EnvSprite? EndSprite => _sprite == EntityHandle.None ? null : FindByHandle<EnvSprite>(_sprite);

    private void TurnOn()
    {
        _on = true;
        if (Beam is not null)
            Beam.Visible = true;
        EndSprite?.TurnOn();
        FireTime = Clock.Now;

        // StrikeThink now: the end where it belongs before the first frame drawn.
        StrikeThink();
    }

    private void TurnOff()
    {
        _on = false;
        if (Beam is not null)
            Beam.Visible = false;
        EndSprite?.TurnOff();
        StopThinking();
    }

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn()
    {
        if (!_on)
            TurnOn();
    }

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff()
    {
        if (_on)
            TurnOff();
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (_on)
            TurnOff();
        else
            TurnOn();
    }

    public override void OnThink() => StrikeThink();

    /// <summary>StrikeThink: at a target picked afresh, or where it last ended; traced; fired at.</summary>
    private void StrikeThink()
    {
        EntityHandle end = RandomTargetname(Vars.Target);
        Vector fireAt = end != EntityHandle.None ? Entities.GetOrigin(end) : _end;

        var trace = World.TraceLine(Origin, fireAt, TraceMask.Solid);
        FireAtPoint(trace);

        SetNextThink(0f);
    }

    /// <summary>FireAtPoint: the beam ends where the trace did; damage and sparks every tenth of a second.</summary>
    private void FireAtPoint(in TraceResult trace)
    {
        _end = trace.End;
        Beam?.SetPoints(Origin, trace.End);
        if (_sprite != EntityHandle.None)
            Entities.SetOrigin(_sprite, trace.End);

        // Apply damage and do sparks every 1/10th of a second.
        if (Clock.Now >= FireTime + 0.1f)
        {
            BeamDamage(trace, Origin);
            DoSparks(Origin, trace.End);
        }
    }
}

/// <summary>
/// The map keys <see cref="EnvLaser"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvLaserVars
{
    [VarName("LaserTarget")]
    public string Target { get; set; } = string.Empty;

    [VarName("texture")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>The glow drawn where the beam ends.</summary>
    [VarName("EndSprite")]
    public string EndSprite { get; set; } = string.Empty;

    public float Width { get; set; } = 2f;

    /// <summary>atoi'd, as stock's KeyValue reads it.</summary>
    [VarName("NoiseAmplitude")]
    public int Noise { get; set; }

    [VarName("TextureScroll")]
    public int ScrollRate { get; set; } = 35;

    [VarName("framestart")]
    public float FrameStart { get; set; }

    [VarName("renderamt")]
    public int Brightness { get; set; } = 255;

    [VarName("rendercolor")]
    public Vector Colour { get; set; } = new(255f, 255f, 255f);

    [VarName("renderfx")]
    public int RenderFx { get; set; }
}
