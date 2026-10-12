using System.Runtime.InteropServices;

using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ViewTargetSetup is laid out as SourceSharpViewTargetSetup (lane camproto):
/// shell_view_targets.cpp's COMPILE_TIME_ASSERTs hold the same numbers from the
/// native side.
/// </summary>
public class ViewTargetSetupLayoutTests
{
    [Fact]
    public void The_setup_is_laid_out_as_the_native_struct()
    {
        Assert.Equal(64, Marshal.SizeOf<ViewTargetSetup>());
        Assert.Equal(24, (int)Marshal.OffsetOf<ViewTargetSetup>(nameof(ViewTargetSetup.Fov)));
        Assert.Equal(36, (int)Marshal.OffsetOf<ViewTargetSetup>(nameof(ViewTargetSetup.OrthoLeft)));
        Assert.Equal(52, (int)Marshal.OffsetOf<ViewTargetSetup>(nameof(ViewTargetSetup.Width)));
        Assert.Equal(60, (int)Marshal.OffsetOf<ViewTargetSetup>(nameof(ViewTargetSetup.Flags)));
    }
}
