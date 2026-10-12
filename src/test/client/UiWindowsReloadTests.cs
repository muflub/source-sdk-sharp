using System.Buffers.Binary;
using System.Text;

using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The UI's windows carried over a reload, in what the UI carries (plan_client_ui U2, U5).</summary>
public class UiWindowsReloadTests
{
    /// <summary>Two windows as the toolkit carries them: console placed and on top, browser unplaced below.</summary>
    private static byte[] TwoWindows()
    {
        var b = new List<byte>();
        void Int(int v) { var x = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(x, v); b.AddRange(x); }
        void Str(string v) { var u = Encoding.UTF8.GetBytes(v); Int(u.Length); b.AddRange(u); }
        void Flt(float v) => Int(BitConverter.SingleToInt32Bits(v));
        Int(2);
        Str("serverbrowser"); Int(0); Flt(0); Flt(0); Flt(0); Flt(0);
        Str("console"); Int(1); Flt(120); Flt(80); Flt(900); Flt(500);
        return b.ToArray();
    }

    private static UiHost Host() => UiHostKit.Build(new Ui.UiFidelityTests.RecordingSurface(), []);

    [Fact]
    public void Placements_and_order_survive_a_save_and_restore()
    {
        var before = Host();
        before.Windows.Restore(TwoWindows());
        var after = Host();
        after.Restore(before.Carry());

        Assert.True(after.Windows.TryGet("console", out var placement));
        Assert.Equal(new UiWindowPlacement(120, 80, 900, 500), placement);
        Assert.Equal("console", after.Windows.Top);
        Assert.Equal(before.Windows.Carry(), after.Windows.Carry());
    }

    [Fact]
    public void A_restore_replaces_what_the_new_build_had()
    {
        var after = Host();
        after.Windows.Restore(TwoWindows());
        Assert.Equal("console", after.Windows.Top);

        after.Restore(Host().Carry());   // nothing open

        Assert.Null(after.Windows.Top);
        Assert.False(after.Windows.TryGet("console", out _));
    }

    [Fact]
    public void A_byte_run_reads_back_and_a_short_one_is_refused()
    {
        var writer = new ReloadWriter();
        writer.Write((ReadOnlySpan<byte>)[1, 2, 3]);
        byte[] bytes = writer.ToArray();
        Assert.Equal([1, 2, 3], new ReloadReader(bytes).ReadBytes());
        Assert.Throws<ReloadFormatException>(() => new ReloadReader(bytes[..5]).ReadBytes());
    }
}
