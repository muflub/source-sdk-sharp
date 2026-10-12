using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_particlelight: four keyvalues, no code, and the reason
/// <see cref="EnvSmokeStack"/> could not move on its own.
///
/// <para>
/// Stock's <c>CParticleLight</c> is a <c>CServerOnlyPointEntity</c> with a
/// datadesc, a constructor and nothing else — no Spawn, no think, no input, no
/// output, and nothing networked. It exists to be FOUND: a smokestack walks the
/// entity list in <c>Activate</c>, picks out the lights that name it, and copies
/// their intensity, colour and position into its own send table. After that the
/// light is never consulted again.
/// </para>
///
/// <para>
/// WHICH IS WHY THE PAIR MOVED TOGETHER. That walk is
/// <c>gEntList.FindEntityByClassname</c>, which walks the NATIVE entity list and
/// cannot see the parallel hierarchy. Move this classname alone and every stock
/// smokestack silently loses its lighting — the stack still draws, still
/// networks, and reads as flat grey smoke with no fire under it. Move the stack
/// alone and it can no longer find lights that are still native. Moving both
/// puts the lookup in C#, where a managed stack finds a managed light through
/// <see cref="IEngineEntities.FindByClassname"/>.
/// </para>
///
/// <para>
/// <see cref="ScriptedEntityKind.Logical"/> because it is server-only in stock
/// too. Nothing about a particle light reaches a client except through the
/// smokestack that read it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvParticlelight, ScriptedEntityKind.Logical)]
public sealed class EnvParticleLight : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="EnvParticleLightVars"/>.</summary>
    [ScriptVars] public EnvParticleLightVars Vars = new();

    /// <summary>How bright, in stock's inverse-square units. Read by the smokestack that names it.</summary>
    internal float Intensity => Vars.Intensity;

    /// <summary>The light's colour, 0-255 as the mapper writes it. The stack divides.</summary>
    internal Vector Colour => Vars.Colour;

    /// <summary>The name of the smokestack this light belongs to.</summary>
    internal string ParticleSystemName => Vars.ParticleSystemName;

    /// <summary>
    /// Directional rather than ambient.
    ///
    /// A stack has exactly one slot of each, so this is which of the two a light
    /// fills — and if two lights of the same kind name one stack, the last one
    /// found wins. That is stock's behaviour and it is not a documented rule; it
    /// falls out of the loop overwriting the same struct.
    /// </summary>
    internal bool Directional => Vars.Directional;

    /// <summary>
    /// Where the light is.
    ///
    /// <para>
    /// Republished because <see cref="ScriptedEntity.Origin"/> is protected —
    /// an entity's own place is its own business, and this is the one case where
    /// another entity's business is exactly that. A stack networks the light's
    /// world position so the client can measure each puff's distance from it.
    /// </para>
    /// </summary>
    internal Vector Place => Origin;

    // No OnSpawn. Stock has no Spawn either — the whole class is four keys and a
    // constructor, and every default is on the vars class where a mapper's key
    // overwrites it.
}

/// <summary>
/// The map keys <see cref="EnvParticleLight"/> declares, and the defaults stock
/// puts in its constructor.
/// </summary>
[ScriptVars]
public partial class EnvParticleLightVars
{
    /// <summary>
    /// How bright. Stock's constructor default is 5000, which is large because
    /// the client divides it by the SQUARE of the distance to each particle.
    /// </summary>
    [VarName("Intensity")]
    public float Intensity { get; set; } = 5000f;

    /// <summary>
    /// The light's colour, 0-255. Stock's constructor default is
    /// <c>Init(1, 0, 0)</c> — which in these units is very nearly black with a
    /// trace of red, not the red it looks like. Kept as stock's, because a map
    /// that sets no colour is asking for whatever stock gave it.
    /// </summary>
    [VarName("Color")]
    public Vector Colour { get; set; } = new(1f, 0f, 0f);

    /// <summary>
    /// The targetname of the smokestack this light belongs to. Stock's key is
    /// "PSName", for "particle system name".
    /// </summary>
    [VarName("PSName")]
    public string ParticleSystemName { get; set; } = "";

    /// <summary>Fill the stack's directional slot rather than its ambient one.</summary>
    [VarName("Directional")]
    public bool Directional { get; set; }
}
