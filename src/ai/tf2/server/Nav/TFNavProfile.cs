using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
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
/// the first jump's top: on paper a double jump clears 52.2 + 45 + 20 ≈ 117.2, the Scout's reach alone. The graph builds
/// it to 52.2 + 45 ≈ 97.2, the tuck not counted: live on cp_granary (P7 Scout lane,
/// the dash pressed at the top) 148 of the Scouts' 165 stuck lines in 300 s were double jumps up 104-116 u, at a crate
/// stack's cells, again and again.</item>
/// <item>The Atomizer's third jump (its <c>air_dash_count</c> 1 over <c>tf_scout_air_dash_count</c>'s 1, stock
/// <c>CTFPlayer::CanAirDash</c>) is a second dash from the first's top: 52.2 + 45 + 45 ≈ 142.2, the tuck again not counted
/// (round 2's S2). A Scout's own kit says whether it has it (<see cref="TFJumpKit"/>).</item>
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
                                  ExperienceRouteSlot? routes = null, TFRouteWeights? weights = null, TFRouteVariation? variation = null,
                                  ITFOwnKit? kits = null) : INavProfile
{
    private readonly Dictionary<int, OwnKit> _kits = new();

    /// <summary>The derivation's numbers, worked from the mod's movement constants.</summary>
    public static NavNumbers ModNumbers
    {
        get
        {
            MovementParams body = TFViewVectors.Params;
            float g = body.Gravity, jump = TFViewVectors.JumpHeight, dash = TFJump.DashSpeed * TFJump.DashSpeed / (2f * g);
            float tuck = body.HullMax.Z - body.DuckHullMax.Z, fall = TFCombatDefs.PlayerMaxSafeFallSpeed;
            return new NavNumbers(body.HullMax.X - body.HullMin.X, body.HullMax.Z, body.DuckHullMax.Z, body.StepSize,
                                  jump + body.StepSize, jump + tuck, fall * fall / (2f * g), jump + dash, WideHull: true,
                                  CrouchOnlyCost: CrouchOnlyCost, MaxTripleJumpRise: jump + 2f * dash);
        }
    }

    /// <summary>
    /// What a crouch-only place costs to cross, times a standing one's: a TF2 player all but never ducks
    /// to get round, and live on ctf_2fort a carrier routed home under the red base's stairs stuck
    /// there with the flag for the rest of the run, and six of its enemies with it.
    /// </summary>
    public const float CrouchOnlyCost = 8f;

    public NavNumbers Numbers(float fileJumpHeight) => ModNumbers;

    /// <summary>
    /// The reach the player's own routes plan with: its class's, as its own kit has it (round 2's S2,
    /// <see cref="TFJumpKit.ReachOf"/>: an Atomizer Scout's third jump, a Baby Face's Blaster Scout's none); the class's alone
    /// with no kit to read. A player whose class is not known yet reaches no further than a crouch jump. The profile is asked
    /// only for a player's own routes.
    /// </summary>
    public JumpReach Reach(EntityHandle player)
        => !actors.TryRead(player.EntryIndex, out TFActorFacts facts) ? JumpReach.None : Own(player.EntryIndex, facts.Class)?.Reach ?? ReachOf(facts.Class);

    /// <summary>What the player's kit says, read again only when it is handed a new kit or changes class; null with no kit service or no kit.</summary>
    private OwnKit? Own(int entIndex, TFClass playerClass)
    {
        if (kits is null)
            return null;
        int version = kits.Version(entIndex);
        lock (_kits)
        {
            if (_kits.TryGetValue(entIndex, out OwnKit? known) && known.Version == version && known.Class == playerClass)
                return known.Empty ? null : known;
            IReadOnlyList<TFOwnKitItem> kit = kits.KitOf(entIndex);
            var read = new OwnKit(version, playerClass, kit.Count == 0, TFJumpKit.ReachOf(playerClass, kit), TFJumpBudget.CostShare(kit));
            _kits[entIndex] = read;
            return read.Empty ? null : read;
        }
    }

    private sealed record OwnKit(int Version, TFClass Class, bool Empty, JumpReach Reach, float CostShare);

    /// <summary>The jump reaches of a player of <paramref name="playerClass"/>: the Scout's double jump, the Soldier's blast jump, else none.</summary>
    public static JumpReach ReachOf(TFClass playerClass) => playerClass switch
    {
        TFClass.Scout => JumpReach.Double,
        TFClass.Soldier => JumpReach.Blast,
        _ => JumpReach.None,
    };

    /// <summary>TF2's Snipers read the long-line pass (TFSniperSpots): its levels build it.</summary>
    public bool LongSightLines => true;

    /// <summary>
    /// A player with the blast reach takes the level's links (none while they are being found), spending on one no more
    /// than its health over the jump's floor as it stands now (<see cref="LinkHealthOf"/>), each costing what its own kit
    /// pays for the blast (round 2's S13, <see cref="TFJumpBudget"/>: the Gunboats' 0.4, nothing for the Rocket Jumper), and none of those the
    /// experience's snapshot leaves out (lane G5e: the routes' <see cref="ExperienceRoutes.LinksShut"/>, when it is over
    /// this set); anyone else none.
    /// </summary>
    public NavLinkAllowance LinksFor(EntityHandle player)
    {
        if ((Reach(player) & JumpReach.Blast) == 0 || links.Set is not { } set)
            return default;
        NavLinkMask? shut = routes?.Current.LinksShut is { } mask && mask.IsOver(set.Links) ? mask : null;
        float share = actors.TryRead(player.EntryIndex, out TFActorFacts facts) && Own(player.EntryIndex, facts.Class) is { } own ? own.CostShare : 1f;
        return new NavLinkAllowance(set.Links, LinkHealthOf(entities.GetHealth(player)), shut, SpendsOf(set, share));
    }

    private JumpLinkSet? _spendsSet;
    private readonly Dictionary<float, float[]?> _spends = new();

    /// <summary>
    /// What a bot whose blasts cost <paramref name="share"/> of the stock's spends on each of <paramref name="set"/>'s links
    /// (round 2's S13, <see cref="TFJumpBudget.Spends"/>), worked once per set and share; null for the stock share.
    /// </summary>
    private float[]? SpendsOf(JumpLinkSet set, float share)
    {
        if (share == 1f)
            return null;
        lock (_spends)
        {
            if (!ReferenceEquals(set, _spendsSet))
            {
                _spendsSet = set;
                _spends.Clear();
            }

            if (!_spends.TryGetValue(share, out float[]? spends))
                _spends[share] = spends = TFJumpBudget.Spends(set, share);
            return spends;
        }
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
