using System.Globalization;

using SourceSharp.Abi;

using TF2.Descent.Rules;
using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Skills;

namespace TF2.Descent.Server.Lamp;

/// <summary>
/// HOW FAR A HERO CAN BE SEEN, as a multiplier on a robot's sight range for that hero
/// (plan_descent_logic HE10; plan_tf2_rpg: "cloak, disguise and darkness are multipliers on
/// range"; HO5: "nothing applies a multiplier yet"). 1 is a robot's own range; above 1 the hero is
/// seen from farther (his lamp lit in a dark room), below 1 from nearer (his lamp out in the dark,
/// a cloak). The horde's sight is the reader: the session folds it into <c>HordeVision</c>'s
/// per-pair range each tick (<c>HeroPose.SightScale</c>).
/// </summary>
public interface IHeroVisibility
{
    /// <summary>The multiplier on a robot's sight range for <paramref name="hero"/> (a player); 1 is neutral.</summary>
    double SightScale(EntityHandle hero);

    /// <summary>The multiplier and where it came from, as one phrase (the dev print's).</summary>
    string Describe(EntityHandle hero);

    /// <summary>How far <paramref name="hero"/>'s lit lamp shows him however dark it is: its radius, in units; 0 when it is out or he carries none.</summary>
    double LampReach(EntityHandle hero);

    /// <summary>
    /// Whether <paramref name="hero"/>'s lit lamp is a beacon: a toggle of his carrying
    /// <c>lamp_beacon</c> is on (Lamplighter, the user's ruling L26), so robots see that lamp from
    /// farther (horde.json <c>sight.beaconReachScale</c>). False while his lamp is out.
    /// </summary>
    bool Beacon(EntityHandle hero);
}

/// <summary>
/// <c>tf2_descent_dark &lt;0..1&gt;</c>: the darkness the horde's sight reads in place of the
/// depth's from lamp.json (cheats; -1, the default, is the depth's).
/// </summary>
[ConVar("tf2_descent_dark", "-1", Min = -1, Max = 1, Flags = ConVarFlags.Cheat,
        Help = "tf2_descent_dark <0..1>: the darkness robots' sight reads (0 lit, 1 black) in place of the depth's from lamp.json; -1 the depth's")]
public sealed class DescentDarkConVar : FloatConVar { }

/// <summary>
/// THE LAMP AND THE DARK IN ROBOT SIGHT (plan_descent_logic HE10; mine plan M9 and section 6;
/// plan_tf2_rpg B.3): a hero's multiplier is <see cref="LampRules.SightScale"/> of the darkness
/// where he stands and whether his lamp is lit (<see cref="HeroLamps.Lit"/>). The level carries
/// no light the server can read (<c>ILevelData</c> has rooms, doors and markers, no light level),
/// so the darkness is the depth's, from lamp.json's table (lit to depth 10: the lamp changes
/// nothing there), or <see cref="DescentDarkConVar"/>'s when it is set. A player with no hero
/// component carries no lamp: his lamp counts as out.
///
/// <para><b>The lamp's range and brightness do not enter the multiplier</b> (the lampstat lane's
/// choice, 2026-11-13): a ceiling lamp's <see cref="SightScale"/> is a bare one's, and a lamp out
/// is seen as lamp.json's <see cref="LampSight.LampOff"/> whatever its stats. <b>Its radius is
/// its reach</b> (<see cref="LampReach"/>; the aggro lane's): however dark his room, a robot sees
/// a lit lamp within that radius (horde.json <c>sight.lampReachScale</c>), up to its own range.
/// <b>Lamplighter makes it a beacon</b> (the user's ruling L26, 2026-11-22): while a toggle of his
/// carrying <see cref="BeaconCondition"/> is on, his lit lamp's radius has Lamplighter's bonus and
/// the horde's sight reads it times <c>sight.beaconReachScale</c> (<see cref="Beacon"/>, carried on
/// his pose so a replay reads what the game read).</para>
/// </summary>
[SideService(Side.Server, As = [typeof(IHeroVisibility)])]
public sealed class HeroVisibility(HeroLamps lamps, Heroes heroes, ILampWorld world, TF2DescentRules rules, DescentDarkConVar dark, SkillCasters casters)
    : IHeroVisibility
{
    private LampSettings Lamp => rules.Content.Lamp;

    /// <summary>Whether the darkness is the convar's (a dev override) rather than the depth's.</summary>
    public bool Overridden => dark.Value >= 0f;

    /// <summary>The darkness the sight reads now: the convar's when set, else the depth's.</summary>
    public double Darkness => Overridden ? Math.Clamp(dark.Value, 0f, 1f) : LampRules.Darkness(Lamp, world.Depth);

    /// <summary>Whether <paramref name="hero"/>'s lamp is lit.</summary>
    public bool Lit(EntityHandle hero) => heroes.Of(hero.EntryIndex) is { } h && lamps.Lit(h);

    public double SightScale(EntityHandle hero) => LampRules.SightScale(Lamp, Darkness, Lit(hero));

    public double LampReach(EntityHandle hero) => heroes.Of(hero.EntryIndex) is { } h && lamps.Lit(h) ? lamps.RadiusOf(h) : 0;

    /// <summary>The condition word a toggle carries to make its hero's lit lamp a beacon to robots (Lamplighter's).</summary>
    public const string BeaconCondition = "lamp_beacon";

    public bool Beacon(EntityHandle hero) =>
        heroes.Of(hero.EntryIndex) is { Sheet: { } sheet } h && lamps.Lit(h) && casters.Of(h.PlayerIndex) is { } caster
        && rules.Skills.ToggledOn(sheet, caster.State.Toggles, BeaconCondition);

    public string Describe(EntityHandle hero) =>
        string.Create(CultureInfo.InvariantCulture,
            $"scale {SightScale(hero):0.00}: darkness {Darkness:0.00} ({(Overridden ? "tf2_descent_dark" : $"depth {world.Depth}, lamp.json")}), lamp {(Lit(hero) ? $"lit (x{Lamp.Sight.LampOn:0.00} in the black)" : $"out (x{Lamp.Sight.LampOff:0.00} in the black)")}" +
            $"{(heroes.Of(hero.EntryIndex) is { } h ? $", its reach {LampReach(hero):0} (radius {lamps.RadiusOf(h):0}; brightness x{lamps.BrightnessOf(h):0.##} not counted), lamplighter {(Beacon(hero) ? "on: a beacon" : "off")}" : "")}");
}
