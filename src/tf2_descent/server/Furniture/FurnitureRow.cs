using System.Globalization;

using SourceSharp.Abi;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// One piece of furniture of the level, as data (plan_descent_logic WO3): what
/// the rules placed on a marker (<see cref="Placed"/>), where it stands, the
/// room it is in, and its state as the rules keep it (a shrine used, a
/// container opened, a Dispenser's charge, an explosive's health, a trap's
/// phase). It holds an entity only while its room is awake and the pool has
/// one; its state is the row's either way, so a room that sleeps and wakes
/// finds everything as it was.
/// </summary>
public sealed class FurnitureRow
{
    internal FurnitureRow(int index, Placed placed, int marker, Vector position, float yaw, int room)
    {
        Index = index;
        Placed = placed;
        Marker = marker;
        Position = position;
        Yaw = yaw;
        Room = room;
    }

    /// <summary>The row's number on the level: what the dev commands name.</summary>
    public int Index { get; }

    public Placed Placed { get; }

    /// <summary>The level's marker it stands on (<c>LevelMarker.Id</c>).</summary>
    public int Marker { get; }

    public FurnitureKind Kind => Placed.Kind;

    public string Variant => Placed.Variant;

    public Vector Position { get; }

    /// <summary>Its facing, degrees counter-clockwise from +x: the marker's.</summary>
    public float Yaw { get; }

    public int Room { get; }

    public ShrineState? Shrine { get; internal set; }

    public DispenserState? Dispenser { get; internal set; }

    public ContainerState? Container { get; internal set; }

    public ExplosiveState? Explosive { get; internal set; }

    public TrapState? Trap { get; internal set; }

    /// <summary>An explosive that has gone off: its entity parked for good, never to stand again.</summary>
    public bool Went { get; internal set; }

    /// <summary>When a set-off explosive goes off (seconds, the server's clock); null when none is pending.</summary>
    public double? ExplodeAt { get; internal set; }

    /// <summary>The hero who set the explosive off (his player), or none.</summary>
    public EntityHandle SetOffBy { get; internal set; }

    /// <summary>The entity it stands on; null while it is data.</summary>
    public FurnitureEntity? Entity { get; internal set; }

    public bool OnEntity => Entity is not null;

    public FurnitureKindId KindId => (FurnitureKindId)(int)Kind;

    /// <summary>The state as one short phrase, for the dev command and the logs.</summary>
    public string StateText() => Kind switch
    {
        FurnitureKind.Shrine => Shrine!.Used ? "used" : "unused",
        FurnitureKind.Dispenser => string.Create(CultureInfo.InvariantCulture, $"charge {Dispenser!.Charge:0.#}"),
        FurnitureKind.Container => (Container!.Opened ? "opened" : "shut")
                                   + (Container.KeyId is { } key && !Container.Opened ? $", locked ({key}, key in room {Placed.KeyRoom})" : "")
                                   + (Container.Trap is { } trap && !Container.Opened ? $", trapped ({trap})" : ""),
        FurnitureKind.Explosive => Went ? "gone off"
            : ExplodeAt is { } at ? string.Create(CultureInfo.InvariantCulture, $"set off, goes at {at:0.00}")
            : string.Create(CultureInfo.InvariantCulture, $"health {Explosive!.Health:0.#}"),
        FurnitureKind.Trap => Trap!.Phase switch
        {
            TrapPhase.Armed => "armed",
            TrapPhase.Tripped => string.Create(CultureInfo.InvariantCulture, $"tripped, fires at {Trap.Until:0.00}"),
            TrapPhase.Rearming => string.Create(CultureInfo.InvariantCulture, $"re-arming at {Trap.Until:0.00}"),
            _ => "spent",
        },
        _ => "",
    };
}

/// <summary>
/// The size and stance of each furniture model, from the models' own headers
/// (studiohdr hull, read from TF2's packs): its box about its origin, and how
/// far its origin is lifted so it stands on the floor (a model whose origin is
/// at its middle).
/// </summary>
public static class FurnitureModels
{
    public static (Vector Mins, Vector Maxs, float Lift) Of(string model) => model switch
    {
        FurnitureCatalog.ShrineModel => (new(-8, -27, -34), new(28, 27, 31), 34f),
        FurnitureCatalog.ShrineSpentModel => (new(-16, 0, 0), new(10, 24, 33), 0f),
        FurnitureCatalog.DispenserModel => (new(-20, -20, 0), new(20, 20, 58), 0f),
        FurnitureCatalog.CrateModel => (new(-21, -36, 0), new(22, 37, 39), 0f),
        FurnitureCatalog.LockerModel => (new(-25, -23, 0), new(26, 23, 43), 0f),
        FurnitureCatalog.CrateOpenedModel => (new(-21, -36, 0), new(23, 39, 41), 0f),
        FurnitureCatalog.SarcophagusModel => (new(-26, -30, 0), new(26, 0, 117), 0f),
        FurnitureCatalog.SarcophagusOpenedModel => (new(-26, -25, 0), new(26, 0, 117), 0f),
        FurnitureCatalog.BarrelModel => (new(-28, -28, 0), new(28, 28, 80), 0f),
        FurnitureCatalog.PumpkinBombModel => (new(-22, -18, 0), new(18, 18, 46), 0f),
        FurnitureCatalog.PlateModel => (new(-14, -18, 0), new(14, 18, 5), 0f),
        FurnitureCatalog.ChargeModel => (new(-6, -6, -6), new(6, 6, 6), 6f),
        _ => (new(-16, -16, 0), new(16, 16, 32), 0f),
    };
}
