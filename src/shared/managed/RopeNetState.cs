using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What one rope span tells the client: where its far end is, how much it
/// sags, how wide it draws, and what it is made of.
///
/// <para>
/// A stock rope is a chain of entities — move_rope at the head, keyframe_rope
/// links after it — and every entity whose NextKey resolves is one drawn span
/// from its own origin to the next link's. The origin end travels as the
/// entity's networked origin, outside this blob; the FAR end is here, because
/// the next link is a server-side fact the client cannot look up (a managed
/// entity's targetname never reaches the client).
/// </para>
///
/// <para>
/// Declared once and used by both classnames through
/// <see cref="MoveRopeNetState"/> and <see cref="KeyframeRopeNetState"/> —
/// the same shape as <see cref="ISteamJetNetState"/>, and for the same
/// reason: a table is keyed by the classname the entity spawned under, and
/// stock registers one class under two names.
/// </para>
/// </summary>
public interface IRopeNetState
{
    /// <summary>
    /// This link's NextKey resolved and there is a span to draw. A terminal
    /// link — the last keyframe_rope of a chain, or a keyframe with no
    /// NextKey at all — networks false and draws nothing, which is stock's
    /// behaviour for the same entity.
    /// </summary>
    bool HasRope { get; set; }

    /// <summary>Where the span ends: the next link's origin, resolved server-side.</summary>
    Vector EndPos { get; set; }

    /// <summary>Extra length beyond the endpoint distance. The sag, in units. The mapper's Slack key.</summary>
    float Slack { get; set; }

    /// <summary>How wide the span draws, in units.</summary>
    float Width { get; set; }

    /// <summary>Texture repeats: pixels per inch. Stock defaults to 4.</summary>
    float TextureScale { get; set; }

    /// <summary>
    /// How many simulated nodes the span gets. Stock derives it from the Type
    /// key — 10 for a full rope, 4 semi-rigid, 2 rigid, 5 when no Type key was
    /// given (the constructor's default) — and the derivation lives on the
    /// server half so both classnames share it.
    /// </summary>
    int Segments { get; set; }

    /// <summary>Smoothing subdivisions between nodes. The mapper's Subdiv key.</summary>
    int Subdiv { get; set; }

    /// <summary>How fast the texture crawls along the span. The mapper's ScrollSpeed key.</summary>
    float ScrollSpeed { get; set; }

    /// <summary>
    /// The rope material, WITHOUT the .vmt extension — the material system
    /// refuses the suffixed form, so the server half strips it at ingest and
    /// the fact travels in the one shape the consumer can use.
    /// </summary>
    string Material { get; set; }
}

/// <summary>move_rope's table. See <see cref="IRopeNetState"/> for what the fields mean.</summary>
[StandaloneNetState(EntityNames.MoveRope)]
public partial class MoveRopeNetState : IRopeNetState
{
    [NetField(Bits = 1)] public partial bool HasRope { get; set; }
    public partial Vector EndPos { get; set; }
    public partial float Slack { get; set; }
    public partial float Width { get; set; }
    public partial float TextureScale { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 4)] public partial int Segments { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 4)] public partial int Subdiv { get; set; }
    public partial float ScrollSpeed { get; set; }
    [NetString(64)] public partial string Material { get; set; }
}

/// <summary>keyframe_rope's table. See <see cref="IRopeNetState"/> for what the fields mean.</summary>
[StandaloneNetState(EntityNames.KeyframeRope)]
public partial class KeyframeRopeNetState : IRopeNetState
{
    [NetField(Bits = 1)] public partial bool HasRope { get; set; }
    public partial Vector EndPos { get; set; }
    public partial float Slack { get; set; }
    public partial float Width { get; set; }
    public partial float TextureScale { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 4)] public partial int Segments { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 4)] public partial int Subdiv { get; set; }
    public partial float ScrollSpeed { get; set; }
    [NetString(64)] public partial string Material { get; set; }
}
