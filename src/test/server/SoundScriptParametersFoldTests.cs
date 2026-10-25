using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A sound script's parameters through ISoundEmitterSystemBase's forwarders
/// (2026-10-19), where the SoundScriptParameters slot made the same calls on
/// both sides: GetActorGender( NULL ), then GetParametersForSound into a
/// CSoundParameters its constructor had filled first.
/// </summary>
public sealed unsafe class SoundScriptParametersFoldTests : System.IDisposable
{
    private static int s_Gender = -1;
    private static int s_GenderAsked;
    private static nint s_ActorAsked = -1;
    private static SoundParametersLayout s_Seen;

    [UnmanagedCallersOnly]
    private static int Gender(void* self, byte* actor)
    {
        s_ActorAsked = (nint)actor;
        s_GenderAsked++;
        return 0;   // GENDER_NONE
    }

    [UnmanagedCallersOnly]
    private static byte Parameters(void* self, byte* name, void* parameters, int gender, byte emitted)
    {
        s_Gender = gender;
        var p = (SoundParametersLayout*)parameters;
        s_Seen = *p;
        if (Marshal.PtrToStringUTF8((nint)name) != "Wood.ScrapeRough")
            return 0;

        p->Volume = 0.7f;
        p->Pitch = 95;
        p->PitchLow = 90;
        p->PitchHigh = 110;
        p->SoundLevel = 80;
        return 1;
    }

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

    public SoundScriptParametersFoldTests()
    {
        s_Gender = -1;
        s_GenderAsked = 0;
        s_ActorAsked = -1;
        s_Seen = default;
    }

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(_self);
    }

    private IEngineSound Sound(Side side)
    {
        _root.Interface(SoundEmitterBase.InterfaceVersion, _self).Forwarders(out SoundEmitterSystemFwd* fwd);
        fwd->GetActorGender = &Gender;
        fwd->GetParametersForSound = &Parameters;

        var api = TestSides.Create(side);
        api.Attachment.Initialize();
        string why;
        Assert.True(api.Attachment.AttachShims(_root.Bind(), out why), why);
        return SideBindings.Of<IEngineSound>(api);
    }

    [Fact]
    public void AScriptsParametersAreReadOnTheServer()
        => Assert.Equal(new SoundScriptParams(0.7f, 95, 90, 110, 80),
                        Sound(Side.Server).SoundScriptParameters("Wood.ScrapeRough"));

    [Fact]
    public void AndOnTheClient()
        => Assert.Equal(new SoundScriptParams(0.7f, 95, 90, 110, 80),
                        Sound(Side.Client).SoundScriptParameters("Wood.ScrapeRough"));

    [Fact]
    public void AScriptWithNoSuchEntryIsNull()
        => Assert.Null(Sound(Side.Server).SoundScriptParameters("Nothing.Here"));

    [Fact]
    public void TheGenderIsTheNullActorsAndTheParametersStartAsTheConstructorLeavesThem()
    {
        Sound(Side.Server).SoundScriptParameters("Nothing.Here");

        Assert.Equal((1, (nint)0, 0), (s_GenderAsked, s_ActorAsked, s_Gender));
        Assert.Equal((0, 1.0f, 100, 100, 100, 75), (s_Seen.Channel, s_Seen.Volume, s_Seen.Pitch,
                                                   s_Seen.PitchLow, s_Seen.PitchHigh, s_Seen.SoundLevel));
    }

    [Theory]
    [InlineData(nameof(SoundParametersLayout.Channel), 0)]
    [InlineData(nameof(SoundParametersLayout.Volume), 4)]
    [InlineData(nameof(SoundParametersLayout.Pitch), 8)]
    [InlineData(nameof(SoundParametersLayout.PitchLow), 12)]
    [InlineData(nameof(SoundParametersLayout.PitchHigh), 16)]
    [InlineData(nameof(SoundParametersLayout.SoundLevel), 20)]
    [InlineData(nameof(SoundParametersLayout.PlayToOwnerOnly), 24)]
    [InlineData(nameof(SoundParametersLayout.Count), 28)]
    [InlineData(nameof(SoundParametersLayout.SoundName), 32)]
    [InlineData(nameof(SoundParametersLayout.DelayMsec), 160)]
    public void EachFieldIsAtTheHeadersOffset(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SoundParametersLayout>(field));

    [Fact]
    public void TheLayoutIsTheHeadersSize()
        => Assert.Equal(SoundParametersLayout.SizeInBytes, sizeof(SoundParametersLayout));
}
