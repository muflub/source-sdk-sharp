using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// Which way a transition moves the player (map-tools' TransitionDirection).
/// <see cref="Up"/> is a level's way up: to the depth above, and from level 1 to the town
/// (<see cref="TravelService.UpFrom"/>). <see cref="Corpse"/> is D-H16's way back to where the
/// character died (plan_descent_death §4 M1: <c>Travel.ReturnToCorpse</c>). <see cref="Portal"/>
/// is the town portal (WO4): to the town from any depth.
/// </summary>
public enum TravelDirection { Down, Up, Corpse, Portal }

/// <summary>What a travel request came to, for the game thread.</summary>
public sealed record TravelAnswer(TravelState State, TimeSpan Eta, string Reason);

/// <summary>The SDK's travel call a request makes (spec §7).</summary>
public enum TravelCall { RequestDescent, StairsDown, TownPortal, ReturnToCorpse }

public sealed partial class HostLink
{
    /// <summary>
    /// Which call a request makes, and to what depth: from the hub, down is
    /// RequestDescent to <paramref name="depth"/> (the picked one, which the
    /// caller has checked); on a level, down is StairsDown to the next depth
    /// whatever <paramref name="depth"/> says. A level's way up goes to the
    /// depth above (<see cref="TravelService.UpFrom"/>): the SDK has no stairs
    /// up, so it is StairsDown to that depth (the host allows any depth up to
    /// one below the leader's reached, and its level claim brings the party
    /// back to the same level copy); from level 1 it is the town portal, as
    /// the portal itself is.
    /// </summary>
    internal static (TravelCall Call, int Depth) Route(TravelDirection direction, BootInfo boot, int depth)
    {
        bool hub = string.Equals(boot.InstanceKind, "hub", StringComparison.Ordinal);
        return direction switch
        {
            TravelDirection.Corpse => (TravelCall.ReturnToCorpse, 0),
            TravelDirection.Portal => (TravelCall.TownPortal, 0),
            TravelDirection.Up when !hub && TravelService.UpFrom(boot.Depth) > 0 => (TravelCall.StairsDown, TravelService.UpFrom(boot.Depth)),
            TravelDirection.Up => (TravelCall.TownPortal, 0),
            _ => hub ? (TravelCall.RequestDescent, depth) : (TravelCall.StairsDown, boot.Depth + 1),
        };
    }

    /// <summary>
    /// Spec §7, travel, for the player <paramref name="steamId"/>, as
    /// <see cref="Route"/> says: <paramref name="depth"/> is the depth picked
    /// for a descent from the hub. The party is the player's own, made solo if
    /// they have none. Started means a PrepareHop follows.
    /// </summary>
    public Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot, int depth) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return new TravelAnswer(TravelState.Refused, TimeSpan.Zero, "no character held here");

        var lease = held.Lease;
        var (call, to) = Route(direction, boot, depth);

        HostResult<TravelOutcome> outcome;
        if (call == TravelCall.ReturnToCorpse)
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
        else if (call == TravelCall.TownPortal)
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

            outcome = call == TravelCall.RequestDescent
                ? await sdk.Travel.RequestDescent(lease, party.Value.Id, to)
                : await sdk.Travel.StairsDown(lease, party.Value.Id, to);
        }

        return outcome.Ok
            ? new TravelAnswer(outcome.Value.State, outcome.Value.Eta, outcome.Value.Reason)
            : new TravelAnswer(TravelState.Refused, TimeSpan.Zero, $"{outcome.Refusal!.Error}: {outcome.Refusal.Reason}");
    });
}
