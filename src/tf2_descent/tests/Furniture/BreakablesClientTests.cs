extern alias client;

using SourceSharp.Abi;

using Xunit;

using BoneRay = client::SourceSharp.Shared.Bones.BoneRay;
using BoneTrace = client::SourceSharp.Shared.Bones.BoneTrace;
using BreakableDrawing = client::TF2.Descent.Client.Furniture.BreakableDrawing;
using BreakableGeometry = client::TF2.Descent.Shared.BreakableGeometry;
using ClientBreakablesEntity = client::TF2.Descent.Client.Furniture.ClientBreakablesEntity;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using FurnitureCatalog = client::TF2.Descent.Shared.FurnitureCatalog;
using FurnitureKindId = client::TF2.Descent.Shared.FurnitureKindId;
using IBreakableSlots = client::TF2.Descent.Client.Furniture.IBreakableSlots;
using NVector = System.Numerics.Vector3;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// The client's breakables entity (the user, 2026-11-15): each live prop drawn as a client-only model,
/// erased when its broken bit arrives, and prediction's traces answered by the same test as the server's.
/// </summary>
public sealed class BreakablesClientTests
{
    private sealed class Slots : IBreakableSlots
    {
        private int _next = 10;
        public Dictionary<int, (string Model, Vector At, float Yaw)> Drawn { get; } = [];
        public List<int> Erased { get; } = [];

        public int Draw(string model, Vector origin, float yaw)
        {
            int slot = _next++;
            Drawn[slot] = (model, origin, yaw);
            return slot;
        }

        public void Erase(int slot)
        {
            Erased.Add(slot);
            Drawn.Remove(slot);
        }
    }

    private static readonly byte Crate = FurnitureCatalog.Number(FurnitureKindId.Breakable, "crate");
    private static readonly byte Barrel = FurnitureCatalog.Number(FurnitureKindId.Breakable, "barrel");

    /// <summary>A client breakables entity holding a crate at (100 0 0) and a barrel at (300 0 0), as the registry would make it.</summary>
    private static (ClientBreakablesEntity Entity, Slots Slots, BreakableDrawing Drawing) Entity()
    {
        var engine = new ClientEngine();
        var slots = new Slots();
        var drawing = new BreakableDrawing(slots);
        var e = new ClientBreakablesEntity(engine, drawing);
        typeof(ClientStandaloneEntity).GetProperty("Index")!.SetValue(e, 40);
        typeof(ClientStandaloneEntity).GetProperty("Handle")!.SetValue(e, new EntityHandle((uint)((3 << 16) | 40)));
        e.State.BindLocal();
        e.State.Count = 2;
        (e.State.X[0], e.State.Y[0], e.State.Z[0], e.State.Entry[0], e.State.Yaw[0]) = ((short)100, (short)0, (short)0, Crate, (byte)64);
        (e.State.X[1], e.State.Y[1], e.State.Z[1], e.State.Entry[1], e.State.Yaw[1]) = ((short)300, (short)0, (short)0, Barrel, (byte)0);
        e.OnSpawn();
        return (e, slots, drawing);
    }

    [Fact]
    public void Each_live_prop_is_drawn_once_with_its_model_place_and_facing()
    {
        var (e, slots, _) = Entity();

        e.OnDataChanged([], default);

        Assert.Equal(2, slots.Drawn.Count);
        Assert.Contains(slots.Drawn.Values, d => d.Model == FurnitureCatalog.CrateModel && (d.At.X, d.At.Y, d.At.Z) == (100f, 0f, 0f) && d.Yaw == 90f);
        Assert.Contains(slots.Drawn.Values, d => d.Model == FurnitureCatalog.WoodenBarrelModel && (d.At.X, d.At.Y, d.At.Z) == (300f, 0f, BreakableGeometry.Lift(Barrel)));
    }

    [Fact]
    public void A_prop_whose_bit_arrives_is_erased_and_the_other_stays()
    {
        var (e, slots, drawing) = Entity();
        int drawn = drawing.Drawn;

        e.State.Broken[0] = 1u;
        e.OnDataChanged([], default);

        Assert.Equal(2, drawn);
        Assert.Single(slots.Erased);
        Assert.Equal(FurnitureCatalog.WoodenBarrelModel, Assert.Single(slots.Drawn.Values).Model);
        Assert.Equal(1, drawing.Drawn);
    }

    [Fact]
    public void A_removed_entity_erases_what_it_drew()
    {
        var (e, slots, drawing) = Entity();

        e.OnRemoved();

        Assert.Equal(2, slots.Erased.Count);
        Assert.Empty(slots.Drawn);
        Assert.Equal(0, drawing.Drawn);
    }

    [Fact]
    public void Predictions_clip_stops_the_hull_at_the_live_crates_face_as_the_server_does()
    {
        var (e, _, drawing) = Entity();
        var clip = new client::TF2.Descent.Client.Furniture.BreakablesPredictionClip(drawing);
        var start = new Vector(0, 0, 1);
        var end = new Vector(200, 0, 1);
        var mins = new Vector(-24, -24, 0);
        var maxs = new Vector(24, 24, 82);

        bool stopped = clip.Clip(start, end, mins, maxs, BreakableGeometry.ContentsSolid, out var hit);
        e.State.Broken[0] = 1u;
        bool afterBreak = clip.Clip(start, end, mins, maxs, BreakableGeometry.ContentsSolid, out _);

        // The crate faces 90 degrees: its box is 43 deep along x about (100 0), so its near face is x 78.5.
        Assert.True(stopped);
        Assert.Equal(e.Handle.Value, hit.HitEntity.Value);
        Assert.Equal(BreakableGeometry.WorldBox(e.Props[0]).Mins.X - 24f, hit.EndPos.X, 3);
        Assert.False(afterBreak);
    }

    [Fact]
    public void Prediction_stops_a_step_at_a_live_crate_and_not_at_a_broken_one()
    {
        var (e, _, _) = Entity();
        var step = BoneRay.FromBox(new NVector(0, 0, 1), new NVector(200, 0, 1), new NVector(-24, -24, 0), new NVector(24, 24, 82));
        var live = new BoneTrace();
        var broken = new BoneTrace();

        e.TestTrace(step, BreakableGeometry.ContentsSolid, live);
        e.State.Broken[0] = 1u;
        e.TestTrace(step, BreakableGeometry.ContentsSolid, broken);

        Assert.Equal(0, live.HitBox);
        Assert.True(live.Fraction < 1f);
        Assert.Equal(1f, broken.Fraction);
    }
}
