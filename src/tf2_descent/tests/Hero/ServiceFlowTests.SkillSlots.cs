using TF2.Descent.Server.Skills;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The hosted skill bar: what the server's slot store puts on a hosted hero's sheet goes to
/// the host in the next checkpoint, and the host's rules take it with no evidence.
/// </summary>
public sealed partial class ServiceFlowTests
{
    [Fact]
    public async Task A_hosted_heros_slotted_skill_goes_to_the_host_in_the_checkpoint()
    {
        var h = HostedHero(LevelThree);
        var leased = LevelThree(h.Rules);
        Assert.True(h.Sheets.Apply(3, s => h.Rules.Skills.Buy(s, "patriot_missile").Sheet).Ok);
        var store = new SkillSlotStore(new TF2.Descent.Server.Hero.Heroes(), h.Sheets);

        Assert.True(store.Set(3, 4, "patriot_missile").Ok);
        Assert.True(await h.Players.Checkpoint(Steam));

        Assert.Equal(["", "", "", "patriot_missile", "", ""], Sent(h).SkillSlots.ToArray());
        var payload = h.W.Link.Sheets[^1].Sheet!;
        Assert.True(h.Rules.ValidateCheckpoint(leased, payload.Sheet, payload.Evidence).Ok);
    }
}
