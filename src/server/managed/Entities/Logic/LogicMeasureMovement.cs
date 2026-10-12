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
/// Copies one entity's movement onto another, measured against a pair of
/// reference points.
///
/// Four names, and the arithmetic between them is the whole entity: where the
/// measured entity sits RELATIVE to its reference is worked out every tick and
/// put back on the target relative to the target's own reference. A player
/// walking around a room can drive a model walking around a copy of the room
/// somewhere else, and the scale is what lets the copy be a different size.
///
/// It names no path nodes and owns no graph, despite sitting beside them in the
/// SDK: what it measures is a transform, and the four entities it holds are
/// ordinary ones.
/// </summary>
[ScriptedEntity(EntityNames.LogicMeasureMovement)]
public sealed class LogicMeasureMovement : ScriptedEntity
{
    /// <summary>
    /// Measure where the entity is LOOKING from rather than where it is, which
    /// for a player is not the same place. Anything else, 0 included, means the
    /// position — stock leaves the matrix uninitialised for a third value, and
    /// there is no reading of that worth reproducing.
    /// </summary>
    private const int MeasureEyePosition = 1;

    /// <summary>What a map may set on this entity. See <see cref="LogicMeasureMovementVars"/>.</summary>
    [ScriptVars] public LogicMeasureMovementVars Vars = new();

    private EntityHandle _measureTarget = EntityHandle.None;
    private EntityHandle _measureReference = EntityHandle.None;
    private EntityHandle _target = EntityHandle.None;
    private EntityHandle _targetReference = EntityHandle.None;

    private bool _enabled = true;
    private bool _reported;

    public override void OnSpawn() => SetNextThink(0f);

    public override void OnThink()
    {
        if (!_enabled)
            return;

        _measureTarget = Resolve(_measureTarget, Vars.MeasureTargetName);
        _measureReference = Resolve(_measureReference, Vars.MeasureReferenceName);
        _target = Resolve(_target, Vars.TargetName);
        _targetReference = Resolve(_targetReference, Vars.TargetReferenceName);

        // Once, on the first tick, and then never again however long it stays
        // broken: a name that will not resolve is a mapping mistake worth one
        // line, and a mistake repeated every tick is a mistake nobody reads.
        if (!_reported)
        {
            _reported = true;
            Report("MeasureTarget", _measureTarget, Vars.MeasureTargetName);
            Report("MeasureReference", _measureReference, Vars.MeasureReferenceName);
            Report("target", _target, Vars.TargetName);
            Report("TargetReference", _targetReference, Vars.TargetReferenceName);
        }

        if (Entities.IsValid(_measureTarget) && Entities.IsValid(_measureReference) &&
            Entities.IsValid(_target) && Entities.IsValid(_targetReference))
        {
            Apply();
        }

        SetNextThink(0f);
    }

    /// <summary>
    /// Finds a named entity, and keeps trying on later ticks.
    ///
    /// Stock resolves all four once and then special-cases "!player", because a
    /// player does not exist while the map is loading. Retrying whatever is
    /// currently missing covers that without naming it, and covers what stock
    /// does not: an entity that spawns late, and one that was removed and put
    /// back.
    /// </summary>
    private EntityHandle Resolve(EntityHandle current, string name)
        => Entities.IsValid(current) || name.Length == 0 ? current : Entities.FindByName(name);

    private void Report(string key, EntityHandle found, string name)
    {
        if (!Entities.IsValid(found) && name.Length > 0)
            Log.Warning($"[SourceSharp] logic_measure_movement '{Name}': {key} names '{name}', which is not in the map\n");
    }

    private void Apply()
    {
        // Where the measured entity's reference sits, expressed in the measured
        // entity's own frame. Everything the measured entity does to itself
        // divides out here, which is what makes this a measurement of MOVEMENT
        // rather than of position.
        var worldToMeasure = Vars.MeasureType == MeasureEyePosition
            ? Frame.OfEyes(Entities, _measureTarget).Inverted
            : Frame.Of(Entities, _measureTarget).Inverted;

        var referenceInMeasure = worldToMeasure * Frame.Of(Entities, _measureReference);

        // Scale divides the offset and leaves the rotation alone: a half-size
        // copy of a room is still the same way up.
        if (Vars.Scale != 0f && Vars.Scale != 1f)
            referenceInMeasure = referenceInMeasure.WithScaledOffset(Vars.Scale);

        var placed = Frame.Of(Entities, _targetReference) * referenceInMeasure.Inverted;

        Entities.SetOrigin(_target, placed.Offset);
        Entities.SetAngles(_target, placed.Angles);
    }

    /// <summary>
    /// The arithmetic on its own, so it can be checked without a map.
    ///
    /// Separated because it is the part that can be wrong in a way nothing
    /// reports: a sign error here does not fail, it puts the target somewhere
    /// plausible and slightly wrong.
    /// </summary>
    internal static (Vector Origin, Vector Angles) Place(
        Vector measureOrigin, Vector measureAngles,
        Vector measureReferenceOrigin, Vector measureReferenceAngles,
        Vector targetReferenceOrigin, Vector targetReferenceAngles,
        float scale)
    {
        var referenceInMeasure =
            Frame.FromAngles(measureAngles, measureOrigin).Inverted *
            Frame.FromAngles(measureReferenceAngles, measureReferenceOrigin);

        if (scale != 0f && scale != 1f)
            referenceInMeasure = referenceInMeasure.WithScaledOffset(scale);

        var placed = Frame.FromAngles(targetReferenceAngles, targetReferenceOrigin) *
                     referenceInMeasure.Inverted;

        return (placed.Offset, placed.Angles);
    }

    [ScriptedEvent] private void OnSetMeasureTarget(string name)
    {
        Vars.MeasureTargetName = name;
        _measureTarget = EntityHandle.None;
    }

    [ScriptedEvent] private void OnSetMeasureReference(string name)
    {
        Vars.MeasureReferenceName = name;
        _measureReference = EntityHandle.None;
    }

    [ScriptedEvent] private void OnSetTarget(string name)
    {
        Vars.TargetName = name;
        _target = EntityHandle.None;
    }

    [ScriptedEvent] private void OnSetTargetReference(string name)
    {
        Vars.TargetReferenceName = name;
        _targetReference = EntityHandle.None;
    }

    [ScriptedEvent] private void OnSetTargetScale(float scale) => Vars.Scale = scale;

    [ScriptedEvent] private void OnEnable()
    {
        _enabled = true;
        SetNextThink(0f);
    }

    [ScriptedEvent] private void OnDisable()
    {
        _enabled = false;
        StopThinking();
    }

    /// <summary>
    /// A position and an orientation together, as the three basis columns Source
    /// stores in a matrix3x4_t.
    ///
    /// Here rather than as arithmetic on pitch/yaw/roll because what this entity
    /// does is change frame, twice, and doing that with euler angles is exactly
    /// where the sign errors live. Composing and inverting three columns and an
    /// offset is exact, and the conversion back to angles happens once at the end.
    /// </summary>
    private readonly struct Frame
    {
        // Source's AngleMatrix columns, in its order: forward, LEFT — which is
        // the negated right — and up.
        private readonly Vector _forward;
        private readonly Vector _left;
        private readonly Vector _up;

        /// <summary>Where the frame's origin sits in whatever frame contains it.</summary>
        public readonly Vector Offset;

        private Frame(Vector forward, Vector left, Vector up, Vector offset)
        {
            _forward = forward;
            _left = left;
            _up = up;
            Offset = offset;
        }

        public static Frame FromAngles(Vector angles, Vector offset)
        {
            // Qualified because this struct has an Angles of its own, going the
            // other way.
            var basis = SourceSharp.Abi.Angles.Vectors(angles);
            return new Frame(basis.Forward, basis.Right * -1f, basis.Up, offset);
        }

        // The engine arrives as a PARAMETER here, unlike everywhere else in
        // this file. Frame is a nested struct, so it has no outer instance to
        // read an injected property from, and making it hold one would put a
        // reference in a value type that is copied on every multiplication.
        public static Frame Of(IEngineEntities entities, EntityHandle entity)
            => FromAngles(entities.GetAngles(entity), entities.GetOrigin(entity));

        /// <summary>Where something looks from, which for a player is neither its origin nor its angles.</summary>
        public static Frame OfEyes(IEngineEntities entities, EntityHandle entity)
            => FromAngles(entities.GetEyeAngles(entity), entities.GetEyePosition(entity));

        public Frame WithScaledOffset(float scale)
            => new(_forward, _left, _up, Offset * (1f / scale));

        /// <summary>Applies <paramref name="inner"/> and then this one.</summary>
        public static Frame operator *(Frame outer, Frame inner) => new(
            outer.Rotate(inner._forward),
            outer.Rotate(inner._left),
            outer.Rotate(inner._up),
            outer.Rotate(inner.Offset) + outer.Offset);

        /// <summary>
        /// The frame that undoes this one. A transpose, which is the inverse only
        /// because the basis is orthonormal — and it always is, being built from
        /// an angle. Stock's MatrixInvert assumes the same thing.
        /// </summary>
        public Frame Inverted
        {
            get
            {
                var offset = Unrotate(Offset);

                return new Frame(
                    new Vector(_forward.X, _left.X, _up.X),
                    new Vector(_forward.Y, _left.Y, _up.Y),
                    new Vector(_forward.Z, _left.Z, _up.Z),
                    offset * -1f);
            }
        }

        /// <summary>The pitch, yaw and roll this frame is. Source's MatrixAngles.</summary>
        public Vector Angles
        {
            get
            {
                float flat = MathF.Sqrt(_forward.X * _forward.X + _forward.Y * _forward.Y);
                float pitch = Degrees(MathF.Atan2(-_forward.Z, flat));

                // Straight up or straight down: forward carries no yaw at all, so
                // it has to come from the sideways axis instead, and the roll
                // folds into it.
                if (flat <= 0.001f)
                    return new Vector(pitch, Degrees(MathF.Atan2(-_left.X, _left.Y)), 0f);

                return new Vector(pitch,
                                  Degrees(MathF.Atan2(_forward.Y, _forward.X)),
                                  Degrees(MathF.Atan2(_left.Z, _up.Z)));
            }
        }

        private Vector Rotate(Vector v)
            => _forward * v.X + _left * v.Y + _up * v.Z;

        private Vector Unrotate(Vector v)
            => new(_forward.Dot(v), _left.Dot(v), _up.Dot(v));

        private static float Degrees(float radians) => radians * 180f / MathF.PI;
    }
}

/// <summary>
/// The map keys <see cref="LogicMeasureMovement"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicMeasureMovementVars
{
    [VarName("MeasureTarget")]
    public string MeasureTargetName { get; set; } = string.Empty;

    [VarName("MeasureReference")]
    public string MeasureReferenceName { get; set; } = string.Empty;

    [VarName("target")]
    public string TargetName { get; set; } = string.Empty;

    [VarName("TargetReference")]
    public string TargetReferenceName { get; set; } = string.Empty;

    [VarName("TargetScale")]
    public float Scale { get; set; }

    public int MeasureType { get; set; }
}
