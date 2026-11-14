using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Server;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Mode;
using TF2.Descent.Server.Portal;
using TF2.Descent.Shared;

namespace TF2.Descent.Tests.Portal;

/// <summary>
/// The town portals over a fake world (heroes by slot, a placement answer, pads as numbers), a
/// recording route (hosted or not) and a recording host anchor store. The route's tasks are
/// complete when asked, unless a fact holds one open, so a use runs to its end in the call.
/// </summary>
internal sealed class PortalRig
{
    public const int Slot = 1, Other = 2;
    public const string Hero = "76561197960287930", OtherHero = "76561197960287931";
    public const string Level = "tf2_descent-1-s1", Town = "tf2_descent_town";

    public readonly FakeWorld World = new();
    public readonly FakeRoute Route = new();
    public readonly FakeAnchors Anchors = new();
    public readonly RigMode Mode = new();
    public readonly ListLog Log = new();
    public readonly TownPortals Portals;

    public PortalRig(bool hosted = false, IPortalParty? party = null)
    {
        Route.Hosted = hosted;
        Portals = new TownPortals(Mode, World, Route, Anchors, party ?? new SoloPortalParty(), Log);
        World.Heroes[Slot] = new FakeHero(Hero) { Feet = new Vector(0f, 0f, 0f) };
    }

    /// <summary>The map starts: its init, then its ready.</summary>
    public void Load(string map)
    {
        Portals.OnLevelShutdown();
        World.Pads.Clear();
        Portals.OnLevelInit(map);
        Portals.OnLevelReady(map);
    }

    /// <summary>On the level, a portal opened by the hero in <see cref="Slot"/>; its record.</summary>
    public PortalRecord Opened()
    {
        Load(Level);
        var answer = Portals.Open(Slot);
        if (!answer.Opened)
            throw new InvalidOperationException("the rig's portal did not open: " + answer.Why);
        return Portals.All[^1];
    }

    /// <summary>Stands the hero on the pad at <paramref name="pad"/> for <paramref name="seconds"/>, ticking at the portals' rate.</summary>
    public void StandOn(int slot, Vector pad, float seconds)
    {
        World.Heroes[slot].Feet = pad + new Vector(0f, 0f, PortalPlacement.Height);
        for (float t = 0f; t < seconds - 1e-4f; t += TownPortals.TickSeconds)
            Portals.Tick(TownPortals.TickSeconds);
    }

    /// <summary>Steps the hero off every pad and lets a tick see it.</summary>
    public void StepOff(int slot)
    {
        World.Heroes[slot].Feet = new Vector(-5000f, -5000f, 0f);
        Portals.Tick(TownPortals.TickSeconds);
    }

    /// <summary>The pad standing on this map for <paramref name="portal"/>, or null.</summary>
    public FakePad? PadOf(PortalRecord portal) => World.Pads.Values.FirstOrDefault(p => p.Portal == portal.Id);

    internal sealed class RigMode : IDescentModeState
    {
        public bool Active { get; set; } = true;
    }

    internal sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    internal sealed class FakeHero(string key)
    {
        public string Key { get; } = key;
        public bool Alive { get; set; } = true;
        public Vector Feet { get; set; }
        public int Team { get; set; } = 2;
        public string? Character { get; set; }
    }

    internal sealed record FakePad(int Portal, PortalEnd End, Vector At, float Yaw);

    internal sealed class FakeWorld : IPortalWorld
    {
        public Dictionary<int, FakeHero> Heroes { get; } = [];
        public Dictionary<int, FakePad> Pads { get; } = [];
        public List<int> Removed { get; } = [];
        public List<(int Player, Vector At, float Yaw)> Set { get; } = [];
        public List<(PortalShow Show, int Pad, int Player)> Shows { get; } = [];
        public List<(int Player, string Token, string Arg)> Notices { get; } = [];
        public Func<int, PortalPlace>? Placing { get; set; }
        public bool RefusePads { get; set; }
        public bool NoTownSpot { get; set; }
        public string LevelId { get; set; } = "";
        public bool Live { get; set; } = true;
        public int MadeEver { get; private set; }
        private int _next = 100;

        public PortalHere Here(string map) =>
            new(map, LevelNames.IsLevel(map), LevelNames.Depth(map), LevelNames.Seed(map), LevelNames.IsLevel(map) ? LevelId : "", Live);

        public string? HeroOf(int player) => Heroes.TryGetValue(player, out var h) ? h.Key : null;

        public string? CharacterOf(int player) => Heroes.TryGetValue(player, out var h) ? h.Character : null;

        IReadOnlyList<int> IPortalWorld.Heroes() => [.. Heroes.Keys.Order()];

        public bool IsAlive(int player) => Heroes.TryGetValue(player, out var h) && h.Alive;

        public Vector? Feet(int player) => Heroes.TryGetValue(player, out var h) ? h.Feet : null;

        public int Team(int player) => Heroes.TryGetValue(player, out var h) ? h.Team : 0;

        public PortalPlace Place(int player) =>
            Placing?.Invoke(player) ?? PortalPlace.At(Heroes[player].Feet + new Vector(PortalPlacement.Distance, 0f, 0f), 0f);

        public (Vector At, float Yaw)? TownSpot(int index) =>
            NoTownSpot ? null : (new Vector(1000f, index * TownPadLayout.Spacing, 0f), 180f);

        public int MakePad(PortalRecord portal, PortalEnd end, Vector at, float yaw)
        {
            if (RefusePads)
                return 0;
            int pad = _next++;
            Pads[pad] = new FakePad(portal.Id, end, at, yaw);
            MadeEver++;
            return pad;
        }

        public void RemovePad(int pad)
        {
            Pads.Remove(pad);
            Removed.Add(pad);
        }

        public void SetOn(int player, Vector at, float yaw)
        {
            Set.Add((player, at, yaw));
            Heroes[player].Feet = at;
        }

        public void Show(PortalShow show, int pad, int player) => Shows.Add((show, pad, player));

        public void Notice(int player, string token, string arg = "") => Notices.Add((player, token, arg));
    }

    internal sealed class FakeRoute : IPortalRoute
    {
        public bool Hosted { get; set; }
        public List<string> Calls { get; } = [];
        public PortalTrip UpAnswer { get; set; } = PortalTrip.Go;
        public PortalTrip BackAnswer { get; set; } = PortalTrip.Go;

        /// <summary>When set, the next trip waits on it.</summary>
        public TaskCompletionSource<PortalTrip>? Hold { get; set; }

        public Task<PortalTrip> Up(int player, string hero, PortalRecord portal)
        {
            Calls.Add($"up {player} {hero} portal {portal.Id}");
            return Hold?.Task ?? Task.FromResult(UpAnswer);
        }

        public Task<PortalTrip> Back(int player, string hero, PortalRecord portal)
        {
            Calls.Add($"back {player} {hero} portal {portal.Id} to {portal.Map} depth {portal.Depth} seed {portal.Seed} level {portal.LevelId}");
            return Hold?.Task ?? Task.FromResult(BackAnswer);
        }
    }

    internal sealed class FakeAnchors : IHostAnchors
    {
        public List<string> Calls { get; } = [];
        public List<PortalAnchor> PartyAnswer { get; } = [];
        public List<PortalAnchor> LevelAnswer { get; } = [];
        public Dictionary<string, string> Arrivals { get; } = [];

        public Task<bool> Place(string steamId, Vector at, float yaw)
        {
            Calls.Add($"place {steamId} ({at.X:0} {at.Y:0} {at.Z:0}) {yaw:0}");
            return Task.FromResult(true);
        }

        public Task<bool> Clear(string steamId)
        {
            Calls.Add("clear " + steamId);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PortalAnchor>> Party(string steamId)
        {
            Calls.Add("party " + steamId);
            return Task.FromResult<IReadOnlyList<PortalAnchor>>(PartyAnswer);
        }

        public TravelAnswer ReturnAnswer { get; set; } = new(TravelState.Started, TimeSpan.Zero, "");

        public Task<TravelAnswer> Return(string steamId, string anchorOwner, string levelId)
        {
            Calls.Add($"return {steamId} {anchorOwner} {levelId}");
            return Task.FromResult(ReturnAnswer);
        }

        public Task<string?> TakeArrival(string steamId)
        {
            Calls.Add("arrival " + steamId);
            return Task.FromResult(Arrivals.Remove(steamId, out var owner) ? owner : null);
        }

        public Task<IReadOnlyList<PortalAnchor>> OnThisLevel()
        {
            Calls.Add("level anchors");
            return Task.FromResult<IReadOnlyList<PortalAnchor>>(LevelAnswer);
        }
    }
}
