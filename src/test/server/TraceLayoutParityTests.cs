using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: <see cref="EngineRay"/> and
/// <see cref="EngineGameTrace"/> are trace_layout.h's rows, which the header's
/// static_asserts hold <c>Ray_t</c> and <c>CGameTrace</c> to. A header that
/// cannot be read, or names no rows, fails.
/// </summary>
public class TraceLayoutParityTests
{
    private readonly record struct Row(string Name, int Offset, int Size);

    private static string Header()
    {
        string path = RepoLayout.Repo("src", "shared", "native", "trace_layout.h");
        Assert.True(File.Exists(path), $"{path} is the layout's one statement and must exist");
        return File.ReadAllText(path);
    }

    private static List<Row> Rows(string macro)
    {
        string text = Header();
        int at = text.IndexOf("#define " + macro + "(F)", StringComparison.Ordinal);
        Assert.True(at >= 0, $"trace_layout.h defines {macro}");

        var rows = new List<Row>();
        foreach (string line in text[at..].Split('\n').Skip(1))
        {
            Match m = Regex.Match(line, @"^\s*F\(\s*(\w+),\s*(\d+),\s*(\d+)\s*\)\s*\\?\s*$");
            if (!m.Success)
                break;
            rows.Add(new Row(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private static int Define(string name)
    {
        Match m = Regex.Match(Header(), @"#define\s+" + name + @"\s+(\d+)");
        Assert.True(m.Success, $"trace_layout.h defines {name}");
        return int.Parse(m.Groups[1].Value);
    }

    // The header's C++ field -> the mirror's field that starts it.
    private static readonly Dictionary<string, string> RayFields = new()
    {
        ["m_Start"] = nameof(EngineRay.Start), ["m_Delta"] = nameof(EngineRay.Delta),
        ["m_StartOffset"] = nameof(EngineRay.StartOffset), ["m_Extents"] = nameof(EngineRay.Extents),
        ["m_IsRay"] = nameof(EngineRay.IsRay), ["m_IsSwept"] = nameof(EngineRay.IsSwept),
    };

    private static readonly Dictionary<string, string> TraceFields = new()
    {
        ["startpos"] = nameof(EngineGameTrace.StartPos), ["endpos"] = nameof(EngineGameTrace.EndPos),
        ["plane"] = nameof(EngineGameTrace.PlaneNormal), ["fraction"] = nameof(EngineGameTrace.Fraction),
        ["contents"] = nameof(EngineGameTrace.Contents), ["dispFlags"] = nameof(EngineGameTrace.DispFlags),
        ["allsolid"] = nameof(EngineGameTrace.AllSolid), ["startsolid"] = nameof(EngineGameTrace.StartSolid),
        ["fractionleftsolid"] = nameof(EngineGameTrace.FractionLeftSolid), ["surface"] = nameof(EngineGameTrace.SurfaceName),
        ["hitgroup"] = nameof(EngineGameTrace.HitGroup), ["physicsbone"] = nameof(EngineGameTrace.PhysicsBone),
        ["m_pEnt"] = nameof(EngineGameTrace.Entity), ["hitbox"] = nameof(EngineGameTrace.HitBox),
    };

    [Fact]
    public void EachRayRowStartsTheMirrorsFieldAtTheHeadersOffset()
    {
        List<Row> rows = Rows("SOURCESHARP_RAY_LAYOUT");
        Assert.Equal(RayFields.Keys.Order(), rows.Select(r => r.Name).Order());
        foreach (Row row in rows)
            Assert.True(row.Offset == (int)Marshal.OffsetOf<EngineRay>(RayFields[row.Name]),
                $"EngineRay.{RayFields[row.Name]} is at Ray_t::{row.Name}'s {row.Offset}");
    }

    [Fact]
    public void EachTraceRowStartsTheMirrorsFieldAtTheHeadersOffset()
    {
        List<Row> rows = Rows("SOURCESHARP_GAMETRACE_LAYOUT");
        Assert.Equal(TraceFields.Keys.Order(), rows.Select(r => r.Name).Order());
        foreach (Row row in rows)
            Assert.True(row.Offset == (int)Marshal.OffsetOf<EngineGameTrace>(TraceFields[row.Name]),
                $"EngineGameTrace.{TraceFields[row.Name]} is at CGameTrace::{row.Name}'s {row.Offset}");
    }

    [Fact]
    public void TheScalarTraceFieldsAreTheHeadersWidths()
    {
        // The aggregate rows (plane, surface) are read as their first member; the rest are one field each.
        foreach (Row row in Rows("SOURCESHARP_GAMETRACE_LAYOUT").Where(r => r.Name is not ("plane" or "surface")))
        {
            Type type = typeof(EngineGameTrace).GetField(TraceFields[row.Name])!.FieldType;
            Assert.True(row.Size == Marshal.SizeOf(type), $"CGameTrace::{row.Name} is {row.Size} bytes; the mirror's is {Marshal.SizeOf(type)}");
        }
    }

    [Fact]
    public void TheMirrorsAreTheHeadersSizes()
    {
        Assert.Equal(Define("SOURCESHARP_RAY_SIZEOF"), Marshal.SizeOf<EngineRay>());
        Assert.Equal(EngineRay.SizeOf, Define("SOURCESHARP_RAY_SIZEOF"));
        Assert.Equal(EngineRay.AlignOf, Define("SOURCESHARP_RAY_ALIGNOF"));
        Assert.Equal(Define("SOURCESHARP_GAMETRACE_SIZEOF"), Marshal.SizeOf<EngineGameTrace>());
        Assert.Equal(EngineGameTrace.SizeOf, Define("SOURCESHARP_GAMETRACE_SIZEOF"));
    }

    [Fact]
    public void ThePlaneAndSurfaceMembersSitWhereCplaneAndCsurfaceLayThemOut()
    {
        Assert.Equal(24 + 12, (int)Marshal.OffsetOf<EngineGameTrace>(nameof(EngineGameTrace.PlaneDist)));
        Assert.Equal(24 + 16, (int)Marshal.OffsetOf<EngineGameTrace>(nameof(EngineGameTrace.PlaneType)));
        Assert.Equal(64 + 8, (int)Marshal.OffsetOf<EngineGameTrace>(nameof(EngineGameTrace.SurfaceProps)));
        Assert.Equal(64 + 10, (int)Marshal.OffsetOf<EngineGameTrace>(nameof(EngineGameTrace.SurfaceFlags)));
    }
}
