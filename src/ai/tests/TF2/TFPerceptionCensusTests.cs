using System.Reflection;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Who in TF2's binding reads what a player or a thing is (plan_hl2mp_ai §4.4 over P6 step 3): an
/// IL census over SourceSharp.Ai.TF2. Only the view (what a client draws), the objective feed
/// (what every HUD shows) and the map's placements (a spawn, a pack, a room: never a player, read
/// as the level comes up) read the mod's player facts, the entities' places and the buildings'
/// rows; only the view reads a player's real team, which it uses to choose how a disguise is
/// drawn; and nothing in the binding moves or turns a player outside its command.
/// </summary>
public sealed class TFPerceptionCensusTests
{
    private static readonly Assembly Binding = typeof(TFBotGame).Assembly;

    private static readonly string[] Reads =
    [
        "ITFActorFacts::TryRead", "ITFPlayerData::Networked", "TFDisguiseWeapons::DressOf",
        "IEngineEntities::GetOrigin", "IEngineEntities::GetVelocity", "IEngineEntities::GetWorldSpaceCenter", "IEngineEntities::GetHealth",
        "IEntityDirectory::OfClass", "TFBaseObject::get_Health", "TFBaseObject::get_Level", "IEnginePlayers::GetTeam", "Arsenal::PoolOf",
    ];

    private static readonly string[] Forbidden =
    [
        "IEnginePlayers::PlaceAtSpawn", "IEnginePlayers::SnapEyeAngles", "IEnginePlayers::Respawn",
        "IEnginePlayers::SetMoveType", "IEnginePlayers::ForceButtons", "IEnginePlayers::SetFrozen",
        "IEngineEntities::SetOrigin", "IEngineEntities::SetAngles", "IEngineEntities::SetVelocity", "IEngineEntities::Teleport",
    ];

    private static string Name(MethodBase m) => $"{m.DeclaringType?.Name}::{m.Name}";

    [Fact]
    public void OnlyTheViewTheObjectiveFeedAndThePlacementsReadWhatAPlayerOrAThingIs()
    {
        string[] callers = IlCallCensus.Callers(m => Reads.Contains(Name(m)), Binding);
        // The navigation profile reads the bot's own class, for the jumps it can make (P6 step 2); the
        // class pick the bot's own team, as its HUD shows it, to know it may pick a class (P6 step 7).
        Assert.Equal(new[] { typeof(TFBotGameView).FullName!, typeof(TFObjectiveFeed).FullName!, typeof(TFPlacedEntities).FullName!,
                             typeof(SourceSharp.Ai.TF2.Nav.TFNavProfile).FullName!, typeof(TFBotClassPick).FullName! }.Order(StringComparer.Ordinal), callers);
    }

    /// <summary>The placements read no player: none of the mod's player facts, no team of a player.</summary>
    [Fact]
    public void ThePlacementsReadNoPlayer()
    {
        string[] calls = [.. IlCallCensus.Calls(_ => true, Binding).Where(c => c.Caller == typeof(TFPlacedEntities).FullName).Select(c => c.Callee).Distinct()];
        Assert.Contains("OfClass", calls);
        Assert.DoesNotContain("TryRead", calls);
        Assert.DoesNotContain("GetTeam", calls);
    }

    [Fact]
    public void OnlyTheViewReadsAPlayersRealTeam()
        => Assert.Equal([typeof(TFBotGameView).FullName!],
                        IlCallCensus.Callers(m => m.DeclaringType?.FullName == "TF2.Server.Player.TFActorFacts" && m.Name == "get_Team", Binding));

    [Fact]
    public void NothingInTheBindingMovesOrTurnsAPlayer()
        => Assert.Empty(IlCallCensus.Callers(m => Forbidden.Contains(Name(m)), Binding));

    [Fact]
    public void TheCensusSeesTheViewsReads()
        // The control: the reads above are calls the census finds.
        => Assert.Contains(typeof(TFBotGameView).FullName!, IlCallCensus.Callers(m => Name(m) == "ITFActorFacts::TryRead", Binding));
}
