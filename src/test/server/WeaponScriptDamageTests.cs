using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// HL2MP's bullet damage is the weapon script's (CHL2MPSWeaponInfo's "damage",
/// m_iPlayerDamage, which CHL2MP_Player::FireBullets makes every round's), not
/// HL2's sk_plr_dmg_* (HL2MP's ammo types carry none). FireBullets marks a round
/// over 16 as always gibbing and the rest as never. Found in a glass A/B on
/// ss_sandbox (2026-10-28): stock's pistol round took 8 off a pane, ours 5; the
/// scripts say pistol 8, smg1 5, ar2 11, shotgun 9, 357 75, and ours dealt HL2's
/// 5, 4, 8, 8 and 40.
/// </summary>
public class WeaponScriptDamageTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Struck = new(0x0002_000Au);

    [Fact]
    public void The_parser_reads_the_damage_key()
    {
        var scripts = new WeaponScriptDatabase(new RecordingLog());

        Assert.Equal(8, scripts.AddFromText("weapon_pistol", "WeaponData { \"damage\" \"8\" }")!.PlayerDamage);
        Assert.Equal(0, scripts.AddFromText("weapon_crowbar", "WeaponData { \"printname\" \"x\" }")!.PlayerDamage);
    }

    private static RecordingEngine Fired(string weapon, string script)
    {
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f), new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(true, 0.001f, new Vector(64f, 0f, 64f), new Vector(-1f, 0f, 0f), Struck, 13, 0, HitGroup.Generic))
            .Returns("WorldQuery.PointContents", Contents.Empty)
            .Returns("Clock.Now", 10f);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var scripts = new WeaponScriptDatabase(new RecordingLog());
        scripts.AddFromText(weapon, script);

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(), scripts: scripts);
        arsenal.GiveWeapon(Player, weapon);

        engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd { Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
        Assert.True(engine.Called("Sound.EntityEmitSound"), "the weapon fired");
        return engine;
    }

    private static (float Damage, DamageType Type) Dealt(RecordingEngine engine)
    {
        var call = engine.Last("Damage.ApplyAt")!.Value;
        return (call.ArgAs<float>(2), call.ArgAs<DamageType>(3));
    }

    [Fact]
    public void A_pistol_round_deals_its_scripts_eight_and_never_gibs()
    {
        var (damage, type) = Dealt(Fired("weapon_pistol",
            "WeaponData { \"damage\" \"8\" \"primary_ammo\" \"Pistol\" \"clip_size\" \"18\" }"));

        Assert.Equal(8f, damage);
        Assert.Equal(DamageType.Bullet | DamageType.NeverGib, type);
    }

    [Fact]
    public void A_357_round_deals_its_scripts_seventy_five_and_always_gibs()
    {
        var (damage, type) = Dealt(Fired("weapon_357",
            "WeaponData { \"damage\" \"75\" \"primary_ammo\" \"357\" \"clip_size\" \"6\" }"));

        Assert.Equal(75f, damage);
        Assert.Equal(DamageType.Bullet | DamageType.AlwaysGib, type);
    }
}
