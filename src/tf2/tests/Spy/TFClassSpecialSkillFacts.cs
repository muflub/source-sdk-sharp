using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// <c>CTFPlayer::DoClassSpecialSkill</c>
/// (<c>tf_player_shared.cpp:12330-12403</c>) as the server answers it: the
/// spy's branch reaches <c>ActivateInvisibilityWatch</c> behind the alive
/// gate (:12332) and the class switch (:12353), and the demoman's
/// detonates his tray from any weapon.
/// </summary>
public class TFClassSpecialSkillFacts
{
    private static TFClassSpecialSkill Service(SpyRig rig)
    {
        rig.Engine.Returns("Players.GetHandle", SpyRig.Spy);
        return new(rig.States, rig.Engine, rig.Spies);
    }

    [Fact]
    public void ASpysSpecialCloaksHim()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;

        Assert.True(Service(rig).DoSpecialSkill(3));
        Assert.True(rig.State.Conditions().Has(TFCond.Stealthed));
    }

    [Fact]
    public void ADeadSpysSpecialDoesNothing()
    {
        var rig = new SpyRig { SpyAlive = false };

        Assert.False(Service(rig).DoSpecialSkill(3));
        Assert.False(rig.State.Conditions().Has(TFCond.Stealthed));
    }

    [Fact]
    public void ASecondPressInsideTheToggleCooldownCloaksNoMore()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        var skill = Service(rig);

        Assert.True(skill.DoSpecialSkill(3));
        Assert.False(skill.DoSpecialSkill(3));
        Assert.True(rig.State.Conditions().Has(TFCond.Stealthed));
    }

    [Fact]
    public void ASoldiersSpecialDoesNothing()
    {
        var rig = new SpyRig(TFClass.Soldier);

        Assert.False(Service(rig).DoSpecialSkill(3));
    }

    [Fact]
    public void ADemomansSpecialDetonatesHisTrayFromAnyWeapon()
    {
        var rig = new ProjectileRig();
        var states = new TFMovementFixtures.Players();
        states.Add(3, TFClass.Demoman);
        rig.Engine.Returns("Players.IsAlive", true);
        var sticky = rig.Sticky();
        sticky.OnThink(); // arms it, the pre-arm think's first tick
        rig.Tray.Register(3, sticky);
        var skill = new TFClassSpecialSkill(states, rig.Engine, tray: rig.Tray);

        Assert.Equal(1, rig.Tray.Count(3));
        Assert.True(skill.DoSpecialSkill(3));
        Assert.Equal(0, rig.Tray.Count(3));
    }
}
