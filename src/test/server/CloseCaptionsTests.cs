using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The close caption, decided and sent by C# (user-message payload model):
/// stock's CSoundEmitterSystem::EmitCloseCaption past the facts sound_emitter.cpp
/// looks up and forwards.
/// </summary>
public sealed unsafe class CloseCaptionsTests : IDisposable
{
    private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));

    public void Dispose() => NativeMemory.Free(_globals);

    private (CloseCaptions Captions, RecordingEngine Engine) Make(int maxClients, string? closecaption)
    {
        _globals->maxClients = maxClients;
        // What the side checks before it accepts a block as the engine's
        // (EngineSide.RefuseGlobalVars).
        _globals->nTimestampNetworkingBase = 100;
        _globals->nTimestampRandomizeWindow = 32;
        var engine = Mock();
        engine.Returns("Console.FindConVarValue", closecaption);
        var side = TestSides.Create(Side.Server);
        side.BindGlobalVars(Side.Server, (nint)_globals);
        return (new CloseCaptions(engine, SideBindings.Of<IEngineEnvironment>(side), engine), engine);
    }

    private static List<(string Key, int Player, bool Reliable, byte[] Payload)> Sent(RecordingEngine engine) =>
        engine.Calls.Where(c => c.Member == "UserMessages.Send")
                    .Select(c => ((string)c.Arg(0)!, (int)c.Arg(2)!, (bool)c.Arg(3)!, (byte[])c.Arg(4)!))
                    .ToList();

    //-------------------------------------------------------------------------
    // Whether a caption goes at all: stock's first line.
    //-------------------------------------------------------------------------

    [Fact]
    public void NoCaptionsInMultiplayer()
    {
        var (captions, engine) = Make(maxClients: 2, closecaption: "1");

        Assert.False(captions.Wanted);
        Assert.Equal(0, captions.Emit([1, 2], true, "Hello", 1f, false, false, 0));
        Assert.Empty(Sent(engine));
    }

    [Fact]
    public void SinglePlayerFollowsTheConVar()
    {
        Assert.True(Make(1, "1").Captions.Wanted);
        Assert.False(Make(1, "0").Captions.Wanted);
        Assert.False(Make(1, null).Captions.Wanted);
    }

    [Fact]
    public void NoGlobalsYetMeansNoCaption()
    {
        var engine = Mock();
        engine.Returns("Console.FindConVarValue", "1");
        var captions = new CloseCaptions(engine, SideBindings.Of<IEngineEnvironment>(TestSides.Create(Side.Server)), engine);

        Assert.False(captions.Wanted);
    }

    //-------------------------------------------------------------------------
    // What is sent.
    //-------------------------------------------------------------------------

    [Fact]
    public void EachRecipientGetsTheCaption()
    {
        var (captions, engine) = Make(1, "1");

        Assert.Equal(1, captions.Emit([1, 0], reliable: false, "NPC.Hello", 2.5f, fromPlayer: false, warnIfMissing: true, gender: 0));

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("CloseCaption", 1, false), (only.Key, only.Player, only.Reliable));
        Assert.Equal([.. "npc.hello"u8, 0, 25, 0, 1], only.Payload);
    }

    [Fact]
    public void TheTokenIsLoweredAndItsNewlineEscapesMadeReal()
    {
        Assert.Equal("a\nb\\c", CloseCaptions.PayloadOf("A\\nB\\C", 1f, false, false, 0).Token);
        Assert.Equal("plain", CloseCaptions.FixEscapes("plain"));
        Assert.Equal("end\\", CloseCaptions.FixEscapes("end\\"));
    }

    [Fact]
    public void TheDurationHintIsTenthsCappedAt255()
    {
        Assert.Equal(25, CloseCaptions.PayloadOf("t", 2.5f, false, false, 0).Duration);
        Assert.Equal(255, CloseCaptions.PayloadOf("t", 30f, false, false, 0).Duration);
    }

    [Fact]
    public void TheFlagsCarryWarnPlayerAndGender()
    {
        Assert.Equal(1 | 2 | 4, CloseCaptions.PayloadOf("t", 1f, true, true, CloseCaptions.GenderMale).Flags);
        Assert.Equal(8, CloseCaptions.PayloadOf("t", 1f, false, false, CloseCaptions.GenderFemale).Flags);
        Assert.Equal(0, CloseCaptions.PayloadOf("t", 1f, false, false, 0).Flags);
    }

    //-------------------------------------------------------------------------
    // The native forward.
    //-------------------------------------------------------------------------

    private sealed class Heard : ICloseCaptions
    {
        public bool Wanted { get; set; } = true;
        public List<(int[] Slots, bool Reliable, string Token, float Duration, bool FromPlayer, bool Warn, int Gender)> Calls { get; } = new();

        public int Emit(ReadOnlySpan<int> recipients, bool reliable, string token, float duration,
                        bool fromPlayer, bool warnIfMissing, int gender)
        {
            Calls.Add((recipients.ToArray(), reliable, token, duration, fromPlayer, warnIfMissing, gender));
            return recipients.Length;
        }
    }

    [Fact]
    public void TheBindingHandsNativesFactsToTheCaptions()
    {
        var heard = new Heard();
        var binding = new UserMessagesBinding(new UserMessageRegistry(new RecordingLog(), receives: false), heard);

        int[] slots = [3, 5];
        byte[] token = [.. "Hi.There"u8, 0];
        fixed (int* s = slots)
        fixed (byte* t = token)
            binding.EmitCloseCaption(s, 2, 1, t, 1.5f, 1, 0, 2);

        var call = Assert.Single(heard.Calls);
        Assert.Equal([3, 5], call.Slots);
        Assert.Equal((true, "Hi.There", 1.5f, true, false, 2),
                     (call.Reliable, call.Token, call.Duration, call.FromPlayer, call.Warn, call.Gender));
    }

    [Fact]
    public void TheBindingAnswersWhetherACaptionIsWanted()
    {
        var heard = new Heard { Wanted = false };
        var binding = new UserMessagesBinding(new UserMessageRegistry(new RecordingLog(), receives: false), heard);
        Assert.Equal(0, binding.CloseCaptionsWanted());

        heard.Wanted = true;
        Assert.Equal(1, binding.CloseCaptionsWanted());

        Assert.Equal(0, new UserMessagesBinding(new UserMessageRegistry(new RecordingLog(), receives: false)).CloseCaptionsWanted());
    }

    /// <summary>
    /// The four bits C# sets in the CloseCaption message are shareddefs.h's
    /// own: the client reads them by those names. Read from the farm's
    /// shareddefs.h, the one client.so compiles (carried over from
    /// closecaption_recipient_test's TheFlagBitsAreSharedDefsOwn, lane HC).
    /// </summary>
    [Fact]
    public void TheFlagBitsAreSharedDefsOwn()
    {
        string header = File.ReadAllText(RepoLayout.Sdk("src", "game", "shared", "shareddefs.h"));

        int Bit(string name)
        {
            var m = Regex.Match(header, @"^#define\s+" + name + @"\s+\(\s*1\s*<<\s*(\d+)\s*\)\s*$", RegexOptions.Multiline);
            Assert.True(m.Success, $"shareddefs.h has no `#define {name} ( 1<<n )`");
            return 1 << int.Parse(m.Groups[1].Value);
        }

        Assert.Equal((int)CloseCaptions.CaptionFlags.WarnIfMissing, Bit("CLOSE_CAPTION_WARNIFMISSING"));
        Assert.Equal((int)CloseCaptions.CaptionFlags.FromPlayer, Bit("CLOSE_CAPTION_FROMPLAYER"));
        Assert.Equal((int)CloseCaptions.CaptionFlags.GenderMale, Bit("CLOSE_CAPTION_GENDER_MALE"));
        Assert.Equal((int)CloseCaptions.CaptionFlags.GenderFemale, Bit("CLOSE_CAPTION_GENDER_FEMALE"));
    }
}
