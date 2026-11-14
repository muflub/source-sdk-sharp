using SourceSharp.Abi;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde;
using TF2.Server.Combat;
using TF2.Shared.Combat;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A status's damage tick through the engine (<see cref="EngineRobotStatusDamage"/>, HO8): dealt
/// as the applying hero's hit under the status's own weapon name and TF's burning custom, the
/// body taking the tick's own amount; a hit the engine never hands on is dealt to the bodies.
/// </summary>
public sealed class EngineStatusDamageTests
{
    private static readonly EntityHandle Hero = new(0x0001_0000u | 3);

    [Fact]
    public void A_tick_is_dealt_as_the_appliers_hit_under_the_statuss_weapon_name()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.ReturnsEachCall("Players.GetHandle", () => rig.Engine.Calls[^1].ArgAs<int>(0) == 3 ? Hero : EntityHandle.None);
        var scopes = new TFDamageScopes();
        (TFDamageCustom Custom, string Weapon)? seen = null;
        rig.Engine.WhenCalled("Damage.ApplyAt", call =>
        {
            scopes.TryCurrent(out var custom, out _, out string weapon);
            seen = (custom, weapon);
            body.OnDamagedAt(call.ArgAs<EntityHandle>(1), 999f, DamageType.Generic, default, default, HitGroup.Generic);   // the engine's own number
        });
        var damage = new EngineRobotStatusDamage(rig.Engine, rig.Engine, rig.Engine, scopes);

        damage.Deal(rig.Bodies, body, 3, 4, "fire", "burn");

        var apply = Assert.Single(rig.Engine.AllOf("Damage.ApplyAt"));
        Assert.Equal((body.Handle, Hero, 4f, DamageType.Burn),
                     (apply.ArgAs<EntityHandle>(0), apply.ArgAs<EntityHandle>(1), apply.ArgAs<float>(2), apply.ArgAs<DamageType>(3)));
        Assert.Equal((TFDamageCustom.Burning, EngineRobotStatusDamage.Prefix + "burn"), seen);
        var hit = Assert.Single(rig.Hits.Hits);
        Assert.Equal((4f, "fire", Hero), (hit.Dealt, hit.DamageType, hit.Attacker));
    }

    [Fact]
    public void A_tick_the_engine_never_hands_on_is_dealt_to_the_bodies()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        rig.Engine.Returns("Entities.IsValid", true);
        double life = rig.Roster.Life(id.Index);
        var damage = new EngineRobotStatusDamage(rig.Engine, rig.Engine, rig.Engine, new TFDamageScopes());

        damage.Deal(rig.Bodies, body, 0, 4, "bullet", "bleed");

        Assert.Single(rig.Engine.AllOf("Damage.ApplyAt"));
        var hit = Assert.Single(rig.Hits.Hits);
        Assert.Equal((4f, "bullet", EntityHandle.None), (hit.Dealt, hit.DamageType, hit.Attacker));
        Assert.Equal(life - RobotRules.DamageTaken(rig.Roster.Stats(id.Index), "bullet", 4), rig.Roster.Life(id.Index), 9);
    }
}
