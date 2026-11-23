using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a jump room's floor takes (user ruling 2026-11-10: "dont need to keep floor totally clear for
/// rocket jumping"): clutter anywhere but the take-off patch beside a ledge only a jump reaches and the
/// air between, which <see cref="RoomDresser.JumpAirRefuses"/> keeps for the dresser and the dress lint.
/// </summary>
public sealed class DescentJumpFloorTests
{
    private static readonly Room Gallery = RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, RoomKit.Normal, RoomTheme.Grey, 0), "gallery_a");

    private static Box At(float x, float y, float size, float height) =>
        new(new Point(x - size / 2, y - size / 2, RoomKit.Normal.Sill), new Point(x + size / 2, y + size / 2, RoomKit.Normal.Sill + height));

    /// <summary>The level distance from (x, y) to the nearest column of a ledge only a jump reaches.</summary>
    private static float ToLedge(FloorPlan plan, float x, float y) =>
        plan.JumpLedges.Min(s => MathF.Sqrt(MathF.Pow(s.I * 16 + 8 - x, 2) + MathF.Pow(s.J * 16 + 8 - y, 2)));

    /// <summary>An open floor column of the gallery at least <paramref name="from"/> from every jump ledge, and at most <paramref name="to"/>.</summary>
    private static (float X, float Y) Floor(FloorPlan plan, float from, float to)
    {
        for (int i = 0; i < plan.Size; i++)
            for (int j = 0; j < plan.Size; j++)
            {
                if (!plan.IsFloor(i, j) || plan.WallDistance(i, j, 8) <= 7)
                    continue;
                float x = i * 16 + 8, y = j * 16 + 8, d = ToLedge(plan, x, y);
                if (d >= from && d <= to)
                    return (x, y);
            }

        throw new InvalidOperationException($"no open floor {from} to {to} from a ledge");
    }

    [Fact]
    public void A_crate_on_a_jump_rooms_open_floor_away_from_its_ledges_is_allowed()
    {
        var plan = new FloorPlan(Gallery);
        var (x, y) = Floor(plan, RoomDresser.TakeOff + 96, 2000);

        Assert.True(Gallery.Entry.Jump);
        Assert.False(RoomDresser.JumpAirRefuses(Gallery, plan, At(x, y, 48, 200)));
    }

    [Fact]
    public void A_crate_on_a_ledges_take_off_patch_is_refused()
    {
        var plan = new FloorPlan(Gallery);
        var (x, y) = Floor(plan, 0, RoomDresser.TakeOff - 72);

        Assert.True(RoomDresser.JumpAirRefuses(Gallery, plan, At(x, y, 48, 60)));
    }

    [Fact]
    public void A_room_that_is_no_jump_room_refuses_nothing_by_its_ledges()
    {
        Room chamber = Gallery with { Entry = Gallery.Entry with { Jump = false } };
        var plan = new FloorPlan(chamber);
        var (x, y) = Floor(new FloorPlan(Gallery), 0, RoomDresser.TakeOff - 72);

        Assert.False(RoomDresser.JumpAirRefuses(chamber, plan, At(x, y, 48, 60)));
    }
}
