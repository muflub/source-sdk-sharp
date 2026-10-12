using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_spritetrail: a sprite that leaves a ribbon behind it.
///
/// <para>
/// A <see cref="SpriteBase"/> and not a separate entity, because stock's
/// <c>CSpriteTrail</c> derives from <c>CSprite</c> and inherits all of its keys,
/// all of its inputs and its whole spawn. What it adds is six numbers about the
/// ribbon and a client half that keeps a point history.
/// </para>
///
/// <para>
/// It was refused with the rest of the family on <c>SpriteTrailCreate</c>, and
/// that reason is now measurably gone: <c>objdump -d server.so</c> finds
/// <b>zero</b> calls to it. The factory is compiled and nothing reaches it —
/// its only callers are <c>env_effectscript</c>, which has been evicted, and
/// <c>npc_grenade_frag</c>, which is under <c>hl2/</c> and is not compiled.
/// </para>
///
/// <para>
/// NOTHING IN THIS TREE PLACES ONE. Zero in <c>dm_lockdown</c>, zero in
/// <c>ss_sandbox</c> before this change. It moved anyway, for the reason
/// <c>env_rockettrail</c> and <c>env_sporetrail</c> did: it is a MAP entity with
/// real keys a mapper can set, so leaving it native would leave one member of a
/// family behind for no reason a mapper could see.
/// </para>
///
/// <para>
/// Unlike <see cref="EnvSprite"/>, the client half keeps a stock object. A
/// sprite is one quad and simulates nothing; a trail is a ring of up to 256
/// points laid down over time, and the only way to keep that is to keep the
/// <c>C_SpriteTrail</c> that owns it. Same shape as
/// <c>ClientSteamJet</c> — see <c>shell_sprite_render.cpp</c>.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSpritetrail, ScriptedEntityKind.Effect)]
public sealed class EnvSpriteTrail : SpriteBase
{
    public EnvSpriteTrail(IEnginePrecache precache, IEngineEntityRender render, IGameClock clock)
        : base(precache, render, clock)
    {
    }

    /// <summary>What a map may set beyond a plain sprite's keys. See <see cref="EnvSpriteTrailVars"/>.</summary>
    [ScriptVars] public EnvSpriteTrailVars TrailVars = new();

    [NetState] public EnvSpriteTrailNetState Trail = new();

    protected override ISpriteNetState State => Trail;

    /// <summary>
    /// Stock's <c>CSpriteTrail::Spawn</c>, which prefers the model key and falls
    /// back to "spritename" — written the other way round here because the fall
    /// back is what every map actually uses. Same set either way.
    /// </summary>
    protected override string MaterialName
        => Vars.Model.Length > 0 ? Vars.Model : TrailVars.SpriteName;

    /// <summary>The six the sprite half knows nothing about.</summary>
    protected override void OnSpriteSpawned()
    {
        Trail.LifeTime = TrailVars.LifeTime;
        Trail.StartWidth = TrailVars.StartWidth;
        Trail.EndWidth = TrailVars.EndWidth;

        // Stock's CONSTRUCTOR values, not its Spawn's, and this is the family's
        // third trap written down again: a managed field starts at zero, so a
        // trail that reported every key correctly would still have no width
        // variance it wanted, no texture resolution and no fade — and, worse,
        // an end width of zero where stock's -1 means "as wide as the start".
        Trail.StartWidthVariance = 0f;
        Trail.TextureRes = 0f;
        Trail.MinFadeLength = 0f;
    }
}

/// <summary>
/// The map keys env_spritetrail adds to <see cref="EnvSpriteVars"/>.
///
/// <para>
/// Stock's <c>CSpriteTrail</c> datadesc declares exactly these five as
/// <c>DEFINE_KEYFIELD</c>. The other four fields on the class are
/// <c>DEFINE_FIELD</c> and no map can reach them.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvSpriteTrailVars
{
    /// <summary>
    /// The material the ribbon is drawn with. Stock's own key, and separate from
    /// "model" because a trail was meant to be given one by code and a material
    /// by the map.
    /// </summary>
    [VarName("spritename")]
    public string SpriteName { get; set; } = "";

    /// <summary>How long a laid-down segment survives.</summary>
    [VarName("lifetime")]
    public float LifeTime { get; set; }

    /// <summary>How wide the ribbon is at the entity.</summary>
    [VarName("startwidth")]
    public float StartWidth { get; set; }

    /// <summary>
    /// How wide at the far end. STOCK'S CONSTRUCTOR DEFAULT IS −1, meaning "the
    /// same as the start", and that is the default here — a keyfield left at
    /// zero would taper every unset trail to a point.
    /// </summary>
    [VarName("endwidth")]
    public float EndWidth { get; set; } = -1.0f;

    /// <summary>
    /// Whether the sprite's own frames advance. Read and unused on this side:
    /// stock's <c>CSpriteTrail</c> keeps the key and its <c>Spawn</c> calls
    /// <c>TurnOn</c> regardless, which is what the sprite half already does.
    /// Claimed rather than dropped so that a mapper setting it is not told they
    /// made a typo.
    /// </summary>
    [VarName("animate")]
    public bool Animate { get; set; }
}
