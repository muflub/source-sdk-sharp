using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Shared;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// TF2's answers to the server binding (<see cref="IBotGame"/>, plan_bots_teams_tf2 C.2): the
/// knowledge from the mod's own tables (<see cref="TFKnowledge"/>, <see cref="TFMapKnowledge"/>,
/// <see cref="TFAbilities"/>).
/// Until the TF2 trees land (P6 step 6) a bot's decider stands still (<see cref="TFIdleDecider"/>).
/// TF2 has no thrown grenade: its HUD counts none.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotGame)])]
public sealed class TFBotGame(WeaponCatalog catalog, TFWeaponScripts scripts, TFClassTable classes, TFPlacedEntities placed) : IBotGame
{
    private readonly AbilityTable _abilities = TFAbilities.Table();

    public IDecider Decider(BrainSetup setup) => new TFIdleDecider(setup);

    public AbilityTable Abilities => _abilities;

    public WeaponKnowledge Weapons() => TFKnowledge.Weapons(catalog, scripts.GetQuiet);

    /// <summary>The map from the level's placed entities (<see cref="TFPlacedEntities"/>): a shipped TF2 map's lump is compressed, and the lump reader cannot read it.</summary>
    public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge weapons) => TFMapKnowledge.Build(mapName, placed.Read(), weapons, _abilities);

    /// <summary>The classes as the class table has them now (the scripts load with the level).</summary>
    public System.Collections.Immutable.ImmutableArray<TFClassFacts> Classes()
        => TFKnowledge.Classes(c => classes.IsLoaded ? (classes[c].MaxSpeed, classes[c].MaxHealth) : (0f, 0));

    /// <summary>A TF2 player's model is its class's, the game's: a bot asks for nothing in its USERINFO.</summary>
    public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => [];

    public string GrenadeWeapon => "";

    public bool Thrown(in WeaponType weapon) => false;

    public IBotGameView View { get; } = new PlainBotGameView();
}
