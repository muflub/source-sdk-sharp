using TF2.Descent.Rules;
using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// A skill's hit is resisted as the damage type the skill declares (R25), whatever bits
/// its projectile carries: <c>SkillRules.DamageTypeOf</c>, for the horde's hit path.
/// </summary>
public sealed class SkillDamageTypeTests
{
    private static readonly TF2DescentRules Rules = new();

    [Theory]
    [InlineData("emp_charge", "shock")]
    [InlineData("patriot_missile", "blast")]
    [InlineData("rocket_leap", "blast")]
    public void A_skills_damage_type_is_the_one_it_declares(string skillId, string type)
    {
        Assert.Equal(type, Rules.Skills.DamageTypeOf(skillId));
    }

    [Fact]
    public void An_upgrade_and_an_unknown_id_declare_no_damage_type()
    {
        Assert.Null(Rules.Skills.DamageTypeOf("marksmanship"));
        Assert.Null(Rules.Skills.DamageTypeOf("no_such_skill"));
    }

    [Fact]
    public void Every_skill_that_declares_a_type_answers_one_the_damage_types_table_has()
    {
        var declared = Rules.Content.Skills.Rows.Where(s => s.DamageType is not null).ToList();

        Assert.NotEmpty(declared);
        Assert.All(declared, s =>
        {
            Assert.Equal(s.DamageType, Rules.Skills.DamageTypeOf(s.Id));
            Assert.True(Rules.Content.DamageTypes.Contains(Rules.Skills.DamageTypeOf(s.Id)!), s.Id);
        });
    }
}
