using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// What a bot's own body tells its view (plan_bots_teams_tf2 P6 step 6): on the ground and ducked,
/// as its client predicts them. Live no bot's view ever said it was grounded, so a TF2 Soldier,
/// whose rocket jump needs the ground under it, never jumped.
/// </summary>
public sealed class BotBodyFlagsTests
{
    private sealed class Stances(bool ground, bool ducked) : IPlayerStances
    {
        public bool Ducked(EntityHandle player) => ducked;

        public bool OnGround(EntityHandle player) => ground;
    }

    [Theory]
    [InlineData(false, false, SelfFlags.None)]
    [InlineData(true, false, SelfFlags.Grounded)]
    [InlineData(false, true, SelfFlags.Ducking)]
    [InlineData(true, true, SelfFlags.Grounded | SelfFlags.Ducking)]
    public void TheBodysOwnStanceSaysGroundedAndDucked(bool ground, bool ducked, SelfFlags want)
        => Assert.Equal(want, BotBodyFlags.Of(new Stances(ground, ducked), new EntityHandle(0x0001_0002u)));
}
