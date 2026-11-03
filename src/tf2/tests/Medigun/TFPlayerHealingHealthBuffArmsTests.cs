using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Player;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The HEALTH_BUFF arms of <c>CTFPlayerShared::OnConditionAdded</c>/
/// <c>OnConditionRemoved</c> (tf_player_shared.cpp:1578-1584, 1935-1939) that
/// stock keeps on the shared class itself, beside the state they reset: the
/// add (every add, refreshes included) starts the accumulation from nothing
/// and winds the second-window clock; the last healer leaving leaves no
/// carry-over behind — neither for the next attach nor for the item
/// overheal's drain, which shares the fraction.
///
/// <para>
/// The rig is the medigun rig's fakes over the real
/// <see cref="TFPlayerConditions"/> — the arms run through the condition
/// chain exactly as production drives them — and the player's own carrier
/// from the <see cref="PlayerRig"/> holds the condition set.
/// </para>
/// </summary>
public sealed class TFPlayerHealingHealthBuffArmsTests
{
    private static readonly EntityHandle Medic = new(0x0001_0000u | 1);

    /// <summary>
    /// The 0.5-fraction tick: a tenth of a second at the dispenser's level 1
    /// ten-a-second asks half a health — the residue the arms are about.
    /// </summary>
    private const float HalfTick = 0.05f, Rate = 10f;

    [Fact]
    public void A_refreshed_health_buff_restarts_the_heal_accumulation()
    {
        var rig = new ArmsRig();

        rig.Healing.Heal(PlayerRig.Player, Medic, Rate, now: 10f, dispenserHeal: true);
        rig.Healing.TickPatient(PlayerRig.Player, now: 10f, frametime: HalfTick);      // half a health of fraction

        // Stock's AddCond re-runs OnConditionAdded on every add, refreshes
        // included — the same healer handing the patient over again zeroes the
        // fraction, losing its half. (The dispenser's attach edge, aligned to
        // stock in the same series, is what keeps its own heals off this path.)
        rig.Healing.Heal(PlayerRig.Player, Medic, Rate, now: 10.05f, dispenserHeal: true);
        rig.Healing.TickPatient(PlayerRig.Player, now: 10.05f, frametime: HalfTick);   // from zero: still half

        Assert.Equal(0f, rig.Resupply.Taken.GetValueOrDefault(PlayerRig.Index));
    }

    [Fact]
    public void The_heal_fraction_does_not_survive_the_last_healer_leaving()
    {
        var rig = new ArmsRig();

        rig.Healing.Heal(PlayerRig.Player, Medic, Rate, now: 10f, dispenserHeal: true);
        rig.Healing.TickPatient(PlayerRig.Player, now: 10f, frametime: HalfTick);      // half a health of fraction
        rig.Healing.StopHealing(PlayerRig.Player, Medic, now: 10.05f);                 // the buff ends with it

        rig.Health = 160;   // an item overheals him past the 150 class max
        // The drain — the fraction shared with the heal (:2704): (225-150)/15
        // per second, so a tenth pays half — from a fraction stock left at 0.
        rig.Healing.TickPatient(PlayerRig.Player, now: 10.1f, frametime: 0.1f);

        // The carried half added to the drain's own half would have paid out a
        // health and cut the player to 159.
        Assert.Equal(160, rig.Health);
    }

    /// <summary>
    /// The healing over the real condition chain: the player's carrier holds
    /// the set, the chain carries the healing's own arms, and the health is a
    /// store the drain writes through.
    /// </summary>
    private sealed class ArmsRig
    {
        public readonly PlayerRig Player = new();
        public readonly TFHealingResupply Resupply = new();
        public readonly TFPlayerConditions Conditions;
        public readonly TFPlayerHealing Healing;
        public int Health = 100;

        public ArmsRig()
        {
            Player.Join();   // the carrier that holds the condition set

            var conVars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
            conVars.RegisterAll(typeof(TFPlayerHealing).Assembly);

            var chain = new Chain();
            Conditions = new TFPlayerConditions(Player.Carriers, Player.Engine, chain);
            Healing = new TFPlayerHealing(Player.Engine, Player.Engine, Conditions, Resupply,
                                          conVars.Get<TFMaxHealthBoostConVar>()!,
                                          conVars.Get<TFBoostDrainTimeConVar>()!,
                                          conVars.Get<TFInvulnTimeConVar>()!, new FakeClock());
            chain.Add(Healing);   // stock's arms live on the shared class: here, on the healing

            Resupply.Maxes[PlayerRig.Index] = 150;
            Player.Engine.ReturnsEachCall("Entities.GetHealth", () => Health);
            Player.Engine.WhenCalled("Entities.SetHealth", c => Health = c.ArgAs<int>(1));
        }
    }

    /// <summary>A hook list the healing joins after its conditions were built.</summary>
    private sealed class Chain : IHooks<ITFConditionHook>
    {
        private readonly List<ITFConditionHook> hooks = new();

        public IReadOnlyList<ITFConditionHook> All => hooks;

        public void Add(ITFConditionHook hook) => hooks.Add(hook);

        public void Each(string what, Action<ITFConditionHook> call)
        {
            foreach (var hook in hooks)
                call(hook);
        }
    }
}
