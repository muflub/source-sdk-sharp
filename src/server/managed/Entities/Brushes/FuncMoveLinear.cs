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
/// A brush that slides between two points and can be parked anywhere between
/// them. Lifts and shutters, and anything a map wants to set to a fraction
/// rather than open or shut.
///
/// Water that rises and falls is the same entity: a volume of water IS a brush
/// sliding along a line, and stock says so by registering the third name below
/// to this class rather than writing another one.
/// </summary>
[ScriptedEntity(EntityNames.FuncMovelinear, ScriptedEntityKind.Brush)]
// The SDK registers this name to the same class, for maps old enough to use it.
[ScriptedEntity(EntityNames.MomentaryDoor, ScriptedEntityKind.Brush)]
// And this one, for water whose level moves. See IsWater.
[ScriptedEntity(EntityNames.FuncWaterAnalog, ScriptedEntityKind.Brush)]
public sealed class FuncMoveLinear : ScriptedEntity
{
    public FuncMoveLinear(IEnginePrecache precache, IEngineSound sound, IEngineDamage damage)
    {
        Precache = precache;
        Sound = sound;
        Damage = damage;
    }

    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;
    private readonly IEngineDamage Damage;

    /// <summary>Stock's Spawn makes it SOLID_VPHYSICS.</summary>
    protected override SolidType BrushSolidType => SolidType.VPhysics;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public readonly Output OnFullyOpen = new();
    public readonly Output OnFullyClosed = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncMoveLinearVars"/>.</summary>
    [ScriptVars] public FuncMoveLinearVars Vars = new();

    /// <summary>
    /// The two ends of the travel, in world space. Computed ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so they
    /// are <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> for both and every move after a <c>cs_sv_reload</c>
    /// headed for the world origin (FuncMoveLinearReloadTests).
    /// </summary>
    [SaveField] private Vector _closed;

    /// <inheritdoc cref="_closed"/>
    [SaveField] private Vector _open;

    /// <summary>
    /// Where it is heading, as a fraction of the travel. 0 shut, 1 open.
    /// Carried for the same reason: the arrival reports by it.
    /// </summary>
    [SaveField] private float _target;

    /// <summary>m_vecFinalDest: where the last move was headed, which SetSpeed re-aims at.</summary>
    [SaveField] private Vector _finalDest;

    /// <summary>m_currentSound: the start or the stop sound, whichever played last.</summary>
    [SaveField] private string _currentSound = string.Empty;

    private const string StopSoundContext = "MoveLinearStopSound";

    /// <summary>Makes the brush passable. Ignored by water, which see.</summary>
    private const int SF_NotSolid = 8;

    /// <summary>
    /// Whether this one is a body of water rather than a slab of world.
    ///
    /// The only thing the classname decides, and stock decides it the same way
    /// — <c>FClassnameIs</c>, twice, inside Spawn. What it changes is that the
    /// water is never made passable by the spawnflag: a volume of water you
    /// cannot enter is not water.
    /// </summary>
    private bool IsWater
        => string.Equals(ClassName, EntityNames.FuncWaterAnalog, StringComparison.OrdinalIgnoreCase);

    public override void OnSpawn()
    {
        SetSolidity(!IsWater && (SpawnFlags & SF_NotSolid) != 0
            ? BrushSolidity.None
            : BrushSolidity.Solid);

        // func_water_analog's CreateVPhysics: FSOLID_VOLUME_CONTENTS, so the
        // engine's point contents report the moving brush as water.
        if (IsWater)
            SetSolidity(BrushSolidType, SolidFlags.VolumeContents);

        // CFuncMoveLinear::CreateVPhysics: a shadow, unless it is not solid
        // (water always has one).
        if (IsWater || (SpawnFlags & SF_NotSolid) == 0)
            InitShadowPhysics();

        SetVisible(true);

        // Same rule as a door (stock's CFuncMoveLinear::Spawn): the direction
        // is the movedir key, 0 0 0 (+x) when the map gives none, and the
        // angles stay the brush's rotation.
        var direction = Angles.Forward(Vars.MoveDir);

        // No distance: the brush's own depth along the direction, less the 2
        // the engine grows its bounds by and less the lip.
        float distance = Vars.MoveDistance > 0f
            ? Vars.MoveDistance
            : FuncDoor.DepthAlong(Entities.GetBoundsSize(Handle), direction) - Vars.Lip;

        // StartPosition is a fraction, so the drawn position is wherever along
        // the travel the mapper parked it — not necessarily the shut end.
        var drawn = SpawnOrigin;
        _closed = drawn - direction * (distance * Vars.StartPosition);
        _open = _closed + direction * distance;

        _target = Vars.StartPosition;

        if (Vars.Speed <= 0f)
            Vars.Speed = 100f;

        if (Vars.StartSound.Length > 0)
            Precache.PrecacheSound(Vars.StartSound);
        if (Vars.StopSound.Length > 0)
            Precache.PrecacheSound(Vars.StopSound);
    }

    /// <summary>InputOpen: to the open end, unless it is there.</summary>
    [ScriptedEvent("Open")]
    private void OnOpenInput()
    {
        if (!Origin.Equals(_open))
            Move(_open, 1f);
    }

    /// <summary>InputClose: to the shut end, unless it is there.</summary>
    [ScriptedEvent("Close")]
    private void OnCloseInput()
    {
        if (!Origin.Equals(_closed))
            Move(_closed, 0f);
    }

    /// <summary>CFuncMoveLinear::SetPosition: that fraction of the travel, unclamped, unless it is there already.</summary>
    [ScriptedEvent]
    private void OnSetPosition(float fraction)
    {
        Vector target = _closed + (_open - _closed) * fraction;
        if ((target - Origin).Length > 0.001f)
            Move(target, fraction);
    }

    /// <summary>InputSetSpeed: the speed, and a move under way re-aimed at it.</summary>
    [ScriptedEvent]
    private void OnSetSpeed(float speed)
    {
        Vars.Speed = speed;
        if ((_finalDest - Origin).LengthSquared > float.Epsilon * float.Epsilon)
            MoveTo(_finalDest, Vars.Speed);
    }

    /// <summary>CFuncMoveLinear::MoveTo: the start sound, unless it is already playing, then the travel.</summary>
    private void Move(Vector destination, float fraction)
    {
        if (Vars.Speed == 0f)
            return;

        _target = fraction;

        if (Vars.StartSound.Length > 0)
        {
            if (_currentSound == Vars.StartSound)
            {
                if (Vars.StopSound.Length > 0)
                    Sound.EntityStopSound(Handle, Vars.StopSound);
            }
            else
            {
                _currentSound = Vars.StartSound;
                Sound.EntityEmitSound(Handle, Vars.StartSound);
            }
        }

        _finalDest = destination;
        MoveTo(destination, Vars.Speed);
        StopThinking(StopSoundContext);
    }

    public override void OnMoveDone()
    {
        // The sounds stop a tenth of a second on, as another move may follow at once.
        SetContextThink(StopSoundContext, 0.1f);

        // Only the ends are reported. A map that parks this halfway is not
        // opening or closing it, and firing either output there would be a lie.
        if (_target >= 1f)
            OnFullyOpen.Fire();
        else if (_target <= 0f)
            OnFullyClosed.Fire();
    }

    public override void OnThink(string context)
    {
        if (context == StopSoundContext)
            StopMoveSound();
    }

    /// <summary>CFuncMoveLinear::StopMoveSound: the start sound off, and the stop sound once.</summary>
    private void StopMoveSound()
    {
        if (Vars.StartSound.Length > 0 && _currentSound == Vars.StartSound)
            Sound.EntityStopSound(Handle, Vars.StartSound);

        if (Vars.StopSound.Length > 0 && _currentSound != Vars.StopSound)
        {
            _currentSound = Vars.StopSound;
            Sound.EntityEmitSound(Handle, Vars.StopSound);
        }
    }

    /// <summary>
    /// CFuncMoveLinear::Blocked: BlockDamage's crush to what is in the way;
    /// a gib, which takes damage events only, is removed instead.
    /// </summary>
    public override void OnBlocked(EntityHandle other)
    {
        if (Vars.BlockDamage == 0f)
            return;

        if (!(FindByHandle<ScriptedEntity>(other)?.TakesDamage ?? true))
        {
            if (string.Equals(Entities.GetClassName(other), EntityNames.Gib, StringComparison.OrdinalIgnoreCase))
                Entities.Remove(other);
            return;
        }

        Damage.Apply(other, Handle, Vars.BlockDamage, DamageType.Crush);
    }
}

/// <summary>
/// The map keys <see cref="FuncMoveLinear"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncMoveLinearVars
{
    public QAngle MoveDir { get; set; }

    /// <summary>How far it slides. Zero (stock's default) means its own depth along movedir, less the lip.</summary>
    public float MoveDistance { get; set; }

    /// <summary>CBaseToggle's lip: how much of the depth it does not travel when the distance is its own.</summary>
    public float Lip { get; set; }

    public float StartPosition { get; set; }

    public float Speed { get; set; } = 100f;

    /// <summary>The sound when it starts to move.</summary>
    [VarName("StartSound")]
    public string StartSound { get; set; } = string.Empty;

    /// <summary>The sound when it stops.</summary>
    [VarName("StopSound")]
    public string StopSound { get; set; } = string.Empty;

    /// <summary>The crush it deals to what blocks it, each frame.</summary>
    [VarName("BlockDamage")]
    public float BlockDamage { get; set; }
}
