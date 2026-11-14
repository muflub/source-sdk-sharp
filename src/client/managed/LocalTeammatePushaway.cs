using SourceSharp.Abi;
using SourceSharp.Client.Input;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The local player's half of the push-apart (plan A.7): where stock's
/// <c>C_TFPlayer::CreateMove</c> calls <c>AvoidPlayers</c>, after the input has
/// built the command, the local player overlapping a teammate gets
/// <see cref="TeammatePushaway"/>'s sideways move added to it. Only while
/// teammates walk through each other (<c>mp_teammates_pass</c>) and
/// <c>mp_teammates_pushaway</c> is on. The command carries the push to the
/// server and to the prediction's replay alike.
/// </summary>
public sealed class LocalTeammatePushaway(IClientStandalones standalones, IEngineHud screen, IEngineEntityRender render,
                                          ClientTeammatesPassRule teams, MpTeammatesPushawayConVar pushaway,
                                          InputCvars input, Func<int> maxClients) : IInputCommandAdjust
{
    /// <summary><c>FL_ONGROUND</c>.</summary>
    private const uint FlOnGround = 1u << 0;

    /// <summary><c>FL_DUCKING</c>.</summary>
    private const uint FlDucking = 1u << 1;

    private readonly List<PushawayBody> _teammates = [];

    public void Adjust(ref InputCommand cmd)
    {
        if (!teams.TeammatesPass || !pushaway.Value)
            return;

        int local = screen.LocalPlayerIndex;
        if (standalones.GetByIndex(local) is not ClientSsPlayer self || !self.State.Alive)
            return;

        int team = teams.TeamOf(local);
        if (team < TeammateCollision.FirstGameTeam)
            return;

        _teammates.Clear();
        int count = maxClients();
        for (int index = 1; index <= count; index++)
        {
            if (index == local || standalones.GetByIndex(index) is not ClientSsPlayer other || teams.TeamOf(index) != team
                || !other.State.Alive || other.State.ObserverMode != 0 || render.IsDormant(other.Handle))
                continue;

            _teammates.Add(Body(other.State));
        }

        if (_teammates.Count == 0)
            return;

        PushawayBody body = Body(self.State);
        uint flags = self.State.Flags;
        var me = new PushawaySelf(body.Origin, body.Mins, body.Maxs, self.State.Velocity, cmd.ViewAngles.Yaw,
                                  self.State.MaxSpeed, (flags & FlDucking) != 0 && (flags & FlOnGround) != 0);
        TeammatePushaway.Adjust(me, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_teammates),
                                TeammatePushaway.MaxSeparationForce, input.ForwardSpeed.Float, input.BackSpeed.Float,
                                input.SideSpeed.Float, ref cmd.ForwardMove, ref cmd.SideMove);
    }

    /// <summary>A player's place and hull: the duck hull while FL_DUCKING, as their box is.</summary>
    private static PushawayBody Body(PlayerNetState state)
    {
        MoveConstants hulls = MoveConstantDefaults.Stock;
        bool ducked = (state.Flags & FlDucking) != 0;
        return new PushawayBody(state.Origin, ducked ? hulls.DuckHullMin : hulls.HullMin,
                                ducked ? hulls.DuckHullMax : hulls.HullMax);
    }
}
