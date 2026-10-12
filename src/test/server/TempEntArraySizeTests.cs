using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One number, pinned, because it is invisible in every build this project
/// gates on.
///
/// <para>
/// An array field's <c>SizeBytes</c> is the size of ONE ELEMENT. Native expands
/// such a field into the two props stock's <c>SendPropArray</c> macro expands to
/// (plan_tmp.md 3.3) — an element template built with <c>nKind</c> forced to
/// <c>VECTOR</c>, then the <c>DPT_Array</c> prop — and the template goes through
/// <c>SendPropVector</c>, which takes this number as <c>sizeofVar</c> and
/// asserts it equals <c>sizeof(Vector)</c>
/// (<c>shell_tempents.cpp:265</c>, <c>dt_send.cpp</c>).
/// </para>
///
/// <para>
/// It was zero. An <c>Assert</c> is compiled out at <c>-O2</c>, and
/// <c>build_check.sh</c> builds release — so the only build that would have said
/// anything is <c>make debug</c>, and only at the moment a spline registers.
/// Lane F found it by reading rather than by running. **This test is a check of
/// the value, not of the assert**: nothing here can fire an engine assert, so a
/// debug run is still the only thing that proves the other half.
/// </para>
/// </summary>
public class TempEntArraySizeTests
{
    [Fact]
    public void AnArrayFieldMeasuresOneElementRatherThanTheWholeRun()
    {
        TeClass spline = TeClassBuilder.Build(typeof(TeBeamSpline));

        TeField points = spline.Fields.Single(f => f.Kind == TePropKind.Array);

        Assert.True(points.SizeBytes == points.ElemStride,
            "an array field's SizeBytes is its element stride, because that is what "
            + "SendPropVector is handed as sizeofVar for the element template");

        Assert.True(points.SizeBytes == 12,
            "and a spline point is a whole Vector, so twelve bytes — the number "
            + "SendPropVector asserts against sizeof(Vector) on a debug build");

        Assert.True(points.Elems * points.ElemStride <= spline.BlobBytes,
            "the whole run still fits inside the blob it is addressed in");
    }

    [Fact]
    public void NoArrayFieldAnywhereMeasuresZero()
    {
        // Every declaration in the shared assembly, found the way the registry
        // finds them. Base offsets are not resolved here and do not need to be:
        // an element's SIZE does not depend on where its field starts.
        IEnumerable<Type> declared = typeof(TeBeamSpline).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TempEntityAttribute>() is not null);

        int arrays = 0;

        foreach (Type type in declared)
        {
            TeClass te = TeClassBuilder.Build(type);

            foreach (TeField f in te.Fields.Where(f => f.Kind == TePropKind.Array))
            {
                Assert.True(f.SizeBytes > 0,
                    $"{te.TableName}.{f.Name} measures a real element — a zero here trips "
                    + "SendPropVector's sizeofVar assert on every debug build, and says "
                    + "nothing at all on a release one");
                arrays++;
            }
        }

        Assert.True(arrays == 1,
            "and the sweep actually found the one array in all forty-four tables — "
            + $"a zero here would mean this test passed by looking at nothing (found {arrays})");
    }
}
