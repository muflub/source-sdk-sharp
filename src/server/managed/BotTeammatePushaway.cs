using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A bot's half of the push-apart (plan A.7): a human's command carries the
/// push from the client's <c>CreateMove</c> (<c>LocalTeammatePushaway</c>),
/// and a bot's command is made on the server and queued here, so the same
/// <see cref="TeammatePushaway"/> is added to it before it runs. Two bots
/// spawned on one spot drift apart as two humans would. Only while teammates
/// walk through each other and <c>mp_teammates_pushaway</c> is on; the input
/// limits are stock's <c>cl_forwardspeed</c>/<c>cl_backspeed</c>/<c>cl_sidespeed</c>
/// defaults, which a bot has no client to change.
/// </summary>
public sealed class BotTeammatePushaway(IPlayerCommandRunner inner, TeammatesPassRule rule,
                                        MpTeammatesPushawayConVar pushaway, IEntityDirectory directory,
                                        IEnginePlayers players) : IPlayerCommandRunner
{
    /// <summary>Stock's <c>cl_forwardspeed</c>, <c>cl_backspeed</c> and <c>cl_sidespeed</c> default (450).</summary>
    public const float InputLimit = 450f;

    /// <summary><c>FL_ONGROUND</c>.</summary>
    private const uint FlOnGround = 1u << 0;

    /// <summary><c>FL_DUCKING</c>.</summary>
    private const uint FlDucking = 1u << 1;

    private readonly List<PushawayBody> _teammates = [];

    public void Queue(EntityHandle player, in UserCmd cmd)
    {
        UserCmd pushed = cmd;
        Push(player, ref pushed);
        inner.Queue(player, pushed);
    }

    public int RunQueued(EntityHandle player) => inner.RunQueued(player);

    /// <summary>Adds the push away from an overlapping teammate to <paramref name="cmd"/>; false when none applies.</summary>
    public bool Push(EntityHandle player, ref UserCmd cmd)
    {
        if (!pushaway.Value || !rule.TeammatesPass || directory.Get(player) is not SsPlayer self || !self.State.Alive)
            return false;

        int team = players.GetTeam(player);
        if (team < TeammateCollision.FirstGameTeam)
            return false;

        _teammates.Clear();
        foreach (ScriptedEntity entity in directory.OfClass(EntityNames.Player))
        {
            if (entity is not SsPlayer other || other.Handle == player || !other.State.Alive || other.State.ObserverMode != 0
                || players.GetTeam(other.Handle) != team)
                continue;

            _teammates.Add(Body(other.State));
        }

        if (_teammates.Count == 0)
            return false;

        PushawayBody body = Body(self.State);
        uint flags = self.State.Flags;
        var me = new PushawaySelf(body.Origin, body.Mins, body.Maxs, self.State.Velocity, cmd.Yaw, self.State.MaxSpeed,
                                  (flags & FlDucking) != 0 && (flags & FlOnGround) != 0);
        float forward = cmd.ForwardMove;
        float side = cmd.SideMove;
        if (!TeammatePushaway.Adjust(me, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_teammates),
                                     TeammatePushaway.MaxSeparationForce, InputLimit, InputLimit, InputLimit,
                                     ref forward, ref side))
            return false;

        cmd.ForwardMove = forward;
        cmd.SideMove = side;
        return true;
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
