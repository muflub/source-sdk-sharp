using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// Which way a transition moves the player (map-tools' TransitionDirection).
/// <see cref="Corpse"/> is D-H16's way back to where the character died
/// (plan_descent_death §4 M1: <c>Travel.ReturnToCorpse</c>).
/// </summary>
public enum TravelDirection { Down, Up, Corpse }

/// <summary>What a travel request came to, for the game thread.</summary>
public sealed record TravelAnswer(TravelState State, TimeSpan Eta, string Reason);

public sealed partial class HostLink
{
    /// <summary>
    /// Spec §7, travel, for the player <paramref name="steamId"/>: from the hub,
    /// down is RequestDescent to depth 1; on a level, down is StairsDown to the
    /// next depth, and up is the town portal (the SDK has no stairs up). The
    /// party is the player's own, made solo if they have none. Started means a
    /// PrepareHop follows.
    /// </summary>
    public Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return new TravelAnswer(TravelState.Refused, TimeSpan.Zero, "no character held here");

        var lease = held.Lease;
        bool hub = string.Equals(boot.InstanceKind, "hub", StringComparison.Ordinal);

        HostResult<TravelOutcome> outcome;
        if (direction == TravelDirection.Corpse)
        {
            // D-H16: the character goes alone back to the level they died on —
            // the running instance while it runs, else a fresh instance of the
            // same level, whose corpse items wait in Lost &amp; Found. With no
            // recorded death the host refuses (no_corpse_level).
            // The host deprecated ReturnToCorpse at 2.5.0 for M3's placed
            // teleporter; P2's M1 is the plan's interim way back and M3 cuts
            // this arm with the enum value.
            #pragma warning disable CS0618 // the plan's M1, until M3 lands
            outcome = await sdk.Travel.ReturnToCorpse(lease);
            #pragma warning restore CS0618
        }
        else if (direction == TravelDirection.Up)
        {
            outcome = await sdk.Travel.TownPortal(lease);
        }
        else
        {
            var party = await sdk.Parties.Get("", lease.CharacterId);
            if (!party.Ok)
                party = await sdk.Parties.Create(lease);
            if (!party.Ok)
                return new TravelAnswer(TravelState.Refused, TimeSpan.Zero, $"{party.Refusal!.Error}: {party.Refusal.Reason}");

            outcome = hub
                ? await sdk.Travel.RequestDescent(lease, party.Value.Id, 1)
                : await sdk.Travel.StairsDown(lease, party.Value.Id, boot.Depth + 1);
        }

        return outcome.Ok
            ? new TravelAnswer(outcome.Value.State, outcome.Value.Eta, outcome.Value.Reason)
            : new TravelAnswer(TravelState.Refused, TimeSpan.Zero, $"{outcome.Refusal!.Error}: {outcome.Refusal.Reason}");
    });
}
