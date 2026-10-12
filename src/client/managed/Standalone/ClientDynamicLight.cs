using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client halves of <c>light_dynamic</c> and <c>spotlight_end</c>.
///
/// <para>
/// Both do the same thing at bottom — ask the engine's dlight pool for a light
/// and fill it in every frame — so they share the bookkeeping that makes that
/// safe: a registry of live lights, one update pass per frame, and a release
/// when the entity goes away.
/// </para>
///
/// <para>
/// The per-frame pass is driven from the client module's own frame hook rather
/// than from <c>OnDraw</c>, and that is the important decision here. OnDraw
/// only runs for an entity the leaf system considers visible, and a light does
/// not have to be visible to matter: a lamp round a corner still lights the
/// wall you can see. Stock uses a client think for the same reason.
/// </para>
/// </summary>
public sealed class DynamicLights : IDynamicLights
{
    /// <summary>
    /// How long each frame's light is asked for.
    ///
    /// <para>
    /// Longer than a frame on purpose, and stock's own number: at 20fps a
    /// tenth of a second still covers the gap, and a light that expires
    /// between two refreshes flickers. Nothing is lost by overrunning, because
    /// the next refresh moves the same keyed light rather than adding one.
    /// </para>
    /// </summary>
    public const float RefreshInterval = 0.1f;

    /// <summary>how the pool is asked for a slot and told to let one go.</summary>
    private readonly IEngineVisibility _visibility;

    public DynamicLights(IEngineVisibility visibility) => _visibility = visibility;

    private readonly List<IDynamicLightSource> _live = new();

    /// <summary>
    /// THE KEYS THIS SIDE HAS LIT, and the reason this class now owns the two
    /// engine calls instead of each source making them.
    ///
    /// <para>
    /// <c>SourceSharp_DynamicLightRelease</c> (shell_standalone_client.cpp:2865)
    /// asks BOTH pools for the key and sets <c>die = curtime</c> on whatever
    /// comes back — and <c>CL_AllocDlight</c> on a key that is not lit hands
    /// back a FREE SLOT rather than nothing. So a release for a key that was
    /// never allocated is not merely a wasted crossing: it takes a dlight and
    /// an elight out of two small fixed pools for the rest of the frame. The
    /// native comment calls that "cheap, and it cannot leave one lit", which it
    /// is — as long as it happens when a light is actually going out.
    /// </para>
    ///
    /// <para>
    /// It was not. A source whose light is switched off calls
    /// <c>ReleaseLights</c> from <c>UpdateLights</c> EVERY FRAME, so a map with
    /// nine dark <c>spotlight_end</c>s released nine keys that had never been
    /// allocated, sixty times a second: measured at 9.000
    /// <c>DynamicLightRelease</c> crossings per client frame on
    /// <c>dm_lockdown</c>, all nine of them for lights that were already out.
    /// </para>
    ///
    /// <para>
    /// Managed allocated every one of these keys, so managed can say which are
    /// live without asking anyone. The crossing survives — it is how a light
    /// goes out — but it happens at the RATE LIGHTS GO OUT rather than at the
    /// frame rate.
    /// </para>
    ///
    /// <para>
    /// One-shot lights are deliberately NOT in here: a muzzle flash
    /// (<c>ClientAnimationEffects</c>) and a temp-entity light
    /// (<c>ClientTeDynamicLight</c>) are placed with a lifetime and left to
    /// expire, and nothing ever releases them. This ledger is only for the keys
    /// something refreshes and therefore something must eventually put out.
    /// </para>
    /// </summary>
    private readonly HashSet<int> _lit = new();

    /// <inheritdoc/>
    public void Add(IDynamicLightSource source) => _live.Add(source);

    /// <inheritdoc/>
    public void Remove(IDynamicLightSource source)
    {
        if (_live.Remove(source))
            source.ReleaseLights();
    }

    /// <inheritdoc/>
    public void Refresh(in DynamicLight light)
    {
        _lit.Add(light.Key);
        Refreshes++;
        _visibility.DynamicLightUpdate(light);
    }

    /// <inheritdoc/>
    public void Release(int key)
    {
        // The whole of the change. Not "release it again cheaply" — do not
        // cross at all, because this side knows there is nothing to put out.
        if (!_lit.Remove(key))
            return;

        Releases++;
        _visibility.DynamicLightRelease(key);
    }

    /// <inheritdoc/>
    public bool IsLit(int key) => _lit.Contains(key);

    /// <inheritdoc/>
    public long Refreshes { get; private set; }

    /// <inheritdoc/>
    public long Releases { get; private set; }

    /// <summary>How many lights are being kept alive. For tests and debug-api.</summary>
    public int LiveCount => _live.Count;

    /// <inheritdoc/>
    public int LitCount => _lit.Count;

    /// <summary>Every live light, refreshed. Called once a frame by the client module.</summary>
    public void Frame()
    {
        // Backwards, so that a source removing itself mid-pass cannot make the
        // walk skip its neighbour — the mistake StandaloneEntities_RunFrame
        // made natively and which stayed latent until two went at once.
        for (int i = _live.Count - 1; i >= 0; i--)
            _live[i].UpdateLights();
    }

    /// <summary>Forgets everything, without releasing: the level is going away with them.</summary>
    ///
    /// <remarks>
    /// The ledger goes with the list, and for the same reason: the pool itself
    /// is torn down with the level, so a key remembered across one would be a
    /// claim about slots that no longer exist — and the first release after the
    /// next map would cross for nothing.
    /// </remarks>
    public void Reset()
    {
        _live.Clear();
        _lit.Clear();

        // The tallies are per level too, so that a gate measuring "how often did
        // a light actually go out" is not reading a number a previous map left.
        Refreshes = 0;
        Releases = 0;
    }
}

/// <summary>
/// The client half of <c>light_dynamic</c>.
///
/// <para>
/// It asks for one light, or two: a cone gets a second at the point it lands,
/// which is what makes a spotlight look like it is hitting something rather
/// than fading into the dark. The second is keyed with the NEGATIVE of the
/// first, which is the engine's convention for one entity's second light.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.LightDynamic)]
public sealed class ClientDynamicLight : ClientStandaloneEntity, IDynamicLightSource
{
    private readonly IDynamicLights _lights;

    /// <summary>
    /// The registry this light puts itself into, from the container.
    ///
    /// It was a static, and the reason a static was wrong here is particular to
    /// this side: the client module is reloadable, so a static list survives a
    /// reload that replaced everything holding it — leaving the previous load's
    /// lights being refreshed by a Frame nothing calls, against dlight slots the
    /// new load believes are free.
    /// </summary>
    public ClientDynamicLight(IDynamicLights lights, IEngineEntityRender render)
        : base(render)
    {
        _lights = lights;
    }

    public override string ClassName => EntityNames.LightDynamic;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public DynamicLightNetState State = new();

    /// <summary>
    /// The networked flags, which ARE their domain since plan Step 11 -- the
    /// [NetField] is the enum, backed by a byte and four bits wide on the wire.
    /// Kept as a name of its own because "Lighting" is what the tests below are
    /// asking about.
    /// </summary>
    private DynamicLightFlags Lighting => State.Flags;

    /// <summary>
    /// The pool slot, which has to be stable for the light's whole life.
    /// The entity index is the obvious stable number and is what stock uses.
    ///
    /// <para>
    /// READ OFF THE ENTITY, not asked for. It was
    /// <c>Entities.GetIndex(Handle)</c>, evaluated once per frame per light —
    /// a crossing to re-read a number the shell wrote once in its constructor.
    /// See <see cref="ClientStandaloneEntity.Index"/>.
    /// </para>
    /// </summary>
    private int Key => Index;

    public override void OnSpawn() => _lights.Add(this);

    public override void OnRemoved() => _lights.Remove(this);

    public void UpdateLights()
    {
        // A light asked to illuminate neither the world nor models is one that
        // has been switched off, and there is no point spending a pool slot on
        // it. This is also how the server switches it off, so the test is the
        // whole of "is it on".
        // BOTH bits, and one of the few places HasFlag's "all of them" reading
        // is the one wanted: a light that still illuminates one of the two is
        // still on.
        bool off = Lighting.HasFlag(DynamicLightFlags.NoWorldIllumination
                                  | DynamicLightFlags.NoModelIllumination);

        if (off)
        {
            ReleaseLights();
            return;
        }

        var light = new DynamicLight
        {
            Key      = Key,

            // A light that does not touch the world is much cheaper as an
            // elight, which is a separate pool that lights models only. Stock
            // makes the same substitution and for the same reason.
            IsElight = Lighting.HasFlag(DynamicLightFlags.NoWorldIllumination) ? 1 : 0,

            OriginX = State.Origin.X,
            OriginY = State.Origin.Y,
            OriginZ = State.Origin.Z,

            DirectionX = State.Direction.X,
            DirectionY = State.Direction.Y,
            DirectionZ = State.Direction.Z,

            Radius   = State.Radius,

            // The engine's dlight carries three loose ints, so the colour is
            // taken apart again here, at the boundary it leaves C# by -- the
            // same place the flags enum is cast. The alpha is not passed: a
            // dlight has no opacity, and its brightness is the exponent.
            Red      = State.Colour.R,
            Green    = State.Colour.G,
            Blue     = State.Colour.B,
            Exponent = State.Exponent,
            Style    = State.Style,

            // A cone always implies "not the world", whatever the map asked
            // for: the engine's world lighting has no notion of a cone, so a
            // spot that did not say so would light a whole room evenly.
            // The engine's own struct carries an int, so the enum is cast once,
            // here, at the boundary it leaves C# by.
            Flags = (int)(State.OuterAngle > 0f
                ? Lighting | DynamicLightFlags.NoWorldIllumination
                : Lighting),

            InnerAngle = State.InnerAngle,
            OuterAngle = State.OuterAngle,
            LiveFor    = DynamicLights.RefreshInterval,
        };

        _lights.Refresh(light);
    }

    public void ReleaseLights() => _lights.Release(Key);
}

/// <summary>
/// The client half of <c>spotlight_end</c>: the pool of light where a spotlight
/// lands.
///
/// <para>
/// The smallest client entity in the tree, and it exists for the same reason
/// <c>ClientBeam</c> does — a server class with networked state needs a class
/// on this side to host the matching table, or the join stops with
/// "Client missing DT class spotlight_end" and looks like a slow load.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SpotlightEnd)]
public sealed class ClientSpotlightEnd : ClientStandaloneEntity, IDynamicLightSource
{
    private readonly IDynamicLights _lights;

    /// <inheritdoc cref="ClientDynamicLight(IDynamicLights, IEngineEntityRender)"/>
    public ClientSpotlightEnd(IDynamicLights lights, IEngineEntityRender render)
        : base(render)
    {
        _lights = lights;
    }

    public override string ClassName => EntityNames.SpotlightEnd;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public SpotlightEndNetState State = new();

    /// <summary>
    /// Stock's magic number: the light is three times as wide as the sprite it
    /// stands in for. It is the sprite's proportion rather than a rule, which
    /// is why it is here rather than networked.
    /// </summary>
    private const float RadiusPerScale = 3.0f;

    /// <summary>
    /// Stock lifts the light five units off the surface. Without it the light
    /// sits exactly in the floor and lights almost nothing.
    /// </summary>
    private const float FloorOffset = 5.0f;

    /// <inheritdoc cref="ClientDynamicLight.Key"/>
    private int Key => Index;

    public override void OnSpawn() => _lights.Add(this);

    public override void OnRemoved() => _lights.Remove(this);

    public void UpdateLights()
    {
        // Scaled to nothing, or faded to black. Both mean the spotlight has
        // stretched past its useful length, and neither is worth a pool slot.
        if (State.LightScale <= 0f)
        {
            ReleaseLights();
            return;
        }

        // Stock's fade: the three components are scaled by the alpha rather
        // than the engine being told about one, because a dynamic light has no
        // opacity of its own — a dimmer light IS a darker light.
        Color32 tint = State.Tint;

        int r = tint.R * tint.A / 255;
        int g = tint.G * tint.A / 255;
        int b = tint.B * tint.A / 255;

        if (r == 0 && g == 0 && b == 0)
        {
            ReleaseLights();
            return;
        }

        Vector origin = Entities.GetOrigin(Handle);

        _lights.Refresh(new DynamicLight
        {
            Key      = Key,
            OriginX  = origin.X,
            OriginY  = origin.Y,
            OriginZ  = origin.Z + FloorOffset,
            Radius   = State.LightScale * RadiusPerScale,
            Red      = r,
            Green    = g,
            Blue     = b,
            Exponent = 0,
            LiveFor  = DynamicLights.RefreshInterval,
        });
    }

    public void ReleaseLights() => _lights.Release(Key);
}
