using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The numbers the server browser's Steam reads take by layout
/// (plan_gameui_managed.md G3): steam_server_item_layout_test holds the same
/// numbers to gameserveritem_t's declaration; and a list entry read from its bytes.
/// </summary>
public class SteamServerBrowserTests
{
    [Fact]
    public void The_server_items_offsets_are_the_native_tests()
    {
        Assert.Equal((0, 2, 4, 8, 12), (SteamServerItemLayout.ConnectionPort, SteamServerItemLayout.QueryPort, SteamServerItemLayout.Ip,
                                        SteamServerItemLayout.Ping, SteamServerItemLayout.HadSuccessfulResponse));
        Assert.Equal((14, 46, 78), (SteamServerItemLayout.GameDir, SteamServerItemLayout.Map, SteamServerItemLayout.Description));
        Assert.Equal((148, 152, 156, 160, 161, 172), (SteamServerItemLayout.Players, SteamServerItemLayout.MaxPlayers,
                                                      SteamServerItemLayout.Bots, SteamServerItemLayout.Password,
                                                      SteamServerItemLayout.Secure, SteamServerItemLayout.Name));
        Assert.Equal((256, 512), (SteamServerItemLayout.FilterKeyBytes, SteamServerItemLayout.FilterBytes));
    }

    [Fact]
    public unsafe void An_item_reads_every_field_from_its_bytes()
    {
        byte* p = stackalloc byte[376];
        new Span<byte>(p, 376).Clear();
        *(ushort*)p = 27016;
        *(ushort*)(p + 2) = 27015;
        *(uint*)(p + 4) = 0xC0A8010A;
        *(int*)(p + 8) = 42;
        p[12] = 1;
        "mod_test"u8.CopyTo(new Span<byte>(p + 14, 32));
        "dm_runoff"u8.CopyTo(new Span<byte>(p + 46, 32));
        "SourceSharp"u8.CopyTo(new Span<byte>(p + 78, 64));
        *(int*)(p + 148) = 5;
        *(int*)(p + 152) = 16;
        *(int*)(p + 156) = 2;
        p[160] = 1;
        p[161] = 0;
        "Server"u8.CopyTo(new Span<byte>(p + 172, 64));
        Assert.Equal(new SteamServerItem(0xC0A8010A, 27015, 27016, 42, true, "mod_test", "dm_runoff", "SourceSharp", 5, 16, 2, true,
                                         false, "Server"), SteamServerItemLayout.Read(p));
    }
}
