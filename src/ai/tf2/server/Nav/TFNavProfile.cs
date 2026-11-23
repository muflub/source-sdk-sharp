using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// TF2's navigation profile (<see cref="INavProfile"/>, plan_bots_teams_tf2 P6 step 2, C.7):
/// <list type="bullet">
/// <item>One hull for every class (<c>TFViewVectors.Params</c>): 48 wide, 82 standing, 62
/// crouched; the step 18; gravity 800.</item>
/// <item>The ground jump leaves at 289 u/s (<c>TFViewVectors.JumpHeight</c>), 289² / 1600 ≈
/// 52.2 u up; a plain jump clears that and a step (70.2), a crouched one that and the 20 u the
/// duck tucks the feet up (72.2).</item>
/// <item>The Scout's air dash leaves at 268.33 u/s (<c>TFJump.DashSpeed</c>), 45 u more from
/// the first jump's top: a double jump clears 52.2 + 45 + 20 ≈ 117.2, the Scout's reach alone.</item>
/// <item>A landing under 650 u/s does no damage (<c>TFCombatDefs.PlayerMaxSafeFallSpeed</c>):
/// 650² / 1600 ≈ 264 u of safe drop.</item>
/// <item>A crouch-only place costs <see cref="CrouchOnlyCost"/> times a standing one to cross.</item>
/// <item>The respawn room visualizers are solid to the other team (<see cref="TFTeamSolids"/>), read
/// from the level's entities when the map's lump is compressed (<see cref="TFPlacedEntities"/>).</item>
/// <item>The Soldier blast jumps (lane G5c-2b): its searches take the level's blast links
/// (<see cref="TFBlastLinks"/>), each that costs no more health than it has over the floor a jump
/// leaves it at (<see cref="TFDecider.RocketJumpHealthFloor"/>), and none the bots' own tries say fail (lane G5e). A link
/// that lands by a health pack costs a search what the pack does not give back (lane G9, <see cref="JumpHealthRule"/>).</item>
/// <item>Where its team's players died (lane G4a): a bot's searches weigh its team's field of the
/// experience's routes (<see cref="ExperienceRouteSlot"/>) by its Cunning (<see cref="TFRouteWeights"/>).</item>
/// <item>Its own way (lane Groutes): a bot's searches over the level's graph mind the field of the corridor to the point in
/// play its life drew, composed with that, so bots and lives from one place take different ones of the near-equal ways, none
/// dearer than 1.15 times the way they would take without it (<see cref="TFRouteVariation"/>).</item>
/// </list>
/// A player's reach is read from its own carrier, and its health from its own entity: the profile is
/// asked for the bot itself.
/// </summary>
[SideService(Side.Server, As = [typeof(INavProfile)])]
public sealed class TFNavProfile(ITFActorFacts actors, ITFLevelEntities placed, IEngineEntities entities, TFBlastLinks links,
                                  ExperienceRouteSlot? routes = null, TFRouteWeights? weights = null, TFRouteVariation? variation = null) : INavProfile
{
    /// <summary>The derivation's numbers, worked from the mod's movement constants.</summary>
    public static NavNumbers ModNumbers
    {
        get
        {
            MovementParams body = TFViewVectors.Params;
            float g = body.Gravity, jump = TFViewVectors.JumpHeight, dash = TFJump.DashSpeed * TFJump.DashSpeed / (2f * g);
            float tuck = body.HullMax.Z - body.DuckHullMax.Z, fall = TFCombatDefs.PlayerMaxSafeFallSpeed;
            return new NavNumbers(body.HullMax.X - body.HullMin.X, body.HullMax.Z, body.DuckHullMax.Z, body.StepSize,
                                  jump + body.StepSize, jump + tuck, fall * fall / (2f * g), jump + dash + tuck, WideHull: true,
                                  CrouchOnlyCost: CrouchOnlyCost);
        }
    }

    /// <summary>
    /// What a crouch-only place costs to cross, times a standing one's: a TF2 player all but never ducks
    /// to get round, and live on ctf_2fort a carrier routed home under the red base's stairs stuck
    /// there with the flag for the rest of the run, and six of its enemies with it.
    /// </summary>
    public const float CrouchOnlyCost = 8f;

    public NavNumbers Numbers(float fileJumpHeight) => ModNumbers;

    /// <summary>The Scout double-jumps, the Soldier blast jumps; a player whose class is not known yet reaches no further than a crouch jump.</summary>
    public JumpReach Reach(EntityHandle player)
        => actors.TryRead(player.EntryIndex, out TFActorFacts facts) ? ReachOf(facts.Class) : JumpReach.None;

    /// <summary>The jump reaches of a player of <paramref name="playerClass"/>: the Scout's double jump, the Soldier's blast jump, else none.</summary>
    public static JumpReach ReachOf(TFClass playerClass) => playerClass switch
    {
        TFClass.Scout => JumpReach.Double,
        TFClass.Soldier => JumpReach.Blast,
        _ => JumpReach.None,
    };

    /// <summary>
    /// A player with the blast reach takes the level's links (none while they are being found), spending on one no more
    /// than its health over the jump's floor as it stands now (<see cref="LinkHealthOf"/>), and none of those the
    /// experience's snapshot leaves out (lane G5e: the routes' <see cref="ExperienceRoutes.LinksShut"/>, when it is over
    /// this set); anyone else none.
    /// </summary>
    public NavLinkAllowance LinksFor(EntityHandle player)
    {
        if ((Reach(player) & JumpReach.Blast) == 0 || links.Set is not { } set)
            return default;
        NavLinkMask? shut = routes?.Current.LinksShut is { } mask && mask.IsOver(set.Links) ? mask : null;
        return new NavLinkAllowance(set.Links, LinkHealthOf(entities.GetHealth(player)), shut);
    }

    /// <summary>
    /// A player's team's field of where its players died (TF2's experience role is the team's number: no field for any
    /// other team), and its own weight (its Cunning, as a bot of ours): none with no field (no store, no death) or no
    /// weight (not our bot, Cunning 0). The team is the navigation's, as the team's solids are chosen by.
    /// </summary>
    public NavHazardAllowance HazardFor(EntityHandle player, int team)
    {
        if (routes is null || weights is null)
            return default;
        float weight = weights.Of(player.EntryIndex);
        return weight > 0f && routes.Current.For(team) is { } hazard ? new NavHazardAllowance(hazard, weight) : default;
    }

    /// <summary>
    /// What a bot's searches over <paramref name="graph"/> mind: its team's field at its weight (<see cref="HazardFor(EntityHandle, int)"/>)
    /// composed with the field of the corridor its life drew (<see cref="TFRouteVariation.For"/>, found from its
    /// <paramref name="obstacles"/>), at weight 1; the team's alone for a player that is no bot of ours, a bot with the
    /// switch off, or no variation.
    /// </summary>
    public NavHazardAllowance HazardFor(EntityHandle player, int team, NavGraph graph, NavObstacleState obstacles)
    {
        NavHazardAllowance own = HazardFor(player, team);
        if (variation is null)
            return own;
        Vector at = entities.GetOrigin(player);
        return variation.For(player.EntryIndex, graph, team, own, obstacles, new AiVector(at.X, at.Y, at.Z));
    }

    /// <summary>
    /// The health a bot of <paramref name="health"/> may spend on a link: what it has over the floor a jump must leave it at,
    /// under 0 under it. A link's published health is what a search spends on it (lane G9: under its blast's cost for a link
    /// that lands by a health pack, <see cref="JumpHealthRule.Spend"/>, which may be under 0), so a bot under the floor is
    /// still let onto a link its landing's pack pays for, and onto no other (every blast costs).
    /// </summary>
    public static float LinkHealthOf(int health) => health - TFDecider.RocketJumpHealthFloor;

    /// <summary>
    /// The visualizers' solids, from the map's lump or, when the lump is compressed (every shipped
    /// TF2 map's), from the level's own entities: live on ctf_2fort the lump read none, and no
    /// visualizer was solid to a bot.
    /// </summary>
    public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities)
        => TFTeamSolids.Read(entities.Unread is null ? entities : placed.Read());
}
