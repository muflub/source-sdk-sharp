using TF2.Descent.Rules.World;
using TF2.Descent.Server.Skills;

using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// Lamplighter is a toggle (the user's ruling L26, 2026-11-22: "a new passive skill that can turn
/// on/off and robots notice it when on"): learned and off, the lamp is lamp.json's; switched on from
/// its bar slot, its rank's radius and brightness bonuses hold; switched off, they go. It drains no
/// mana and costs none to switch on.
/// </summary>
public sealed class LamplighterToggleTests
{
    private const int Rank = 5;

    private static SkillRig Lamplighter(int rank = Rank)
    {
        var rig = new SkillRig();
        rig.Join(level: 3 * rank);
        rig.Learn("lamplighter", rank);
        rig.Slot(1, "lamplighter");
        rig.Run(1);
        return rig;
    }

    private static LampSettings Lamp(SkillRig rig) => rig.Heroes.Rules.Content.Lamp;

    [Fact]
    public void Learned_and_off_it_leaves_the_lamp_as_lamp_json_has_it()
    {
        var rig = Lamplighter();

        Assert.Equal(Lamp(rig).Light.Radius, rig.Hero().Stats!.LampRadius, 6);
        Assert.Equal(1, rig.Hero().Stats!.LampBrightness, 6);
    }

    [Fact]
    public void Switched_on_its_ranks_radius_and_brightness_bonuses_hold()
    {
        var rig = Lamplighter();

        Assert.Equal(SkillUseKind.Cast, rig.Runtime.Press(SkillRig.Index, 1).Kind);
        rig.Run(1);

        Assert.Equal(Lamp(rig).Light.Radius * (1 + 0.15 * Rank), rig.Hero().Stats!.LampRadius, 6);
        Assert.Equal(1 + 0.15 * Rank, rig.Hero().Stats!.LampBrightness, 6);
    }

    [Fact]
    public void Switched_off_again_the_bonuses_go()
    {
        var rig = Lamplighter();
        rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);

        Assert.Equal(SkillUseKind.ToggledOff, rig.Runtime.Press(SkillRig.Index, 1).Kind);
        rig.Run(1);

        Assert.Equal(Lamp(rig).Light.Radius, rig.Hero().Stats!.LampRadius, 6);
        Assert.Equal(1, rig.Hero().Stats!.LampBrightness, 6);
    }

    [Fact]
    public void Its_lamp_beacon_holds_only_while_it_is_on()
    {
        var rig = Lamplighter();
        bool Beacon() => rig.Skills.ToggledOn(rig.Hero().Sheet!, rig.Casters.Of(SkillRig.Index)!.State.Toggles, "lamp_beacon");

        bool before = Beacon();
        rig.Runtime.Press(SkillRig.Index, 1);
        bool on = Beacon();
        rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal((false, true, false), (before, on, Beacon()));
    }

    [Fact]
    public void Another_toggle_on_is_no_lamp_beacon()
    {
        var rig = new SkillRig();
        rig.Join(level: 18);
        rig.Learn("jump_training", 3);
        rig.Learn("parachute");
        rig.Slot(1, "parachute");

        rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Contains("parachute", rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        Assert.False(rig.Skills.ToggledOn(rig.Hero().Sheet!, rig.Casters.Of(SkillRig.Index)!.State.Toggles, "lamp_beacon"));
    }

    /// <summary>A level change as the core makes it: every player's components detach at the level's end and attach again on the next.</summary>
    private static void ChangeLevel(SkillRig rig)
    {
        rig.Runtime.Detach(SkillRig.Index);
        rig.Runtime.Attach(SkillRig.Index, SkillRig.PlayerOf());
        rig.Run(1);
    }

    [Fact]
    public void Switched_on_it_is_still_on_after_a_level_change()
    {
        var rig = Lamplighter();
        rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);
        var before = rig.Casters.Of(SkillRig.Index);

        ChangeLevel(rig);

        Assert.NotSame(before, rig.Casters.Of(SkillRig.Index));   // a fresh caster, as on a new level
        Assert.Contains("lamplighter", rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        Assert.Equal(Lamp(rig).Light.Radius * (1 + 0.15 * Rank), rig.Hero().Stats!.LampRadius, 6);
    }

    [Fact]
    public void Switched_off_it_is_still_off_after_a_level_change()
    {
        var rig = Lamplighter();
        rig.Runtime.Press(SkillRig.Index, 1);
        rig.Runtime.Press(SkillRig.Index, 1);

        ChangeLevel(rig);

        Assert.Empty(rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        Assert.Equal(Lamp(rig).Light.Radius, rig.Hero().Stats!.LampRadius, 6);
    }

    [Fact]
    public void A_player_who_leaves_the_server_takes_none_of_his_toggles_to_the_next_in_his_slot()
    {
        var rig = Lamplighter();
        rig.Runtime.Press(SkillRig.Index, 1);
        rig.Runtime.Detach(SkillRig.Index);
        var kept = rig.Casters.KeptToggles(SkillRig.Index).ToList();

        rig.Casters.OnClientDisconnected(SkillRig.Index);
        rig.Runtime.Attach(SkillRig.Index, SkillRig.PlayerOf());

        Assert.Equal(["lamplighter"], kept);   // kept while he was between levels
        Assert.Empty(rig.Casters.Of(SkillRig.Index)!.State.Toggles);
    }

    [Fact]
    public void It_switches_on_with_no_mana_and_drains_none_while_on()
    {
        var rig = Lamplighter();
        rig.Hero().SetMana(0);

        Assert.Equal(SkillUseKind.Cast, rig.Runtime.Press(SkillRig.Index, 1).Kind);
        rig.RunSeconds(10);

        Assert.Contains("lamplighter", rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        Assert.Equal(Lamp(rig).Light.Radius * (1 + 0.15 * Rank), rig.Hero().Stats!.LampRadius, 6);
    }
}
