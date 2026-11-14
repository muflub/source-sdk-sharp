extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientFlash = client::TF2.Client.Weapons.TFWeaponMuzzleFlash;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using ClientEffects = client::SourceSharp.Client.WeaponEffects;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientCatalog = client::SourceSharp.Shared.WeaponCatalog;
using ClientScriptTypes = client::TF2.Shared.TFWeaponTypes;
using ClientClassTable = client::TF2.Shared.TFClassTable;
using ClientItemSchema = client::TF2.Shared.Econ.ItemSchema;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientLog = client::SourceSharp.Abi.RecordingLog;

using TF2.Tests;
using TF2.Tests.Weapons;

namespace TF2.Tests.Client;

/// <summary>
/// The world-model muzzle flash (<c>TFWeaponMuzzleFlash</c>): the shot edge
/// hangs the script's <c>MuzzleFlashParticleEffect</c> on the gun's
/// <c>muzzle</c> attachment, <c>PATTACH_POINT_FOLLOW</c> — the parity flash's
/// third-person arm (<c>tf_weaponbase.cpp:3113-3198</c>). The frame a gun is
/// first seen fires nothing, a frame without a shot neither, the first-person
/// viewer's gun hangs nothing (the predicted replay's viewmodel flash is the
/// same shot's), and stock's two <c>ShouldDoMuzzleFlash</c>-false kinds — the
/// minigun's loop, the bow's silent launch — flash none though their scripts
/// name particles.
/// </summary>
public sealed class TFWeaponMuzzleFlashFacts
{
    private const int Local = 7, Other = 9;
    private const string Scattergun = "tf_weapon_scattergun";

    [Fact]
    public void A_shot_fires_the_scripts_muzzle_particle_on_the_world_model()
    {
        var rig = new Rig(Other, Scattergun);
        rig.Frame(next: 10f, clip: 6);
        rig.Frame(next: 10.625f, clip: 5);

        var start = rig.Engine.Last("Effects.ParticleSystemStart");
        Assert.NotNull(start);
        Assert.Equal((rig.Weapon.Handle, "muzzle_scattergun"),
                     (start!.Value.ArgAs<EntityHandle>(0), start!.Value.ArgAs<string>(1)));

        var follow = rig.Engine.Last("Effects.ParticleSystemFollowEntity");
        Assert.NotNull(follow);
        Assert.Equal((rig.Weapon.Handle, 0, rig.Weapon.Handle, 3),
                     (follow!.Value.ArgAs<EntityHandle>(0), follow!.Value.ArgAs<int>(1),
                      follow!.Value.ArgAs<EntityHandle>(2), follow!.Value.ArgAs<int>(3)));
        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    [Fact]
    public void The_frame_a_gun_is_first_seen_fires_nothing()
    {
        var rig = new Rig(Other, Scattergun);
        rig.Frame(next: 10f, clip: 6);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    [Fact]
    public void A_frame_without_a_shot_fires_nothing()
    {
        var rig = new Rig(Other, Scattergun);
        rig.Frame(next: 10f, clip: 6);
        rig.Frame(next: 10.625f, clip: 5);
        rig.Frame(next: 10.625f, clip: 5);

        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    /// <summary>The singly reload pushes the same clock once a shell with the
    /// clip standing (the reload's <c>NextPrimaryAttack</c>, the round kept or
    /// grown): the push is no shot.</summary>
    [Fact]
    public void A_reload_push_is_no_flash()
    {
        var rig = new Rig(Other, Scattergun);
        rig.Frame(next: 10f, clip: 6);
        rig.Frame(next: 10.625f, clip: 6);
        rig.Frame(next: 11.25f, clip: 7);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    /// <summary>The first-person viewer's flash is the predicted replay's: the
    /// same shot hangs nothing on the world model while the camera is at the
    /// eye, and the frames still count, so the camera's switch leaves no stale
    /// flash behind.</summary>
    [Fact]
    public void The_first_person_viewers_shot_hangs_nothing_and_the_edge_is_spent()
    {
        var rig = new Rig(Other, Scattergun);
        rig.Frame(next: 10f, clip: 6, firstPerson: true);
        rig.Frame(next: 10.625f, clip: 5, firstPerson: true);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));

        rig.Frame(next: 10.625f, clip: 5, firstPerson: false);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    /// <summary>Stock's two false overrides (<c>tf_weapon_minigun.h:78</c>,
    /// <c>tf_weapon_compound_bow.h:73</c>): neither reaches the parity, though
    /// both scripts name a particle.</summary>
    [Theory]
    [InlineData("tf_weapon_minigun")]
    [InlineData("tf_weapon_compound_bow")]
    public void The_vetoing_kinds_flash_nothing(string kind)
    {
        var rig = new Rig(Other, kind);
        rig.Frame(next: 10f, clip: 6);
        rig.Frame(next: 10.625f, clip: 5);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    private sealed class Rig
    {
        public readonly ClientEngine Engine = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientFlash Fx;

        public Rig(int owner, string kind)
        {
            var files = new Served(WeaponFixtures.ShippedWithClasses());
            var log = new ClientLog();
            var scripts = new ClientScripts(log, files);
            var catalog = new ClientCatalog(
                () => [new ClientScriptTypes(scripts, new ClientClassTable(log, files), new ClientItemSchema(files, log))], log);

            Engine.Returns("EntityRender.LookupAttachment", 3);
            Engine.Returns("Effects.ParticleSystemStart", true);
            Engine.Returns("Effects.ParticleSystemFollowEntity", true);

            Weapon = WeaponFor(owner, catalog.IndexOf(kind));
            Fx = new ClientFlash(scripts, catalog, Engine, Engine);
        }

        private static ClientWeapon WeaponFor(int owner, int type)
        {
            var weapon = new ClientWeapon(new ClientEngine()) { Handle = new EntityHandle(0x0002_0008u) };
            weapon.State.BindLocal();
            weapon.State.OwnerIndex = owner;
            weapon.State.WeaponTypeId = type;
            weapon.State.NextPrimaryAttack = 0f;
            weapon.State.Clip = 6;
            return weapon;
        }

        /// <summary>One frame over the state the server's word or the prediction
        /// now holds: the shot's next clock and rounds left.</summary>
        public void Frame(float next, int clip, bool firstPerson = false)
        {
            Weapon.State.NextPrimaryAttack = next;
            Weapon.State.Clip = clip;
            Fx.Think(new ClientEffects.WeaponDrawContext(Weapon, null, FirstPerson: firstPerson, Now: 10.4f));
        }
    }

    /// <summary>The shipped pack, served as the client's files.</summary>
    private sealed class Served(TFClassTableTests.Files files) : ClientFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }
}
