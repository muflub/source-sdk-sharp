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
/// env_explosion: a bang that hurts, marks the wall and is seen.
///
/// <para>
/// This is explode.cpp's CEnvExplosion. It was the LAST thing standing between
/// the managed game and its explosions: five managed entities — Rocket,
/// EnergyBall, Grenade, PropPhysics and PhysicsCannister — all reached it, and
/// every one of them did so by asking native to build a C++ entity by classname
/// and fire an input at it. Nothing in C++ called it any more; by the time it
/// moved, <c>ExplosionCreate</c> had exactly ONE caller left in the whole
/// server and that caller was our own shim.
/// </para>
///
/// <para>
/// Three engine asks survive the move, and only three: the temp entity (a PVS
/// broadcast), the radius damage (a sphere query with a trace per victim), and
/// "am I in water". Everything else here — which sprite, how big, how far, and
/// which of fifteen spawnflags suppresses which part of the presentation — is
/// policy, and policy is what C# is for.
/// </para>
///
/// <para>
/// Not to be confused with <see cref="PhysExplosion"/>, which is env_physexplosion
/// and pushes furniture without a fireball. This one is the visible half.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvExplosion)]
public sealed class EnvExplosion : ScriptedEntity
{
    public EnvExplosion(IEngineWorldQuery world, IEngineEffects effects,
                        IEngineDamage damage, IEnginePrecache precache, IEngineHost host, IBlastSweep blast,
                        IServerDecals decals)
    {
        World = world;
        Effects = effects;
        Damage = damage;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
    }

    /// <summary>The decal system, whose "Scorch" group the blast paints from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>
    /// The managed blast traversal — the falloff, the line of sight and the
    /// blocking rule that used to run inside <c>CGameRules::RadiusDamage</c>.
    /// See <see cref="BlastSweep"/>.
    /// </summary>
    private readonly IBlastSweep Blast;

    /// <summary>tracing, and what is at a point.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>getting the sprite and the decal onto the client before they are needed.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>the engine's random stream.</summary>
    private readonly IEngineHost Host;

    /// <summary>What a map may set on this entity. See <see cref="EnvExplosionVars"/>.</summary>
    [ScriptVars] public EnvExplosionVars Vars = new();

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>
    /// env_explosion's own spawnflags, from explode.h's SF_ENVEXPLOSION_*.
    ///
    /// <para>
    /// These are NOT the TE_EXPLFLAG_* bits the temp entity takes, and stock
    /// says so out loud: "It's stupid that this entity's spawnflags and the
    /// flags for the explosion temp ent don't match up." They number
    /// differently and there are more of these than there are of those, so the
    /// translation is a real function and is <see cref="EffectFlags"/>.
    /// </para>
    /// </summary>
    [Flags]
    public enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0,

        /// <summary>Goes off, is seen, and hurts nobody.</summary>
        NoDamage = 0x00000001,

        /// <summary>
        /// Survives its own explosion. Without this the entity removes itself
        /// three tenths of a second after going off, so a map that fires the
        /// same env_explosion twice gets one bang — and the sandbox's effects
        /// station learned that the hard way.
        /// </summary>
        Repeatable = 0x00000002,

        NoFireball = 0x00000004,
        NoSmoke = 0x00000008,
        NoDecal = 0x00000010,
        NoSparks = 0x00000020,
        NoSound = 0x00000040,

        /// <summary>Sprites face a random way rather than the camera.</summary>
        RandomOrientation = 0x00000080,

        NoFireballSmoke = 0x00000100,
        NoParticles = 0x00000200,
        NoDynamicLights = 0x00000400,

        /// <summary>Relaxes the fireball's minimum size. See <see cref="SpriteScale"/>.</summary>
        NoClampMin = 0x00000800,

        /// <summary>Relaxes the fireball's maximum size. See <see cref="SpriteScale"/>.</summary>
        NoClampMax = 0x00001000,

        /// <summary>Spares anyone underwater.</summary>
        SurfaceOnly = 0x00002000,

        /// <summary>Deals DMG_GENERIC rather than DMG_BLAST.</summary>
        GenericDamage = 0x00004000,
    }

    /// <summary>
    /// The TE_EXPLFLAG_* bits, from tempentity.h. What actually goes on the
    /// wire, and deliberately a different type from <see cref="SpawnFlag"/>
    /// because the two are different vocabularies that both live in an int.
    /// </summary>
    [Flags]
    public enum EffectFlag
    {
        None = 0x0,
        NoAdditive = 0x1,
        NoDynamicLights = 0x2,
        NoSound = 0x4,
        NoParticles = 0x8,
        DrawAlpha = 0x10,
        Rotate = 0x20,
        NoFireball = 0x40,
        NoFireballSmoke = 0x80,
    }

    /// <summary>Stock's default fireball. Precached by this entity rather than by a weapon.</summary>
    internal const string DefaultFireballSprite = "sprites/zerogxplode.vmt";

    /// <summary>The mark a blast leaves on the wall.</summary>
    internal const string ScorchDecal = "Scorch";

    /// <summary>Stock's frame rate for the fireball sprite.</summary>
    internal const int FireballFrameRate = 15;

    /// <summary>How long after the bang the entity tidies itself away.</summary>
    internal const float SmokeDelay = 0.3f;

    /// <summary>
    /// The fireball sprite and how big the wire is told to draw it. Resolved
    /// ONCE in <see cref="OnSpawn"/>, which a hot reload does not run again --
    /// so they are <c>[SaveField]</c>. Uncarried, a blast after a
    /// <c>cs_sv_reload</c> would send no fireball at all
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _fireballSprite = -1;

    /// <inheritdoc cref="_fireballSprite"/>
    [SaveField] private int _spriteScale;

    /// <summary>
    /// The render mode, a presentation decision that reaches the wire through
    /// two of the temp entity's flags. The map's key, read in
    /// <see cref="OnSpawn"/>; a code-made explosion is TransAdd instead, which
    /// <see cref="Explosions.Fire"/> passes for itself.
    /// </summary>
    private RenderMode _renderMode = RenderMode.Normal;

    //-------------------------------------------------------------------------
    // The pure decisions. Every one of these is a function of the mapper's
    // numbers and nothing else, which is why they are static and why the tests
    // can reach them without a world.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How big the fireball sprite is drawn, from the magnitude.
    ///
    /// <para>
    /// The curve is Valve's and is odd on purpose: the scale is
    /// <c>(magnitude - 50) * 0.6</c>, so a magnitude of 50 gives ZERO and
    /// anything below it gives a negative — which is why the clamps are not
    /// decoration. A default explosion clamps to between 10 and 50; the two
    /// NOCLAMP flags do not remove the clamp but WIDEN it, to 1 and 200
    /// respectively, and stock's own comment says "don't inhibit clamping
    /// altogether, just relax it a bit".
    /// </para>
    ///
    /// <para>
    /// Note the two flags are independent: NOCLAMPMIN relaxes only the floor
    /// and NOCLAMPMAX only the ceiling, so a mapper who sets one still gets the
    /// other's default.
    /// </para>
    /// </summary>
    internal static int SpriteScale(int magnitude, SpawnFlag flags)
    {
        float scale = (magnitude - 50) * 0.6f;

        float floor = flags.HasFlag(SpawnFlag.NoClampMin) ? 1f : 10f;
        if (scale < floor)
            scale = floor;

        float ceiling = flags.HasFlag(SpawnFlag.NoClampMax) ? 200f : 50f;
        if (scale > ceiling)
            scale = ceiling;

        return (int)scale;
    }

    /// <summary>
    /// How far the blast reaches.
    ///
    /// <para>
    /// A mapper who set iRadiusOverride gets exactly that. Everyone else gets
    /// two and a half units per point of magnitude, which is the same
    /// relationship combat damage uses and the same one
    /// <see cref="PhysExplosion"/> derives its radius by.
    /// </para>
    /// </summary>
    internal static int EffectiveRadius(int magnitude, int radiusOverride)
        => radiusOverride > 0 ? radiusOverride : (int)(magnitude * 2.5f);

    /// <summary>
    /// The entity's spawnflags translated into the temp entity's flags.
    ///
    /// <para>
    /// Seven of the bits are a straight rename. The eighth is not a spawnflag at
    /// all: the render mode decides between DRAWALPHA and NOADDITIVE, and the
    /// rule is that <see cref="RenderMode.TransAlpha"/> draws alpha,
    /// <see cref="RenderMode.TransAdd"/> draws additive, and EVERYTHING ELSE —
    /// including a mapper who set nothing, whose entity is
    /// <see cref="RenderMode.Normal"/> — is explicitly non-additive.
    /// </para>
    ///
    /// <para>
    /// That last clause is the one worth reading twice, because it means a
    /// bare map-placed env_explosion sends NOADDITIVE while every explosion
    /// created in code sends neither flag. Stock has it exactly this way.
    /// </para>
    /// </summary>
    internal static EffectFlag EffectFlags(SpawnFlag flags, RenderMode renderMode)
    {
        EffectFlag result = EffectFlag.None;

        if (flags.HasFlag(SpawnFlag.NoFireball)) result |= EffectFlag.NoFireball;
        if (flags.HasFlag(SpawnFlag.NoSound)) result |= EffectFlag.NoSound;
        if (flags.HasFlag(SpawnFlag.RandomOrientation)) result |= EffectFlag.Rotate;

        if (renderMode == RenderMode.TransAlpha)
            result |= EffectFlag.DrawAlpha;
        else if (renderMode != RenderMode.TransAdd)
            result |= EffectFlag.NoAdditive;

        if (flags.HasFlag(SpawnFlag.NoParticles)) result |= EffectFlag.NoParticles;
        if (flags.HasFlag(SpawnFlag.NoDynamicLights)) result |= EffectFlag.NoDynamicLights;
        if (flags.HasFlag(SpawnFlag.NoFireballSmoke)) result |= EffectFlag.NoFireballSmoke;

        return result;
    }

    /// <summary>
    /// What kind of damage this deals.
    ///
    /// <para>
    /// A caller that named one wins. Otherwise the GENERIC_DAMAGE spawnflag
    /// picks between DMG_GENERIC and DMG_BLAST, and SURFACEONLY adds
    /// DMG_BLAST_SURFACE on top of whichever it was — that one is additive
    /// rather than exclusive, which is why it is applied after and not as part
    /// of the choice.
    /// </para>
    /// </summary>
    internal static DamageType DamageTypeFor(SpawnFlag flags, DamageType? custom)
    {
        DamageType type = custom
            ?? (flags.HasFlag(SpawnFlag.GenericDamage) ? DamageType.Generic : DamageType.Blast);

        if (flags.HasFlag(SpawnFlag.SurfaceOnly))
            type |= DamageType.BlastSurface;

        return type;
    }

    /// <summary>
    /// How big the sprite is told to be on the wire.
    ///
    /// A suppressed fireball is sent as scale ZERO rather than not sent at all,
    /// because the temp entity still carries the sound, the particles and the
    /// dynamic lights that the same message controls.
    /// </summary>
    internal static float WireScale(int spriteScale, SpawnFlag flags)
        => flags.HasFlag(SpawnFlag.NoFireball) ? 0f : spriteScale / 10f;

    //-------------------------------------------------------------------------
    // The entity.
    //-------------------------------------------------------------------------

    public override void OnSpawn()
    {
        // Stock precaches the CUSTOM sprite only, and leans on a global that a
        // weapon file happened to fill in for the default. That file is not in
        // this build at all -- world_adapter.cpp fills the global instead -- so
        // this entity precaches its own default and stops depending on who else
        // happened to run first.
        _fireballSprite = Vars.FireballSprite.Length > 0
            ? Precache.PrecacheModel(Vars.FireballSprite)
            : Precache.PrecacheModel(DefaultFireballSprite);

        _spriteScale = SpriteScale(Vars.Magnitude, Flags);
        _renderMode = (RenderMode)Vars.RenderMode;
    }

    /// <summary>
    /// The map's door. A mapper fires Explode and gets whatever the entity's
    /// own keys and flags say.
    /// </summary>
    [ScriptedEvent("Explode")]
    private void OnExplode() => Explode();

    /// <summary>
    /// Exposed for tests, which have no map to fire an input from.
    ///
    /// <para>
    /// The body lives in <see cref="Explosions.Detonate"/> because code makes
    /// explosions too, and stock's way of sharing it — build one of these
    /// entities and fire an input at it — is a C++ workaround this port does
    /// not need. What stays HERE is what only an entity has: its keys, its
    /// precached indices, and the think that decides whether it survives.
    /// </para>
    /// </summary>
    internal void Explode()
    {
        Explosions.Detonate(World, Effects, Damage, Host, Blast, Decals, new Explosions.Setup(
            Origin: Origin,
            Magnitude: Vars.Magnitude,
            RadiusOverride: Vars.RadiusOverride,
            Flags: Flags,
            Render: _renderMode,
            FireballSprite: _fireballSprite,
            SpriteScale: _spriteScale,
            DamageForce: Vars.DamageForce,
            // NOBODY, and spelled EntityHandle.None rather than default —
            // default is ZERO and zero is a real entity index, the world.
            //
            // Stock's map-placed env_explosion is no better off: its attacker
            // is `GetOwnerEntity() ? GetOwnerEntity() : this`, and nothing sets
            // an owner on an entity the map placed, so stock credits the
            // explosion to ITSELF. "Nobody" and "the env_explosion" score the
            // same.
            //
            // TWO THINGS STOCK CAN SAY HERE AND THIS CANNOT, both only
            // reachable from a map and neither wired: the `ignoredEntity`
            // keyvalue (an EHANDLE field, resolved by name at activate time)
            // and a custom damage type (only ever set from code). Recorded
            // rather than half-built.
            Owner: EntityHandle.None,
            // The explosion itself, as stock's CTakeDamageInfo( this, ... ):
            // the hit is felt FROM here (lane player-msgs).
            Inflictor: Handle,
            Ignored: EntityHandle.None,
            CustomDamageType: null));

        // Tidy up -- unless the mapper said this one goes off more than once.
        SetNextThink(SmokeDelay);
    }

    /// <summary>
    /// Stock calls this Smoke and it makes none — it is the removal timer, and
    /// the only thing it decides is whether this entity survives having gone
    /// off.
    /// </summary>
    public override void OnThink()
    {
        StopThinking();

        if (!Flags.HasFlag(SpawnFlag.Repeatable))
            Remove();
    }
}

/// <summary>
/// The map keys <see cref="EnvExplosion"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvExplosionVars
{
    /// <summary>
    /// How much damage, and — through <see cref="EnvExplosion.SpriteScale"/> —
    /// how big. One number doing two jobs is stock's design and is why a mapper
    /// who wants a big harmless bang needs the NODAMAGE flag rather than a
    /// smaller magnitude.
    /// </summary>
    [VarName("iMagnitude")]
    public int Magnitude { get; set; }

    /// <summary>
    /// How far it reaches, when the designer did not want two and a half units
    /// per point. Zero means "derive it".
    /// </summary>
    [VarName("iRadiusOverride")]
    public int RadiusOverride { get; set; }

    /// <summary>How hard it throws things. Zero lets the engine derive it from the damage.</summary>
    public float DamageForce { get; set; }

    /// <summary>A fireball sprite other than the default. Empty means the default.</summary>
    public string FireballSprite { get; set; } = "";

    /// <summary>
    /// The render mode, as its raw map value. Reaches the wire as two of the
    /// temp entity's flags; see <see cref="EnvExplosion.EffectFlags"/>.
    /// </summary>
    public int RenderMode { get; set; }
}
