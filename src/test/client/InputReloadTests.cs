using SourceSharp.Client;
using SourceSharp.Client.Input;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// cs_cl_reload and the input (plan_client_novalve §3.8's input row): the
/// button states, the mouse's capture and the camera are managed, so they go
/// across as bytes, and native does not Init the reloaded input again.
/// </summary>
public class InputReloadTests
{
    private static byte[] Saved(ClientInput input)
    {
        var writer = new ReloadWriter();
        input.Save(writer);
        return writer.ToArray();
    }

    [Fact]
    public void AForwardHeldAcrossTheReloadStillMovesForwardAndStillReleases()
    {
        var before = new InputRig();
        before.Run("+forward 87");
        Assert.Equal(225.0f, before.Move(1).ForwardMove);  // pressed during the frame: half of cl_forwardspeed

        var after = new InputRig(init: false);
        after.Input.Restore(new ReloadReader(Saved(before.Input)));

        Assert.Equal(450.0f, after.Move(2).ForwardMove);   // held the whole frame: the next command still moves forward
        after.Run("-forward 87");
        Assert.Equal(0.0f, after.Move(3).ForwardMove);     // and the key is not stuck
    }

    [Fact]
    public void TheMouseIsStillCapturedAfterTheReload()
    {
        // Native acquires the input once and does not call Init again: a
        // restore that dropped the capture would leave the mouse dead until the
        // engine next toggled it.
        var before = new InputRig();
        Assert.True(before.Input.MouseActive);

        var after = new InputRig(init: false);
        Assert.False(after.Input.MouseActive);
        after.Input.Restore(new ReloadReader(Saved(before.Input)));

        Assert.True(after.Input.MouseActive);
        after.Devices.RawX = 10;
        Assert.Equal(30, after.Move().MouseDx);            // 10 counts at the default sensitivity, 3
    }

    [Fact]
    public void ARestoredInputSavesTheSameBytes()
    {
        var before = new InputRig();
        before.Run("+speed 304");
        before.Run("+jump 32");
        before.Run("impulse 101");
        before.Devices.RawX = 30;
        before.Devices.RawY = -12;
        before.Move();
        before.Input.ThirdPersonCamera.CamToThirdPerson();
        byte[] bytes = Saved(before.Input);

        var after = new InputRig(init: false);
        var reader = new ReloadReader(bytes);
        after.Input.Restore(reader);

        Assert.True(reader.AtEnd);
        Assert.Equal(bytes, Saved(after.Input));
        Assert.Equal(before.Input.Snapshot(), after.Input.Snapshot());
        Assert.True(after.Input.CamIsThirdPerson());
    }

    [Fact]
    public void ASectionOfAnotherShapeIsRefused()
    {
        var after = new InputRig(init: false);
        Assert.Throws<ReloadFormatException>(() => after.Input.Restore(new ReloadReader([9, 0, 0, 0])));
    }
}
