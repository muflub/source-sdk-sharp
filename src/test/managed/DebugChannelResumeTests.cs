extern alias client;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using System.Buffers.Binary;
using System.Diagnostics;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using Debug.Api;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The debug channel survives a managed reload of either side.
///
/// <para>
/// The bug these pin: after <c>cs_cl_reload</c> on a listen server started with
/// <c>-debuguiserve -debuguiport 0 -debugui</c>, <c>/clients</c> went from [1] to []
/// and stayed there. The join's invitation was the only one and its nonce was
/// spent. Now a client reload is client-driven (it carries resume data across
/// and redials with a single-use resume token), and a server reload is
/// server-driven (the new module invites the clients already in, and a fresh
/// invitation beats the client's stale resume data).
/// </para>
///
/// <para>
/// Real sockets on port 0, as <see cref="DebugChannelSessionTests"/> uses, for
/// its reason: the property is a handshake's, and a double would not notice a
/// header the two ends disagree about.
/// </para>
/// </summary>
public class DebugChannelResumeTests
{
    private static bool Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return condition();
    }

    private static DebugChannelListener Listening()
    {
        var listener = new DebugChannelListener(0, false, _ => { }, _ => { });
        listener.Start();
        return listener;
    }

    private static DebugChannelClient Client()
        => new("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));

    /// <summary>A client attached through an invitation for <paramref name="slot"/>, holding resume data.</summary>
    private static DebugChannelClient Joined(DebugChannelListener listener, int slot, out byte[] resume)
    {
        var client = Client();
        client.Attach(listener.Invite(slot)!.Value);

        byte[]? carried = null;
        Assert.True(Until(() => client.Attached && (carried = client.SaveResume()) is not null),
            "an invited client attaches and its session is handed resume data");

        resume = carried!;
        return client;
    }

    /// <summary>Waits for a resume attempt to finish one way or the other.</summary>
    private static void Settled(DebugChannelClient client)
        => Assert.True(Until(() => client.Attached || !client.Running), "the resume attempt finished");

    //-------------------------------------------------------------------------
    // cs_cl_reload: the client resumes by itself.
    //-------------------------------------------------------------------------

    [Fact]
    public void AReloadedClientWithCarriedResumeDataReconnectsWithoutAnInvitation()
    {
        using var listener = Listening();
        var before = Joined(listener, 2, out byte[] carried);

        // The old module goes, as cs_cl_reload takes it; nothing is invited again.
        before.Dispose();
        Assert.True(Until(() => listener.AttachedSlots().Count == 0), "the old session closed");

        using var reloaded = Client();
        reloaded.Resume(carried);

        Assert.True(Until(() => reloaded.Attached && listener.AttachedSlots().SequenceEqual(new[] { 2 })),
            "the reloaded client is back on slot 2 with no invitation");
    }

    [Fact]
    public void AResumeTokenWorksOnceAndASecondUseIsRefused()
    {
        using var listener = Listening();
        Joined(listener, 1, out byte[] carried).Dispose();

        using (var first = Client())
        {
            first.Resume(carried);
            Assert.True(Until(() => first.Attached), "the first use of the token is accepted");
        }

        int refusedBefore = listener.Status().Refused;

        using var second = Client();
        second.Resume(carried);
        Settled(second);

        Assert.False(second.Attached, "the same token, presented again, is refused");
        Assert.True(Until(() => listener.Status().Refused > refusedBefore), "the listener counted a refusal");
    }

    [Fact]
    public void AResumedSessionIsHandedTheNextToken()
    {
        using var listener = Listening();
        Joined(listener, 1, out byte[] carried).Dispose();

        byte[]? next = null;
        using (var resumed = Client())
        {
            resumed.Resume(carried);
            Assert.True(Until(() => resumed.Attached && (next = resumed.SaveResume()) is not null),
                "a resumed session is handed resume data of its own");
        }

        Assert.NotEqual(carried, next);

        // A second reload in a row resumes with the token the first one was given.
        using var again = Client();
        again.Resume(next!);
        Assert.True(Until(() => again.Attached), "the next token works");
    }

    [Fact]
    public void ATokenPresentedForAnotherSlotIsRefused()
    {
        using var listener = Listening();
        Joined(listener, 1, out byte[] carried).Dispose();

        byte[] otherSlot = (byte[])carried.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(otherSlot.AsSpan(8), 3);

        using (var wrong = Client())
        {
            wrong.Resume(otherSlot);
            Settled(wrong);
            Assert.False(wrong.Attached, "slot 1's token is not good for slot 3");
        }

        // And the wrong-slot attempt did not spend slot 1's token.
        using var right = Client();
        right.Resume(carried);
        Assert.True(Until(() => right.Attached), "the token still works for the slot it was issued to");
    }

    [Fact]
    public void ATokenFromThePreviousListenerIsRefused()
    {
        byte[] carried;

        using (var previous = Listening())
            Joined(previous, 1, out carried).Dispose();

        // cs_sv_reload: a new listener. The carried data is pointed at it, so the
        // only thing that can refuse it is the token.
        using var listener = Listening();
        byte[] pointed = (byte[])carried.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(pointed.AsSpan(4), listener.Port);

        using var stale = Client();
        stale.Resume(pointed);
        Settled(stale);

        Assert.False(stale.Attached, "a token the new listener never issued is refused");
        Assert.True(Until(() => listener.Status().Refused == 1), "refused at the handshake, by the new listener");
    }

    //-------------------------------------------------------------------------
    // cs_sv_reload: the server invites, and the fresh invitation wins.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFreshInvitationWinsOverStaleResumeData()
    {
        DebugChannelClient client;
        byte[] stale;

        using (var previous = Listening())
        {
            client = Joined(previous, 1, out stale);
        }

        using (client)
        {
            Assert.True(Until(() => !client.Attached), "the old listener's session is gone");

            using var listener = Listening();
            client.Attach(listener.Invite(1)!.Value);

            byte[]? fresh = null;
            Assert.True(Until(() => client.Attached && (fresh = client.SaveResume()) is not null &&
                                    !fresh.SequenceEqual(stale)),
                "the client attaches to the new listener and its resume data is replaced");

            Assert.True(DebugChannelResume.TryRead(fresh, out var read, out _));
            Assert.Equal(listener.Port, read.Port);
        }
    }

    [Fact]
    public void AFreshInvitationReplacesAChannelStillRunning()
    {
        // The client has not yet seen its old session die when the reloaded
        // server's invitation arrives: the invitation still wins.
        using var previous = Listening();
        using var client = Joined(previous, 1, out _);

        using var listener = Listening();
        client.Attach(listener.Invite(1)!.Value);

        Assert.True(Until(() => client.Attached && listener.AttachedSlots().SequenceEqual(new[] { 1 })),
            "the fresh invitation is dialled rather than ignored");
    }

    [Fact]
    public void AReloadedServerInvitesEveryConnectedSlotOnItsFirstFrame()
    {
        var invites = new DebugChannelReloadInvites();

        Assert.Equal(new[] { 1, 3 }, invites.Due(new[] { 1, 3 }.Contains));
    }

    [Fact]
    public void TheReloadSweepHappensOnceAndNotEveryFrame()
    {
        // Nothing marks the swept slots invited (a server whose channel is off
        // writes no invitation), and the sweep must still not run every frame.
        var invites = new DebugChannelReloadInvites();
        Assert.NotEmpty(invites.Due(new[] { 1, 3 }.Contains));

        Assert.Empty(invites.Due(new[] { 1, 3 }.Contains));
    }

    [Fact]
    public void TheSweepSkipsASlotTheJoinAlreadyInvited()
    {
        var invites = new DebugChannelReloadInvites();
        invites.Invited(1);

        Assert.Equal(new[] { 4 }, invites.Due(new[] { 1, 4 }.Contains));
    }

    //-------------------------------------------------------------------------
    // The carried bytes and the header.
    //-------------------------------------------------------------------------

    private const string Token = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void ResumeDataRoundTrips()
    {
        var resume = new DebugChannelResume(DebugChannelProtocol.Version, 27120, 5, Token);

        Assert.True(DebugChannelResume.TryRead(resume.ToBytes(), out var read, out string refusal), refusal);
        Assert.Equal(resume, read);
    }

    [Fact]
    public void ResumeDataWithAnythingAfterItsFieldsIsRefused()
    {
        byte[] bytes = new DebugChannelResume(DebugChannelProtocol.Version, 27120, 5, Token).ToBytes();

        Assert.False(DebugChannelResume.TryRead(bytes.Append((byte)'x').ToArray(), out _, out _));
    }

    [Fact]
    public void AResumeHeaderForNoPlayerSlotIsRefused()
    {
        Assert.True(DebugChannelResume.TryReadHeader($"1:{Token}", out _, out _));
        Assert.False(DebugChannelResume.TryReadHeader($"0:{Token}", out _, out _));
    }

    [Fact]
    public void ATokenFromAnotherTableIsRefused()
    {
        var issuing = new DebugChannelResumeTokens();
        var other = new DebugChannelResumeTokens();
        string header = DebugChannelResume.HeaderValue(1, issuing.Issue(1));

        Assert.False(other.Redeem(header, out _));
        Assert.True(issuing.Redeem(header, out int slot));
        Assert.Equal(1, slot);
    }
}
