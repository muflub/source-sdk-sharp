using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Rules;

namespace TF2.Descent.Ai.Boss;

/// <summary>
/// The bosses' deciders (plan_descent_logic HO13), registered over <see cref="HordeDeciders"/>
/// after the families (<see cref="HordeFamilies.Register"/>): each boss's own attack row's family
/// (<see cref="HordeWeapons.NukeCannon"/>, <see cref="HordeWeapons.CritRockets"/>,
/// <see cref="HordeWeapons.BossFists"/>) runs a <see cref="BossDecider"/> over its script and its
/// weapon family's decider; the Tank's (<see cref="HordeWeapons.TankTrack"/>) a
/// <see cref="TankDecider"/>. The game's session and a replay register them here, so both pick
/// the same.
/// </summary>
public static class BossFamilies
{
    public static void Register(HordeDeciders deciders, TF2DescentRules rules, FamilyNumbers numbers)
    {
        ArgumentNullException.ThrowIfNull(deciders);
        ArgumentNullException.ThrowIfNull(rules);
        var settings = rules.Bosses.Settings;
        float margin = (float)settings.ArenaMargin;
        foreach (var script in settings.Bosses)
        {
            var template = rules.Robots.Template(script.Id);
            int family = HordeWeapons.Of(template.Attack);
            int fights = HordeWeapons.Of(template.Family);
            if (family == HordeWeapons.None)
                throw new InvalidOperationException($"boss '{script.Id}' fights with '{template.Attack}', which has no family number");
            deciders.Register(family, (setup, routes) =>
                new BossDecider(setup, routes, script, rules.Bosses, deciders.ForFamily(fights, setup), margin));
        }

        var tank = settings.Tank;
        int track = HordeWeapons.Of(rules.Robots.Template(tank.Template).Attack);
        deciders.Register(track, (setup, routes) => new TankDecider(setup, routes, numbers, tank, margin));
    }
}
