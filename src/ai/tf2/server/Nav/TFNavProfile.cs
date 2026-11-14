using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Knowledge;
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
/// </list>
/// A player's reach is read from its own carrier: the profile is asked for the bot itself.
/// </summary>
[SideService(Side.Server, As = [typeof(INavProfile)])]
public sealed class TFNavProfile(ITFActorFacts actors, ITFLevelEntities placed) : INavProfile
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

    /// <summary>The Scout double-jumps; a player whose class is not known yet reaches no further than a crouch jump.</summary>
    public JumpReach Reach(EntityHandle player)
        => actors.TryRead(player.EntryIndex, out TFActorFacts facts) ? ReachOf(facts.Class) : JumpReach.None;

    /// <summary>The jump reaches of a player of <paramref name="playerClass"/>: the Scout's double jump, else none.</summary>
    public static JumpReach ReachOf(TFClass playerClass) => playerClass == TFClass.Scout ? JumpReach.Double : JumpReach.None;

    /// <summary>
    /// The visualizers' solids, from the map's lump or, when the lump is compressed (every shipped
    /// TF2 map's), from the level's own entities: live on ctf_2fort the lump read none, and no
    /// visualizer was solid to a bot.
    /// </summary>
    public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities)
        => TFTeamSolids.Read(entities.Unread is null ? entities : placed.Read());
}
