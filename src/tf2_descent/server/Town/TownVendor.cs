using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Town;

/// <summary>
/// <c>tf2_descent_vendor</c>: one of the town's stalls (plan_descent_logic WO8): a vendor in a TF2
/// class's model playing its idle (the client plays <see cref="VendorNetState.Sequence"/>), or a
/// piece of TF2's furniture (the resupply locker, a work table). Solid in a player's box, so a
/// hero walks round it; it takes no damage; +USE within reach is told to the town
/// (<see cref="TownService.Use"/>), which opens its window.
/// </summary>
[ScriptedEntity(VendorNetState.ClassName, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, TownStalls.HeavyModel)]
[Precache(PrecacheKind.Model, TownStalls.SpyModel)]
[Precache(PrecacheKind.Model, TownStalls.MedicModel)]
[Precache(PrecacheKind.Model, TownStalls.ScoutModel)]
[Precache(PrecacheKind.Model, TownStalls.EngineerModel)]
[Precache(PrecacheKind.Model, TownStalls.LockerModel)]
[Precache(PrecacheKind.Model, TownStalls.BenchModel)]
[Precache(PrecacheKind.Sound, TownLocker.Sound)]
public sealed class TownVendor(IEngineEntityRender render, TownService town) : ScriptedEntity
{
    [NetState] public VendorNetState State = new();

    /// <summary>The stall's index in <see cref="TownStalls.All"/>; -1 before it stands.</summary>
    public int Stall { get; private set; } = -1;

    public override bool SpawnsPhysics => false;

    public override bool TakesDamage => false;

    public override bool TouchEveryTick => false;

    public override UseCaps ObjectCaps => Stall >= 0 ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None;

    public override void OnSpawn()
    {
        SetModel(TownStalls.LockerModel);
        SetSolidity(SolidType.None, SolidFlags.None);
    }

    /// <summary>Stands it as stall <paramref name="index"/> at <paramref name="at"/>, facing <paramref name="yaw"/>, playing its idle.</summary>
    internal void Stand(int index, TownStall stall, Vector at, float yaw)
    {
        Stall = index;
        SetModel(stall.Model);
        Entities.SetOrigin(Handle, at);
        Entities.SetAngles(Handle, new Vector(0f, yaw, 0f));
        Entities.SetBounds(Handle, stall.Mins, stall.Maxs);
        SetSolidity(SolidType.BoundingBox, SolidFlags.None);
        int idle = stall.Sequence.Length > 0 ? render.LookupSequence(Handle, stall.Sequence) : -1;
        if (idle >= 0)
            render.PlaySequence(Handle, idle, 1f, true);
        State.Stall = (byte)Math.Clamp(index + 1, 0, byte.MaxValue);
        State.Sequence = (short)idle;
        State.Once = false;
    }

    /// <summary>
    /// Plays <paramref name="sequence"/> once and holds its last frame (the locker's doors opening,
    /// then closing), here and on every client. False when the model has no such sequence.
    /// </summary>
    internal bool Animate(string sequence)
    {
        int found = render.LookupSequence(Handle, sequence);
        if (found < 0)
            return false;
        render.PlaySequence(Handle, found, 1f, false);
        State.Sequence = (short)found;
        State.Once = true;
        return true;
    }

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        if (Stall >= 0 && input.Activator != EntityHandle.None)
            town.Use(Entities.GetIndex(Handle), Entities.GetIndex(input.Activator));
    }
}
