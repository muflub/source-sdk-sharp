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
/// Takes something away from the players standing in it: the engine's habit of
/// moving them where they did not steer, or the ability to stand up.
///
/// Two spawnflags and nothing else — no keys, no outputs of its own. Maps use it
/// under a low pipe so the player ducks without being asked, and around a ledge
/// so the auto-step does not walk them off it.
/// </summary>
[ScriptedEntity(EntityNames.TriggerPlayermovement, ScriptedEntityKind.Brush)]
public sealed class TriggerPlayerMovement : TriggerVolume
{
    public TriggerPlayerMovement(IGameClock clock, IEngineNetworking net, IEnginePlayerMovement playerMovement)
        : base(clock)
    {
        Net = net;
        PlayerMovement = playerMovement;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>how a player moves, and what may stop them.</summary>
    private readonly IEnginePlayerMovement PlayerMovement;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>
    /// trigger_playermovement's own spawnflags.
    ///
    /// <para>
    /// The entity that most needs the type. 0x10 means "only player-ally NPCs" on
    /// every other trigger, and this one once used it for auto-movement -- so its
    /// word holds a bit that a sibling entity reads as something else entirely.
    /// Stock rewrites the spawnflags in Spawn and asks mappers to fix the map; here
    /// the old value is simply read as the new one, because a map's spawnflags are
    /// the map's and nothing on this entity uses 0x10 for its own meaning.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        /// <summary>"Disable auto player movement": no auto-step, no auto-ladder, no wall slide.</summary>
        DisableAutoMovement = 0b0000_0000_1000_0000,

        /// <summary>The old spelling of SpawnFlag.DisableAutoMovement, still in shipped maps. 0x10 is "only player-ally NPCs" on every other trigger.</summary>
        ObsoleteAutoMovement = 0b0000_0000_0001_0000,

        /// <summary>"Duck automatically", by holding the duck button down for them.</summary>
        AutoDuck = 0b0000_1000_0000_0000,  // 0x800
    }

    /// <summary>
    /// EITHER spelling of the flag, not both.
    ///
    /// Written as a disjunction rather than HasFlag of the two ORed together,
    /// because those are different questions: HasFlag(A | B) asks for both, and
    /// a map sets exactly one of these -- the old value or the new one. Asking
    /// for both would turn auto-movement off in no map at all.
    /// </summary>
    private bool WantsAutoDisable
        => Flags.HasFlag(SpawnFlag.DisableAutoMovement)
        || Flags.HasFlag(SpawnFlag.ObsoleteAutoMovement);

    public override void OnSpawn()
    {
        base.OnSpawn();

    }

    protected override void Fire(EntityHandle toucher) => Apply(toucher, inside: true);

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);

        // Unconditionally, and not only when the volume is enabled: a trigger
        // switched off around someone would otherwise leave them ducking and
        // unable to stand for the rest of the round.
        Apply(toucher, inside: false);
    }

    /// <summary>
    /// Both flags, in one place, so that entering and leaving cannot drift apart.
    ///
    /// <para>
    /// Stock's own note applies unchanged: a player standing in two of these at
    /// once is not accounted for, and the second one they leave hands back what
    /// the first is still trying to take away. Fixing it means a count on the
    /// player rather than a bool, which is a change to what the flag MEANS and
    /// belongs with whoever owns player state.
    /// </para>
    /// </summary>
    private void Apply(EntityHandle toucher, bool inside)
    {
        // Players only. A physics crate has no buttons to hold down and no
        // movement code to suppress, and stock reaches the same answer by
        // failing the ToBasePlayer cast.
        if ((Net.EntityGetState(toucher, out _) & EntityState.Player) == 0)
            return;

        if (Flags.HasFlag(SpawnFlag.AutoDuck))
            PlayerMovement.ForceButtons(toucher, InputButtons.Duck, inside);

        if (WantsAutoDisable)
            PlayerMovement.SetAllowAutoMovement(toucher, !inside);
    }
}
