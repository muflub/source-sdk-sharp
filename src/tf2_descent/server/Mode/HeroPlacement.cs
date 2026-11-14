using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Mode;

/// <summary>Makes a player a class, as TF2's own <c>joinclass</c> does.</summary>
public interface IHeroClassPick
{
    void Pick(EntityHandle player, TFClass cls);
}

/// <summary>The pick through TF2's <c>joinclass</c>: a player with no class yet spawns as it at once.</summary>
[SideService(Side.Server, As = [typeof(IHeroClassPick)])]
public sealed class TFHeroClassPick(TFJoinClassCommand joinClass) : IHeroClassPick
{
    public void Pick(EntityHandle player, TFClass cls) => joinClass.JoinClass(player, TFClassNames.Raw(cls));
}

/// <summary>
/// A joining player becomes a hero (<see cref="DescentMode"/>): on the
/// heroes' team and the default class, the moment the client is in the game,
/// on every level. No team menu is asked for and none is needed; the class
/// menu still changes the class. A bot is not a hero: robots place
/// themselves.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroPlacement(IDescentModeState mode, ITeams teams, IBotPlayers bots, IHeroClassPick classes, ILog log)
    : IClientLifecycle
{
    /// <summary>After TF2's band, at TF2 Descent's first mode stage.</summary>
    public const int Order = ModStages.First + 100;

    public int ClientLifecycleOrder => Order;

    public void OnClientActive(int entIndex)
    {
        if (!mode.Active)
            return;

        var player = new EntityHandle((uint)entIndex);
        if (bots.IsBot(player))
            return;

        const int hero = (int)DescentMode.HeroTeam;
        if (teams.Team(player) != hero)
            teams.Join(player, hero);

        if (teams.Team(player) != hero)
        {
            log.Warning($"[TF2Descent/Mode] player {entIndex} could not join the heroes (team {teams.Team(player)})\n");
            return;
        }

        classes.Pick(player, DescentMode.DefaultClass);
        log.Msg($"[TF2Descent/Mode] player {entIndex} is a hero: team {hero}, {TFClassNames.Raw(DescentMode.DefaultClass)}\n");
    }
}
