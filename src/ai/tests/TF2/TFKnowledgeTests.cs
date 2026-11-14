using SourceSharp.Ai.TF2.Knowledge;
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

    [Fact]
    public void APipeFallsAndAStickyChargesForFourSeconds()
    {
        WeaponFacts pipe = TFKnowledge.WeaponOf(1, Row("tf_weapon_grenadelauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 4, 1216f),
                                                TFProjectile.Pipebomb, Mode(1.24f, 0.6f), singly: true);
        WeaponFacts sticky = TFKnowledge.WeaponOf(2, Row("tf_weapon_pipebomblauncher", TFWeaponKinds.Key(TFWeaponKind.Projectile), 8),
                                                  TFProjectile.PipebombRemote, Mode(1.09f, 0.67f, 925f), singly: true);
        Assert.Equal((0.4f * G, 0f), (pipe.ProjectileGravity, pipe.ChargeSeconds));
        Assert.Equal((0.4f * G, 4f, 925f), (sticky.ProjectileGravity, sticky.ChargeSeconds, sticky.ProjectileSpeed));
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

    [Fact]
    public void TheRocketJumpLooksDownThenJumpsDuckedAndFiresAtTheGround()
    {
        AbilityRow jump = TFAbilities.Table().Rows.Single(r => r.Name == TFAbilities.RocketJump);
        Assert.Equal(AbilityRun.Sequence, jump.Run);
        const int Jump = (int)SourceSharp.Abi.InputButtons.Jump, Duck = (int)SourceSharp.Abi.InputButtons.Duck, Fire = (int)SourceSharp.Abi.InputButtons.Attack;
        Assert.Equal([0, Jump | Duck, Jump | Duck | Fire, Duck], jump.Steps.Select(s => s.Buttons));
        Assert.All(jump.Steps.Take(3), s => Assert.Equal(TFAbilities.RocketJumpPitch, s.Pitch));
    }

    [Fact]
    public void EveryCommandRowIsTheModsBuildOrDestroyOrder()
    {
        AbilityRow[] commands = [.. TFAbilities.Table().Rows.Where(r => r.Run == AbilityRun.Command)];
        Assert.Equal(8, commands.Length);
        Assert.All(commands, r => Assert.Contains(r.Command.Split(' ')[0], new[] { TFBuildCommand.Verb, TFDestroyCommand.Verb }));
        Assert.Contains(commands, r => r.Command == "build 2 0");
        Assert.Contains(commands, r => r.Command == "destroy 1 1");
    }
}
