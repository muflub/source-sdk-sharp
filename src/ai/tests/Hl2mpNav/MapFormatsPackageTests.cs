using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The reader arrives as map-tools' package at the pinned version
/// (plan_hl2mp_ai §6.1, Phase 2 step 2): a file its own writer wrote opens and
/// answers, and the assembly the binding runs is the version the props pin.
/// </summary>
public sealed class MapFormatsPackageTests
{
    private static Nav3dLevel TwoColumns()
    {
        const Nav3dLeafFlags floor = Nav3dLeafFlags.GroundedPlayer | Nav3dLeafFlags.GroundedNpc
                                     | Nav3dLeafFlags.WalkablePlayer | Nav3dLeafFlags.WalkableNpc;
        byte[] open = Nav3dClearance.Encode([], [], []);
        return new Nav3dLevel
        {
            CellSize = 32,
            VoxelSize = 16,
            CellVoxels = 2,
            Columns = 1,
            Rows = 1,
            Origin = new Vec3(0, 0, 0),
            FloorNormalZ = 0.7f,
            StepHeight = 18,
            JumpHeight = 56,
            JumpDistance = 100,
            Cells = [new Nav3dCell(null, 0, Nav3dRoomRole.None, 0, 0)],
            Presets = [new Nav3dPreset("standing", 32, 72, Nav3dClipClass.Player)],
            Roots = [0],
            ColumnStarts = [0, 1, 2, 2, 2],
            Leaves = [new(0, 2, floor, 256, 0, 0, 0, 0), new(0, 2, floor, 256, 0, 0, 0, 0)],
            Clearance = open,
        };
    }

    [Fact]
    public void TheReaderOpensAFileItsWriterWrote()
    {
        Nav3dReader nav = Nav3dReader.Open(Nav3dWriter.Write(TwoColumns()));

        Assert.Equal(2, nav.LeafCount);
        Assert.Equal(1, nav.FindLeaf(new Vec3(20, 4, 1)));
        Assert.Equal(-1, nav.FindLeaf(new Vec3(20, 20, 1)));
        Assert.Equal(18f, nav.StepHeight);
        Assert.True(nav.StandableAtCentre(0, 4, 20, Nav3dClipClass.Player));
    }

    [Fact]
    public void TheReaderRefusesBytesThatAreNotANav3d()
    {
        byte[] bytes = Nav3dWriter.Write(TwoColumns());
        bytes[0] ^= 0xFF;

        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Nav3dReader.Open(bytes));
        Assert.Contains("magic", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAssemblyIsThePinnedPackageVersion()
    {
        string props = File.ReadAllText(RepoLayout.Repo("src", "ai", "Directory.Build.props"));
        string pinned = props.Split("<MapFormatsVersion>")[1].Split("</MapFormatsVersion>")[0].Trim();
        Version? actual = typeof(Nav3dReader).Assembly.GetName().Version;

        Assert.NotNull(actual);
        Assert.Equal(pinned, $"{actual!.Major}.{actual.Minor}.{actual.Build}");
        Assert.Equal(pinned, SourceSharp.Ai.Hl2mp.Nav.NavReaderVersion.Package);
    }
}
