using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// <c>/ai/friendgate</c> (lane Ge): what TF2's bots' friend gate held (<see cref="FriendLine"/>, TFDecider.Feel.cs), per
/// bot and summed: the decides on which fire was held for a teammate in the way, those of them a prefire's rocket, and
/// the shots and the shots at the feet that went. Read under each brain's lock.
/// </summary>
[SideService(Side.Server)]
public sealed class TFFriendGateRoute(AiBots bots, ServerAiSession ai) : IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/friendgate";

    /// <summary>Among the dev API's route providers: after the course's.</summary>
    public int HookOrder => ModStages.First + 927;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "TF2's bots' friend gate (lane Ge): per bot and summed, the decides on which fire was held for a teammate in the way (held_fire_friend), those a prefire's rocket (prefire_held_friend), and the shots and feet shots that went"),
    ];

    private readonly record struct Gate(int Held, int PrefireHeld, int Shots, int FeetShots);

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        var rows = new List<object>();
        Gate sum = default;
        foreach (AiBot b in bots.Bots)
        {
            bool read = ai.Session.TryReadBrain(b.Id.Slot, br => br.Decider is TFDecider d ? new Gate(d.HeldFireFriend, d.PrefireHeldFriend, d.Shots, d.FeetShots) : default, out Gate g);
            sum = new Gate(sum.Held + g.Held, sum.PrefireHeld + g.PrefireHeld, sum.Shots + g.Shots, sum.FeetShots + g.FeetShots);
            rows.Add(new { slot = b.Id.Slot, name = b.Name, read, held_fire_friend = g.Held, prefire_held_friend = g.PrefireHeld, shots = g.Shots, feet_shots = g.FeetShots });
        }

        return new DebugRouteReply(200, AiJson.Write(new
        {
            held_fire_friend = sum.Held, prefire_held_friend = sum.PrefireHeld, shots = sum.Shots, feet_shots = sum.FeetShots, bots = rows,
        }));
    }
}
