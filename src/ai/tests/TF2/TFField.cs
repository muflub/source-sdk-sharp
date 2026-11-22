using System.Collections.Immutable;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot on a synthetic CTF field, for the tree's facts: a flat floor 4,096 units long on x
/// (64 cells of 64 u, 8 wide), red's base at the west end and blue's at the east, each with its flag,
/// its capture zone, its spawn and a health pack; the bot a red Soldier with the stock launcher and
/// shotgun, the HUD's objectives as the test sets them, and a view per step. Its tree reads the
/// rocket jumps a test hands it (none unless it does: no jump).
/// </summary>
internal sealed class TFField
{
    public const int Red = (int)TFTeam.Red, Blue = (int)TFTeam.Blue;
    public const int Launcher = 3, Shotgun = 5, Shovel = 7;
    public const float CellSize = 64f;
    public const int Length = 64, Width = 8;

    public static readonly AiVector RedFlag = new(256f, 256f, 0f), BlueFlag = new(3840f, 256f, 0f);
    public static readonly AiVector RedZone = new(320f, 128f, 0f), BlueZone = new(3776f, 128f, 0f);
    public static readonly AiVector RedSpawn = new(64f, 256f, 0f), BlueSpawn = new(4032f, 256f, 0f);

    public const int RedFlagId = 10, BlueFlagId = 11, RedZoneId = 12, BlueZoneId = 13;

    public static readonly WeaponKnowledge Weapons = new([
        new WeaponFacts(Launcher, TFDecider.SoldierLauncher, 4, 0.8f, 3.6f, true, true, 1100f),
        new WeaponFacts(Shotgun, TFDecider.SoldierShotgun, 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Shovel, "tf_weapon_shovel", 0, 0.8f, 0f, false, false, 0f),
    ]);

    private readonly PerceivedView _view = new(8);
    private int _tick;

    public TFField(int slot = 2, ulong seed = 7, string preset = "hard", bool graph = true, int floorLength = Length, Func<DecisionParams, DecisionParams>? tune = null,
                   TFFeelSwitches? feel = null, SkillProfile? profile = null, SkillCurves? curves = null, int[]? mates = null,
                   SourceSharp.Ai.TF2.Jump.IJumpAdvisor? jumps = null, TeamZone[]? zones = null, ItemSpawn[]? packs = null, NavGraph? floor = null,
                   ICellSight? sight = null, ImmutableArray<TFCourseEnd> ends = default, WeaponKnowledge? weapons = null,
                   Func<TFDecider, TFClass, TFDecider.ClassPlay?>? plays = null)
    {
        Bot = new ActorId(slot, 1);
        profile ??= SkillPresets.TryGet(preset, out SkillProfile? p) ? p! : throw new ArgumentException(preset);
        // HL2MP's curves unless a fact asks for others (the tf2 mod's own: TFSkillCurvesTests.ShippedCurves).
        Params = (curves ?? Skills.Curves.Game()).For(profile);
        if (tune is not null)
            Params = tune(Params);
        ItemSpawn[] items = packs ??
        [
            new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(512f, 64f, 0f), 10f),
            new ItemSpawn(1, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(3584f, 64f, 0f), 10f),
        ];
        var map = new MapKnowledge("tf_field", [.. items], [], weapons ?? Weapons, [new TeamSpawn(Red, RedSpawn), new TeamSpawn(Blue, BlueSpawn)],
                                   TFAbilities.Table(),
                                   teamZones: [new TeamZone(Blue, ZoneKind.RespawnRoom, new AiVector(3968f, 0f, -16f), new AiVector(4096f, 512f, 200f)), .. zones ?? []]);
        Graph = floor ?? (graph ? Floor(floorLength) : null);
        Memory = new BotMemory(Bot, 33, MemoryParams.For(profile), new MemoryWorld(map, Graph, 0.015f, id => id.ToString(), sight), new HabitStore());
        // The scoreboard's rows of the bot's teammates (red, as Self draws the bot).
        foreach (int mate in mates ?? [])
            Memory.Apply(AiEvent.ActorTeam(Bot, new Tick(1), new ActorId(mate, 1), Red, 0));
        Decider = new TFDecider(new BrainSetup(Bot, seed, Memory, Params, 0.015f),
                                TFKnowledge.Classes(c => (300f, c == TFClass.Soldier ? 200 : 125))) { Feel = feel ?? TFFeelSwitches.All, Jumps = jumps, CourseEnds = ends, Plays = plays };
        Brain = new Brain(Bot, seed, Memory, Params, Decider);
    }

    /// <summary>A bot that defends: the last slot of a team of three (<see cref="TFDecider.CtfRoleSplit"/>).</summary>
    public static TFField Defender(Func<DecisionParams, DecisionParams>? tune = null) => new(slot: 3, tune: tune, mates: [1, 2]);

    public ActorId Bot { get; }
    public DecisionParams Params { get; }
    public BotMemory Memory { get; }
    public NavGraph? Graph { get; }
    public TFDecider Decider { get; }
    public Brain Brain { get; }
    public int Tick => _tick;

    /// <summary>The bot's own HUD each step's view carries (P7: its kit, meters, the teammates' health it shows); none unless a fact sets it.</summary>
    public SourceSharp.Ai.TF2.Senses.TFHud? Hud { get; set; }

    /// <summary>The root's branch the bot chose at its last decide, without the subtree's name before it.</summary>
    public string Branch => Decider.Doing.Branch[(Decider.Doing.Branch.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    /// <summary>The floor: <paramref name="length"/> (west to east) by <see cref="Width"/> cells, 4-connected; past it, no floor.</summary>
    public static NavGraph Floor(int length = Length)
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[length, Width];
        for (int x = 0; x < length; x++)
        {
            for (int y = 0; y < Width; y++)
            {
                ids[x, y] = b.AddCell(new NavCell(x * CellSize, y * CellSize, (x + 1) * CellSize, (y + 1) * CellSize, 0f,
                                                  ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            }
        }

        for (int x = 0; x < length; x++)
        {
            for (int y = 0; y < Width; y++)
            {
                if (x + 1 < length)
                    b.AddBoth(ids[x, y], ids[x + 1, y], NavEdgeType.Walk);
                if (y + 1 < Width)
                    b.AddBoth(ids[x, y], ids[x, y + 1], NavEdgeType.Walk);
            }
        }

        return b.Build();
    }

    /// <summary>The bot, a red Soldier at <paramref name="at"/> holding <paramref name="weapon"/>, on the ground.</summary>
    public static SelfState Self(AiVector at, int health = 200, int clip = 4, int reserve = 20, int weapon = Launcher, AiVector velocity = default,
                                 TFClass playerClass = TFClass.Soldier, SelfFlags extra = SelfFlags.None, int team = Red)
        => new(at, velocity, default, health, 0, weapon, clip, reserve, SelfFlags.Alive | SelfFlags.Grounded | extra, Team: team, Class: (int)playerClass);

    /// <summary>The HUD's objectives: both flags (each at its place and state, with its carrier's slot) and both zones.</summary>
    public static ObjectiveKnowledge Ctf(ObjectiveState red = ObjectiveState.Home, AiVector? redAt = null, int redCarrier = 0,
                                         ObjectiveState blue = ObjectiveState.Home, AiVector? blueAt = null, int blueCarrier = 0)
        => new([
            new Objective(RedFlagId, ObjectiveKind.Carry, Red, 1 << Blue, red, redAt ?? RedFlag, CarrierSlot: redCarrier),
            new Objective(BlueFlagId, ObjectiveKind.Carry, Blue, 1 << Red, blue, blueAt ?? BlueFlag, CarrierSlot: blueCarrier),
            new Objective(RedZoneId, ObjectiveKind.Deliver, Red, (1 << Red) | (1 << Blue), ObjectiveState.Open, RedZone),
            new Objective(BlueZoneId, ObjectiveKind.Deliver, Blue, (1 << Red) | (1 << Blue), ObjectiveState.Open, BlueZone),
        ]);

    /// <summary>Whether <paramref name="intent"/> begins a rocket jump: its one-shot's row is the jump's (a voice line is an ability one-shot too).</summary>
    public bool BeginsJump(in IntentCommand intent)
        => intent.Action.Kind == ActionKind.Ability && Memory.World.Map.Abilities.TryGet(AbilityCall.Row(intent.Action.WeaponSlot), out AbilityRow row)
           && row.Name == TFAbilities.RocketJump;

    /// <summary>One decide: the bot as <paramref name="self"/>, the HUD's <paramref name="objectives"/>, and each of <paramref name="sighted"/> in sight on its team.</summary>
    public IntentCommand Step(SelfState self, ObjectiveKnowledge? objectives, params (ActorId Id, AiVector At, AiVector Velocity, int Team)[] sighted)
        => StepDrawn(self, objectives, [.. sighted.Select(s => new ActorFacts(s.Id, s.At, s.Velocity, Launcher, ActorActivity.None, 1f, s.Team))]);

    /// <summary>One decide with each of <paramref name="sighted"/> in sight as its client draws it: its class, its stance.</summary>
    public IntentCommand StepDrawn(SelfState self, ObjectiveKnowledge? objectives, params ActorFacts[] sighted)
        => StepHearing(self, objectives, [], sighted);

    /// <summary>One decide with <paramref name="events"/> perceived since the last (a sight lost, a sound heard) and each of <paramref name="sighted"/> in sight.</summary>
    public IntentCommand StepHearing(SelfState self, ObjectiveKnowledge? objectives, AiEvent[] events, params ActorFacts[] sighted)
        => StepSeeing(self, objectives, [], events, sighted);

    /// <summary>One decide with each of <paramref name="things"/> in sight too: the projectiles and buildings the bot's client draws.</summary>
    public IntentCommand StepSeeing(SelfState self, ObjectiveKnowledge? objectives, ThingFacts[] things, AiEvent[]? events = null, params ActorFacts[] sighted)
    {
        _tick += DecideSchedule.Period;
        var tick = new Tick(_tick);
        _view.Reset(Bot, tick, self);
        _view.SetObjectives(objectives);
        _view.SetGameHud(Hud);
        foreach (ActorFacts f in sighted)
            _view.AddSighted(f);
        foreach (ThingFacts t in things)
            _view.AddThing(t);
        return Brain.Step(tick, _view, events ?? []);
    }
}
