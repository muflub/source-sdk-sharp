using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// Which robot bodies and weapons are parked (plan_descent_logic HO2, 3.11 row
/// 5): a table by edict index holding the parked entity's handle, so the
/// transmit rule's question costs one array read whatever the pool's size.
/// The pool parks and unparks; the transmit rule reads.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeParking
{
    private readonly uint[] _parked = new uint[EngineLimits.MaxEdicts];

    public void Park(EntityHandle entity)
    {
        int i = (int)entity.EntryIndex;
        if ((uint)i < (uint)_parked.Length)
            _parked[i] = entity.Value;
    }

    public void Unpark(EntityHandle entity)
    {
        int i = (int)entity.EntryIndex;
        if ((uint)i < (uint)_parked.Length && _parked[i] == entity.Value)
            _parked[i] = 0;
    }

    public bool IsParked(EntityHandle entity)
    {
        int i = (int)entity.EntryIndex;
        return (uint)i < (uint)_parked.Length && entity.Value != 0 && _parked[i] == entity.Value;
    }

    /// <summary>How many entities are parked now.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (uint v in _parked)
                if (v != 0) n++;
            return n;
        }
    }

    /// <summary>Forgets every parked entity: a level's handles do not outlive it.</summary>
    public void Clear() => Array.Clear(_parked);
}

/// <summary>
/// A parked body or weapon is never sent (HO0, 3.11 row 5): a client that never
/// saw one never holds it, and one that did keeps it as dormant. Everything
/// else is left to the ordinary check. The pool flags each body and weapon for
/// the full check at its spawn (<c>SetTransmitFullCheck</c>), so the engine
/// asks this for them and for nothing of the horde's otherwise. Registered
/// plain: naming <see cref="ITransmitRule"/> in <c>As</c> registers it twice
/// (HO0's trap).
/// </summary>
[SideService(Side.Server)]
public sealed class HordeTransmit(HordeParking parking) : ITransmitRule, IOrderedHook
{
    public const int Order = ModStages.First + 500;

    public int HookOrder => Order;

    public TransmitAnswer ShouldTransmit(EntityHandle entity, int recipientSlot) =>
        parking.IsParked(entity) ? TransmitAnswer.Never : TransmitAnswer.Default;
}
