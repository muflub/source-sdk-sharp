using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Rumble user message reaches the managed mixer in managed code (lane
/// c6-input): the handler's decode, ClientMessageEffects' Rumble, the input
/// service's RumbleEffect at the client's time, ClientRumble's StartEffect. No
/// native slot is in between any more.
/// </summary>
public class RumbleMessageTests
{
    private sealed class NoRandom : SourceSharp.Abi.IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => min;
    }

    [Fact]
    public void TheMessageStartsTheEffectOnTheMixerAtTheClientsTime()
    {
        var rig = new InputRig();
        var mixer = new ClientRumble(rig.Devices, rig.Cvars, new NoRandom());
        var service = new ClientInputService(rig.Input, mixer, () => true, curTime: () => 12.5f);
        var effects = new ClientMessageEffects(new SourceSharp.Abi.RecordingEngine(), null, rumble: m => service.RumbleEffect(m.Waveform, m.Data, m.Flags));

        new RumbleMessage(effects).OnReceived(new Rumble { Waveform = ClientRumble.Ar2AltFire, Data = 50, Flags = ClientRumble.FlagInitialScale });

        ClientRumble.Channel c = mixer.Channels[0];
        Assert.True(c.InUse);
        Assert.Equal(ClientRumble.Ar2AltFire, c.Waveform);
        Assert.Equal(12.5f, c.StartTime);
        Assert.Equal(0.5f, c.Scale);
    }

    [Fact]
    public void TheFrameDrivesTheMotorsOnlyForALivingLocalPlayer()
    {
        var rig = new InputRig();
        var mixer = new ClientRumble(rig.Devices, rig.Cvars, new NoRandom());
        bool alive = true;
        var service = new ClientInputService(rig.Input, mixer, () => alive, curTime: () => 1.0f);

        service.RumbleEffect(ClientRumble.FlatBoth, 0, 0);
        service.RumbleFrame();
        Assert.Equal((1.0f, 1.0f), rig.Devices.Rumble[^1]);

        alive = false;
        service.RumbleFrame();
        Assert.False(mixer.Channels[0].InUse);
    }
}
