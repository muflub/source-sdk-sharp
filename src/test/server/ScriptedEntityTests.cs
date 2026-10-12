using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

//-----------------------------------------------------------------------------
// Fixtures. Declared here rather than reusing a real entity so a test failure
// points at the binding rather than at a change in game behaviour.
//-----------------------------------------------------------------------------

/// <summary>Exercises every supported field type and both name forms.</summary>
[ScriptedEntity("test_binding")]
public sealed class TestBindingEntity : ScriptedEntity
{
    public readonly Output OnFirst = new();
    public readonly Output OnSecond = new();
    public readonly Output OnThird = new();

    /// <summary>What a map may set on this entity. See <see cref="TestBindingEntityVars"/>.</summary>
    [ScriptVars] public TestBindingEntityVars Vars = new();

    public string? UnclaimedKey;
    public string? UnclaimedInput;

    public int NoArgCalls;
    public float LastNumber;
    public string LastString = string.Empty;
    public EntityHandle LastActivator;
    public bool Spawned;

    public override void OnSpawn() => Spawned = true;

    public override bool OnKeyValue(string key, string value)
    {
        UnclaimedKey = key;
        return true;
    }

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        UnclaimedInput = name;
        return true;
    }

    [ScriptedEvent] public void OnGo() => NoArgCalls++;

    [ScriptedEvent] public void OnSetSpeed(float value) => LastNumber = value;

    [ScriptedEvent] public void OnSetLabel(string value) => LastString = value;

    [ScriptedEvent("Renamed")] public void OnSomethingElse(InputData input)
    {
        LastString = input.Value;
        LastNumber = input.Number;
        LastActivator = input.Activator;
    }

    [ScriptedEvent] public void OnFireAll()
    {
        OnFirst.Fire();
        OnSecond.Fire(2.5f);
        OnThird.Fire("text");
    }
}

/// <summary>A signature the binder must refuse rather than bind wrongly.</summary>
[ScriptedEntity("test_bad_input")]
public sealed class TestBadInputEntity : ScriptedEntity
{
    [ScriptedEvent] public void OnTooManyArgs(float a, float b) { }
}

/// <summary>
/// An entity with no <c>[ScriptVars]</c> field at all, which is the ordinary
/// case: most entities have no map keys. Its absence must mean "no keys" and
/// never an error, and every key must still reach OnKeyValue.
/// </summary>
[ScriptedEntity("test_no_vars")]
public sealed class TestNoVarsEntity : ScriptedEntity
{
    public string? SawKey;
    public bool Spawned;

    public override void OnSpawn() => Spawned = true;

    public override bool OnKeyValue(string key, string value)
    {
        SawKey = key;
        return true;
    }
}

/// <summary>
/// A class for which dying means something other than vanishing, which is the
/// reason <see cref="ScriptedEntity.OnKill"/> is virtual rather than a shell
/// input. It must be THIS body that runs, not the base's — an override that is
/// registered and then dispatched past would remove the entity instead of
/// breaking it, and a mapper would see the connection working.
/// </summary>
[ScriptedEntity("test_kill_override")]
public sealed class TestKillOverrideEntity : ScriptedEntity
{
    public int Killed;

    public override void OnKill() => Killed++;
}

public class ScriptedEntityTests
{
    private static readonly EntityHandle Ent = new(0x0001_0001u);
    private static readonly EntityHandle Other = new(0x0002_0002u);

    private static ScriptedEntityRegistry Fresh()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ScriptedEntityTests).Assembly);
        registry.Services = Services();
        return registry;
    }

    /// <summary>Spawns the fixture and returns it. Throws rather than returning
    /// null, so a binding failure shows up as the test that caused it.</summary>
    private static TestBindingEntity Bound(ScriptedEntityRegistry registry)
    {
        registry.Spawn(Ent, "test_binding");
        return (TestBindingEntity)registry.Get(Ent)!;
    }

    [Fact]
    public void Discovery()
    {
        var registry = Fresh();
        var entity = Bound(registry);

        Assert.True(entity.Spawned, "spawning runs OnSpawn");
        Assert.True(entity.ClassName == "test_binding", "class name is recorded");
        Assert.True(entity.Handle == Ent, "handle is recorded");

        // Slots are what the native side fires, so declaration order is the
        // contract — not the order reflection happens to return fields in.
        Assert.True(registry.ResolveOutput(Ent, "OnFirst") == 0
            && registry.ResolveOutput(Ent, "OnSecond") == 1
            && registry.ResolveOutput(Ent, "OnThird") == 2, "outputs take slots in declaration order");

        Assert.True(registry.ResolveOutput(Ent, "onsecond") == 1, "output lookup ignores case");
        Assert.True(registry.ResolveOutput(Ent, "OnNothing") == -1, "unknown output resolves to -1");
        Assert.True(registry.ResolveOutput(Other, "OnFirst") == -1, "unknown entity resolves to -1");

        // A bad signature must not silently bind: the input would arrive and
        // throw at the call rather than at registration.
        registry.Spawn(Other, "test_bad_input");
        Assert.True(!registry.Input(Other, "TooManyArgs", "", 0.0f, EntityHandle.None), "unbindable input signature is refused");
    }

    /// <summary>
    /// The order Source actually uses: construct, hand over every keyvalue in
    /// the map, THEN spawn.
    ///
    /// This is its own test because getting it wrong is invisible. Keys used to
    /// be dropped when they arrived before spawn, and a dropped key is
    /// indistinguishable from a key the map never wrote — every declared key
    /// in every map read its declared default and nothing said so. The tests
    /// missed it by spawning first, which the game never does.
    /// </summary>
    [Fact]
    public void MapOrder()
    {
        var registry = Fresh();

        Assert.True(registry.KeyValue(Ent, "test_binding", "speed", "42"), "a key before spawn is claimed");

        var early = registry.Get(Ent) as TestBindingEntity;
        Assert.True(early is not null, "a key before spawn creates the instance");
        Assert.True(early?.Vars.Speed == 42f, "a key before spawn is assigned");
        Assert.True(early?.Spawned == false, "a key before spawn does not run OnSpawn");

        registry.Spawn(Ent, "test_binding");
        Assert.True(ReferenceEquals(registry.Get(Ent), early), "spawn reuses the instance the keys built");
        Assert.True(early?.Spawned == true, "spawn still runs OnSpawn");
        Assert.True(early?.Vars.Speed == 42f, "spawn does not undo the map's values");
    }

    [Fact]
    public void KeyBinding()
    {
        var registry = Fresh();
        var entity = Bound(registry);

        Assert.True(registry.KeyValue(Ent, "test_binding", "speed", "2.5") && entity.Vars.Speed == 2.5f, "float key binds");
        Assert.True(registry.KeyValue(Ent, "test_binding", "SPEED", "3") && entity.Vars.Speed == 3.0f, "key name ignores case");
        Assert.True(registry.KeyValue(Ent, "test_binding", "in1", "7") && entity.Vars.Lower == 7.0f, "explicit key name wins");
        Assert.True(registry.KeyValue(Ent, "test_binding", "label", "hello") && entity.Vars.Label == "hello", "string key binds");

        // Maps routinely write "5.0" where an integer is meant.
        Assert.True(registry.KeyValue(Ent, "test_binding", "count", "5") && entity.Vars.Count == 5, "int key accepts a whole number");
        Assert.True(registry.KeyValue(Ent, "test_binding", "count", "9.0") && entity.Vars.Count == 9, "int key accepts a decimal");
        Assert.True(registry.KeyValue(Ent, "test_binding", "count", "4.9") && entity.Vars.Count == 4, "int key truncates");

        // Map booleans are 0/1, not "true"/"false".
        Assert.True(registry.KeyValue(Ent, "test_binding", "enabled", "1") && entity.Vars.Enabled, "bool key reads 1 as true");
        Assert.True(registry.KeyValue(Ent, "test_binding", "enabled", "0") && !entity.Vars.Enabled, "bool key reads 0 as false");

        // An unparseable value must leave the default rather than assign noise.
        entity.Vars.Speed = 1.0f;
        Assert.True(registry.KeyValue(Ent, "test_binding", "speed", "banana") && entity.Vars.Speed == 1.0f, "unparseable value is claimed but not assigned");

        // Maps write vectors as three numbers with spaces, which is how every
        // origin, axis and attach point in a map is spelled.
        Assert.True(registry.KeyValue(Ent, "test_binding", "where", "1 -2 3.5")
            && entity.Vars.Where.X == 1f && entity.Vars.Where.Y == -2f && entity.Vars.Where.Z == 3.5f, "vector key binds");

        entity.Vars.Where = Vector.Zero;
        Assert.True(registry.KeyValue(Ent, "test_binding", "where", "1 2") && entity.Vars.Where.Equals(Vector.Zero), "a vector short of three numbers is refused");

        Assert.True(registry.KeyValue(Ent, "test_binding", "somethingelse", "x") && entity.UnclaimedKey == "somethingelse", "unbound key falls through to the entity");

        Assert.True(!registry.KeyValue(Other, "no_such_class", "speed", "1"), "key on an unknown entity is not claimed");

        NoVarSets();
    }

    /// <summary>
    /// An entity that holds no var set at all. Most do not, and the absence has
    /// to mean "declares no keys" rather than anything going wrong — which is
    /// worth a test of its own, because the registry now looks for a marked
    /// field and finding none is the common answer.
    /// </summary>
    private static void NoVarSets()
    {
        var registry = Fresh();
        var handle = new EntityHandle(0x0044_0044u);

        registry.Spawn(handle, "test_no_vars");
        var entity = (TestNoVarsEntity)registry.Get(handle)!;

        Assert.True(entity.Spawned, "an entity with no var set spawns");
        Assert.True(entity.VarSets.Length == 0, "an entity with no var set holds none");

        Assert.True(registry.KeyValue(handle, "test_no_vars", "anything", "1")
            && entity.SawKey == "anything", "its keys reach OnKeyValue");

        Assert.True(registry.DescribeClasses().Single(c => c.ClassName == "test_no_vars").Vars.Count == 0, "and it declares no keys");
    }

    [Fact]
    public void InputBinding()
    {
        var registry = Fresh();
        var entity = Bound(registry);

        Assert.True(registry.Input(Ent, "Go", "", 0.0f, EntityHandle.None) && entity.NoArgCalls == 1, "no-arg input binds");

        Assert.True(registry.Input(Ent, "go", "", 0.0f, EntityHandle.None) && entity.NoArgCalls == 2, "input name ignores case");

        Assert.True(registry.Input(Ent, "SetSpeed", "12", 12.0f, EntityHandle.None) && entity.LastNumber == 12.0f, "float input receives the number");

        Assert.True(registry.Input(Ent, "SetLabel", "abc", 0.0f, EntityHandle.None) && entity.LastString == "abc", "string input receives the text");

        // InputData is the signature for inputs that need the activator.
        bool renamed = registry.Input(Ent, "Renamed", "v", 3.0f, Other);
        Assert.True(renamed, "explicit input name wins");
        Assert.True(entity.LastString == "v" && entity.LastNumber == 3.0f && entity.LastActivator == Other, "InputData carries value, number and activator");

        Assert.True(registry.Input(Ent, "Whatever", "", 0.0f, EntityHandle.None) && entity.UnclaimedInput == "Whatever", "unbound input falls through to the entity");

        Assert.True(!registry.Input(new EntityHandle(0x9u), "Go", "", 0.0f, EntityHandle.None), "input on an unknown entity is not claimed");
    }

    /// <summary>
    /// <c>Kill</c> is not a feature of any particular class. Stock declares it on
    /// CBaseEntity, so every <c>ent_fire &lt;anything&gt; Kill</c> ever written
    /// assumes it — and the parallel hierarchy's shell claims no inputs of its
    /// own, which left the rule belonging to managed code and declared by
    /// nobody. A managed entity told to die answered <c>accepted: 0</c>.
    ///
    /// <para>
    /// Asserted over EVERY registered class rather than over one fixture,
    /// because "every entity" is the claim. A test that spawned one and fired at
    /// it would pass just as happily if <c>Kill</c> had been added to that one
    /// class.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryScriptedEntityAnswersToKill()
    {
        var registry = Fresh();

        var missing = registry.DescribeClasses()
            .Where(c => !c.Inputs.Contains("Kill", StringComparer.OrdinalIgnoreCase))
            .Select(c => c.ClassName)
            .ToList();

        // The reach check first: a walk that found no classes would report no
        // gaps and look identical to a pass.
        Assert.True(registry.DescribeClasses().Count > 0, "there are entity classes to check");

        Assert.True(missing.Count == 0,
            "every scripted entity class declares the Kill input; these do not: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// And a class may mean something else by it. That is why it is virtual and
    /// not a shell input: a breakable breaks, a round-based entity may refuse,
    /// and the mapper's connection still lands on whatever the class decided.
    /// </summary>
    [Fact]
    public void AClassMayDecideWhatDyingMeans()
    {
        var registry = Fresh();
        registry.Spawn(Ent, "test_kill_override");

        var entity = (TestKillOverrideEntity)registry.Get(Ent)!;

        Assert.True(registry.Input(Ent, "Kill", "", 0.0f, EntityHandle.None), "the Kill input is claimed");
        Assert.True(entity.Killed == 1, "and it reached the class's own OnKill, not the base's Remove");
    }

    [Fact]
    public void Outputs()
    {
        var registry = Fresh();
        var entity = Bound(registry);

        Assert.True(entity.OnFirst.FireCount == 0, "outputs start unfired");

        registry.Input(Ent, "FireAll", "", 0.0f, EntityHandle.None);

        Assert.True(entity.OnFirst.FireCount == 1 && entity.OnFirst.LastValue is null, "valueless output fires with no value");
        Assert.True(entity.OnSecond.LastValue == "2.5", "numeric output carries its number");
        Assert.True(entity.OnThird.LastValue == "text", "text output carries its text");
        Assert.True(entity.OnFirst.Name == "OnFirst", "output knows its mapper-visible name");

        // Clearing must drop instances, or a handle reused by the next map would
        // reach the previous map's entity.
        registry.Clear();
        Assert.True(registry.Get(Ent) is null, "clearing drops instances");
    }

    //-------------------------------------------------------------------------
    // The entities themselves. Outputs record their own firing, so behaviour is
    // observable without the engine.
    //-------------------------------------------------------------------------

    private static ScriptedEntityRegistry Game()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(MathCounter).Assembly);
        registry.Services = Services();
        return registry;
    }

    private static T Spawn<T>(ScriptedEntityRegistry registry, string className) where T : ScriptedEntity
    {
        registry.Spawn(Ent, className);
        return (T)registry.Get(Ent)!;
    }

    [Fact]
    public void LogicBehaviour()
    {
        CounterTests();
        BranchTests();
        CompareTests();
        CaseTests();
        RemapTests();
        RelayTests();
        AutoTests();
    }

    private static void CounterTests()
    {
        var registry = Game();
        var counter = Spawn<MathCounter>(registry, EntityNames.MathCounter);

        registry.KeyValue(Ent, "test_binding", "startvalue", "5");
        registry.KeyValue(Ent, "test_binding", "min", "0");
        registry.KeyValue(Ent, "test_binding", "max", "10");
        registry.Input(Ent, "Add", "3", 3.0f, EntityHandle.None);

        Assert.True(counter.OutValue.LastValue == "8", "counter adds and reports");

        registry.Input(Ent, "Subtract", "2", 2.0f, EntityHandle.None);
        Assert.True(counter.OutValue.LastValue == "6", "counter subtracts");

        registry.Input(Ent, "Add", "100", 100.0f, EntityHandle.None);
        Assert.True(counter.OutValue.LastValue == "10", "counter clamps to max");
        Assert.True(counter.OnHitMax.FireCount == 1, "counter reports hitting max");

        // Sitting at the limit must not report again, or a map counting fires
        // would see one per input rather than one per crossing.
        registry.Input(Ent, "Add", "1", 1.0f, EntityHandle.None);
        Assert.True(counter.OnHitMax.FireCount == 1, "counter reports hitting max once");

        registry.Input(Ent, "SetValue", "0", 0.0f, EntityHandle.None);
        Assert.True(counter.OnHitMin.FireCount == 1, "counter reports hitting min");

        int before = counter.OutValue.FireCount;
        registry.Input(Ent, "SetValueNoFire", "7", 7.0f, EntityHandle.None);
        Assert.True(counter.OutValue.FireCount == before, "SetValueNoFire is silent");

        registry.Input(Ent, "GetValue", "", 0.0f, EntityHandle.None);
        Assert.True(counter.OnGetValue.LastValue == "7", "GetValue reports the stored value");

        registry.Input(Ent, "Disable", "", 0.0f, EntityHandle.None);
        registry.Input(Ent, "Add", "1", 1.0f, EntityHandle.None);
        Assert.True(counter.OnGetValue.LastValue == "7", "a disabled counter ignores input");
    }

    private static void BranchTests()
    {
        var registry = Game();
        var branch = Spawn<LogicBranch>(registry, EntityNames.LogicBranch);

        registry.KeyValue(Ent, "test_binding", "InitialValue", "1");
        registry.Input(Ent, "Test", "", 0.0f, EntityHandle.None);
        Assert.True(branch.OnTrue.FireCount == 1 && branch.OnFalse.FireCount == 0, "branch reports its initial value");

        // SetValue stores without reporting, so a map can seed state quietly.
        registry.Input(Ent, "SetValue", "0", 0.0f, EntityHandle.None);
        Assert.True(branch.OnFalse.FireCount == 0, "SetValue is silent");

        registry.Input(Ent, "Test", "", 0.0f, EntityHandle.None);
        Assert.True(branch.OnFalse.FireCount == 1, "branch reports the stored value");

        registry.Input(Ent, "ToggleTest", "", 0.0f, EntityHandle.None);
        Assert.True(branch.OnTrue.FireCount == 2, "ToggleTest flips and reports");
    }

    private static void CompareTests()
    {
        var registry = Game();
        var compare = Spawn<LogicCompare>(registry, EntityNames.LogicCompare);

        registry.KeyValue(Ent, "test_binding", "CompareValue", "10");

        registry.Input(Ent, "SetValueCompare", "4", 4.0f, EntityHandle.None);
        Assert.True(compare.OnLessThan.FireCount == 1, "compare reports less than");
        Assert.True(compare.OnNotEqualTo.FireCount == 1, "less than also reports not equal");

        registry.Input(Ent, "SetValueCompare", "99", 99.0f, EntityHandle.None);
        Assert.True(compare.OnGreaterThan.FireCount == 1, "compare reports greater than");

        // Captured first: the two comparisons above each reported not-equal, so
        // the interesting thing is that this one adds nothing to that.
        int notEqualBefore = compare.OnNotEqualTo.FireCount;

        registry.Input(Ent, "SetValueCompare", "10", 10.0f, EntityHandle.None);
        Assert.True(compare.OnEqualTo.FireCount == 1, "compare reports equal");
        Assert.True(compare.OnNotEqualTo.FireCount == notEqualBefore, "equal does not report not equal");
    }

    private static void CaseTests()
    {
        var registry = Game();
        var logicCase = Spawn<LogicCase>(registry, EntityNames.LogicCase);

        registry.KeyValue(Ent, "test_binding", "Case01", "red");
        registry.KeyValue(Ent, "test_binding", "Case02", "green");

        registry.Input(Ent, "InValue", "green", 0.0f, EntityHandle.None);
        Assert.True(logicCase.OnCase02.FireCount == 1, "case routes to the matching output");
        Assert.True(logicCase.OnCase01.FireCount == 0, "case does not fire the others");

        registry.Input(Ent, "InValue", "blue", 0.0f, EntityHandle.None);
        Assert.True(logicCase.OnDefault.FireCount == 1, "an unmatched value takes the default");

        // Shuffle must exhaust the configured cases before repeating.
        registry.Input(Ent, "PickRandomShuffle", "", 0.0f, EntityHandle.None);
        registry.Input(Ent, "PickRandomShuffle", "", 0.0f, EntityHandle.None);
        Assert.True(logicCase.OnCase01.FireCount == 1 && logicCase.OnCase02.FireCount == 2, "shuffle deals every case before repeating");
    }

    private static void RemapTests()
    {
        var registry = Game();
        var remap = Spawn<MathRemap>(registry, EntityNames.MathRemap);

        registry.KeyValue(Ent, "test_binding", "in1", "0");
        registry.KeyValue(Ent, "test_binding", "in2", "100");
        registry.KeyValue(Ent, "test_binding", "out1", "0");
        registry.KeyValue(Ent, "test_binding", "out2", "1");
        remap.OnSpawn();

        registry.Input(Ent, "InValue", "50", 50.0f, EntityHandle.None);
        Assert.True(remap.OutValue.LastValue == "0.5", "remap rescales to the output range");

        registry.Input(Ent, "InValue", "500", 500.0f, EntityHandle.None);
        Assert.True(remap.OutValue.LastValue == "1", "remap clamps beyond the input range");

        registry.Input(Ent, "Disable", "", 0.0f, EntityHandle.None);
        int before = remap.OutValue.FireCount;
        registry.Input(Ent, "InValue", "10", 10.0f, EntityHandle.None);
        Assert.True(remap.OutValue.FireCount == before, "a disabled remap ignores input");
    }

    private static void RelayTests()
    {
        var registry = Game();
        var relay = Spawn<LogicRelay>(registry, EntityNames.LogicRelay);
        relay.OnSpawn();

        registry.Input(Ent, "Trigger", "", 0.0f, EntityHandle.None);
        Assert.True(relay.OnTrigger.FireCount == 1, "a relay passes a trigger along");

        // Without the fast-retrigger flag a relay refuses to fire again until
        // its outputs have gone out. This is what stops a map that loops a relay
        // back to itself recursing until the stack gives up.
        registry.Input(Ent, "Trigger", "", 0.0f, EntityHandle.None);
        Assert.True(relay.OnTrigger.FireCount == 1, "a relay does not re-enter while its outputs are going out");

        registry.Think(Ent);
        registry.Input(Ent, "Trigger", "", 0.0f, EntityHandle.None);
        Assert.True(relay.OnTrigger.FireCount == 2, "a relay fires again once its outputs are out");

        registry.Input(Ent, "Disable", "", 0.0f, EntityHandle.None);
        registry.Think(Ent);
        registry.Input(Ent, "Trigger", "", 0.0f, EntityHandle.None);
        Assert.True(relay.OnTrigger.FireCount == 2, "a disabled relay passes nothing");

        registry.Input(Ent, "Toggle", "", 0.0f, EntityHandle.None);
        registry.Input(Ent, "Trigger", "", 0.0f, EntityHandle.None);
        Assert.True(relay.OnTrigger.FireCount == 3, "toggling re-enables it");

        // StartDisabled is read at spawn, so a relay a map starts off stays off.
        var registry2 = Game();
        registry2.KeyValue(Ent, EntityNames.LogicRelay, "StartDisabled", "1");
        var relay2 = Spawn<LogicRelay>(registry2, EntityNames.LogicRelay);

        registry2.Input(Ent, "Trigger", "", 0.0f, EntityHandle.None);
        Assert.True(relay2.OnTrigger.FireCount == 0, "StartDisabled holds from spawn");
    }

    private static void AutoTests()
    {
        var registry = Game();
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);
        auto.OnSpawn();

        // Nothing fires at spawn: the map's other entities do not all exist yet,
        // so outputs sent then would go to targets that cannot be found.
        Assert.True(auto.OnMapSpawn.FireCount == 0, "logic_auto fires nothing at spawn");

        registry.Think(Ent);
        Assert.True(auto.OnMapSpawn.FireCount == 1, "logic_auto fires once the map is up");
        Assert.True(auto.OnMultiNewMap.FireCount == 1, "and reports a new multiplayer map");

        // The singleplayer load types have no meaning here.
        Assert.True(auto.OnLoadGame.FireCount == 0
            && auto.OnMapTransition.FireCount == 0
            && auto.OnNewGame.FireCount == 0, "no save-game or transition outputs");
    }
}

/// <summary>
/// The map keys <see cref="TestBindingEntity"/> declares.
/// </summary>
[ScriptVars]
public partial class TestBindingEntityVars
{
    // Name defaults to the field's, with the underscore dropped.
    public float Speed { get; set; }

    public int Count { get; set; }

    public bool Enabled { get; set; }

    public string Label { get; set; } = string.Empty;

    // Explicit name, for when the map key and a readable field name differ.
    [VarName("in1")]
    public float Lower { get; set; }

    public Vector Where { get; set; }
}
