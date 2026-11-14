using System.Reflection;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Tests;
using TF2.Server.Player;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// TF2's reader for the jump watch (lane G6a, <see cref="TFExperienceJumps"/>): the mod's blast jump state as the watch's
/// blasts, and a census: what it reads of a player (the body, the blast state) is asked by the experience's jump feed alone,
/// so it reaches the store and never a bot's view.
/// </summary>
public sealed class TFExperienceJumpsTests
{
    [Fact]
    public void ARocketJumpIsTheSoldiersOwnBlast() => Assert.Equal(JumpBlast.Own, TFExperienceJumps.Of(TFBlastJump.RocketJumped));

    [Fact]
    public void AStickyJumpIsTheDemomansOwnSticky() => Assert.Equal(JumpBlast.OwnSticky, TFExperienceJumps.Of(TFBlastJump.StickyJumped));

    [Fact]
    public void AnEnemysBlastIsAnothers() => Assert.Equal(JumpBlast.Other, TFExperienceJumps.Of(TFBlastJump.EnemyBlastedMe));

    [Fact]
    public void NoStateIsNoBlast() => Assert.Equal(JumpBlast.None, TFExperienceJumps.Of(TFBlastJump.None));

    [Fact]
    public void AnOwnBlastAndAnothersTogetherAreBoth()
        => Assert.Equal(JumpBlast.Own | JumpBlast.Other, TFExperienceJumps.Of(TFBlastJump.RocketJumped | TFBlastJump.EnemyBlastedMe));

    private static readonly Assembly[] All = [typeof(TFBotGame).Assembly, typeof(ExperienceService).Assembly];

    [Fact]
    public void OnlyTheJumpFeedAsksWhatAPlayersBodyAndBlastsAre()
    {
        string[] callers = [.. IlCallCensus.Callers(m => m.DeclaringType == typeof(IExperienceJumps) || m.DeclaringType == typeof(TFExperienceJumps), All)
                                           .Where(c => c != typeof(TFExperienceJumps).FullName)];
        Assert.Equal([typeof(ShownJumpFeed).FullName!], callers);
    }

    /// <summary>The control: the census sees the reader's own read of a player's state.</summary>
    [Fact]
    public void TheCensusSeesTheReadersReadOfAPlayer()
        => Assert.Contains(typeof(TFExperienceJumps).FullName!, IlCallCensus.Callers(m => m.DeclaringType?.Name == "IEntityDirectory" && m.Name == "Get", All));
}
