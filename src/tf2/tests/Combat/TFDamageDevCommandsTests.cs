using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary><see cref="TFDamageDevCommands"/>: the live stimuli, behind sv_cheats.</summary>
public sealed class TFDamageDevCommandsTests
{
    private sealed class CritSpy(DamageHitGroups hits) : IEngineDamage
    {
        public List<(EntityHandle Victim, EntityHandle Attacker, float Damage, DamageType Type, bool Critical)> Hits { get; } = new();
        public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType) =>
            Hits.Add((victim, attacker, damage, damageType, hits.Critical));
        public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position) { }
        public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
        public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;
        public void RegisterRules(DamageRuleMasks masks) { }
        public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                               Vector forceOrigin = default, bool physicsOnly = false) { }
        // This spy's subject is Apply; the answered ask is not asked here, and
        // answers like the no-op beside it.
        public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                                      Vector forceOrigin = default, bool physicsOnly = false) => 0;
    }

    private static (TFDamageDevCommands Commands, CritSpy Damage, CombatRig Rig, TFAfterburn Slice) Rig(string cheats)
    {
        var rig = new CombatRig();
        var hits = new DamageHitGroups();
        var spy = new CritSpy(hits);
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        return (new TFDamageDevCommands(spy, hits, rig.Afterburns, rig.Clock, DeclaredConVars.Holding<SourceSharp.Shared.SvCheatsConVar>(cheats), rig.Log), spy, rig, slice);
    }

    [Fact]
    public void Without_cheats_neither_command_does_anything()
    {
        var (commands, damage, rig, _) = Rig("0");
        Assert.True(commands.Handle(CombatRig.Victim, TFDamageDevCommands.Burn, []));
        Assert.True(commands.Handle(CombatRig.Victim, TFDamageDevCommands.Hurt, ["50", "crit"]));
        Assert.False(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Empty(damage.Hits);
    }

    [Fact]
    public void Burn_sets_the_player_alight_for_the_given_seconds()
    {
        var (commands, _, rig, slice) = Rig("1");
        Assert.True(commands.Handle(CombatRig.Victim, TFDamageDevCommands.Burn, ["4"]));
        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Equal(3f + 4f, slice.Burn.AfterburnDuration, 4);   // the flamethrower's initial 3 s, then the 4 asked
    }

    [Fact]
    public void Hurt_deals_a_ranged_bullet_from_the_world_critical_when_asked()
    {
        var (commands, damage, _, _) = Rig("1");
        commands.Handle(CombatRig.Victim, TFDamageDevCommands.Hurt, ["50", "crit"]);
        commands.Handle(CombatRig.Victim, TFDamageDevCommands.Hurt, ["20"]);

        Assert.Equal([(CombatRig.Victim, EntityHandle.None, 50f, DamageType.Bullet | TFDamageBits.UseDistanceMod, true),
                      (CombatRig.Victim, EntityHandle.None, 20f, DamageType.Bullet | TFDamageBits.UseDistanceMod, false)],
                     damage.Hits);
    }

    [Fact]
    public void Other_commands_pass_through()
    {
        var (commands, _, _, _) = Rig("1");
        Assert.False(commands.Handle(CombatRig.Victim, "joinclass", ["soldier"]));
    }
}
