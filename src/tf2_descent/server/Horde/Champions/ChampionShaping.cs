using SourceSharp.Abi;

using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Server.Horde.Champions;

/// <summary>
/// THE SEAM WHERE A CHAMPION SHAPES ITS ATTACK (HO12): the horde's hands ask it, for each attack,
/// how many shots and how wide a fan, whether the attack is a crit, and how long a hero it hits
/// burns. A plain robot answers one shot, no crit, no burn.
/// </summary>
public interface IRobotAttackShaping
{
    /// <summary>The shots one attack of roster row <paramref name="row"/> fires, and the degrees between two of them.</summary>
    int Shots(int row, out float fanDegrees);

    /// <summary>Whether <paramref name="body"/>'s attack is a crit now: a crit aura (its own, or a lent pack-mate's in reach).</summary>
    bool Crit(HordeController horde, RobotBody body);

    /// <summary>The seconds a hero row <paramref name="row"/>'s attack hits burns for; 0 for none.</summary>
    float BurnSeconds(int row);
}

/// <summary>
/// The champions that change an attack (plan_tf2_rpg 2f; HO12), from the roster's affixes and
/// champions.json's numbers: <c>multishot</c> fires the row's shots fanned the row's degrees apart
/// (a swing stays one); <c>crit_aura</c> makes every attack of the champion and of its lent
/// pack-mates within the row's radius a crit (TF2's crit: its damage layer triples it, and the hero
/// sees it said); <c>fire_enchanted</c> sets every hero its attacks hit alight for the row's
/// seconds. The stats these affixes carry (multishot's -20 % damage, fire enchanted's +15 % and its
/// fire resistance) are the rules' and already in the robot's stats.
/// </summary>
[SideService(Side.Server, As = [typeof(IRobotAttackShaping)])]
public sealed class ChampionShaping(IHordeRules rules, IHordeCurrent horde) : IRobotAttackShaping
{
    public int Shots(int row, out float fanDegrees)
    {
        fanDegrees = 0f;
        if (Affix(row, ChampionBehaviours.Multishot) is not { } a)
            return 1;
        fanDegrees = (float)a.Param("spreadDegrees");
        return (int)a.Param("shots");
    }

    public bool Crit(HordeController controller, RobotBody body)
    {
        if (controller is not { Roster: { } roster, Bodies: { } bodies } || body.Parked || !roster.IsLive(body.Robot))
            return false;
        int pack = roster.Pack(body.Robot.Index);
        for (int b = 1; b <= bodies.LastBody; b++)
        {
            var other = bodies.Body(b);
            if (other.Parked || !roster.IsLive(other.Robot) || roster.Pack(other.Robot.Index) != pack)
                continue;
            if (AffixOf(roster, other.Robot.Index, ChampionBehaviours.CritAura) is { } aura
                && other.Position.DistanceTo(body.Position) <= aura.Param("radius"))
                return true;
        }

        return false;
    }

    public float BurnSeconds(int row) => Affix(row, ChampionBehaviours.FireEnchanted) is { } a ? (float)a.Param("burnSeconds") : 0f;

    private ChampionAffix? Affix(int row, string behaviour) =>
        horde.Current is { Roster: { } roster } && (uint)row < (uint)roster.Count ? AffixOf(roster, row, behaviour) : null;

    private ChampionAffix? AffixOf(TF2.Descent.Ai.Roster.RobotRoster roster, int row, string behaviour)
    {
        var affixes = roster.Affixes(row);
        if (affixes.IsDefaultOrEmpty)
            return null;
        foreach (var id in affixes)
            if (rules.Rules.Content.Champions.TryGet(id, out var a) && a.Behaviour == behaviour)
                return a;
        return null;
    }
}
