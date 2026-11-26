using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Pyro's own HUD meter (plan_bots_teams_tf2 P7): the Phlogistinator's "Mmmph" meter, 0..1 (<c>pyro.rage</c>), and
/// whether its crits are draining it (<c>pyro.rage_draining</c>, 1 or 0), as the bot's own HUD draws them (stock's
/// <c>m_flRageMeter</c> and <c>m_bRageDraining</c>, sent to the player's own client). Registered as itself: the
/// <see cref="ITFHudMeters"/> chain finds every registered reader (an <c>As</c> claim of the contract is the Medic's
/// <see cref="TFMedigunMeters"/>'s, and a second claim fails the side's composition).
/// </summary>
[SideService(Side.Server)]
public sealed class TFPyroMeters(ITFPlayerData players) : ITFHudMeters, IOrderedHook
{
    public const string Rage = "pyro.rage", RageDraining = "pyro.rage_draining";

    /// <summary>
    /// Its place in the chain: 100 plus the Pyro's class number (the coordinator's rule for every class's reader, notice 1
    /// of 2026-11-21), so it never ties the Medic's meters (order 0) or another class's (live: a tie at 0 threw every sense tick).
    /// </summary>
    public int HookOrder => 100 + (int)TFClass.Pyro;

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass != TFClass.Pyro || players.Networked(bot.EntryIndex) is not { } state)
            return;
        into.Add(new TFMeter(Rage, state.RageMeter / TFRageMeter.FullMeter));
        into.Add(new TFMeter(RageDraining, state.RageDraining ? 1f : 0f));
    }
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Phlogistinator archetype (research A5): no blast (the flamethrower has none, <see cref="PyroPlay.NoAirblast"/>);
    /// its Mmmph meter full, the taunt that turns it into crits (the second trigger, <see cref="TFPyroClass.Mmmph"/>: 2.6 s
    /// invulnerable) once no enemy is in sight within <see cref="TauntClear"/>; while its crits drain, the fight weighs
    /// <see cref="BoostedFight"/> times more (it goes hunting).
    /// </summary>
    private sealed partial class PyroPlay
    {
        /// <summary>The Phlogistinator taunts only with no enemy in sight this near (research A5's 600 u).</summary>
        public const float TauntClear = 600f;

        /// <summary>The fight's weight while the crits drain.</summary>
        public const float BoostedFight = 1.5f;

        /// <summary>The least time between two taunt presses (the taunt itself lasts about 2.6 s).</summary>
        public const float TauntEverySeconds = 3f;

        private int _mmmphRow = -2;
        private Tick _tauntReadyAt;
        private long _taunts;

        private int MmmphRow => _mmmphRow == -2 ? _mmmphRow = D.AbilityRow(TFPyroClass.Mmmph) : _mmmphRow;

        private bool IsPhlog => Archetype == TFPyroClass.Phlog;

        /// <summary>Whether the Phlogistinator's crits are draining its meter, as its HUD shows.</summary>
        private bool Boosted => D.Kit?.Meter(TFPyroMeters.RageDraining, 0f) >= 1f;

        /// <summary>Presses the taunt: a full meter, holding the flamethrower, not boosted, and no enemy in sight within <see cref="TauntClear"/>.</summary>
        private bool Taunt()
        {
            if (D.Kit is not { } kit || kit.Meter(TFPyroMeters.Rage, 0f) < 1f || Boosted || !HoldingFlamethrower || D._now < _tauntReadyAt || MmmphRow < 0)
                return false;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (!TeamRule.Friends(D._self.Team, f.Team) && D._self.Origin.DistanceTo(f.Origin) < TauntClear)
                    return false;
            }

            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(MmmphRow, 0), ++D._oneShot);
            _tauntReadyAt = D._now + (int)(TauntEverySeconds / D._tick);
            _taunts++;
            return true;
        }

        public override float Weigh(string branch) => (IsPhlog && branch == Fight && Boosted ? BoostedFight : 1f) * GuardWeigh(branch);
    }
}
