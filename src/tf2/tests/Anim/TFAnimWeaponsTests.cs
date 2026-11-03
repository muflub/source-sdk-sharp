using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Tests.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// The server's weapon read for the anim layer: the engine's active weapon,
/// its role the script's WeaponType (CTFWeaponBase::GetActivityWeaponRole's
/// script arm, tf_weaponbase.cpp).
/// </summary>
public sealed class TFAnimWeaponsTests
{
    private static TFServerAnimWeapons Owner(RecordingEngine engine)
        => new(engine, new TFWeaponScripts(new RecordingLog(), WeaponFixtures.Shipped()));

    [Fact]
    public void The_active_weapon_reads_as_its_scripts_role_and_id()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_rocketlauncher");

        Assert.Equal(new TFAnimWeapon(TFWeaponRole.Primary, TFWeaponId.Rocketlauncher), Owner(engine).ActiveWeapon(PlayerRig.Index));

        engine.Returns("Players.GetActiveWeapon", "tf_weapon_shotgun_soldier");
        Assert.Equal(TFWeaponRole.Secondary, Owner(engine).ActiveWeapon(PlayerRig.Index)!.Value.Role);
    }

    [Fact]
    public void No_player_no_weapon_or_an_unnamed_weapon_is_the_no_weapon_arm()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", EntityHandle.None);
        Assert.Null(Owner(engine).ActiveWeapon(PlayerRig.Index));

        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "");
        Assert.Null(Owner(engine).ActiveWeapon(PlayerRig.Index));

        engine.Returns("Players.GetActiveWeapon", "tf_weapon_nothing_scripts_name");
        Assert.Null(Owner(engine).ActiveWeapon(PlayerRig.Index));
        Assert.Null(Owner(engine).DisguiseWeapon(PlayerRig.Index));
    }
}
