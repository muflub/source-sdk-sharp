using SourceSharp.Shared;

namespace SourceSharp.Ai.Server;

/// <summary>
/// What the generic server binding asks of the game its bots play
/// (plan_bots_teams_tf2 C.2): the decider a brain runs, the game's weapon and
/// map knowledge, the USERINFO a bot joins with, and which weapon is the
/// thrown grenade. The binding of one game (HL2MP's, later TF2's) registers
/// the one implementation; nothing else in the server binding names a game.
/// </summary>
public interface IBotGame
{
    /// <summary>The decider the brain of the bot <paramref name="setup"/> describes runs.</summary>
    IDecider Decider(BrainSetup setup);

    /// <summary>The game's weapon knowledge (plan_hl2mp_ai §5.0), built when a level is up.</summary>
    WeaponKnowledge Weapons();

    /// <summary>
    /// The game's abilities (plan_bots_teams_tf2 C.3): the applier runs a row an
    /// <see cref="ActionKind.Ability"/> one-shot names. <see cref="Map"/>'s knowledge
    /// carries the same table for the brains. Empty for a game with none.
    /// </summary>
    AbilityTable Abilities { get; }

    /// <summary>
    /// What the bots know of <paramref name="mapName"/> before playing, from its entity lump:
    /// with the game's <see cref="Abilities"/> and the map's objectives (plan_bots_teams_tf2 C.6).
    /// </summary>
    MapKnowledge Map(string mapName, SourceSharp.Ai.Server.Nav.MapEntities entities, WeaponKnowledge weapons);

    /// <summary>The USERINFO a bot whose stream draws <paramref name="random"/> joins with, on <paramref name="team"/> of <paramref name="teams"/> when it joins a fixed one.</summary>
    KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams);

    /// <summary>The weapon whose ammunition the HUD counts as the bot's grenades, or "" for a game with none.</summary>
    string GrenadeWeapon { get; }

    /// <summary>Whether <paramref name="weapon"/> is thrown rather than aimed: its shot is no aimed shot of a hit rate.</summary>
    bool Thrown(in WeaponType weapon);
}
