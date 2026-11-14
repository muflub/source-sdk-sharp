using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of the core's teammate rule (plan A.7): the same answer
/// as the server's <c>TeammatesPassRule</c> (<see cref="TeammateCollision.Passes"/>)
/// from the same inputs as the client sees them: the server's published
/// answer (<c>mp_teammates_pass</c>, which carries the active mode and
/// <c>mp_teammates_collide</c>) and both players' teams from the player
/// resource. Asked by the prediction's player clip
/// (<see cref="PredictionPlayerClip"/>), so a teammate the server walks a
/// player through is walked through in the prediction too.
/// </summary>
public sealed class ClientTeammatesPassRule(MpTeammatesPassConVar pass, IClientStandalones standalones)
    : IEntityCollideRule, IOrderedHook
{
    /// <summary>The server's rule's place on the chain (<c>TeammatesPassRule.Order</c>): a mod's unordered rule does not tie with it.</summary>
    public const int Order = -1000;

    public int HookOrder => Order;

    /// <summary>Whether teammates walk through each other now, as the server published it.</summary>
    public bool TeammatesPass => pass.Value;

    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
    {
        // The cheap refusals first: deathmatch asks no team.
        if ((int)group != TeammateCollision.PlayerMovementGroup || mover == EntityHandle.None || !pass.Value)
            return true;

        return !TeammateCollision.Passes(true, (int)group, TeamOf(mover.EntryIndex), TeamOf(entity.EntryIndex));
    }

    /// <summary>The player resource's team row for a player's index; -1 for anything else.</summary>
    public int TeamOf(int index)
    {
        if (index <= 0 || index >= PlayerResourceNetState.Slots || standalones.GetByIndex(index) is not ClientSsPlayer)
            return -1;

        return standalones.OfClass(EntityNames.PlayerManager).OfType<ClientPlayerResource>().FirstOrDefault() is { } resource
            ? resource.State.Team[index]
            : -1;
    }
}
