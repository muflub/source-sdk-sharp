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

/// <summary>
/// The class a hero plays: the hero sheet's (TF2.Descent.Server.Hero's
/// SheetHeroClass), or <see cref="DescentMode.DefaultClass"/> while the player
/// has no sheet yet (a hosted join that has not answered).
/// </summary>
public interface IHeroClass
{
    TFClass ClassOf(int entIndex);
}

/// <summary>The pick through TF2's <c>joinclass</c>: a player with no class yet spawns as it at once.</summary>
[SideService(Side.Server, As = [typeof(IHeroClassPick)])]
public sealed class TFHeroClassPick(TFJoinClassCommand joinClass) : IHeroClassPick
{
    public void Pick(EntityHandle player, TFClass cls) => joinClass.JoinClass(player, TFClassNames.Raw(cls));
}

/// <summary>
/// A joining player becomes a hero (<see cref="DescentMode"/>): on the
/// heroes' team and the class of their hero sheet (<see cref="IHeroClass"/>),
/// the moment the client is in the game,
/// on every level. No team menu is asked for and none is needed; the class
/// menu still changes the class. A bot is not a hero: robots place
/// themselves. The chat says a player joined the heroes once a connection:
/// the joins on later levels (a level change leaves him on no team) are
/// silent, known by his user id, which a level change keeps and a reconnect
/// does not.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroPlacement(IDescentModeState mode, IEnginePlayers players, ITeams teams, IBotPlayers bots,
                                  IHeroClassPick classes, IHeroClass heroClass, ILog log)
    : IClientLifecycle
{
    /// <summary>After TF2's band, at TF2 Descent's first mode stage.</summary>
    public const int Order = ModStages.First + 100;

    /// <summary>The user ids whose join the chat has announced (the service outlives a level, as it must here).</summary>
    private readonly HashSet<int> _announced = [];

    public int ClientLifecycleOrder => Order;

    public void OnClientActive(int entIndex)
    {
        if (!mode.Active)
            return;

        // The slot's own handle, serial and all: a level change frees and
        // refills the player's entity slot, so entry N with serial 0 is no
        // player on any load after the first (the engine answers team -1).
        var player = players.GetHandle(entIndex);
        if (bots.IsBot(player))
            return;

        const int hero = (int)DescentMode.HeroTeam;
        if (teams.Team(player) != hero)
            teams.Join(player, hero, silent: !_announced.Add(players.GetUserId(player)));

        if (teams.Team(player) != hero)
        {
            log.Warning($"[TF2Descent/Mode] player {entIndex} could not join the heroes (team {teams.Team(player)})\n");
            return;
        }

        var cls = heroClass.ClassOf(entIndex);
        classes.Pick(player, cls);
        log.Msg($"[TF2Descent/Mode] player {entIndex} is a hero: team {hero}, {TFClassNames.Raw(cls)}\n");
    }
}
