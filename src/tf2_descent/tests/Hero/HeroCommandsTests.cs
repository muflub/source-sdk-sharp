using SourceSharp.Shared;

using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The hero's dev commands (cheats only) and the rules' content line
/// (plan_descent_logic HE2, X5).
/// </summary>
public sealed class HeroCommandsTests
{
    private static SvCheatsConVar Cheats(bool on) => DeclaredConVars.Holding<SvCheatsConVar>(on ? "1" : "0");

    private static bool Logged(HeroRig rig, string text) =>
        rig.Log.Lines.Any(l => l.Text.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Setlevel_raises_the_hero_by_the_rules_and_leaves_the_audit_true()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;

        new HeroSetLevelCommand(Cheats(true), rig.Heroes, rig.Log).Execute(HeroRig.Index, 5);

        Assert.Equal(5, hero.Sheet!.Level);
        Assert.Equal(rig.Rules.Heroes.XpAtLevel(5), hero.Sheet.Xp);
        Assert.Equal(20, hero.Sheet.AttributePoints);
        Assert.Equal(4, hero.Sheet.SkillPoints);
        Assert.True(rig.Rules.Heroes.Audit(hero.Sheet).Ok);
        Assert.Equal(5, hero.State!.Level);
        Assert.True(Logged(rig, "set to level 5: applied, audit ok"));
    }

    private sealed class Picks : TF2.Descent.Server.Mode.IHeroClassPick
    {
        public List<TF2.Shared.TFClass> Made { get; } = [];

        public void Pick(SourceSharp.Abi.EntityHandle player, TF2.Shared.TFClass cls) => Made.Add(cls);
    }

    [Fact]
    public void Setclass_makes_the_hero_a_heavy_at_his_level_with_the_heavys_life()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        Assert.True(hero.Apply(s => rig.AtLevel(s, 10)).Ok);
        var picks = new Picks();

        new HeroSetClassCommand(Cheats(true), rig.Heroes, picks, rig.Log).Execute(HeroRig.Index, "heavy");

        Assert.Equal(("heavy", 10), (hero.Sheet!.ClassId, hero.Sheet.Level));
        Assert.True(rig.Rules.Heroes.Audit(hero.Sheet).Ok);
        Assert.Equal(300 + 4 * 20 + 32 * 10, hero.Stats!.MaxLife, 9);   // plan L26: 32 a level
        Assert.Equal([TF2.Shared.TFClass.HeavyWeapons], picks.Made);
        Assert.True(Logged(rig, "set to class heavy level 10: applied, life max 700"));
    }

    [Fact]
    public void Setclass_refuses_a_name_that_is_no_class()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        var picks = new Picks();

        new HeroSetClassCommand(Cheats(true), rig.Heroes, picks, rig.Log).Execute(HeroRig.Index, "civilian");

        Assert.Equal("soldier", hero.Sheet!.ClassId);
        Assert.Empty(picks.Made);
        Assert.True(Logged(rig, "'civilian' is no class"));
    }

    [Fact]
    public void Setclass_does_nothing_without_cheats()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        var picks = new Picks();

        new HeroSetClassCommand(Cheats(false), rig.Heroes, picks, rig.Log).Execute(HeroRig.Index, "scout");

        Assert.Equal("soldier", hero.Sheet!.ClassId);
        Assert.Empty(picks.Made);
    }

    [Fact]
    public void Setlevel_refuses_a_level_the_hero_has_passed()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        Assert.True(hero.Apply(s => rig.AtLevel(s, 6)).Ok);

        new HeroSetLevelCommand(Cheats(true), rig.Heroes, rig.Log).Execute(HeroRig.Index, 4);

        Assert.Equal(6, hero.Sheet!.Level);
    }

    [Fact]
    public void A_hero_command_does_nothing_without_cheats()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;

        new HeroSetLevelCommand(Cheats(false), rig.Heroes, rig.Log).Execute(HeroRig.Index, 5);
        new HeroManaCommand(Cheats(false), rig.Heroes, rig.Log).Execute(HeroRig.Index, 0);

        Assert.Equal(1, hero.Sheet!.Level);
        Assert.Equal(hero.Stats!.MaxMana, hero.Mana);
        Assert.True(Logged(rig, "runs only under sv_cheats"));
    }

    [Fact]
    public void Mana_sets_the_heros_mana_and_the_wire()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;

        new HeroManaCommand(Cheats(true), rig.Heroes, rig.Log).Execute(HeroRig.Index, 12.5f);

        Assert.Equal(12.5, hero.Mana);
        Assert.Equal(12, hero.State!.Mana);
    }

    [Fact]
    public void Refill_fills_life_and_mana()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        hero.SetMana(0);
        rig.Engine.Returns("Entities.GetHealth", 10);

        new HeroRefillCommand(Cheats(true), rig.Heroes, rig.Log).Execute(HeroRig.Index);

        Assert.Equal(hero.Stats!.MaxMana, hero.Mana);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.SetHealth" && c.ArgAs<int>(1) == rig.Health.Max);
    }

    [Fact]
    public void Sheet_prints_the_class_level_and_mana()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        Assert.True(hero.State!.Mana > 0);

        new HeroSheetCommand(Cheats(true), rig.Heroes, rig.Log).Execute(HeroRig.Index);

        Assert.True(Logged(rig, $"[TF2Descent/Hero] {HeroRig.Index} hero-{HeroRig.SteamId}: soldier level 1 xp 0"),
                    string.Join("", rig.Log.Lines.Select(l => l.Text)));
        Assert.True(Logged(rig, $"net mana {hero.State.Mana} level 1"));
    }

    [Fact]
    public void Sheet_prints_the_bestiarys_kills()
    {
        var rig = new HeroRig();
        var hero = rig.Join()!;
        Assert.True(hero.Apply(s => TF2.Descent.Rules.Bestiary.BestiaryRules.RecordKill(
            TF2.Descent.Rules.Bestiary.BestiaryRules.RecordKill(s, "runt_scout"), "runt_scout")).Ok);

        new HeroSheetCommand(Cheats(true), rig.Heroes, rig.Log).Execute(HeroRig.Index);

        Assert.True(Logged(rig, "| bestiary runt_scout 2\n"), string.Join("", rig.Log.Lines.Select(l => l.Text)));
    }

    [Fact]
    public void A_slot_with_no_hero_is_named()
    {
        var rig = new HeroRig();

        new HeroSheetCommand(Cheats(true), rig.Heroes, rig.Log).Execute(9);

        Assert.True(Logged(rig, "player 9 is no hero"));
    }

    [Fact]
    public void The_server_logs_the_rules_content_hash_at_start()
    {
        var rig = new HeroRig();
        var line = new RulesContentLine(rig.Rules, rig.Log);

        line.OnPostInit();

        Assert.Matches("^[0-9a-f]{64}$", rig.Rules.ContentHash);
        Assert.Contains(rig.Log.Lines, l => l.Text == $"[TF2Descent] rules content {rig.Rules.ContentHash}\n");
    }
}
