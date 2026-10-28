/*
 * weapon_stunstick, stock HL2MP's CWeaponStunStick (weapon_stunstick.cpp over
 * weapon_hl2mpbasebasebludgeon.cpp), as a row in the shared weapon table.
 *
 * Driven through the real machine and the real server arms, the way
 * WeaponImpactTests drives the bullet path: a player looking down +X with a
 * surface 64 units ahead, the trigger pulled after the draw has finished.
 */

using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class StunstickTests
{
    private const uint PlayerHandle = 0x0001_0001;

    private static EntityHandle Player => new(PlayerHandle);

    private static EntityHandle Crate => new(0x0002_0007);

    private static Vector Landed => new(64f, 0f, 64f);

    private static Vector Face => new(-1f, 0f, 0f);

    private const int Attack = (int)InputButtons.Attack;

    private sealed class RecordingTemps : TempEntsDouble
    {
        public readonly List<(string Name, TeEffectData Data)> Dispatched = new();
        public readonly List<Vector> Sparked = new();

        public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
            => Dispatched.Add((effectName, data));

        public override void Sparks(TeFilter filter, float delay, Vector position,
                                    int magnitude, int trailLength, Vector? direction)
            => Sparked.Add(position);
    }

    private sealed record Rig(Arsenal Arsenal, RecordingEngine Engine, RecordingTemps Temps,
                              WeaponSwitchTests.SpawningEntities World);

    private static Rig Armed(string weapon, bool hit = true, IMergedAttachments? attachments = null)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f),
                                     new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(hit, hit ? 0.5f : 1f, Landed, Face,
                                            hit ? Crate : EntityHandle.None,
                                            hit ? 13 : -1, 0))
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var temps = new RecordingTemps();

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups())
        {
            Temps = temps,
            Attachments = attachments,
        };

        Assert.True(arsenal.GiveWeapon(Player, weapon), $"the fixture's player is armed with {weapon}");

        // What the swing struck is a live entity from here on, so the damage
        // arm is reached; the carried pair already exists.
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;

        return new Rig(arsenal, engine, temps, world);
    }

    /// <summary>One command with the trigger held, once the draw has finished.</summary>
    private static void Swing(Rig rig)
    {
        rig.Engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd { Buttons = Attack };
        rig.Arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
    }

    private static string[] SoundsPlayed(RecordingEngine engine)
        => engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)).ToArray();

    // =====================================================================
    // The row.
    // =====================================================================

    [Fact]
    public void TheStunstickIsAppendedSoTheFirstNineKeepTheirWireIndices()
    {
        var stock = WeaponCatalog.Stock();

        Assert.Equal(9, stock.IndexOf(EntityNames.WeaponStunstick));
        Assert.Equal(0, stock.IndexOf(EntityNames.WeaponCrowbar));
        Assert.Equal(8, stock.IndexOf(EntityNames.WeaponFrag));
    }

    [Fact]
    public void TheStunstickSwingsAtStocksRefireReachAndDamage()
    {
        var row = WeaponCatalog.Stock().For(EntityNames.WeaponStunstick)!.Value;

        // STUNSTICK_REFIRE, STUNSTICK_RANGE, GetDamageForActivity's 40.
        Assert.Equal(0.8f, row.Interval);
        Assert.Equal(75f, row.MeleeRange);
        Assert.Equal(40f, row.MeleeDamage);
        Assert.True(row.IsMelee, "a bludgeon: no ammunition, nothing launched");
        Assert.True(row.Secondary is null, "CWeaponStunStick::SecondaryAttack is empty");
    }

    [Fact]
    public void TheStunsticksModelsAndSoundsAreItsScripts()
    {
        var row = WeaponCatalog.Stock().For(EntityNames.WeaponStunstick)!.Value;

        Assert.Equal("models/weapons/v_stunstick.mdl", row.ViewModel);
        Assert.Equal("models/weapons/w_stunbaton.mdl", row.WorldModel);
        Assert.Equal("Weapon_StunStick.Swing", row.FireSound);
        Assert.Equal("Weapon_StunStick.Melee_Hit", row.MeleeHitSound);
    }

    [Fact]
    public void TheMapCanPlaceAStunstickPickup()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(WeaponPickup).Assembly);

        Assert.True(registry.IsRegistered(EntityNames.WeaponStunstick),
            "weapon_stunstick is a pickup a map places, as each row's classname is");
    }

    // =====================================================================
    // The swing.
    // =====================================================================

    [Fact]
    public void ASwingThatMissesPlaysOnlyTheSwing()
    {
        var rig = Armed(EntityNames.WeaponStunstick, hit: false);
        rig.Engine.Clear();

        Swing(rig);

        Assert.Equal(new[] { "Weapon_StunStick.Swing" }, SoundsPlayed(rig.Engine));
        Assert.False(rig.Engine.Called("Damage.ApplyAt"), "a miss hurts nobody");
    }

    [Fact]
    public void ASwingThatLandsHitsForFortyAndPlaysTheHitSound()
    {
        var rig = Armed(EntityNames.WeaponStunstick);
        rig.Engine.Clear();

        Swing(rig);

        Assert.Equal(new[] { "Weapon_StunStick.Swing", "Weapon_StunStick.Melee_Hit" }, SoundsPlayed(rig.Engine));

        var damage = rig.Engine.Last("Damage.ApplyAt");
        Assert.True(damage is not null, "the swing landed on the crate");
        Assert.Equal(Crate, damage!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal(40f, damage.Value.ArgAs<float>(2));
        Assert.Equal(DamageType.Club, damage.Value.ArgAs<DamageType>(3));
    }

    [Fact]
    public void ALandedStunstickSwingDispatchesItsImpactFourUnitsOffTheFace()
    {
        var rig = Armed(EntityNames.WeaponStunstick);

        Swing(rig);

        var (name, data) = Assert.Single(rig.Temps.Dispatched);
        Assert.Equal("StunstickImpact", name);
        Assert.Equal(Landed + Face * 4f, data.Origin);
        Assert.Equal(Face, data.Normal);
    }

    [Fact]
    public void ALandedSwingEndsInTheClubImpactTrace()
    {
        var rig = Armed(EntityNames.WeaponCrowbar);

        Swing(rig);

        var impact = rig.Engine.Last("Effects.ImpactEffect");
        Assert.True(impact is not null, "UTIL_ImpactTrace( &traceHit, DMG_CLUB ) ends every bludgeon's ImpactEffect");
        Assert.Equal(Landed, impact!.Value.ArgAs<Vector>(1));
        Assert.Equal(13, impact.Value.ArgAs<int>(2));
        Assert.Equal(DamageType.Club, impact.Value.ArgAs<DamageType>(3));
    }

    [Fact]
    public void TheCrowbarPlaysNoHitSoundAndDispatchesNoEffectOfItsOwn()
    {
        var rig = Armed(EntityNames.WeaponCrowbar);
        rig.Engine.Clear();

        Swing(rig);

        // CBaseHL2MPBludgeonWeapon::PlayFleshyHittySoundOnHit answers false,
        // and CWeaponCrowbar does not override it.
        Assert.Equal(new[] { "Weapon_Crowbar.Single" }, SoundsPlayed(rig.Engine));
        Assert.Empty(rig.Temps.Dispatched);
        Assert.True(rig.Engine.Called("Damage.ApplyAt"), "the stimulus reached the crate");
    }

    // =====================================================================
    // Drawing, putting away, dropping.
    // =====================================================================

    [Fact]
    public void DrawingTheStunstickActivatesItWithSparks()
    {
        var rig = Armed(EntityNames.WeaponStunstick);

        Assert.Contains("Weapon_StunStick.Activate", SoundsPlayed(rig.Engine));
        Assert.Single(rig.Temps.Sparked);
    }

    private sealed class Merged(bool answers, Vector at) : IMergedAttachments
    {
        public readonly List<(EntityHandle Follower, EntityHandle Source, int Attachment)> Asked = [];

        public bool GetMergedAttachment(EntityHandle follower, EntityHandle source, int attachment, out Vector origin)
        {
            Asked.Add((follower, source, attachment));
            origin = answers ? at : default;
            return answers;
        }
    }

    /// <summary>
    /// SetStunState's sparks are at the weapon's attachment 1, which a stock
    /// server answers off the w_ model bone-merged onto its carrier.
    /// </summary>
    [Fact]
    public void TheDrawsSparksAreAtTheStickMergedOntoItsCarrier()
    {
        var head = new Vector(5f, 6f, 70f);
        var merged = new Merged(true, head);

        var rig = Armed(EntityNames.WeaponStunstick, attachments: merged);

        Assert.Equal(head, Assert.Single(rig.Temps.Sparked));
        var (follower, source, attachment) = Assert.Single(merged.Asked);
        Assert.Equal((Player, 1), (source, attachment));
        Assert.NotEqual(Player, follower);
    }

    /// <summary>A stick the skeletons cannot place sparks at its own origin, as a failed GetAttachment answers.</summary>
    [Fact]
    public void ADrawWhoseAttachmentCannotBeFoundSparksAtTheWeapon()
    {
        var merged = new Merged(false, new Vector(5f, 6f, 70f));

        var rig = Armed(EntityNames.WeaponStunstick, attachments: merged);

        Assert.Single(merged.Asked);
        Assert.Equal(Vector.Zero, Assert.Single(rig.Temps.Sparked));
    }

    [Fact]
    public void PuttingTheStunstickAwayDeactivatesIt()
    {
        var rig = Armed(EntityNames.WeaponStunstick);
        Assert.True(rig.Arsenal.GiveWeapon(Player, EntityNames.WeaponPistol), "a second weapon to switch to");
        rig.Engine.Clear();

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, EntityNames.WeaponPistol));

        Assert.Equal(new[] { "Weapon_StunStick.Deactivate" }, SoundsPlayed(rig.Engine));
    }

    [Fact]
    public void ADroppedStunstickLeavesNoPickupBehind()
    {
        var rig = Armed(EntityNames.WeaponStunstick);

        rig.Arsenal.DropActive(Player, Vector.Zero);

        Assert.DoesNotContain(EntityNames.WeaponStunstick, rig.World.CreatedClasses);
        Assert.Contains("Weapon_StunStick.Deactivate", SoundsPlayed(rig.Engine));
    }

    [Fact]
    public void ADroppedCrowbarStillLeavesItsPickup()
    {
        var rig = Armed(EntityNames.WeaponCrowbar);

        rig.Arsenal.DropActive(Player, Vector.Zero);

        Assert.Contains(EntityNames.WeaponCrowbar, rig.World.CreatedClasses);
    }
}
