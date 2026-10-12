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
/// move_rope and keyframe_rope: a hanging cable, one span per link.
///
/// <para>
/// A stock rope is a chain — move_rope at the head, keyframe_rope links after
/// it, each naming the next in NextKey — and every link whose NextKey resolves
/// is one drawn span from its own origin to the next link's. A link without a
/// NextKey (the tail of a chain, or a stray) draws nothing, exactly as stock's
/// does. dm_lockdown ships one move_rope and eight keyframe_ropes; three spans
/// resolve.
/// </para>
///
/// <para>
/// The server half is nothing but the map's facts: resolve the next link once,
/// hand over where it is and how the cable should hang. Everything that MOVES
/// is on the client — the catenary, the wind, the drawing — inside the stock
/// <c>C_RopeKeyframe</c> that <c>shell_rope_render.cpp</c> builds client-side
/// and keeps fed, the same arrangement as the steam jet and the smoke stack.
/// See <see cref="Client.Standalone.ClientRope"/>.
/// </para>
///
/// <para>
/// Deliberately not carried over, all recorded in docs/divergences.md: moving
/// keyframes (MoveSpeed/PositionInterpolator drive a rope whose ENDS travel —
/// nothing in either shipped map moves one), Breakable/Barbed/Collide, the
/// SetScrollSpeed/SetForce/Break inputs, and parentname on a link (stock lets
/// a rope ride a prop; the one dm_lockdown case is a plug wire whose parent
/// never moves).
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.MoveRope, ScriptedEntityKind.Effect)]
[ScriptedEntity(EntityNames.KeyframeRope, ScriptedEntityKind.Effect)]
public sealed class RopeKeyframe : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="RopeVars"/>.</summary>
    [ScriptVars] public RopeVars Vars = new();

    // One table per classname, because a table is keyed by the name the
    // entity is spawned under. The FIELDS are declared once — IRopeNetState —
    // so the two cannot drift.
    [NetState] public MoveRopeNetState Move = new();
    [NetState] public KeyframeRopeNetState Keyframe = new();

    /// <summary>Whichever of the two this instance actually is.</summary>
    internal IRopeNetState State => ClassName == EntityNames.MoveRope
        ? Move
        : (IRopeNetState)Keyframe;

    public override void OnSpawn()
    {
        // The next link may spawn after this one — a rope chain is written in
        // map order, not dependency order — so the lookup waits one think, by
        // which time every map entity exists. Stock waits the same way: its
        // endpoints resolve in Activate, after the whole map has spawned.
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        var state = State;

        state.HasRope = false;

        if (Vars.NextKey.Length > 0)
        {
            var next = Entities.FindByName(Vars.NextKey);

            // Compared against None rather than asked IsValid: a find that
            // misses answers None, and IsValid is a liveness re-check through
            // a slot — the wrong question for a handle we were just handed.
            if (next != EntityHandle.None)
            {
                state.EndPos  = Entities.GetOrigin(next);
                state.HasRope = true;
            }
            else if (++_resolveAttempts < ResolveAttemptLimit)
            {
                // The next link may not exist YET rather than not exist:
                // dm_lockdown's plug_wire_03 arrives from a template well
                // after the map's own spawn pass, and the first live run of
                // this port warned about it and drew two spans of a
                // three-span chain. Keep asking for a few seconds before
                // deciding the name is dead.
                SetNextThink(ResolveRetryDelay);
                return;
            }
            else
            {
                Log.Warning($"[SourceSharp] {ClassName} '{Name}': NextKey " +
                            $"'{Vars.NextKey}' names nothing; no span drawn\n");
            }
        }

        state.Slack        = Vars.Slack;
        state.Width        = Vars.Width;
        state.TextureScale = Vars.TextureScale;
        state.Subdiv       = Math.Clamp(Vars.Subdiv, 1, 8);
        state.ScrollSpeed  = Vars.ScrollSpeed;
        state.Segments     = SegmentsForType(Vars.Type);
        state.Material     = StripVmt(Vars.RopeMaterial);

        // One resolution is the whole job: nothing in this port moves a rope's
        // endpoints after the map has spawned.
        StopThinking();
    }

    /// <summary>How many times a missing NextKey is re-asked before it is a warning.</summary>
    internal const int ResolveAttemptLimit = 20;

    /// <summary>Seconds between those attempts. Twenty at half a second is ten seconds of patience.</summary>
    internal const float ResolveRetryDelay = 0.5f;

    private int _resolveAttempts;

    /// <summary>
    /// Stock's Type-to-segments derivation, from CRopeKeyframe::KeyValue: a
    /// full rope simulates 10 nodes, semi-rigid 4, rigid 2 — and an entity
    /// whose mapper never wrote a Type key keeps the constructor's 5, which is
    /// why the default here is a sentinel rather than zero.
    /// </summary>
    internal static int SegmentsForType(int type) => type switch
    {
        0 => 10,   // ROPE_MAX_SEGMENTS
        1 => 4,    // ROPE_TYPE1_NUMSEGMENTS
        2 => 2,    // ROPE_TYPE2_NUMSEGMENTS
        _ => 5,    // the constructor's default, kept when no Type key was given
    };

    /// <summary>
    /// The material, in the one shape the client's material system accepts.
    /// Stock appends .vmt server-side (the model precache table wants it) and
    /// strips it again client-side; this port never has a model index in
    /// between, so the name travels bare.
    /// </summary>
    internal static string StripVmt(string material) =>
        material.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)
            ? material[..^4]
            : material;
}

/// <summary>The map keys <see cref="RopeKeyframe"/> declares.</summary>
[ScriptVars]
public partial class RopeVars
{
    /// <summary>The next link in the chain. Empty on a chain's tail.</summary>
    public string NextKey { get; set; } = "";

    /// <summary>Extra length beyond the endpoint distance: the sag, in units.</summary>
    public float Slack { get; set; }

    /// <summary>How wide the cable draws. Stock's constructor default is 2.</summary>
    public float Width { get; set; } = 2f;

    /// <summary>Texture repeats, pixels per inch. Stock's constructor default is 4.</summary>
    public float TextureScale { get; set; } = 4f;

    /// <summary>Smoothing subdivisions between simulated nodes. Stock's constructor default is 2.</summary>
    public int Subdiv { get; set; } = 2;

    /// <summary>
    /// 0 full simulation, 1 semi-rigid, 2 rigid. −1 is "no key given", which
    /// stock expresses by never entering the KeyValue branch — see
    /// <see cref="RopeKeyframe.SegmentsForType"/>.
    /// </summary>
    public int Type { get; set; } = -1;

    /// <summary>The cable's material. Stock's default rope is cable/cable.vmt.</summary>
    public string RopeMaterial { get; set; } = "cable/cable.vmt";

    /// <summary>How fast the texture crawls along the span.</summary>
    public float ScrollSpeed { get; set; }
}
