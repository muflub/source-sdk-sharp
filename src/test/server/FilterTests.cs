using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The filters, which are pure predicates and so can be asked directly.
///
/// Worth testing rather than only spawning: a filter that answers wrongly does
/// not crash or log, it just makes a trigger respond to the wrong people — and
/// negation, which every filter inherits, is exactly the kind of thing that
/// reads correct and inverts in practice.
/// </summary>
public class FilterTests
{
    private static readonly EntityHandle Ent = new(0x0021_0021u);
    private static readonly EntityHandle Subject = new(0x0022_0022u);

    private static (ScriptedEntityRegistry, T) Spawn<T>(string className) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FilterBase).Assembly);
        registry.Services = Ignoring("Entities.GetOrigin", "EntityPhysics.GetMass", "Entities.IsValid");
        registry.Spawn(Ent, className);
        return (registry, (T)registry.Get(Ent)!);
    }

    [Fact]
    public void Negation()
    {
        // Without an engine an entity has no name, so a name filter looking for
        // something specific finds nothing — which is the "does not match" case
        // and exactly what is needed to check negation both ways.
        var (registry, filter) = Spawn<FilterActivatorName>(EntityNames.FilterActivatorName);
        registry.KeyValue(Ent, EntityNames.FilterActivatorName, "filtername", "somebody");

        Assert.True(!filter.Passes(Subject), "a filter rejects what it does not match");

        var (registry2, negated) = Spawn<FilterActivatorName>(EntityNames.FilterActivatorName);
        registry2.KeyValue(Ent, EntityNames.FilterActivatorName, "filtername", "somebody");
        registry2.KeyValue(Ent, EntityNames.FilterActivatorName, "Negated", "1");

        Assert.True(negated.Passes(Subject), "negating inverts the answer");

        // Reported through outputs too, since a map can branch on the answer.
        registry2.Input(Ent, "TestActivator", "", 0f, Subject);
        Assert.True(negated.OnPass.FireCount == 1, "TestActivator reports passing");
        Assert.True(negated.OnFail.FireCount == 0, "and does not report failing");

        registry.Input(Ent, "TestActivator", "", 0f, Subject);
        Assert.True(filter.OnFail.FireCount == 1, "a failing filter reports the failure");
    }

    [Fact]
    public void DamageTypes()
    {
        var (registry, filter) = Spawn<FilterDamageType>(EntityNames.FilterDamageType);
        // The NUMBER, because that is what a map writes into the keyvalue and
        // what the entity parses. Cast rather than DamageType.Bullet.ToString():
        // once the constants became an enum, ToString started yielding "Bullet",
        // which parses as nothing and left the filter matching zero types.
        registry.KeyValue(Ent, EntityNames.FilterDamageType, "damagetype",
            ((int)DamageType.Bullet).ToString(CultureInfo.InvariantCulture));

        Assert.True(filter.PassesDamage(DamageType.Bullet, Subject), "a damage filter matches its own type");
        Assert.True(!filter.PassesDamage(DamageType.Blast, Subject), "and rejects another");

        // A bitmask, so damage of several types at once still matches.
        Assert.True(filter.PassesDamage(DamageType.Bullet | DamageType.Blast, Subject), "mixed damage still matches on one bit");

        // An entity is not a damage type; the entity question has no answer here
        // and says so rather than guessing.
        Assert.True(!filter.Passes(Subject), "an entity never passes a damage filter");
    }

    /// <summary>
    /// The damage question asked of a filter that is not about damage, which is
    /// what a native entity's `damagefilter` naming a filter_activator_name does.
    /// Stock answers it by testing whoever struck, and a map written against
    /// stock depends on that rather than on the damage bits.
    /// </summary>
    [Fact]
    public void DamageFromWhom()
    {
        var (registry, filter) = Spawn<FilterActivatorName>(EntityNames.FilterActivatorName);
        registry.KeyValue(Ent, EntityNames.FilterActivatorName, "filtername", "somebody");

        Assert.True(!filter.PassesDamage(DamageType.Bullet, Subject), "a damagefilter refuses damage from somebody it does not name");

        var (negatedRegistry, negated) = Spawn<FilterActivatorName>(EntityNames.FilterActivatorName);
        negatedRegistry.KeyValue(Ent, EntityNames.FilterActivatorName, "filtername", "somebody");
        negatedRegistry.KeyValue(Ent, EntityNames.FilterActivatorName, "Negated", "1");

        Assert.True(negated.PassesDamage(DamageType.Bullet, Subject), "and negated, admits it");

        // The damage bits are not consulted at all by this kind of filter, so
        // changing them must not change the answer.
        Assert.True(!filter.PassesDamage(DamageType.Blast, Subject), "the damage type does not enter into it");
    }

    [Fact]
    public void MultiCombining()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FilterBase).Assembly);
        registry.Services = Ignoring("Entities.GetOrigin", "EntityPhysics.GetMass", "Entities.IsValid");
        registry.Spawn(Ent, EntityNames.FilterMulti);

        var multi = (FilterMulti)registry.Get(Ent)!;

        // Naming nothing: there is no reason to reject anybody.
        Assert.True(multi.Passes(Subject), "a multi filter with no members passes everything");

        // Naming a filter that does not exist is a mapping mistake, and it must
        // not silently reject everything — one typo would disable the trigger.
        registry.KeyValue(Ent, EntityNames.FilterMulti, "Filter01", "no_such_filter");
        Assert.True(multi.Passes(Subject), "a named filter that is missing is skipped");

        // The damage question travels through the same walk, so a multi filter
        // with nothing to consult admits damage for the same reason it admits
        // entities.
        Assert.True(multi.PassesDamage(DamageType.Bullet, Subject), "a multi filter with no members admits damage");
    }

    [Fact]
    public void AlwaysPassing()
    {
        var (registry, filter) = Spawn<FilterAlways>(EntityNames.FilterBase);
        Assert.True(filter.Passes(Subject), "filter_base passes everybody");

        // Which is the only reason it is worth placing: negated it is a filter
        // that lets nobody through, and a trigger wired to one is switched off.
        registry.KeyValue(Ent, EntityNames.FilterBase, "Negated", "1");
        Assert.True(!filter.Passes(Subject), "and negated, nobody");
    }

    [Fact]
    public void MassThreshold()
    {
        var (registry, filter) = Spawn<FilterActivatorMassGreater>(EntityNames.FilterActivatorMassGreater);

        // Nothing here has a physics object, so nothing has a mass — and a
        // threshold below zero is what catches a filter that forgot to ask
        // whether there was a physics object at all before comparing.
        registry.KeyValue(Ent, EntityNames.FilterActivatorMassGreater, "filtermass", "-1");
        Assert.True(!filter.Passes(Subject), "an entity with no physics passes no mass filter");
    }

    [Fact]
    public void EnemyNaming()
    {
        var (registry, filter) = Spawn<FilterEnemy>(EntityNames.FilterEnemy);
        Assert.True(filter.Passes(Subject), "filter_enemy with no criteria passes everything");

        // The part of this filter that reads backwards and is stock's: it
        // constrains the one enemy it names and says nothing about the rest, so
        // an entity that is not that enemy goes straight through.
        registry.KeyValue(Ent, EntityNames.FilterEnemy, "filtername", "gordon");
        Assert.True(filter.Passes(Subject), "something that is not the named enemy passes untouched");

        // Negated, the same non-match is rejected — stock applies Negated inside
        // the name test and then inverts the result again, and this is the pair
        // of answers that pins that down.
        var (registry2, negated) = Spawn<FilterEnemy>(EntityNames.FilterEnemy);
        registry2.KeyValue(Ent, EntityNames.FilterEnemy, "filtername", "gordon");
        registry2.KeyValue(Ent, EntityNames.FilterEnemy, "Negated", "1");
        Assert.True(!negated.Passes(Subject), "negated, the same entity is rejected");

        // Distance is measured from the filter to the candidate; without an
        // engine both sit at the origin, which is inside any radius.
        var (registry3, near) = Spawn<FilterEnemy>(EntityNames.FilterEnemy);
        registry3.KeyValue(Ent, EntityNames.FilterEnemy, "filter_radius", "512");
        Assert.True(near.Passes(Subject), "a radius passes what is inside it");
    }
}
