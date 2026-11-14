using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Knowledge;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// HL2MP's answers to the server binding (<see cref="IBotGame"/>): the HL2MP
/// decider, the weapon knowledge from HL2MP's catalog and behaviours, the map
/// knowledge from HL2MP's pickups and the respawn convars, stock HL2MP's
/// model list, and the frag grenade. It has no abilities and no objectives:
/// its map knowledge carries the empty table and none (plan_bots_teams_tf2 C.3, C.6).
/// </summary>
[SideService(Side.Server, As = [typeof(IBotGame)])]
public sealed class Hl2mpBotGame(WeaponCatalog weapons, ModelDescriptors models, WeaponRespawnTimeConVar weaponRespawnTime,
                                 ItemRespawnTimeConVar itemRespawnTime) : IBotGame
{
    public IDecider Decider(BrainSetup setup) => new Trees.Hl2mpDecider(setup);

    /// <summary>HL2MP's bots use no ability: their every action is a weapon's (plan_bots_teams_tf2 C.3).</summary>
    public AbilityTable Abilities => default;

    public WeaponKnowledge Weapons() => WeaponKnowledgeBuilder.Build(weapons, w => WeaponKnowledgeBuilder.ReloadSeconds(models, w));

    public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge facts)
        => MapKnowledgeBuilder.Build(mapName, entities, weapons, facts, weaponRespawnTime.Value, itemRespawnTime.Value);

    public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => BotAppearance.For(random, team, teams);

    public string GrenadeWeapon => EntityNames.WeaponFrag;

    public bool Thrown(in WeaponType weapon) => weapon.Behaviour == Hl2mpWeaponBehaviours.Frag;

    /// <summary>HL2MP's client draws nothing past the engine's: no disguise, no things, no objectives.</summary>
    public SourceSharp.Ai.Server.Senses.IBotGameView View { get; } = new SourceSharp.Ai.Server.Senses.PlainBotGameView();
}
