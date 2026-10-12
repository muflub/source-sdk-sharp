using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// phys_convert's conversion, ported from stock (physobj.cpp at the pristine
/// clone: CPhysConvert::InputConvertTarget's loop body, CreateSimplePhysicsObject
/// and TransferPhysicsObject; hierarchy.cpp's TransferChildren; util.cpp's
/// UTIL_TransferPoseParameters). Native's SourceSharp_EntityConvertToPhysics
/// at 4602cf071 answered 0 for every entity; these facts are stock's.
/// </summary>
public class PhysicsConversionTests
{
    private static readonly EntityHandle Wall = new(0x0001_0010u);
    private static readonly EntityHandle Donor = new(0x0001_0011u);
    private static readonly EntityHandle Made = new(0x0001_0012u);
    private static readonly EntityHandle Child = new(0x0001_0013u);

    private sealed class FakeWorld(List<string> log) : IPhysicsConversionWorld
    {
        public MoveType Move = MoveType.None;
        public bool Brush = true;
        public EntityHandle Creates = Made;
        public readonly Dictionary<EntityHandle, nint> Objects = new() { [Made] = 0x8000 };
        public readonly Dictionary<EntityHandle, string> Models = new() { [Wall] = "*3", [Donor] = "*7" };
        public readonly List<(EntityHandle, int)> Kids = [];
        public readonly Dictionary<(EntityHandle, int), float> Poses = new() { [(Wall, 0)] = 0.25f, [(Wall, 1)] = 0.75f };
        public bool SwapRefused;

        public MoveType MoveTypeOf(EntityHandle entity) => Move;
        public string ClassName(EntityHandle entity) => "func_brush";
        public string Name(EntityHandle entity) => entity == Wall ? "wall_a" : string.Empty;
        public string ModelName(EntityHandle entity) => Models.TryGetValue(entity, out string? m) ? m : string.Empty;
        public bool IsBrushModel(EntityHandle entity) => Brush;
        public Vector AbsOrigin(EntityHandle entity) => new(5, 6, 7);
        public Vector AbsAngles(EntityHandle entity) => new(0, 90, 0);
        public void Unparent(EntityHandle entity) => log.Add($"unparent {entity.Value:x}");
        public IReadOnlyList<(EntityHandle Child, int Attachment)> Children(EntityHandle parent) => parent == Wall ? Kids : [];
        public void SetParent(EntityHandle child, EntityHandle parent, int attachment) => log.Add($"parent {child.Value:x} {parent.Value:x} {attachment}");
        public void SetModel(EntityHandle entity, string model)
        {
            Models[entity] = model;
            log.Add($"setmodel {entity.Value:x} {model}");
        }
        public EntityHandle Create(string className)
        {
            log.Add($"create {className}");
            return Creates;
        }
        public void SetKeyValue(EntityHandle entity, string key, string value) => log.Add($"key {entity.Value:x} {key}={value}");
        public void SetOrigin(EntityHandle entity, Vector origin) => log.Add($"origin {origin.X} {origin.Y} {origin.Z}");
        public void SetAngles(EntityHandle entity, Vector angles) => log.Add($"angles {angles.X} {angles.Y} {angles.Z}");
        public void Spawn(EntityHandle entity) => log.Add($"spawn {entity.Value:x}");
        public nint PhysicsObject(EntityHandle entity) => Objects.TryGetValue(entity, out nint o) ? o : 0;
        public bool Swap(EntityHandle entity, nint physicsObject, out nint previous)
        {
            previous = PhysicsObject(entity);
            if (SwapRefused && entity == Made && physicsObject != 0)
                return false;
            Objects[entity] = physicsObject;
            log.Add($"hold {entity.Value:x} {physicsObject:x}");
            return true;
        }
        public void DestroyPhysics(EntityHandle entity)
        {
            Objects[entity] = 0;
            log.Add($"destroyphysics {entity.Value:x}");
        }
        public readonly Dictionary<EntityHandle, MoveType> MoveTypes = new();
        public void SetMoveType(EntityHandle entity, MoveType moveType) => MoveTypes[entity] = moveType;
        public void SetMass(EntityHandle entity, float mass) => log.Add($"mass {mass}");
        public void SetCollisionGroup(EntityHandle entity, CollisionGroup group) => log.Add($"group {group}");
        public int PoseParameterCount(EntityHandle entity) => 2;
        public float GetPoseParameter(EntityHandle entity, int parameter) => Poses.TryGetValue((entity, parameter), out float v) ? v : 0f;
        public void SetPoseParameter(EntityHandle entity, int parameter, float value) => log.Add($"pose {entity.Value:x} {parameter} {value}");
        public void SetNotSolid(EntityHandle entity) => log.Add($"notsolid {entity.Value:x}");
        public void SetNoDraw(EntityHandle entity) => log.Add($"nodraw {entity.Value:x}");
        public void Remove(EntityHandle entity) => log.Add($"remove {entity.Value:x}");
        public void Message(string text) => log.Add($"msg {text}");
    }

    private static (PhysicsConversion Convert, FakeWorld World, EntityPhysicsCreationTests.FakeVPhysics V) Make()
    {
        var v = new EntityPhysicsCreationTests.FakeVPhysics();
        var world = new FakeWorld(v.Calls);
        var create = new EntityPhysicsCreation(v, v, new EntityPhysicsCreationTests.FakeHost(v));
        return (new PhysicsConversion(world, v, create), world, v);
    }

    [Fact]
    public void SomethingAlreadyPhysicsIsLeftAloneWithStocksMessage()
    {
        var (c, w, v) = Make();
        w.Move = MoveType.VPhysics;

        Assert.Equal(EntityHandle.None, c.Convert(Wall, EntityHandle.None, 0f, false, false));
        Assert.Equal(["msg ERROR phys_convert func_brush ! Already MOVETYPE_VPHYSICS\n"], v.Calls);
    }

    /// <summary>
    /// A solid prop_dynamic (ss_sandbox's physbrush crate, live at fa0ab15dd):
    /// its only object is static, and like stock's CDynamicProp it is
    /// MOVETYPE_NONE beside it (EntityPhysicsCreation writes that word), so it
    /// converts.
    /// </summary>
    [Fact]
    public void AStaticObjectIsNotAlreadyPhysicsAndIsConverted()
    {
        var (c, w, v) = Make();
        w.Move = MoveType.None;
        w.Objects[Wall] = 0x7000;
        v.Static.Add(0x7000);

        Assert.Equal(Made, c.Convert(Wall, EntityHandle.None, 0f, false, false));
        Assert.DoesNotContain(v.Calls, call => call.StartsWith("msg ERROR", StringComparison.Ordinal));
        Assert.Equal("remove 10010", v.Calls[^1]);
    }

    [Fact]
    public void AMoveableObjectIsAlreadyPhysics()
    {
        var (c, w, v) = Make();
        w.Move = MoveType.VPhysics;
        w.Objects[Wall] = 0x7000;

        Assert.Equal(EntityHandle.None, c.Convert(Wall, EntityHandle.None, 0f, false, false));
        Assert.Contains("msg ERROR phys_convert func_brush ! Already MOVETYPE_VPHYSICS\n", v.Calls);
    }

    [Fact]
    public void ABrushBecomesASimplePhysicsBrushWithItsModelAndPoseThenSpawns()
    {
        var (c, w, v) = Make();

        Assert.Equal(Made, c.Convert(Wall, EntityHandle.None, 0f, false, false));

        int at = v.Calls.IndexOf("create simple_physics_brush");
        Assert.True(at > 0);
        Assert.Equal(["key 10012 model=*3", "origin 5 6 7", "angles 0 90 0", "spawn 10012"], v.Calls.GetRange(at + 1, 4));
    }

    [Fact]
    public void AModelThatIsNotABrushBecomesASimplePhysicsProp()
    {
        var (c, w, v) = Make();
        w.Brush = false;

        c.Convert(Wall, EntityHandle.None, 0f, false, false);

        Assert.Contains("create simple_physics_prop", v.Calls);
        Assert.DoesNotContain("create simple_physics_brush", v.Calls);
    }

    [Fact]
    public void TheTargetIsUnparentedFirst()
    {
        var (c, _, v) = Make();

        c.Convert(Wall, EntityHandle.None, 0f, false, false);

        Assert.Equal("unparent 10010", v.Calls[0]);
    }

    [Fact]
    public void ASwapModelKillsTheTargetsObjectAndLendsItsModelBeforeTheCopyIsMade()
    {
        var (c, _, v) = Make();

        c.Convert(Wall, Donor, 0f, false, false);

        Assert.Equal(["unparent 10010", "destroyphysics 10010", "setmodel 10010 *7", "create simple_physics_brush", "key 10012 model=*7"],
                     v.Calls.GetRange(0, 5));
    }

    [Fact]
    public void AMassOverrideIsAppliedAndZeroKeepsTheModels()
    {
        var (c, _, v) = Make();
        c.Convert(Wall, EntityHandle.None, 150f, false, false);
        Assert.Contains("mass 150", v.Calls);

        var (c2, _, v2) = Make();
        c2.Convert(Wall, EntityHandle.None, 0f, false, false);
        Assert.DoesNotContain(v2.Calls, call => call.StartsWith("mass", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNamePosesAndChildrenMoveOverAndTheTargetIsHiddenAndRemovedLast()
    {
        var (c, w, v) = Make();
        w.Kids.Add((Child, 2));

        c.Convert(Wall, EntityHandle.None, 0f, false, false);

        int at = v.Calls.IndexOf("key 10012 targetname=wall_a");
        Assert.True(at > 0);
        Assert.Equal(["pose 10012 0 0.25", "pose 10012 1 0.75", "parent 10013 10012 2",
                      "notsolid 10010", "nodraw 10010", "remove 10010"], v.Calls.GetRange(at + 1, 6));
        Assert.Equal("remove 10010", v.Calls[^1]);
    }

    [Fact]
    public void AsleepPutsTheNewObjectToSleepAndDebrisMakesItDebris()
    {
        var (c, _, v) = Make();

        c.Convert(Wall, EntityHandle.None, 0f, asleep: true, debris: true);

        Assert.Contains("sleep", v.Calls);
        Assert.Contains("group Debris", v.Calls);
    }

    [Fact]
    public void AwakeAndNotDebrisLeavesTheNewObjectAsItsSpawnMadeIt()
    {
        var (c, _, v) = Make();

        c.Convert(Wall, EntityHandle.None, 0f, false, false);

        Assert.DoesNotContain("sleep", v.Calls);
        Assert.DoesNotContain(v.Calls, call => call.StartsWith("group", StringComparison.Ordinal));
    }

    [Fact]
    public void ACopyWhoseSpawnMadeNoObjectIsGivenOneAsStocksInitNormal()
    {
        var (c, w, v) = Make();
        w.Objects[Made] = 0;

        c.Convert(Wall, EntityHandle.None, 0f, asleep: true, debris: false);

        Assert.Contains("create", v.Calls);          // EntityPhysicsCreation's CreatePolyObject
        Assert.Contains("sleep", v.Calls);
    }

    [Fact]
    public void AMoveableObjectIsHandedToTheCopyKeepingItsMotion()
    {
        var (c, w, v) = Make();
        w.Objects[Wall] = 0x7000;

        Assert.True(c.TransferPhysicsObject(Wall, Made, wakeUp: true));

        Assert.Equal(["destroyphysics 10012", "hold 10010 0", "unshadow 7000", "hold 10012 7000",
                      $"gamedata 7000 {PhysicsGameData.FromHandle(Made):x}", "motion True", "gravity True",
                      "recheck", "wake"], v.Calls);
    }

    [Fact]
    public void ATransferredObjectMakesTheCopyVPhysics()
    {
        // The simple classes are MOVETYPE_VPHYSICS (their Spawn); the word
        // native reports follows the object the copy now holds.
        var (c, w, _) = Make();
        w.Objects[Wall] = 0x7000;

        Assert.True(c.TransferPhysicsObject(Wall, Made, wakeUp: true));
        Assert.Equal(MoveType.VPhysics, w.MoveTypes[Made]);
        Assert.False(w.MoveTypes.ContainsKey(Wall));
    }

    [Fact]
    public void ATransferAsleepDoesNotWake()
    {
        var (c, w, v) = Make();
        w.Objects[Wall] = 0x7000;

        Assert.True(c.TransferPhysicsObject(Wall, Made, wakeUp: false));
        Assert.DoesNotContain("wake", v.Calls);
    }

    [Fact]
    public void AStaticObjectOrNoneIsNotTransferred()
    {
        var (c, w, v) = Make();
        Assert.False(c.TransferPhysicsObject(Wall, Made, wakeUp: true));

        w.Objects[Wall] = 0x7000;
        v.Static.Add(0x7000);
        Assert.False(c.TransferPhysicsObject(Wall, Made, wakeUp: true));
        Assert.Empty(v.Calls);
    }

    [Fact]
    public void ATransferredObjectSkipsTheNewObjectsSleepAndDebris()
    {
        var (c, w, v) = Make();
        w.Objects[Wall] = 0x7000;

        c.Convert(Wall, EntityHandle.None, 0f, asleep: true, debris: true);

        Assert.Contains("hold 10012 7000", v.Calls);
        Assert.DoesNotContain("sleep", v.Calls);
        Assert.DoesNotContain("group Debris", v.Calls);
    }

    [Fact]
    public void ACopyThatWillNotHoldTheObjectGivesItBack()
    {
        var (c, w, v) = Make();
        w.Objects[Wall] = 0x7000;
        w.SwapRefused = true;

        Assert.False(c.TransferPhysicsObject(Wall, Made, wakeUp: true));
        Assert.Equal((nint)0x7000, w.Objects[Wall]);
    }

    [Fact]
    public void NothingMadeLeavesTheTargetStanding()
    {
        var (c, w, v) = Make();
        w.Creates = EntityHandle.None;

        Assert.Equal(EntityHandle.None, c.Convert(Wall, EntityHandle.None, 0f, false, false));
        Assert.DoesNotContain("remove 10010", v.Calls);
    }
}
