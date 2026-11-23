using SourceSharp.Abi;
using SourceSharp.Ai.TF2;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>The harness's damage tally (P7 step 5): hits on players and buildings by their attacker; a player's own blast on himself is not counted.</summary>
public sealed class TFDamageTallyTests
{
    private static readonly EntityHandle Bot = new(0x0001_0003u), Enemy = new(0x0001_0005u), Sentry = new(0x0002_0040u);

    [Fact]
    public void HitsOnOthersAreTalliedByAttackerAndOwnBlastIsNot()
    {
        var tally = new TFDamageTally();
        tally.OnPlayerHurt(new PlayerHurt(Enemy, Bot, 60, 140));
        tally.OnEntityHurt(new EntityHurt(Sentry, Bot, 40, 110));
        tally.OnPlayerHurt(new PlayerHurt(Bot, Bot, 50, 150));
        Assert.Equal((100L, 0L), (tally.Dealt(3), tally.Dealt(5)));
    }
}
