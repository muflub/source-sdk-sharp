//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Moves whoever set it off onto another team.
///
/// Stock reaches that team through two hops, and neither one exists in this SDK.
/// The `target` key names a `game_team_master`, whose `TeamID` is a team NAME,
/// and the name goes to `CGameRules::ChangePlayerTeam`. Nothing registers
/// `game_team_master` anywhere in the tree, and `CTeamplayRules::ChangePlayerTeam`
/// is a body Valve commented out — so the stock entity moves nobody, on any map,
/// ever. The fire-once spawnflag is the only part of it that does anything.
///
/// What survived both hops was a team name, so that is what `target` holds here:
/// "Combine", "Rebels", "Spectator", or the number. The indirection is dropped
/// rather than rebuilt, because a lookup through a class nothing can spawn is
/// not a feature, and the key still means what it meant at the end of the chain.
/// </summary>
[ScriptedEntity(EntityNames.GamePlayerTeam)]
public sealed class GamePlayerTeam : ScriptedEntity
{
    public GamePlayerTeam(IEngineDamage damage, IEnginePlayers players)
    {
        Damage = damage;
        Players = players;
    }

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="GamePlayerTeamVars"/>.</summary>
    [ScriptVars] public GamePlayerTeamVars Vars = new();

    /// <summary>
    /// game_player_team's own spawnflags -- the boxes a mapper ticks on this
    /// entity, numbered from one upward the way every entity's are.
    ///
    /// <para>
    /// Deliberately a separate type from <see cref="GibBehaviour"/> below, which
    /// sits in the same run of constants and is a different domain entirely.
    /// These three are read off the spawnflags word; those two are DMG_ bits
    /// handed to the damage call, and they are not the same number for the same
    /// idea -- GibPlayer is bit 3 here, and the gib bit it selects is bit 13
    /// there. One enum spanning both would make that gap invisible.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        FireOnce = 0b0000_0001,
        KillPlayer = 0b0000_0010,
        GibPlayer = 0b0000_0100,
    }

    /// <summary>
    /// What the corpse does afterwards: shareddefs.h's DMG_NEVERGIB and
    /// DMG_ALWAYSGIB, which ride in the damage-type word alongside the kinds of
    /// damage.
    ///
    /// <para>
    /// Kept here rather than added to <see cref="DamageType"/> because neither
    /// is a KIND of damage -- they say what happens to the body, and this is the
    /// only entity in the mod with an opinion on it. That is the existing
    /// judgement in this file and it stands; what changes is that the two bits
    /// now say which word they belong to, instead of sitting in a run of
    /// spawnflag constants looking like more spawnflags.
    /// </para>
    /// </summary>
    [Flags]
    private enum GibBehaviour
    {
        /// <summary>Neither asked for. The damage decides on its own.</summary>
        None      = 0b0000_0000_0000_0000,

        NeverGib  = 0b0001_0000_0000_0000,  // DMG_NEVERGIB, shareddefs.h -- 0x1000
        AlwaysGib = 0b0010_0000_0000_0000,  // DMG_ALWAYSGIB, shareddefs.h -- 0x2000
    }

    /// <summary>Enough to kill anybody. The spawnflag says kill, not how hard.</summary>
    private const float Lethal = 1000f;

    /// <summary>
    /// The HL2MP teams, in the order the rules create them, which is the order
    /// their numbers run in.
    /// </summary>
    private static readonly ImmutableArray<string> TeamNames = [ "Unassigned", "Spectator", "Combine", "Rebels" ];

    // Fired as an ordinary I/O connection, the way stock's Use is: this entity
    // declares no input of its own, so a map reaches it by wiring an output to
    // Use — a button, a trigger, a logic_relay.
    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        // Only a player has a team to be moved between. Anything else setting
        // this off is a map wiring mistake rather than something to guess at —
        // but it still counts as a firing, which is stock's order too.
        if (Players.GetTeam(input.Activator) >= 0)
            Move(input.Activator);

        if (Flags.HasFlag(SpawnFlag.FireOnce))
            Remove();
    }

    private void Move(EntityHandle player)
    {
        int team = TeamNumberFor(Vars.Target);
        if (team < 0)
            return;

        // SetTeam is CBaseEntity's own input, and on a player it lands in
        // CHL2MP_Player::ChangeTeam: the roster move, the model swap, the
        // spectator transition and the suicide that comes with switching sides
        // mid-round all arrive with it. None of that is a decision, which is
        // why it stays the engine's to carry out.
        Entities.FireInput(player, "SetTeam",
            team.ToString(System.Globalization.CultureInfo.InvariantCulture), Handle);

        // Neither asked for. A disjunction rather than HasFlag of the pair,
        // because HasFlag(Kill | Gib) would ask whether BOTH are set -- and the
        // two are alternatives a mapper picks between, so both is the one case
        // that almost never happens.
        if (!Flags.HasFlag(SpawnFlag.KillPlayer) && !Flags.HasFlag(SpawnFlag.GibPlayer))
            return;

        // After the move, not before: stock hands the team and the two flags to
        // one call, and a corpse cannot change sides.
        // The cast is where the two domains meet, and it is the one place they
        // are allowed to: ApplyDamage takes the DMG_ word, and these two bits
        // live in it beside the kinds of damage.
        Damage.Apply(player, Handle, Lethal,
            (DamageType)(Flags.HasFlag(SpawnFlag.GibPlayer) ? GibBehaviour.AlwaysGib : GibBehaviour.NeverGib));
    }

    /// <summary>
    /// The team a map named, or -1 for something that is not a team here.
    ///
    /// Both spellings are accepted, and there is no ambiguity between them: no
    /// HL2MP team is called "2". A map written against the stock chain wrote a
    /// name, and one written against `SetTeam` wrote a number.
    /// </summary>
    internal static int TeamNumberFor(string target)
    {
        if (target.Length == 0)
            return -1;

        for (int team = 0; team < TeamNames.Length; team++)
        {
            if (string.Equals(target, TeamNames[team], StringComparison.OrdinalIgnoreCase))
                return team;
        }

        int number = ParseInt(target, -1);
        return (number >= 0 && number < TeamNames.Length) ? number : -1;
    }
}

/// <summary>
/// The map keys <see cref="GamePlayerTeam"/> declares.
/// </summary>
[ScriptVars]
public partial class GamePlayerTeamVars
{
    /// <summary>The team to move the activator to: an HL2MP team name, or its number.</summary>
    public string Target { get; set; } = string.Empty;
}
