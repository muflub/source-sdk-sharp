extern alias client;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using Debug.Api;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The two opt-ins, one per side, and the rule that the host is the client's
/// decision.
///
/// <para>
/// The feature's whole shape is "a server tells a client to open a control
/// channel". What keeps that a development tool rather than a lever is that both
/// ends have to have asked, separately, on their own command lines — and that
/// the client never takes an address from the server.
/// </para>
/// </summary>
public class DebugChannelGateTests
{
    /// <summary>A command line, as the process would have been given one.</summary>
    private static string[] Line(params string[] args)
        => new[] { "/opt/game/hl2_linux" }.Concat(args).ToArray();

    //-------------------------------------------------------------------------
    // GATE ONE: the client.
    //-------------------------------------------------------------------------

    [Fact]
    public void AClientWithoutTheFlagHasNotAskedForAChannel()
    {
        Assert.True(!DebugChannelClient.RequestedBy(Line("-game", "mod_sharp", "-debugapi")),
            "no -debugui on this client's own command line means no channel is built at all, so " +
            "there is nothing for a server's invitation to reach — and -debugapi being on the " +
            "line is what says this is about -debugui and not about an empty command line");
    }

    [Fact]
    public void AClientWithTheFlagHasAsked()
    {
        Assert.True(DebugChannelClient.RequestedBy(Line("-game", "mod_sharp", "-debugui")),
            "and with it, it has — so the negative above is about the flag and not about a " +
            "check that always says no");
    }

    [Fact]
    public void TheServersOwnFlagOnAClientsLineIsNotTheClientsOptIn()
    {
        Assert.True(!DebugChannelClient.RequestedBy(Line("-debuguiserve")),
            "the two flags are deliberately different words, and -debugui is a PREFIX of " +
            "-debuguiserve: a client started with the SERVER's flag has not opted in, and a " +
            "prefix match would have said it had");
    }

    /// <summary>
    /// THERE IS NO ENGINE ROUTE TO THE COMMAND LINE ANY MORE. Its two slots were
    /// filled on the server only and answered a plausible false on the client;
    /// both sides read their own argv now, and the host area has no member that
    /// could bring the route back unnoticed.
    /// </summary>
    [Fact]
    public void TheEngineHostHasNoCommandLineMember()
    {
        var members = typeof(IEngineHost).GetMembers().Select(m => m.Name).ToList();

        Assert.NotEmpty(members);
        Assert.DoesNotContain(members, m => m.Contains("CommandLine", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------
    // GATE TWO: the server. It reads its own command line the same way the
    // client does: the engine's command-line slots were retired for it.
    //-------------------------------------------------------------------------

    [Fact]
    public void AServerWithoutTheFlagHasNotOptedIn()
    {
        Assert.True(!DebugChannelListener.RequestedBy(Line()),
            "the channel is off by default: -debugapi puts the dev API in memory, and this is a " +
            "separate decision about letting a client connect back to it");
    }

    [Fact]
    public void AServerWithTheFlagHasOptedIn()
    {
        Assert.True(DebugChannelListener.RequestedBy(Line(DebugChannelProtocol.ServerParam)),
            "and with it, it has");
    }

    [Fact]
    public void TheFlagTheServerLooksForIsItsOwnAndNotTheClients()
    {
        Assert.True(!DebugChannelListener.RequestedBy(Line("-debugui"))
                        && DebugChannelProtocol.ServerParam != "-debugui",
            "a server that saw -debugui on a client's command line would be reading the wrong " +
            "side's intent; the two flags are deliberately different words");
    }

    //-------------------------------------------------------------------------
    // Loopback by default, and beyond it only on its own flag.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheChannelStaysOnLoopbackUnlessAskedOtherwise()
    {
        Assert.True(!DebugChannelListener.PublicFor(Line(DebugChannelProtocol.ServerParam, "-debuguiserve")),
            "a development channel reachable from the network is a different decision from " +
            "turning the channel on, and nobody gets it by typing -debuguiserve");
    }

    [Fact]
    public void BindingBeyondLoopbackHasItsOwnFlag()
    {
        Assert.True(DebugChannelListener.PublicFor(Line(DebugChannelProtocol.ServerPublicParam))
                        && !DebugChannelListener.PublicFor(Line(DebugChannelProtocol.ServerParam)),
            "-debuguipublic is its own word, so a server that only meant to enable the channel " +
            "does not get a public bind with it");
    }

    [Fact]
    public void ALoopbackChannelReallyBindsLoopbackAndSaysSo()
    {
        using var listener = new DebugChannelListener(0, false, _ => { }, _ => { });

        Assert.True(listener.Address.Contains("127.0.0.1", StringComparison.Ordinal)
                 && !listener.Public,
            "the default is not merely a flag that is off — the socket is on the loopback " +
            "address, which is what makes it unreachable from another machine");
    }

    [Fact]
    public void ThePortDefaultsWhenNoneWasNamed()
    {
        Assert.True(DebugChannelListener.PortFor(Line(DebugChannelProtocol.ServerParam))
                        == DebugChannelProtocol.DefaultPort,
            "a server that named no port gets the documented default");
    }

    [Fact]
    public void ANamedPortIsHonoured()
    {
        Assert.True(DebugChannelListener.PortFor(Line(DebugChannelProtocol.ServerPortParam, "27333")) == 27333,
            "two servers on one machine need to be able to differ");
    }

    [Fact]
    public void Port_zero_asks_for_any_free_port()
    {
        Assert.Equal(0, DebugChannelListener.PortFor(Line(DebugChannelProtocol.ServerPortParam, "0")));

        using var listener = new DebugChannelListener(0, false, _ => { }, _ => { });
        Assert.True(listener.Port > 0, "the bound port is what the invitation carries");
    }

    [Fact]
    public void ANonsensePortFallsBackRatherThanThrowing()
    {
        Assert.True(DebugChannelListener.PortFor(Line(DebugChannelProtocol.ServerPortParam, "99999"))
                        == DebugChannelProtocol.DefaultPort,
            "a typo on the command line must not take the bind down with an exception");
    }

    //-------------------------------------------------------------------------
    // THE HOST IS THE CLIENT'S DECISION.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheDefaultDialHostIsLoopback()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-debugui"), _ => { }) == "127.0.0.1",
            "a listen server and a dedicated server on this machine are nearly every " +
            "development case, and neither needs anybody to configure an address");
    }

    [Fact]
    public void AHostNamedOnTheClientsOwnCommandLineIsHonoured()
    {
        Assert.True(DebugChannelClient.ResolveHost(
                        Line("-debugui", "-debuguihost", "10.0.0.5"), _ => { }) == "10.0.0.5",
            "for a server across the network the person running THIS client names it — an " +
            "address they chose, and by construction one they already trusted enough to join");
    }

    [Fact]
    public void AHostAfterSomeOtherFlagIsNotMistakenForTheDialHost()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-connect", "10.0.0.5", "-debugui"), _ => { })
                    == DebugChannelProtocol.DefaultHost,
            "-debuguihost is the only flag a dial host comes from; a value sitting after some " +
            "other flag belongs to that flag, and reading it would be dialling an address " +
            "nobody offered for this");
    }

    [Fact]
    public void AHostFlagWithNothingAfterItFallsBackRatherThanReadingPastTheEnd()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-debugui", "-debuguihost"), _ => { })
                    == DebugChannelProtocol.DefaultHost,
            "a flag at the end of the line has no value, and the fallback is loopback");
    }

    [Fact]
    public void AHostFlagFollowedByAnotherFlagFallsBack()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-debuguihost", "-debugui"), _ => { })
                    == DebugChannelProtocol.DefaultHost,
            "somebody left the host off; the next flag is not the host's name");
    }

    [Fact]
    public void AUrlShapedHostIsRefusedAndLoopbackIsUsedInstead()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-debuguihost", "http://evil/x"), _ => { })
                    == DebugChannelProtocol.DefaultHost,
            "pasting a URL into the middle of a ws:// URI turns a host into a path; a value " +
            "with a slash in it is not a bare host");
    }

    [Fact]
    public void AHostWithUserinfoIsRefused()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-debuguihost", "someone@elsewhere"), _ => { })
                    == DebugChannelProtocol.DefaultHost,
            "an at-sign makes everything before it userinfo and everything after it the real " +
            "host, which is not what the person typing it meant");
    }

    [Fact]
    public void ARefusedHostSaysSoRatherThanSilentlyDiallingLoopback()
    {
        var warnings = new List<string>();

        DebugChannelClient.ResolveHost(Line("-debuguihost", "http://evil/x"), warnings.Add);

        Assert.True(warnings.Count == 1,
            "somebody who named a host and silently got loopback would spend the session " +
            "wondering why nothing connected");
    }

    [Fact]
    public void AGoodHostIsNotWarnedAbout()
    {
        var warnings = new List<string>();

        DebugChannelClient.ResolveHost(Line("-debuguihost", "10.0.0.5"), warnings.Add);

        Assert.True(warnings.Count == 0,
            "the warning above is about that value and not about every value");
    }

    [Fact]
    public void AnIpv6LiteralIsBracketedSoItsColonsAreNotReadAsAPort()
    {
        Assert.True(DebugChannelClient.ResolveHost(Line("-debuguihost", "fd00::1"), _ => { })
                    == "[fd00::1]",
            "a bare IPv6 literal inside a URI has its last colon read as a port separator");
    }

    /// <summary>
    /// THE COMPOSITION RULE, held directly: the host in the dialled URI is the
    /// one this side resolved, and the invitation contributes only the port.
    ///
    /// <para>
    /// This is the case that would catch somebody plumbing an address through
    /// from the invitation later. The type cannot carry one today, so the case
    /// asks the question the other way round — given a resolved host, is that
    /// what is dialled.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDialledUriUsesTheHostThisSideResolved()
    {
        var uri = DebugChannelClient.UriFor(
            "10.0.0.5", new DebugChannelInvitation(DebugChannelProtocol.Version, 27110, "n"));

        Assert.True(uri.Host == "10.0.0.5", "the host comes from this side and nowhere else");
    }

    [Fact]
    public void TheDialledUriUsesThePortTheInvitationNamed()
    {
        var uri = DebugChannelClient.UriFor(
            "10.0.0.5", new DebugChannelInvitation(DebugChannelProtocol.Version, 27119, "n"));

        Assert.True(uri.Port == 27119,
            "the port is the one thing the server does get to choose, because it is a port on " +
            "the server the client already joined");
    }

    [Fact]
    public void TheDialledUriIsTheChannelsOwnPath()
    {
        var uri = DebugChannelClient.UriFor(
            "127.0.0.1", new DebugChannelInvitation(DebugChannelProtocol.Version, 27110, "n"));

        Assert.True(uri.AbsolutePath == DebugChannelProtocol.Path,
            "one path, fixed on both sides; the handshake refuses anything else with a 404");
    }

    [Fact]
    public void AChannelThatWasNeverAttachedHasNotDialled()
    {
        using var channel = new DebugChannelClient(
            "127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(4)));

        Assert.True(channel.DialAttempts == 0 && !channel.Running && !channel.Attached,
            "constructing the client is not connecting: nothing dials until an invitation is " +
            "accepted, and no invitation is accepted without the flag");
    }
}
