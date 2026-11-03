using SourceSharp.Abi;

using TF2.Server.Entities;

namespace TF2.Server.Modes;

/// <summary>
/// Payload (<c>"pl"</c>, stock <c>TF_GAMETYPE_ESCORT</c>): the team-round
/// machine with escort's game type. On even teams a new player joins the
/// attackers (<c>CTFPlayer::GetAutoTeam</c>'s <c>TF_GAMETYPE_ESCORT</c> arm),
/// and a cart's points may always be blocked.
/// The cart, its checkpoints, the attackers' win at the last one and the
/// defenders' at the timer are Phase 2.6's, here.
/// </summary>
[SideService(Side.Server)]
public sealed class PayloadMode(TFModeServices services, IModeObjectives objectives, IEngineEntities entities)
    : TFModeBase(services)
{
    /// <summary>The id <c>sv_mode</c> and the selector name.</summary>
    public const string ModeId = "pl";

    public override string Id => ModeId;

    public override TFGameType GameType => TFGameType.Escort;

    public override bool EvenTeamsJoinAttackers => true;

    /// <summary><c>CTFGameRules::PointsMayAlwaysBeBlocked</c>: <c>GetGameType() == TF_GAMETYPE_ESCORT</c>.</summary>
    public override bool PointsMayAlwaysBeBlocked => true;

    /// <summary>
    /// Stock's escort game-type init (tf_gamerules.cpp:4350-4357): the map
    /// fields more than one train when a tf_logic_multiple_escort is present
    /// — the gEntList classname search. The client reads the flag for the
    /// escort HUD's group test and alarm gate, so it goes over the objective
    /// table (stock networks it on the gamerules DT).
    /// </summary>
    public override void SetupRound(float now)
    {
        base.SetupRound(now);
        objectives.SetMultipleTrains(entities.FindAllByClassname("tf_logic_multiple_escort").Any());
    }
}
