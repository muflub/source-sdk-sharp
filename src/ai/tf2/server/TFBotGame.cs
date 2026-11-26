using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Shared;
using TF2.Server.Rules;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// TF2's answers to the server binding (<see cref="IBotGame"/>, plan_bots_teams_tf2 C.2): the
/// knowledge from the mod's own tables (<see cref="TFKnowledge"/>, <see cref="TFMapKnowledge"/>,
/// <see cref="TFAbilities"/>), the view as a TF2 client draws it (<see cref="TFBotGameView"/>).
/// A bot's decider is TF2's tree (<see cref="Trees.TFDecider"/>), a subtree per class.
/// TF2 has no thrown grenade: its HUD counts none.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotGame)])]
public sealed class TFBotGame(WeaponCatalog catalog, TFWeaponScripts scripts, TFClassTable classes, TFPlacedEntities placed, TFBotGameView view,
                              TFFeelSwitchConVars feel, Jump.TFJumps? jumps = null, TFBotPins? pins = null, Nav.TFRouteDashes? dashes = null) : IBotGame
{
    private readonly AbilityTable _abilities = TFAbilities.Table();

    private System.Collections.Immutable.ImmutableArray<TFCourseEnd> _ends = [];

    /// <summary>
    /// TF2's tree. A bot's decider has the feel layer's parts as the <c>ai_tf_feel_*</c> convars stand when it is added, for its
    /// life, reads the level's rocket jumps from the binding's book (plan_bots_teams_tf2_part_g G.5 step 6), and knows the
    /// level's course ends (lane Ge) as <see cref="Map"/> read them.
    /// </summary>
    public IDecider Decider(BrainSetup setup)
        => new Trees.TFDecider(setup, Classes()) { Feel = feel.Read(), Jumps = jumps?.Book, CourseEnds = _ends, Pin = pins?.Of(setup.Bot), RouteDashes = dashes };

    public AbilityTable Abilities => _abilities;

    /// <summary>The rocket jump's program (<see cref="Jump.RocketJumpProgram"/>).</summary>
    public IAbilityPrograms? Programs => jumps;

    public WeaponKnowledge Weapons() => TFKnowledge.Weapons(catalog, scripts.GetQuiet);

    /// <summary>
    /// The map from the level's placed entities (<see cref="TFPlacedEntities"/>): a shipped TF2 map's lump is compressed, and the lump
    /// reader cannot read it. The course ends among them (<see cref="TFCourseEnds"/>) are kept for the level's deciders.
    /// </summary>
    public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge weapons)
    {
        MapEntities level = placed.Read();
        _ends = TFCourseEnds.Of(level);
        return TFMapKnowledge.Build(mapName, level, weapons, _abilities, TFKnowledge.Bodies(Classes()));
    }

    /// <summary>The classes as the class table has them now (the scripts load with the level).</summary>
    public System.Collections.Immutable.ImmutableArray<TFClassFacts> Classes()
        => TFKnowledge.Classes(c => classes.IsLoaded ? (classes[c].MaxSpeed, classes[c].MaxHealth) : (0f, 0));

    /// <summary>A TF2 player's model is its class's, the game's: a bot asks for nothing in its USERINFO.</summary>
    public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => [];

    public string GrenadeWeapon => "";

    public bool Thrown(in WeaponType weapon) => false;

    public IBotGameView View => view;

    /// <summary>TF2's team menu sends a team's name (<see cref="TFJoinTeamCommand"/>): auto for the mode's pick, red, blue, spectate.</summary>
    public string? JoinTeamWord(int team) => team switch
    {
        0 => $"{TFJoinTeamCommand.Command} auto",
        (int)TFTeam.Spectator => $"{TFJoinTeamCommand.Command} spectate",
        (int)TFTeam.Red => $"{TFJoinTeamCommand.Command} red",
        (int)TFTeam.Blue => $"{TFJoinTeamCommand.Command} blue",
        _ => null,
    };
}
