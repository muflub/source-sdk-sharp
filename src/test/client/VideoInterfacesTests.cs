using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The numbers the video options' engine reads take by layout
/// (plan_gameui_managed.md G2): video_config_layout_test holds the same
/// numbers to the C++ declarations.
/// </summary>
public class VideoInterfacesTests
{
    [Fact]
    public void The_video_configs_offsets_are_the_native_tests()
    {
        Assert.Equal(0, VideoConfigLayout.Width);
        Assert.Equal(4, VideoConfigLayout.Height);
        Assert.Equal(52, VideoConfigLayout.Flags);
        Assert.Equal(1u, VideoConfigLayout.WindowedFlag);
    }

    [Fact]
    public void A_video_mode_is_four_ints_like_vmode_t()
    {
        Assert.Equal(16, Marshal.SizeOf<VideoMode>());
        Assert.Equal(12, (int)Marshal.OffsetOf<VideoMode>("<RefreshRate>k__BackingField"));
    }
}
