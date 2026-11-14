using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Plan A.7's push-apart on the client: the local player's command, after the
/// input built it, gets the push away from an overlapping teammate
/// (<see cref="LocalTeammatePushaway"/>, stock's <c>CreateMove</c> /
/// <c>AvoidPlayers</c>). One behaviour per fact.
/// </summary>
public class LocalTeammatePushawayTests
{
    private const int Combine = 2;
    private const int Rebels = 3;

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = Fake().Returns("EntityRender.IsDormant", false).Returns("Hud.LocalPlayerIndex", 1);
        public readonly MpTeammatesPassConVar Pass = DeclaredConVars.Fresh<MpTeammatesPassConVar>();
        public readonly MpTeammatesPushawayConVar Pushaway = DeclaredConVars.Fresh<MpTeammatesPushawayConVar>();
        public readonly LocalTeammatePushaway Subject;

        public Rig(int otherTeam, bool pass = true)
        {
            ClientSsPlayer local = Player(1, Vector.Zero);
            ClientSsPlayer other = Player(2, new Vector(0f, -8f, 0f));
            var resource = new ClientPlayerResource(Engine) { Handle = new EntityHandle(0x0003_0040u) };
            resource.State.BindLocal();
            resource.State.SetRow(1, 0, 0, 100, 1, Combine, true, true, true);
            resource.State.SetRow(2, 0, 0, 100, 2, otherTeam, true, true, true);
            if (pass)
                Pass.Hold("1");

            var standalones = new PredictionPlayerClipTests.Players(local, other, resource);
            Subject = new LocalTeammatePushaway(standalones, Engine, Engine, new ClientTeammatesPassRule(Pass, standalones),
                                                Pushaway, new InputCvars(), () => 2);
        }

        private ClientSsPlayer Player(int index, Vector origin)
        {
            var player = new ClientSsPlayer(Engine) { Handle = new EntityHandle((uint)(index | (index << 16))) };
            player.State.BindLocal();
            player.State.Origin = origin;
            player.State.Alive = true;
            player.State.MaxSpeed = 320f;
            return player;
        }

        public InputCommand Adjusted()
        {
            var cmd = new InputCommand { ViewAngles = new InputAngles(0f, 0f, 0f) };
            Subject.Adjust(ref cmd);
            return cmd;
        }
    }

    [Fact]
    public void AnOverlappingTeammatePushesTheLocalCommand()
        => Assert.True(new Rig(Combine).Adjusted().SideMove < -1f);

    [Fact]
    public void AnOverlappingEnemyDoesNotPush()
        => Assert.Equal(0f, new Rig(Rebels).Adjusted().SideMove);

    [Fact]
    public void NoPushawayWhenTheConVarIsOff()
    {
        var rig = new Rig(Combine);
        Assert.True(rig.Adjusted().SideMove < -1f);

        rig.Pushaway.Hold("0");

        Assert.Equal(0f, rig.Adjusted().SideMove);
    }

    [Fact]
    public void NoPushWhileTeammatesDoNotPass()
        => Assert.Equal(0f, new Rig(Combine, pass: false).Adjusted().SideMove);

    private sealed class Recording : IInputCommandAdjust
    {
        public void Adjust(ref InputCommand cmd) => cmd.SideMove = 123f;
    }

    /// <summary>The input service sends the command the adjust left: the push reaches the wire.</summary>
    [Fact]
    public void TheInputServiceSendsTheAdjustedCommand()
    {
        var rig = new InputRig();
        var service = new ClientInputService(rig.Input, null, () => true, adjust: new Recording());
        var cmd = new DecodedUserCmd();

        service.CreateMove(1, 0.015f, true, ref cmd);

        Assert.Equal(123f, cmd.Cmd.SideMove);
    }
}
