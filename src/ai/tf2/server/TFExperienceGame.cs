using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Server;
using TF2.Server.Objectives;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Shared.Weapons;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// TF2's answers to the experience feed (<see cref="IExperienceGame"/>,
/// plan_bots_teams_tf2 Part G, G.3): TF2's bots keep experience. The mode is
/// the active mode's id (<c>ctf</c>, <c>pl</c>, <c>cp</c>, <c>koth</c>); the
/// stage is how many control points Blue owns (the attackers' progress in
/// attack/defend and payload, the front's place in 5CP, 0 in CTF); a player's
/// role is its real team and its class its real class (a disguise is drawn,
/// not played); a weapon's class is its projectile type. A round is being
/// played while its state is running or sudden death and the server is not
/// waiting for players: outside that (the setup freeze, the winners' time,
/// the wait) nothing is gathered.
/// </summary>
[SideService(Side.Server, As = [typeof(IExperienceGame)])]
public sealed class TFExperienceGame(GameModes modes, ITFRoundFacts round, ITFActorFacts actors, ITFObjectives objectives, IEnginePlayers players,
                                     TFWeaponScripts scripts) : IExperienceGame
{
    public bool Keeps => true;

    public string Mode => ModeOf(modes.ActiveId);

    public int Stage => StageOf(objectives.All);

    public (int Role, int Class) Of(EntityHandle player)
        => actors.TryRead(player.EntryIndex, out TFActorFacts facts) ? Of(in facts) : (0, 0);

    public int WeaponClass(EntityHandle killer) => (int)(scripts.GetQuiet(players.GetActiveWeapon(killer))?.Primary.Projectile ?? TFProjectile.None);

    public bool RoundOver => Over(round.State, round.IsWaitingForPlayers);

    public int WinningRole => RoleOf(round.WinningTeam);

    /// <summary>The key's mode for the active mode's id: none for no mode and for the core's null mode.</summary>
    public static string ModeOf(string? activeId) => activeId is null or GameModes.NullModeId ? "" : activeId;

    /// <summary>The stage: the control points Blue owns.</summary>
    public static int StageOf(ReadOnlySpan<TFObjective> all)
    {
        int owned = 0;
        foreach (ref readonly TFObjective o in all)
        {
            if (o.Kind == TFObjectiveKind.ControlPoint && o.Owner == TFTeam.Blue)
                owned++;
        }

        return owned;
    }

    /// <summary>A player's role and class: its real team's number and its real class's; no role off the two playing teams.</summary>
    public static (int Role, int Class) Of(in TFActorFacts facts) => (RoleOf(facts.Team), (int)facts.Class);

    /// <summary>A playing team's number as its role; 0 for any other.</summary>
    public static int RoleOf(TFTeam team) => team is TFTeam.Red or TFTeam.Blue ? (int)team : 0;

    /// <summary>Whether no round is being played: any state but running and sudden death, and the wait for players.</summary>
    public static bool Over(RoundState state, bool waitingForPlayers) => waitingForPlayers || state is not (RoundState.RoundRunning or RoundState.Stalemate);
}
