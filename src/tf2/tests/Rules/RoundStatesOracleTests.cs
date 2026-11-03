
using SourceSharp.OracleTests;

using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The team-round machine's three enums (<c>src/tf2/shared/Rules/RoundStates.cs</c>)
/// held against the PRISTINE <c>teamplayroundbased_gamerules.h</c>, read
/// through the oracle project's <see cref="ValveSdk"/> (stage1 Correction 11):
/// the counts are equal and every stock enumerator has a member of its value,
/// so a member added, dropped or renumbered on either side alone fails.
///
/// <para>
/// <c>WINREASON_</c>'s body carries a <c>#if defined(TF_CLIENT_DLL) || defined(TF_DLL)</c>
/// arm; TF2 compiles it, so the enumerators inside it count and the directive
/// lines themselves are dropped.
/// </para>
/// </summary>
public sealed class RoundStatesOracleTests
{
    private const string Header = "src/game/shared/teamplayroundbased_gamerules.h";

    /// <summary>Stock names whose mechanical spelling (prefix dropped, PascalCase) we do not use.</summary>
    private static string? Renames(string stock) => stock switch
    {
        "GR_STATE_STARTGAME" => nameof(RoundState.StartGame),
        "GR_STATE_RND_RUNNING" => nameof(RoundState.RoundRunning),
        "GR_STATE_BETWEEN_RNDS" => nameof(RoundState.BetweenRounds),
        "GR_NUM_ROUND_STATES" => nameof(RoundState.Count),
        "WINREASON_TIMELIMIT" => nameof(WinReason.TimeLimit),
        "WINREASON_WINLIMIT" => nameof(WinReason.WinLimit),
        "WINREASON_WINDIFFLIMIT" => nameof(WinReason.WinDiffLimit),
        "STALEMATE_SERVER_TIMELIMIT" => nameof(StalemateReason.ServerTimeLimit),
        "NUM_STALEMATE_REASONS" => nameof(StalemateReason.Count),
        _ => null,
    };

    [Fact]
    public void Every_gamerules_roundstate_t_enumerator_is_a_RoundState_member_of_the_same_value()
        => StockEnums.AssertMirrors<RoundState>(StockEnums.Enumerators(ValveSdk.Read(Header), "enum gamerules_roundstate_t"), "GR_STATE_", 12, Renames);

    [Fact]
    public void Every_WINREASON_enumerator_with_the_TF_arm_is_a_WinReason_member_of_the_same_value()
        => StockEnums.AssertMirrors<WinReason>(StockEnums.Enumerators(ValveSdk.Read(Header), "WINREASON_NONE"), "WINREASON_", 17, Renames);

    [Fact]
    public void Every_stalemate_reasons_t_enumerator_is_a_StalemateReason_member_of_the_same_value()
        => StockEnums.AssertMirrors<StalemateReason>(StockEnums.Enumerators(ValveSdk.Read(Header), "enum stalemate_reasons_t"), "STALEMATE_", 4, Renames);

    [Fact]
    public void The_parser_keeps_the_TF_arm_and_drops_its_directives()
    {
        // Positive arm: the TF-only enumerators are read, and nothing named '#...' is.
        var reasons = StockEnums.Enumerators(ValveSdk.Read(Header), "WINREASON_NONE");
        Assert.Contains(reasons, r => r.Name == "WINREASON_STOPWATCH_PLAYING_ROUNDS");
        Assert.DoesNotContain(reasons, r => r.Name.StartsWith('#'));
    }

    [Fact]
    public void A_header_without_the_enum_fails_the_parser_rather_than_yielding_nothing()
    {
        Assert.NotEmpty(StockEnums.Enumerators(ValveSdk.Read(Header), "enum gamerules_roundstate_t"));
        Assert.Throws<InvalidDataException>(() => StockEnums.Enumerators("// no enums here\n", "enum gamerules_roundstate_t"));
    }
}
