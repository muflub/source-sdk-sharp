using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TFThingType = SourceSharp.Ai.TF2.Senses.TFThingType;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Server.Projectiles;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// What a TF2 bot knows of the game (plan_bots_teams_tf2 P6 step 4), from the mod's own tables:
/// the classes (the script's speed and health, the one hull, the class's eye), the weapons (a
/// whole reload, a projectile's speed and fall, a charge's time), the buildings (metal, levels and
/// their health), the abilities (the Soldier's rocket jump a sequence, the build orders the mod's
/// verbs) and the feet a splash is aimed at.
/// </summary>
public sealed class TFKnowledgeTests
{
    private const float G = 800f;

    [Fact]
    public void EachClassRunsAndSpawnsAsItsScriptSaysInTheModsOneHull()
    {
        var facts = TFKnowledge.Classes(c => c == TFClass.Soldier ? (240f, 200) : (300f, 125));
        Assert.Equal(9, facts.Length);
        TFClassFacts soldier = facts.Single(f => f.Class == TFClass.Soldier);
        Assert.Equal((240f, 200, 68f), (soldier.MaxSpeed, soldier.MaxHealth, soldier.EyeHeight));
        Assert.Equal((new AiVector(-24, -24, 0), new AiVector(24, 24, 82), 62f), (soldier.HullMins, soldier.HullMaxs, soldier.DuckHeight));
        Assert.Equal(300f, facts.Single(f => f.Class == TFClass.Scout).MaxSpeed);
    }

    private static WeaponType Row(string name, string behaviour, int clip, float speed = 0f)
        => new(name, Interval: 0.8f) { ClipSize = clip, Behaviour = behaviour, LaunchSpeed = speed };

    private static TFWeaponModeData Mode(float start, float each, float speed = 0f) => new() { TimeReloadStart = start, TimeReload = each, ProjectileSpeed = speed };

    /// <summary>A launcher whose script names no speed flies at stock's constant, as the mod's launch does (live: the shipped rocket launcher's script has none).</summary>
    [Fact]
    public void ALauncherWhoseScriptNamesNoSpeedFliesAtStocks()
    {
        WeaponFacts f = TFKnowledge.WeaponOf(3, Row("tf_weapon_rocketlauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 4, 0f),
                                             TFProjectile.Rocket, Mode(0.92f, 0.8f), singly: true);
        Assert.Equal(TFProjectileDefs.RocketSpeed, f.ProjectileSpeed);
        Assert.Equal(0f, f.ProjectileGravity);
    }

    [Fact]
    public void ARocketLaunchersRocketFliesFlatAndItsReloadFillsTheClipShellByShell()
    {
        WeaponFacts f = TFKnowledge.WeaponOf(3, Row("tf_weapon_rocketlauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 4, 1100f),
                                             TFProjectile.Rocket, Mode(0.92f, 0.8f), singly: true);
        Assert.Equal((3, 4, 1100f, 0f, 0f, true), (f.Kind, f.ClipSize, f.ProjectileSpeed, f.ProjectileGravity, f.ChargeSeconds, f.ShellByShell));
        Assert.Equal(0.92f + 4 * 0.8f, f.ReloadSeconds, 4);
    }

    /// <summary>
    /// A pipe and a sticky fall at the whole of the world's gravity (<c>TFProjectileDefs.GrenadeFallScale</c>: a physics
    /// body, which no <c>SetGravity(0.4)</c> reaches; stock's flight measured at about 800 u/s², lane r2s4's A/B). Was 0.4 of
    /// it: a solver reading 320 alone would land short, and the base's lobs hit only because that error happened to cancel
    /// the launch's +200 up-kick, which the lob takes off the aim on its own (<c>AimLead.LobLaunch</c>).
    /// </summary>
    [Fact]
    public void APipeFallsAndAStickyChargesForFourSeconds()
    {
        WeaponFacts pipe = TFKnowledge.WeaponOf(1, Row("tf_weapon_grenadelauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 4, 1216f),
                                                TFProjectile.Pipebomb, Mode(1.24f, 0.6f), singly: true);
        WeaponFacts sticky = TFKnowledge.WeaponOf(2, Row("tf_weapon_pipebomblauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 8),
                                                  TFProjectile.PipebombRemote, Mode(1.09f, 0.67f, 925f), singly: true);
        Assert.Equal((G, 0f), (pipe.ProjectileGravity, pipe.ChargeSeconds));
        Assert.Equal((G, 4f, 925f), (sticky.ProjectileGravity, sticky.ChargeSeconds, sticky.ProjectileSpeed));
    }

    [Fact]
    public void ASniperRifleIsHitscanThatChargesAndABowAnArrowThatFallsLeastFullyDrawn()
    {
        WeaponFacts rifle = TFKnowledge.WeaponOf(5, Row("tf_weapon_sniperrifle", TFWeaponKinds.Key(TFWeaponKind.SniperRifle), -1),
                                                 TFProjectile.Bullet, Mode(0f, 0f), singly: false);
        WeaponFacts bow = TFKnowledge.WeaponOf(6, Row("tf_weapon_compound_bow", TFWeaponKinds.Key(TFWeaponKind.Bow), -1, 1800f),
                                               TFProjectile.Arrow, Mode(0f, 0f), singly: false);
        Assert.Equal((0f, 0f, 3f), (rifle.ProjectileSpeed, rifle.ProjectileGravity, rifle.ChargeSeconds));
        Assert.Equal(0.1f * G, bow.ProjectileGravity, 3);
        Assert.Equal(1f, bow.ChargeSeconds);
        Assert.False(rifle.HasClip);
    }

    private sealed class TFRows : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows()
            => [Row("tf_weapon_rocketlauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 4, 1100f), Row("tf_weapon_unscripted", "tf2.gun", 6),
                Row("weapon_hl2_row", "", 18)];
    }

    /// <summary>The table is the catalog's TF2 rows that have a TF2 script, at their catalog kind, reloading as the script's weapon does; a core row with a script is not TF2's.</summary>
    [Fact]
    public void TheWeaponTableIsTheCatalogsScriptedRowsAtTheirKinds()
    {
        var catalog = new WeaponCatalog(() => [new TFRows()]);
        int rocket = catalog.IndexOf("tf_weapon_rocketlauncher");
        var script = new TFWeaponScript { Base = new WeaponScript(), Id = TFWeaponId.Rocketlauncher, Primary = Mode(0.92f, 0.8f) with { Projectile = TFProjectile.Rocket } };
        WeaponKnowledge table = TFKnowledge.Weapons(catalog, name => name is "tf_weapon_rocketlauncher" or "weapon_hl2_row" ? script : null);
        WeaponFacts only = Assert.Single(table.All);
        Assert.Equal((rocket, "tf_weapon_rocketlauncher", true), (only.Kind, only.Name, only.ShellByShell));
        Assert.True(rocket >= 0 && catalog.IndexOf("tf_weapon_unscripted") >= 0 && catalog.IndexOf("weapon_hl2_row") >= 0);
    }

    [Fact]
    public void ABuildingsLevelsGainAFifthOfTheirHealthEach()
    {
        TFBuildingFacts sentry = TFKnowledge.Buildings().Single(b => b.Type == TFObjectType.Sentrygun);
        Assert.Equal((130, 200, 3), (sentry.Cost, sentry.UpgradeCost, sentry.MaxLevel));
        Assert.Equal([150, 180, 216], sentry.MaxHealthByLevel.ToArray());
        Assert.Equal(3, TFKnowledge.Buildings().Length);
    }

    [Fact]
    public void ASplashIsAimedAtTheFeet()
    {
        Assert.Equal(TFKnowledge.FeetAim, TFKnowledge.AimHeight(AimPoint.Feet, TFClass.Soldier).Standing);
        Assert.True(TFKnowledge.AimHeight(AimPoint.Body, TFClass.Soldier).Standing > 30f);
        Assert.Equal(68f, TFKnowledge.AimHeight(AimPoint.Head, TFClass.Soldier).Standing);
    }

    /// <summary>What a bot gets out of the way of (plan_bots_teams_tf2_part_g G.2.3): a rocket's blast and a grenade's are the mod's own radii, and what has no blast has none.</summary>
    [Fact]
    public void ARocketAndAGrenadeHaveTheModsBlastAndAFlareNone()
    {
        Assert.Equal((TFProjectileDefs.RocketRadius, TFProjectileDefs.GrenadeJumpRadius, TFProjectileDefs.GrenadeJumpRadius, 0f, 0f),
                     (TFKnowledge.BlastRadius(TFThingType.Rocket), TFKnowledge.BlastRadius(TFThingType.Pipe), TFKnowledge.BlastRadius(TFThingType.Sticky),
                      TFKnowledge.BlastRadius(TFThingType.Flare), TFKnowledge.BlastRadius(TFThingType.Sentry)));
    }

    [Fact]
    public void ASeenPipeFallsAsTheModsPipeDoesAndASeenRocketFliesFlat()
        => Assert.Equal((TFKnowledge.ProjectileGravity(TFProjectile.Pipebomb), 0f), (TFKnowledge.FallOf(TFThingType.Pipe), TFKnowledge.FallOf(TFThingType.Rocket)));

    [Fact]
    public void ASentrysRangeIsTheModsSentryGuns() => Assert.Equal(TFSentryGun.SentryMaxRange, TFKnowledge.SentryRange);

    /// <summary>A sticky leaves at the launcher's least charge's speed, 900 u/s (the Demoman research: it read 0, so no sticky was ever led).</summary>
    [Fact]
    public void AStickyLeavesAtTheLaunchersLeastChargesSpeed()
        => Assert.Equal(TFProjectileDefs.StickyMinChargeSpeed, TFKnowledge.StockSpeed(TFProjectile.PipebombRemote));

    [Fact]
    public void TheRocketJumpIsAProgramWithCommandsForItsApproachAndAFlight()
    {
        AbilityRow jump = TFAbilities.Table().Rows.Single(r => r.Name == TFAbilities.RocketJump);
        Assert.Equal((AbilityRun.Program, 0), (jump.Run, jump.StepCount));
        // The approach's own limit, and as many commands again as a search gives one flight.
        Assert.True(jump.HoldTicks >= SourceSharp.Ai.TF2.Jump.RocketJumpProgram.ApproachCommands + new SourceSharp.Ai.TF2.Jump.JumpSearchSettings().MaxCommands,
                    $"{jump.HoldTicks} commands");
        Assert.True(TFAbilities.Table().Rows.IndexOf(jump) <= AbilityCall.MaxRow);
    }

    [Fact]
    public void EveryBuildingCommandRowIsTheModsBuildOrDestroyOrder()
    {
        // The shared block's rows: a class's own block holds its own words (the Spy's disguise, sapper).
        AbilityRow[] commands = [.. TFAbilities.Shared().Where(r => r.Run == AbilityRun.Command && r.Name != TFClassSpecs.Unused
                                                                    && !r.Name.StartsWith("voice_", StringComparison.Ordinal))];
        Assert.Equal(8, commands.Length);
        Assert.All(commands, r => Assert.Contains(r.Command.Split(' ')[0], new[] { TFBuildCommand.Verb, TFDestroyCommand.Verb }));
        Assert.Contains(commands, r => r.Command == "build 2 0");
        Assert.Contains(commands, r => r.Command == "destroy 1 1");
    }

    /// <summary>The voice lines (plan_bots_teams_tf2_part_g G.2.4) are the mod's own voicemenu word with the menu and item stock's script puts each line at.</summary>
    [Theory]
    [InlineData(TFAbilities.VoiceMedic, "voicemenu 0 0")]
    [InlineData(TFAbilities.VoiceThanks, "voicemenu 0 1")]
    [InlineData(TFAbilities.VoiceSpy, "voicemenu 1 1")]
    [InlineData(TFAbilities.VoiceSentryAhead, "voicemenu 1 2")]
    public void AVoiceLineIsTheModsVoiceMenuWord(string row, string word)
    {
        AbilityRow line = TFAbilities.Table().Rows.Single(r => r.Name == row);
        Assert.Equal((AbilityRun.Command, word), (line.Run, line.Command));
        Assert.StartsWith(global::TF2.Server.Voice.TFVoiceMenuCommand.Verb + " ", line.Command, StringComparison.Ordinal);
    }

    /// <summary>The rows a decider found by index before the voice lines are where they were: the lines are added after them.</summary>
    [Fact]
    public void TheVoiceLinesComeAfterEveryOtherRow()
    {
        string[] names = [.. TFAbilities.Table().Rows.Select(r => r.Name)];
        Assert.Equal((TFAbilities.RocketJump, 11), (names[0], Array.FindIndex(names, n => n.StartsWith("voice_", StringComparison.Ordinal))));
    }

    /// <summary>The Loose Cannon's ball is the pipebomb's body in cannonball mode: it falls as a pipe does, and the lob aims for it.</summary>
    [Fact]
    public void TheLooseCannonsBallFallsAsAPipe()
    {
        Assert.True(TFKnowledge.ProjectileGravity(TFProjectile.Cannonball) > 0f);
        Assert.Equal(TFKnowledge.ProjectileGravity(TFProjectile.Pipebomb), TFKnowledge.ProjectileGravity(TFProjectile.Cannonball));
    }
}
