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
/// The game_* map rules, in C#.
///
/// These are the entities through which a map reaches into the rules — award a
/// point, hurt whoever set this off, hand out a weapon, end the round. They are
/// pure gameplay logic with no physical presence, which makes them the most
/// natural thing in the map to own in managed code, and they sit directly
/// alongside the game rules that are already here.
///
/// Every one of them acts on the ACTIVATOR: the entity that set off whatever
/// eventually fired this. That is why the handlers take InputData — the value
/// is usually irrelevant and the activator is the whole point.
/// </summary>
[ScriptedEntity(EntityNames.GameScore)]
public sealed class GameScore : ScriptedEntity
{
    public GameScore(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>What a map may set on this entity. See <see cref="GameScoreVars"/>.</summary>
    [ScriptVars] public GameScoreVars Vars = new();

    /// <summary>
    /// game_score's own spawnflags. One decides what a score may be, the other who
    /// gets it, and a map commonly sets both -- which reads better as a member list
    /// than as two ints consulted at opposite ends of the class.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        AllowNegativeScore = 0b0000_0001,
        AwardToTeam = 0b0000_0010,
    }

    [ScriptedEvent("ApplyScore")]
    private void OnApplyScore(InputData input)
    {
        // Only a player has a score. Anything else setting this off is a map
        // wiring mistake rather than something to guess at.
        if (Players.GetTeam(input.Activator) < 0)
            return;

        // Team scoring is a spawnflag this mod does not implement: with no team
        // play there is no team to award. Awarding the player instead would be
        // inventing a rule the map did not ask for, so it is left alone and the
        // player is scored either way.
        Players.AddScore(input.Activator, Vars.Points, Flags.HasFlag(SpawnFlag.AllowNegativeScore));
    }
}

/// <summary>
/// The map keys <see cref="GameScore"/> declares.
/// </summary>
[ScriptVars]
public partial class GameScoreVars
{
    /// <summary>Points to award. Negative takes them away, if the map allows it.</summary>
    public int Points { get; set; } = 1;
}
