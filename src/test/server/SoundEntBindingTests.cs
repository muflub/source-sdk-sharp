using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The disconnect path hands managed the leaving client's entity index, and
/// managed takes one off it for the reserved slot (CSoundEnt::ClientSoundIndex):
/// the arithmetic left native code with it (plan_server_native_v2 T4).
/// </summary>
public class SoundEntBindingTests
{
    public class Module : DispatchProxy
    {
        public IServerSoundEnt? Sounds;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method!.Name == "get_SoundEnt" ? Sounds : throw new NotSupportedException(method.Name);
    }

    private sealed class Recorder : IServerSoundEnt
    {
        public readonly List<int> ResetFor = [];

        public int Query(SoundType typeMask, Span<HeardSound> into) => throw new NotSupportedException();

        public void Insert(int type, Vector origin, int volume, float duration, EntityHandle owner = default,
                           int channel = SoundChannel.Unspecified, EntityHandle target = default)
            => throw new NotSupportedException();

        public void Initialize(int maxClients, float curTime) => throw new NotSupportedException();

        public void Shutdown() => throw new NotSupportedException();

        public void ResetClientSoundOf(int clientEntIndex) => ResetFor.Add(clientEntIndex);
    }

    [Fact]
    public void ALeavingClientIsResetByItsEntityIndex()
    {
        var sounds = new Recorder();
        var module = DispatchProxy.Create<IServerModule, Module>();
        ((Module)(object)module).Sounds = sounds;

        new SoundEntBinding(new Crossings(), module).ServerSoundEntResetClientSound(3);

        Assert.Equal([3], sounds.ResetFor);
    }
}
