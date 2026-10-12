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
/// env_sporeexplosion: a cloud of spores a map switches on and off.
///
/// <para>
/// The last of the six in <c>smoke_trail.cpp</c> and the only one with map
/// inputs of its own, which makes it the only one of them a mapper can actually
/// drive. Its neighbour <see cref="EnvSporeTrail"/> shares five of its
/// networked fields and none of that.
/// </para>
///
/// <para>
/// And the pair is the clearest evidence that <see cref="EnvSporeTrail"/>'s
/// inert emit flag is a stock oversight rather than a decision: this class's
/// constructor sets <c>m_bEmit</c> true and its <c>Spawn</c> then clears it, so
/// an explosion deliberately waits for its Enable. The trail's constructor sets
/// six fields and never mentions the flag at all.
/// </para>
///
/// <para>
/// It needed a client half written before the classname could move, for the
/// reason the whole family turns on — see
/// <see cref="Client.Standalone.ClientSporeExplosion"/> and
/// its sim in SourceSharp.Client.Effects (lane c2-sims).
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSporeexplosion, ScriptedEntityKind.Effect)]
public sealed class EnvSporeExplosion : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="EnvSporeExplosionVars"/>.</summary>
    [ScriptVars] public EnvSporeExplosionVars Vars = new();

    [NetState] public SporeExplosionNetState State = new();

    /// <summary>
    /// Whether a map has switched this one off.
    ///
    /// <para>
    /// Carried because stock carries it — <c>m_bDisabled</c> is a saved field
    /// set by the <c>startdisabled</c> key and by both inputs — and NOT
    /// networked, because stock does not network it either. Nothing in stock
    /// reads it: it decides nothing, it is not on the send table, and the emit
    /// flag beside it is what actually stops the spores. Kept so that a saved
    /// game and a <c>debug-api payload</c> say what stock says.
    /// </para>
    /// </summary>
    internal bool Disabled { get; private set; }

    public override void OnSpawn()
    {
        // Stock's constructor. Only the rate is a key; the other four are
        // whatever SporeExplosion::SporeExplosion set, and a managed field
        // would otherwise start at zero.
        State.SpawnRate        = Vars.SpawnRate;
        State.ParticleLifetime = 1f;
        State.StartSize        = 1f;
        State.EndSize          = 0f;
        State.SpawnRadius      = 16f;

        Disabled = Vars.StartDisabled;

        // The constructor sets this TRUE and Spawn then clears it, so an
        // explosion always waits for its Enable however the map placed it.
        // Written as the one line the two amount to.
        State.Emit = false;

        // Not yet: stock's m_bDontRemove is only raised by an input, so the
        // client is free to collect an explosion no map ever spoke to.
        State.DontRemove = false;
    }

    /// <summary>
    /// Stock's <c>InputEnable</c>. Raises DontRemove as well, which is what
    /// stops the client effect collecting itself between a Disable and the next
    /// Enable.
    /// </summary>
    [ScriptedEvent("Enable")]
    private void OnEnable()
    {
        State.DontRemove = true;
        Disabled = false;
        State.Emit = true;
    }

    /// <summary>
    /// Stock's <c>InputDisable</c>, which also raises DontRemove — an explosion
    /// a map can switch back on must not have been collected in between.
    /// </summary>
    [ScriptedEvent("Disable")]
    private void OnDisable()
    {
        State.DontRemove = true;
        Disabled = true;
        State.Emit = false;
    }
}

/// <summary>
/// The map keys <see cref="EnvSporeExplosion"/> declares.
///
/// <para>
/// Two, which is two more than four of its five neighbours in
/// <c>smoke_trail.cpp</c> have.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvSporeExplosionVars
{
    /// <summary>
    /// Spores per second, and the one networked value of the five that the
    /// client obeys. Stock's constructor default is 100.
    /// </summary>
    [VarName("spawnrate")]
    public float SpawnRate { get; set; } = 100f;

    /// <summary>
    /// Stock's "startdisabled". It reaches a saved field and nothing reads it —
    /// see <see cref="EnvSporeExplosion.Disabled"/> — because an explosion
    /// starts not emitting either way.
    /// </summary>
    [VarName("startdisabled")]
    public bool StartDisabled { get; set; }
}
