using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// The capture's voice concepts: the team that lost the point speaks
/// MP_CONCEPT_LOST_CONTROL_POINT, each capper MP_CONCEPT_CAPTURED_POINT
/// (team_control_point.cpp:635-665, :959-965), and the blocker of a broken
/// capture MP_CONCEPT_CAPTURE_BLOCKED (:751-773, :970-976) — a true cap only,
/// through <see cref="TF2.Server.Voice.TFConceptSpeech"/>.
/// </summary>
public sealed class ControlPointConceptTests
{
    private const string Sound = "vo/concept.ogg";

    [Fact]
    public void ATrueCapTellsTheOldTeamLostAndTheCappersCaptured()
    {
        var rig = new Rig();
        EntityHandle loser = rig.AddPlayer(TFClass.Soldier);
        EntityHandle capper = rig.AddPlayer(TFClass.Scout);
        rig.Teams.SetTeam((int)TFTeam.Red, loser);
        rig.Teams.SetTeam((int)TFTeam.Blue, capper);

        var point = rig.Point("capA", 0, TFTeam.Red);
        point.SetOwner((int)TFTeam.Blue, makeSound: true, [capper.EntryIndex]);

        // the lost line first, the captured one second (stock's order), each
        // asked with its speaker's class behind the concept
        Assert.Equal(
            [(TF2.Server.Voice.TFConceptSpeech.LostControlPoint, "Soldier"),
             (TF2.Server.Voice.TFConceptSpeech.CapturedPoint, "Scout")],
            rig.Rules.Asked);

        // each line sounded from its own speaker
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle h && h == loser);
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle h && h == capper);
    }

    [Fact]
    public void AnOwnerSetWithoutASoundSpeaksNothing()
    {
        var rig = new Rig();
        EntityHandle loser = rig.AddPlayer(TFClass.Soldier);
        EntityHandle capper = rig.AddPlayer(TFClass.Scout);
        rig.Teams.SetTeam((int)TFTeam.Red, loser);
        rig.Teams.SetTeam((int)TFTeam.Blue, capper);

        var point = rig.Point("capA", 0, TFTeam.Red);
        point.SetOwner((int)TFTeam.Blue, makeSound: false, [capper.EntryIndex]);

        Assert.Empty(rig.Rules.Asked);
        Assert.DoesNotContain(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle);
    }

    [Fact]
    public void ABlockedCaptureTellsTheBlocker()
    {
        var rig = new Rig();
        EntityHandle blocker = rig.AddPlayer(TFClass.Soldier);
        rig.Teams.SetTeam((int)TFTeam.Red, blocker);

        var point = rig.Point("capA", 0, TFTeam.Red);
        point.CaptureBlocked(blocker, EntityHandle.None);

        Assert.Equal(
            [(TF2.Server.Voice.TFConceptSpeech.CaptureBlocked, "Soldier")],
            rig.Rules.Asked);
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle h && h == blocker);
    }

    [Fact]
    public void ADeadPlayerSpeaksNothing()
    {
        var rig = new Rig();
        EntityHandle loser = rig.AddPlayer(TFClass.Soldier);
        EntityHandle capper = rig.AddPlayer(TFClass.Scout);
        rig.Teams.SetTeam((int)TFTeam.Red, loser);
        rig.Teams.SetTeam((int)TFTeam.Blue, capper);
        rig.Engine.Returns("Players.IsAlive", false);

        var point = rig.Point("capA", 0, TFTeam.Red);
        point.SetOwner((int)TFTeam.Blue, makeSound: true, [capper.EntryIndex]);

        Assert.Empty(rig.Rules.Asked);
    }

    /// <summary>The objective rig, plus the players the speech path asks for:
    /// each a carrier over the rig's engine, its class on the carrier's state.</summary>
    private sealed class Rig
    {
        public readonly ObjectiveRig Base = new();

        private readonly Dictionary<EntityHandle, ScriptedEntity> _carrierEntities = [];

        private readonly Dictionary<int, EntityHandle> _playersByIndex = [];

        private int _nextIndex = 16;

        public RecordingEngine Engine => Base.Engine;

        public FakeConceptRules Rules => Base.ConceptRules;

        public FakeConceptTeams Teams => Base.ConceptTeams;

        public TeamControlPoint Point(string name, int index, TFTeam owner)
            => Base.Point(name, index, owner);

        public Rig()
        {
            Engine.Returns("Players.IsAlive", true);

            // UTIL_PLAYERBYINDEX: the entry index a capper list names is the
            // player the fixture added at it.
            Engine.ReturnsEachCall("Players.GetHandle", () =>
                _playersByIndex.TryGetValue((int)(Engine.Calls[^1].Args[0] ?? 0), out var handle)
                    ? handle
                    : EntityHandle.None);
        }

        /// <summary>A carried player the engine knows: the next index, its
        /// carrier entity the one the attach creates.</summary>
        public EntityHandle AddPlayer(TFClass playerClass)
        {
            int index = _nextIndex++;
            var player = new EntityHandle(0x0001_0000u | (uint)index);
            var carrierHandle = new EntityHandle(0x0002_0000u | (uint)index);

            Engine.Returns("Entities.Create", carrierHandle);
            var entity = new TFPlayerDataEntity(Engine);
            entity.State.BindLocal();
            _carrierEntities[carrierHandle] = entity;
            _playersByIndex[index] = player;

            Engine.Returns("Entities.GetIndex", index);
            var carrier = new TFPlayerCarrier(Engine, new CarrierDirectory(_carrierEntities), Base.Carriers, new RecordingLog());
            carrier.Attach(player);
            Assert.True(Base.Carriers.SetClass(index, playerClass));
            return player;
        }

        private sealed class CarrierDirectory(Dictionary<EntityHandle, ScriptedEntity> entities) : IEntityDirectory
        {
            public ScriptedEntity? Get(EntityHandle handle)
                => entities.TryGetValue(handle, out var entity) ? entity : null;

            public ScriptedEntity? FindByName(string name) => null;

            public IEnumerable<ScriptedEntity> OfClass(string className) => [];

            public bool IsRegistered(string className) => false;

            public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
        }
    }
}
