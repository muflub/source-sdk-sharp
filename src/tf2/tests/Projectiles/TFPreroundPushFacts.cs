using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Movement;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The freeze gate of <c>CTFPlayer::ApplyPushFromDamage</c>
/// (tf_player.cpp:10234-10236): with
/// <c>tf_preround_push_from_damage_enable</c> off, the push waits on
/// <c>CanPlayerMove</c> (tf_player_shared.cpp:10804-10849). This port has no
/// window that refuses — no match-start countdown, no competitive or
/// movement-restart freeze — so the gate's refusal arm is not yet reachable,
/// and the facts hold the arms that are: the convar's off, its on, and the
/// absent convar's default.
/// </summary>
public class TFPreroundPushFacts
{
    private static TFPreroundPushFromDamageEnableConVar Set(string value)
    {
        var convar = new TFPreroundPushFromDamageEnableConVar();
        convar.Bind(-1, "tf_preround_push_from_damage_enable", value);
        return convar;
    }

    /// <summary>The attacker at the blast's heart, the self pass's one jumper.</summary>
    private static ProjectileRig PushingRig(string? push)
    {
        var rig = push is null ? new ProjectileRig() : new ProjectileRig(Set(push));
        rig.Engine.FillsSphere();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, TFViewVectors.Params.HullMax.Z)));
        return rig;
    }

    private static Vector Impulse(ProjectileRig rig)
        => rig.Engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1);

    private static void Apply(ProjectileRig rig)
        => rig.Blast.Apply(new Vector(0f, 0f, 0f),
                           TFBlast.Of(90f, TFProjectileDefs.RocketRadiusForRocketJumps,
                                      TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher)),
                           ProjectileRig.Player, ProjectileRig.ProjectileHandle,
                           ignored: ProjectileRig.ProjectileHandle,
                           TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                           selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

    [Fact]
    public void The_default_gate_leaves_the_free_player_their_push()
    {
        var rig = PushingRig("0");

        Apply(rig);

        Assert.NotEqual(default, Impulse(rig));
    }

    [Fact]
    public void The_convar_on_pushes_through_all_the_same()
    {
        var rig = PushingRig("1");

        Apply(rig);

        Assert.NotEqual(default, Impulse(rig));
    }

    [Fact]
    public void The_absent_convar_is_the_defaults_off()
    {
        var rig = PushingRig(null);

        Apply(rig);

        Assert.NotEqual(default, Impulse(rig));
    }
}
