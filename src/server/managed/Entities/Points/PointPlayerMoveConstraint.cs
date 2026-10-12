//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Holds players near a point by making it harder and harder to walk away,
/// rather than by putting a wall in front of them.
///
/// <para>
/// The slowing itself is not here and cannot be: it happens inside movement, per
/// command, on both the server and the client — <c>ComputeConstraintSpeedFactor</c>
/// reads a centre, a radius, a width and a factor off the move data every tick.
/// Those four numbers live on the PLAYER and are networked, which is what makes
/// the client's prediction agree with the server about how slow the player is.
/// So the division is the natural one: the per-command arithmetic stays where it
/// already runs, and the DECISIONS — who is inside, when they got out, what
/// happens then — are all here.
/// </para>
///
/// <para>
/// Stock hands the player a pointer to the constraint entity as well, and the
/// engine re-reads the centre off that entity every tick. This does not, because
/// <see cref="IEnginePlayerMovement.SetMovementConstraint"/> deliberately passes NULL: a
/// point_playermoveconstraint never moves, so re-reading its origin every tick
/// answers a question that cannot change, and the networked
/// <c>m_vecConstraintCenter</c> the engine falls back to is the same value. The
/// consequence is a saving rather than a divergence — with no entity pointer to
/// dereference, the client has no reason to know this entity exists, so stock's
/// FL_EDICT_ALWAYS transmit is not needed and no ABI call was invented for it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointPlayermoveconstraint)]
public sealed class PointPlayerMoveConstraint : ScriptedEntity
{
    public PointPlayerMoveConstraint(IEnginePlayerMovement playerMovement)
    {
        PlayerMovement = playerMovement;
    }

    /// <summary>how a player moves, and what may stop them.</summary>
    private readonly IEnginePlayerMovement PlayerMovement;
    /// <summary>What a map may set on this entity. See <see cref="PointPlayerMoveConstraintVars"/>.</summary>
    [ScriptVars] public PointPlayerMoveConstraintVars Vars = new();

    /// <summary>
    /// Fired once per player who walked out, as they are let go.
    ///
    /// The entity does not stop them leaving — the constraint only makes leaving
    /// slow — so this is the map's chance to react to somebody having managed it
    /// anyway. Per player and not once for the group: two players leaving are two
    /// separate escapes.
    /// </summary>
    public readonly Output OnConstraintBroken = new();

    /// <summary>
    /// Who this entity is currently holding.
    ///
    /// Kept here rather than asked of the engine each think, because "constrained
    /// by ME" is not a question the player can answer: the player carries four
    /// numbers and no idea who set them. Two constraints overlapping means the
    /// second one wins on the player and both still believe they hold him, which
    /// is stock's behaviour too.
    /// </summary>
    private readonly List<EntityHandle> _constrained = new();

    private const float ThinkInterval = 0.1f;

    /// <summary>
    /// Grabs every player already inside the radius.
    ///
    /// Players who wander in afterwards are NOT picked up — stock takes the
    /// snapshot at TurnOn and never looks again — so a map that wants a late
    /// arrival held has to fire this a second time. Kept, because the entity is
    /// used to pin players to a scripted moment that has already started, and a
    /// think that kept grabbing would hold somebody who arrived after it ended.
    /// </summary>
    [ScriptedEvent("TurnOn")]
    private void OnTurnOn(InputData input)
    {
        Vector centre = Origin;

        foreach (var player in Entities.FindAllByClassname("player"))
        {
            if (Outside(centre, Vars.Radius, Entities.GetOrigin(player)))
                continue;

            // Already ours: re-applying would be harmless, but the list would then
            // hold him twice and the first break would fire the output twice.
            if (_constrained.Contains(player))
                continue;

            _constrained.Add(player);
            PlayerMovement.SetMovementConstraint(player, centre, Vars.Radius,
                                               Vars.ConstraintWidth, Vars.SpeedFactor);
        }

        // Nothing to watch if nobody was in range, and stock is careful about this
        // too: an entity switched on in an empty room should cost nothing until it
        // is switched on again with somebody in it.
        if (_constrained.Count > 0)
            SetNextThink(ThinkInterval);
    }

    /// <summary>Lets everybody go, without firing the broken output — nobody escaped.</summary>
    [ScriptedEvent("TurnOff")]
    private void OnTurnOff(InputData input)
    {
        foreach (var player in _constrained)
        {
            if (Entities.IsValid(player))
                PlayerMovement.ClearMovementConstraint(player);
        }

        _constrained.Clear();
        StopThinking();
    }

    /// <summary>
    /// Whether somebody at <paramref name="at"/> has got out.
    ///
    /// STRICTLY outside, which is stock's comparison and is the reason this is
    /// one function rather than two: the same boundary decides who gets grabbed
    /// at TurnOn and who gets released later, so a player standing exactly on
    /// the radius must be held by both readings or the constraint would grab
    /// them and let them go on the same tick, firing the broken output forever.
    ///
    /// Static and taking its inputs, so the boundary can be checked without an
    /// engine — the same reason PointHurt.Settle is.
    /// </summary>
    internal static bool Outside(Vector centre, float radius, Vector at)
        => (at - centre).LengthSquared > radius * radius;

    public override void OnThink()
    {
        Vector centre = Origin;

        // Backwards, because the list is written to while it is walked.
        for (int i = _constrained.Count - 1; i >= 0; i--)
        {
            var player = _constrained[i];

            // A player who disconnected or was removed is dropped silently. Stock
            // leaves the dead handle in its vector forever and skips it every
            // think; that is a leak rather than a behaviour, so it is not kept.
            if (!Entities.IsValid(player))
            {
                _constrained.RemoveAt(i);
                continue;
            }

            if (!Outside(centre, Vars.Radius, Entities.GetOrigin(player)))
                continue;

            PlayerMovement.ClearMovementConstraint(player);
            _constrained.RemoveAt(i);

            OnConstraintBroken.Fire();
        }

        if (_constrained.Count > 0)
            SetNextThink(ThinkInterval);
        else
            StopThinking();
    }

    /// <summary>
    /// Releases anybody still held when this entity goes away.
    ///
    /// Stock does not, and the result there is a player left permanently slow
    /// around a point that no longer exists — survivable only because the engine
    /// re-reads the centre off the entity handle, which has gone NULL, so the
    /// constraint quietly centres on the world origin instead. Neither behaviour
    /// is one a map asked for, and since this passes the centre by value there is
    /// no NULL handle to save us, so the constraint is lifted properly.
    /// See docs/divergences.md.
    /// </summary>
    public override void OnRemoved()
    {
        foreach (var player in _constrained)
        {
            if (Entities.IsValid(player))
                PlayerMovement.ClearMovementConstraint(player);
        }

        _constrained.Clear();
    }
}

/// <summary>
/// The map keys <see cref="PointPlayerMoveConstraint"/> declares.
/// </summary>
[ScriptVars]
public partial class PointPlayerMoveConstraintVars
{
    /// <summary>How far from here a player may get before the constraint breaks.</summary>
    public float Radius { get; set; }

    /// <summary>
    /// The band, measured inwards from the radius, over which the slowing comes on.
    ///
    /// Inside <c>radius - width</c> the player moves normally; across the band the
    /// factor lerps to <see cref="PointPlayerMoveConstraintVars.SpeedFactor"/> at the edge. A width of zero is
    /// therefore not "no slowing" but a divide by zero in the engine's lerp, which
    /// is stock's behaviour and a map that wrote one has always had it.
    /// </summary>
    [VarName("width")]
    public float ConstraintWidth { get; set; }

    /// <summary>What the player's speed is multiplied by at the very edge.</summary>
    public float SpeedFactor { get; set; }
}
