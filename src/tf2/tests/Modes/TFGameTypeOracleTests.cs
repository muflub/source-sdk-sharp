using SourceSharp.OracleTests;

using TF2.Server.Modes;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Modes;

/// <summary><see cref="TFGameType"/> held against the pristine <c>tf_shareddefs.h</c>'s <c>ETFGameType</c>.</summary>
public sealed class TFGameTypeOracleTests
{
    [Fact]
    public void Every_ETFGameType_enumerator_is_a_TfGameType_member_of_the_same_value()
        => StockEnums.AssertMirrors<TFGameType>(
            StockEnums.Enumerators(ValveSdk.Read("src/game/shared/tf/tf_shareddefs.h"), "enum ETFGameType"),
            "TF_GAMETYPE_", 10, _ => null);
}
