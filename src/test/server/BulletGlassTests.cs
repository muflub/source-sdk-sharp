using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Stock CBaseEntity::FireBullets' glass rule (baseentity_shared.cpp), ported
/// from bullet_glass.h with its facts.
/// </summary>
public class BulletGlassTests
{
    [Fact]
    public void AClassnameNobodyCouldAnswerIsNotPenetrable()
    {
        Assert.False(BulletGlass.PassesThrough(true, null, 0));
        Assert.False(BulletGlass.PassesThrough(true, "", 0));
    }

    [Fact]
    public void AGlassBreakableLetsTheRoundThrough()
        => Assert.True(BulletGlass.PassesThrough(true, "func_breakable", 0));

    [Fact]
    public void TheClassnameMatchesWhateverItsCase()
        => Assert.True(BulletGlass.PassesThrough(true, "Func_Breakable", 0));

    [Fact]
    public void TheMapperCanMakeTheGlassBulletproof()
        => Assert.False(BulletGlass.PassesThrough(true, "func_breakable", BulletGlass.NoBulletPenetration));

    [Fact]
    public void OtherSpawnflagsLeaveItPenetrable()
        => Assert.True(BulletGlass.PassesThrough(true, "func_breakable", ~BulletGlass.NoBulletPenetration));

    [Fact]
    public void ASurfaceThatIsNotGlassStopsTheRound()
        => Assert.False(BulletGlass.PassesThrough(false, "func_breakable", 0));

    [Fact]
    public void ABreakableSurfIsNotABreakable()
        => Assert.False(BulletGlass.PassesThrough(true, "func_breakable_surf", 0));
}
