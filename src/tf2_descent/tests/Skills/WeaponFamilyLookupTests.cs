using TF2.Descent.Server.Hero;
using TF2.Shared;
using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The server's two readers of a weapon's family, the hit path (<see cref="HeroHitKinds"/>)
/// and the skills runtime (family-scoped upgrades), answer from the rules' one lookup
/// (<c>DescentContent.TryGetFamilyOfItemClass</c>), and a family's kinds from families.json.
/// </summary>
public sealed class WeaponFamilyLookupTests
{
    [Fact]
    public void The_hit_path_and_the_skills_runtime_name_the_same_family_for_every_TF_weapon_entity()
    {
        var rig = new SkillRig();
        var hits = new HeroHitKinds(rig.Heroes.Rules);
        var names = Enum.GetValues<TFWeaponId>().Where(id => id != TFWeaponId.None && id != TFWeaponId.Count)
            .Select(id => TFWeaponNames.Name(id).ToLowerInvariant()).ToList();

        Assert.Contains(names, n => hits.FamilyOf(n) is not null);   // the stimulus: some resolve
        Assert.All(names, n => Assert.Equal(hits.FamilyOf(n)?.Id, rig.Runtime.FamilyOf(n)));
    }

    [Fact]
    public void A_hits_kinds_are_its_familys_kinds_in_the_content()
    {
        var rules = new SkillRig().Heroes.Rules;

        Assert.All(rules.Content.Families.Rows, f => Assert.Equal(f.Kinds, HeroHitKinds.KindsOf(f)));
    }
}
