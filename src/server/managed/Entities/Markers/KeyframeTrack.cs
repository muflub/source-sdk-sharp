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
/// One position and orientation on a keyframed path, and the name of the key
/// that comes after it.
///
/// Not a path_corner and not a path_track, which are the node types a train or
/// an NPC walks. Those are followed at a speed the FOLLOWER owns, one node at a
/// time; a keyframe owns the speed itself, and so decides how long the leg from
/// here to the next key takes. The only thing that walks these is a
/// <see cref="MoveKeyframed"/>.
///
/// The chain is named rather than pointed at, and there is no second pass to
/// resolve it in. Stock links every node from Activate, after the whole map has
/// spawned, because a key may name one that does not exist yet; managed entities
/// spawn together at the end of the map load in no order worth relying on, so
/// the links are built on demand instead — see <see cref="LinkChain"/>.
/// </summary>
[ScriptedEntity(EntityNames.KeyframeTrack)]
public class KeyframeTrack : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="KeyframeTrackVars"/>.</summary>
    [ScriptVars] public KeyframeTrackVars Vars = new();

    private KeyframeTrack? _next;
    private KeyframeTrack? _previous;
    private bool _linked;

    //-------------------------------------------------------------------------
    // Where the key is, captured at spawn because a mover moves off it and the
    // key has to stay where the mapper drew it.
    //
    // Settable within the assembly so a chain can be built with no map to have
    // drawn one, which is how the tests reach the arithmetic below.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Captured from <c>SpawnOrigin</c> once in <see cref="OnSpawn"/>, which a
    /// hot reload does not run again -- so it is <c>[SaveField]</c>. Uncarried,
    /// the rebuilt key sat at the world origin and every leg length off it was
    /// wrong (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] public Vector KeyOrigin { get; internal set; }

    public Vector KeyAngles { get; internal set; }

    public override void OnSpawn()
    {
        KeyOrigin = SpawnOrigin;
        KeyAngles = SpawnAngles;
    }

    //-------------------------------------------------------------------------
    // Walking the chain.
    //
    // A direction rather than two methods, because that is what the mover
    // carries: it holds 1 or -1 and negates it at the end of the chain, and
    // asking for "the next key in direction d" is then the whole of turning
    // round. Zero answers this key, which is stock's answer and means "not
    // moving".
    //-------------------------------------------------------------------------
    public KeyframeTrack? NextKey(int direction) => direction switch
    {
        1 => _next,
        -1 => _previous,
        _ => this,
    };

    public KeyframeTrack? PrevKey(int direction) => direction switch
    {
        1 => _previous,
        -1 => _next,
        _ => this,
    };

    /// <summary>
    /// Seconds to travel from this key to the next at this key's own speed.
    ///
    /// Distance first, and the orientation only when there is no distance to
    /// divide — a pair of keys in the same place that differ only in facing is
    /// how a mapper writes a turn, and there Vars.MoveSpeed reads as degrees.
    ///
    /// The turn is wrapped into [0,360] before its largest component is taken,
    /// which is stock's arithmetic and worth knowing about rather than
    /// correcting: ten degrees one way measures 10 and the same ten degrees the
    /// other way measures 350, so which way a leg goes round changes how long it
    /// takes. Maps were built against that.
    /// </summary>
    public float TimeToNext
    {
        get
        {
            if (_next is null || Vars.MoveSpeed <= 0f)
                return 0f;

            float distance = (KeyOrigin - _next.KeyOrigin).Length;

            if (distance > 0f)
                return distance / Vars.MoveSpeed;

            var turn = KeyAngles - _next.KeyAngles;

            float largest = MathF.Max(Wrap360(turn.X), MathF.Max(Wrap360(turn.Y), Wrap360(turn.Z)));

            return largest / Vars.MoveSpeed;
        }
    }

    /// <summary>
    /// Joins this key to the next one, both ways.
    ///
    /// The back-link is written here rather than looked up, because a map only
    /// ever names forward: nothing says what comes BEFORE a key, so the only way
    /// to know is to be told by whoever points at you.
    /// </summary>
    internal void LinkTo(KeyframeTrack? next)
    {
        _linked = true;
        _next = next;

        if (next is not null)
            next._previous = this;
    }

    /// <summary>
    /// Resolves the chain forward from this key, once.
    ///
    /// Walked from the head rather than resolved per key at spawn, for the
    /// ordering reason on the class. An already-linked key ends the walk, which
    /// is what makes a circular chain terminate — the walk arrives back at where
    /// it started — and means a second mover sharing the chain costs nothing.
    /// </summary>
    protected void LinkChain()
    {
        for (KeyframeTrack? node = this; node is not null && !node._linked; node = node._next)
            node.LinkTo(node.ResolveNamedNext());
    }

    private KeyframeTrack? ResolveNamedNext()
    {
        // The last key in a chain names nothing, which is how a chain ends and
        // not a mistake. A name that resolves to nothing is one.
        if (Vars.NextKeyName.Length == 0)
            return null;

        var next = FindByName<KeyframeTrack>(Vars.NextKeyName);

        if (next is null)
            Log.Warning($"[SourceSharp] keyframe_track '{Name}': no keyframe named '{Vars.NextKeyName}'\n");

        return next;
    }

    /// <summary>Stock's Fix(): an angle brought into [0,360], where 360 stays 360.</summary>
    private static float Wrap360(float angle)
    {
        while (angle < 0f)
            angle += 360f;

        while (angle > 360f)
            angle -= 360f;

        return angle;
    }
}

/// <summary>
/// The map keys <see cref="KeyframeTrack"/> declares.
/// </summary>
[ScriptVars]
public partial class KeyframeTrackVars
{
    [VarName("NextKey")]
    public string NextKeyName { get; set; } = string.Empty;

    /// <summary>
    /// How fast the leg out of this key runs: units per second when the next key
    /// is somewhere else, degrees per second when it is in the same place and
    /// only the orientation differs.
    /// </summary>
    public float MoveSpeed { get; set; }
}
