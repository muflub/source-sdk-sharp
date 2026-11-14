using SourceSharp.Abi;

namespace TF2.Descent.Server.Lamp;

/// <summary>
/// HOW FAR A HERO CAN BE SEEN, as a multiplier on a robot's sight range for that hero
/// (plan_descent_logic HE10; plan_tf2_rpg: "cloak, disguise and darkness are multipliers on
/// range"; HO5: "nothing applies a multiplier yet"). 1 is a robot's own range; above 1 the hero is
/// seen from farther (his lamp lit in a dark room), below 1 from nearer (his lamp out in the dark,
/// a cloak). The horde's sight is the reader: <c>HordeVision</c>'s per-pair <c>BodyEye.Range</c>
/// times this, folded in by the horde lane that owns the vision code.
/// </summary>
public interface IHeroVisibility
{
    /// <summary>The multiplier on a robot's sight range for <paramref name="hero"/> (a player); 1 is neutral.</summary>
    double SightScale(EntityHandle hero);
}

/// <summary>
/// The stand-in: every hero is seen at a robot's own range, lamp or no lamp. The RPG plan's
/// open-question 11 kept darkness a mood, not a stat, and no level is dark yet (the mine plan's
/// levels lean on the lamp from level 11), so nothing scales sight. A later lane replaces this
/// with one that reads the lamp (<see cref="HeroLamps.Lit"/>) and the light where the hero stands.
/// </summary>
[SideService(Side.Server, As = [typeof(IHeroVisibility)])]
public sealed class NeutralHeroVisibility : IHeroVisibility
{
    /// <summary>A robot's own range.</summary>
    public const double Neutral = 1.0;

    public double SightScale(EntityHandle hero) => Neutral;
}
