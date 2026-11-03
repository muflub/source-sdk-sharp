extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientBackblast = client::TF2.Client.Weapons.TFRocketBackblast;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using ClientEffects = client::SourceSharp.Client.WeaponEffects;

namespace TF2.Tests.Client;

/// <summary>
/// The rocket's backblast (<c>CTFRocketLauncher::CreateMuzzleFlashEffects</c>'
/// tail, <c>tf_weapon_rocketlauncher.cpp:378-390</c>): the frame the launch's
/// state lands — the next shot pushed later, the round spent — the
/// <c>rocketbackblast</c> particle starts on the weapon's <c>backblast</c>
/// attachment, <c>PATTACH_POINT_FOLLOW</c>. The local player's own gun blasts
/// nothing (stock's <c>IsLocalPlayer</c> arm: their flash is the viewmodel's),
/// and the reload's pushes of the same clock, the round standing or growing,
/// are no shots.
/// </summary>
public sealed class TFRocketBackblastFacts
{
    private const int Local = 7, Other = 9;

    [Fact]
    public void A_launch_blasts_the_backblast_attachment()
    {
        var rig = new Rig(Other);
        rig.Frame(next: 10f, clip: 4);
        rig.Frame(next: 10.8f, clip: 3);

        var start = rig.Engine.Last("Effects.ParticleSystemStart");
        Assert.NotNull(start);
        Assert.Equal((rig.Weapon.Handle, "rocketbackblast"),
                     (start!.Value.ArgAs<EntityHandle>(0), start!.Value.ArgAs<string>(1)));

        var follow = rig.Engine.Last("Effects.ParticleSystemFollowEntity");
        Assert.NotNull(follow);
        Assert.Equal((rig.Weapon.Handle, 0, rig.Weapon.Handle, 3),
                     (follow!.Value.ArgAs<EntityHandle>(0), follow!.Value.ArgAs<int>(1),
                      follow!.Value.ArgAs<EntityHandle>(2), follow!.Value.ArgAs<int>(3)));
        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    /// <summary>Stock's IsLocalPlayer arm: the shooter's own gun, whose flash
    /// is the viewmodel's, blasts nothing even in third person.</summary>
    [Fact]
    public void The_local_players_own_gun_never_blasts()
    {
        var rig = new Rig(Local);
        rig.Frame(next: 10f, clip: 4);
        rig.Frame(next: 10.8f, clip: 3);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    /// <summary>The singly reload pushes the same clock once a shell with the
    /// clip standing; the blast is the shot's, not the reload's.</summary>
    [Fact]
    public void A_reload_push_is_no_blast()
    {
        var rig = new Rig(Other);
        rig.Frame(next: 10f, clip: 4);
        rig.Frame(next: 10.8f, clip: 4);
        rig.Frame(next: 11.6f, clip: 4);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    private sealed class Rig
    {
        public readonly ClientEngine Engine = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientBackblast Fx;

        public Rig(int owner)
        {
            Weapon = WeaponFor(owner);
            Engine.Returns("Hud.LocalPlayerIndex", Local);
            Engine.Returns("EntityRender.LookupAttachment", 3);
            Engine.Returns("Effects.ParticleSystemStart", true);
            Engine.Returns("Effects.ParticleSystemFollowEntity", true);
            Fx = new ClientBackblast(Engine, Engine, Engine);
        }

        private static ClientWeapon WeaponFor(int owner)
        {
            var weapon = new ClientWeapon(new ClientEngine()) { Handle = new EntityHandle(0x0002_0008u) };
            weapon.State.BindLocal();
            weapon.State.OwnerIndex = owner;
            weapon.State.NextPrimaryAttack = 0f;
            weapon.State.Clip = 4;
            return weapon;
        }

        /// <summary>One frame over the state the server's word or the prediction
        /// now holds: the launch's next shot and the rounds left.</summary>
        public void Frame(float next, int clip)
        {
            Weapon.State.NextPrimaryAttack = next;
            Weapon.State.Clip = clip;
            Fx.Think(new ClientEffects.WeaponDrawContext(Weapon, null, FirstPerson: false, Now: 10.4f));
        }
    }
}
