using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The runtime pickup's entity, on the standalone path.
///
/// <para>
/// Runtime pickups used to be spawned on <c>sourcesharp_netentity</c> — the
/// datatable round-trip fixture's shell — because it was the only shell C#
/// could create at the time. That shell networks no origin, no angles and no
/// model index, so a pickup's model was a server-side fact no client could
/// draw: the pickups worked and were invisible. The standalone shell networks
/// all three, which is the whole reason this class exists — a pickup on it
/// gains a drawable half for free.
/// </para>
///
/// <para>
/// Deliberately thin. What a pickup IS — its kind, its respawn timer, what
/// collecting it gives — belongs to <see cref="Pickups"/>, the directory that
/// spawned it; this class is only the entity-shaped end of that service, and
/// relays the one event an entity hears that a service cannot: being walked
/// into. The map items (<see cref="PickupItem"/>) are the other way round —
/// each owns itself — and the difference is who decides placement: a mapper
/// placed those, the directory places these.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsPickup, ScriptedEntityKind.Item)]
public sealed class SsPickup : ScriptedEntity
{
    private readonly IPickupDirectory _pickups;
    private readonly IGameClock _clock;

    public SsPickup(IPickupDirectory pickups, IGameClock clock)
    {
        _pickups = pickups;
        _clock = clock;
    }

    /// <summary>
    /// A player walked in. The registry has already filtered to players — the
    /// same rule the old native touch handler applied — so this only relays.
    /// The directory answers whether it was collected; an entity the directory
    /// does not know (say, one spawned by hand from the console after a
    /// directory reset) is simply not collectable, which is honest.
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
        => _pickups.OnTouched(Handle, toucher, _clock.Now);
}
