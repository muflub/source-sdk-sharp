using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE GENERATED DISPATCH AGAINST THE REFLECTIVE ONE, over every
/// <c>[ScriptVars]</c> class in the build.
///
/// <para>
/// The reflective binding is the oracle: it is what the game used, it is right
/// by construction, and the commit after this one deletes it. What replaces it
/// is <c>OnKeyDataUpdate</c>, generated from the same declarations, and the two
/// need not agree — the way they would disagree is by binding a key to a
/// different member, or by not knowing a key at all, and both are silent. So
/// both are run over every declared key with values a map plausibly writes, and
/// every property of both sets is compared afterwards.
/// </para>
///
/// <para>
/// WHAT IS AND IS NOT UNDER TEST. Both sides parse through
/// <see cref="ScriptedVarParse"/>, deliberately: a second parser here would mean
/// a disagreement was as likely to be this file's fault as the generator's, and
/// the thing being replaced is the mapping from a key NAME to a member, not the
/// reading of "5.0" as five. The oracle also reproduces the old registry's
/// odd-looking rule that an unreadable value still CLAIMS the key, because that
/// rule is behaviour a map depends on.
/// </para>
///
/// <para>
/// IsSet is checked SEPARATELY, below, and it has to be: reflection assigning a
/// property cannot stamp a private field, so comparing it between the two would
/// be comparing a thing the oracle was never able to do. What is checked instead
/// is stronger — that a claimed key stamps ITSELF and nothing else.
/// </para>
///
/// <para>
/// AND THE CHECK CAN FAIL. Two negative controls run the same comparison against
/// oracles that are deliberately wrong — one that has lost a key, one that binds
/// a key to the wrong property — and fail if the comparison reports them as
/// agreeing. This project has twice shipped a test that compared a thing with
/// itself and passed happily, so a comparison that has never been seen to fail
/// is not evidence of anything.
/// </para>
/// </summary>
public class ScriptedVarDispatchTests
{
    [Fact]
    public void ThereAreScriptVarsClassesToCheck()
    {
        var sets = SetTypes();

        Assert.True(sets.Count > 0, $"there are [ScriptVars] classes to check ({sets.Count})");
    }

    [Fact]
    public void TheGeneratedDispatchAgreesWithReflectionOnEveryDeclaredKey()
    {
        var sets = SetTypes();

        int keys = 0;
        var disagreements = new List<string>();

        foreach (var type in sets)
        {
            var oracle = ReflectiveMap(type);
            keys += oracle.Count;
            disagreements.AddRange(Disagreements(type, oracle));
        }

        foreach (string line in disagreements.Take(20))
            Assert.Fail($"generated dispatch disagrees: {line}");

        Assert.True(disagreements.Count == 0, $"the generated dispatch agrees with reflection on every declared key " +
              $"({keys} keys over {sets.Count} classes)");
    }

    //-------------------------------------------------------------------------
    // Both doors fold, and neither folds twice.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A MAP'S OWN SPELLING ARRIVES, WHICHEVER WAY IT COMES IN.
    ///
    /// <para>
    /// The generated dispatch stopped folding case when it became an ordinary
    /// switch. There are two ways in past that and both are checked here,
    /// because for one commit only one of them worked: the registry, which folds
    /// once for all of an entity's sets, and the public OnKeyDataUpdate, which
    /// folds for anyone else. That second one was briefly the abstract method
    /// itself, so a caller handing it "PushDir" got a silent false — see
    /// <see cref="TheOutsideDoorFoldsForYou"/>, which is that bug written down.
    /// </para>
    ///
    /// <para>
    /// Every spelling of "DisappearDist" a mapper might type, against a class
    /// whose declared casing is none of them.
    /// </para>
    /// </summary>
    [Fact]
    public void CaseIsHandledAtTheBoundary()
    {
        string[] spellings = { "DisappearDist", "disappeardist", "DISAPPEARDIST", "DiSaPpEaRdIsT" };

        foreach (string spelling in spellings)
        {
            var registry = new ScriptedEntityRegistry();
            registry.RegisterAll(typeof(ScriptedVarDispatchTests).Assembly);
            registry.Services = Services();

            var handle = new EntityHandle(0x0077_0077u);
            bool claimed = registry.KeyValue(handle, EntityNames.FuncLod, spelling, "1234");
            var entity = registry.Get(handle) as FuncLod;

            Assert.True(claimed && entity is not null && entity.Vars.DisappearDistance == 1234f, $"a map writing '{spelling}' reaches the key");

            Assert.True(entity is not null && entity.Vars.DisappearDistanceIsSet, $"and '{spelling}' is recorded as written");
        }

        // The other half: folding case must not fold two keys together. A key
        // that differs by more than case is still a different key.
        var fresh = new ScriptedEntityRegistry();
        fresh.RegisterAll(typeof(ScriptedVarDispatchTests).Assembly);
        fresh.Services = Services();

        var other = new EntityHandle(0x0078_0078u);

        Assert.True(!fresh.KeyValue(other, EntityNames.FuncLod, "disappeardistance", "1"), "a key that is not a spelling of a declared one is not claimed");

        TheOutsideDoorFoldsForYou();
        NearMissComparesLikeForLike();
    }

    /// <summary>
    /// THE PUBLIC METHOD IS CORRECT FOR ANY INPUT, which is the whole reason it
    /// exists separately from the dispatch it calls.
    ///
    /// <para>
    /// This is a regression test for a real defect, briefly shipped: when the
    /// dispatch became a switch on lower-case labels, the abstract method a
    /// caller reached WAS that dispatch, so
    /// <c>set.OnKeyDataUpdate("PushDir", "90 0 0")</c> — a correctly spelled
    /// key, in the casing the class itself declares — returned false and wrote
    /// nothing. Nothing threw and nothing was logged.
    /// </para>
    ///
    /// <para>
    /// A precondition in a doc comment is not a precondition. The fix was one
    /// non-virtual method that folds before dispatching, and this is what says
    /// the failure is unreachable rather than merely documented.
    /// </para>
    /// </summary>
    private static void TheOutsideDoorFoldsForYou()
    {
        // The declared casing, which is the spelling a reader of the class would
        // most naturally type and the one that used to fail.
        var declared = new TriggerPushVars();

        Assert.True(declared.OnKeyDataUpdate("PushDir", "90 0 0")
              && declared.PushDir == new QAngle(90f, 0f, 0f)
              && declared.PushDirIsSet, "the public method takes a key in its declared casing");

        var upper = new TriggerPushVars();

        Assert.True(upper.OnKeyDataUpdate("PUSHDIR", "90 0 0") && upper.PushDirIsSet, "and in upper case");

        var mixed = new TriggerPushVars();

        Assert.True(mixed.OnKeyDataUpdate("pUsHdIr", "90 0 0") && mixed.PushDirIsSet, "and in any case at all");

        var lower = new TriggerPushVars();

        Assert.True(lower.OnKeyDataUpdate("pushdir", "90 0 0") && lower.PushDirIsSet, "and the already-folded spelling still works");

        // Folding is not matching: a key that differs by more than case is
        // still refused, so the fix did not buy its safety by loosening.
        var other = new TriggerPushVars();

        Assert.True(!other.OnKeyDataUpdate("PushDirection", "90 0 0") && !other.PushDirIsSet, "a genuinely different key is still not claimed");
    }

    /// <summary>
    /// THE NEAR-MISS WARNING SURVIVES THE FOLD.
    ///
    /// <para>
    /// It is the one thing the reflective binding could never do — telling a
    /// mistyped key apart from an engine key passing through — and lowering the
    /// match key put it one careless step from uselessness. If it compared a
    /// lowered key against the DECLARED casing without folding both, then
    /// "DisappearDist" would differ from "disappeardist" by twelve characters
    /// and every key a mapper spelled correctly would be reported as a near miss
    /// of itself.
    /// </para>
    ///
    /// <para>
    /// It compares both sides folded, and it always did; this is what says so
    /// out loud now that the caller's spelling and the declared spelling are
    /// deliberately allowed to differ.
    /// </para>
    /// </summary>
    private static void NearMissComparesLikeForLike()
    {
        Assert.True(!ScriptedEntityRegistry.IsNearMiss("disappeardist", "DisappearDist"), "a key spelled in another case is not a near miss of itself");

        Assert.True(!ScriptedEntityRegistry.IsNearMiss("DISAPPEARDIST", "DisappearDist"), "nor is it the other way round");

        Assert.True(ScriptedEntityRegistry.IsNearMiss("disapeardist", "DisappearDist")
              && ScriptedEntityRegistry.IsNearMiss("DisapearDist", "DisappearDist"), "a genuine typo still is one, whatever case it arrives in");

        Assert.True(ScriptedEntityRegistry.IsNearMiss("disappeardis", "DisappearDist"), "a dropped character is a near miss");

        Assert.True(ScriptedEntityRegistry.IsNearMiss("disappeardistt", "DisappearDist"), "an extra one is too");

        Assert.True(!ScriptedEntityRegistry.IsNearMiss("origin", "DisappearDist"), "a different word is not");
    }

    //-------------------------------------------------------------------------
    // The half reflection could never have done.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A claimed key marks itself set, and marks nothing else.
    ///
    /// Not a comparison against the oracle — the oracle assigns through
    /// <c>PropertyInfo</c> and cannot reach the stamp at all. This is the check
    /// that the generated dispatch wires each key's assignment to that key's own
    /// record, which is the one place a copy-paste in generated code would land.
    /// </summary>
    [Fact]
    public void EveryKeyStampsItselfAndNothingElse()
    {
        var sets = SetTypes();

        var wrong = new List<string>();
        int checked_ = 0;

        foreach (var type in sets)
        {
            foreach (var declared in New(type).Descriptors)
            {
                var set = New(type);

                // Whichever spelling this key's type can actually read. A key
                // CLAIMS a value it cannot read, and does not stamp it, so
                // "claimed" is not the signal to stop on.
                foreach (string value in new[] { "1", "1 1 1", "1 1 1 1", "x" })
                {
                    set.OnKeyDataUpdate(declared.Name, value);

                    if (declared.IsSet(set))
                        break;
                }

                checked_++;

                if (!declared.IsSet(set))
                {
                    wrong.Add($"{type.Name}: '{declared.Name}' would not read any test value");
                    continue;
                }

                foreach (var other in set.Descriptors)
                {
                    bool expected = other.Name == declared.Name;

                    if (other.IsSet(set) != expected)
                    {
                        wrong.Add($"{type.Name}: writing '{declared.Name}' left '{other.Name}' " +
                                  $"{(other.IsSet(set) ? "set" : "unset")}");
                    }
                }
            }
        }

        foreach (string line in wrong.Take(10))
            Assert.Fail($"stamp landed wrong: {line}");

        Assert.True(wrong.Count == 0, $"every key stamps itself and nothing else ({checked_} keys)");
    }

    //-------------------------------------------------------------------------
    // The controls that prove the comparison above is capable of failing.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A set with at least two keys of the same type, so "bound to the wrong
    /// property" is expressible at all.
    /// </summary>
    private static Type Mutable()
    {
        var subject = SetTypes().FirstOrDefault(t =>
            ReflectiveMap(t).Values.GroupBy(pr => pr.PropertyType, SymbolEq).Any(g => g.Count() >= 2));

        Assert.True(subject is not null, "a set with two keys of one type exists to mutate");

        return subject!;
    }

    [Fact]
    public void ASetWithTwoKeysOfOneTypeExistsToMutate()
        => Assert.True(Mutable() is not null, "a set with two keys of one type exists to mutate");

    [Fact]
    public void TheHonestOracleAgrees()
    {
        var subject = Mutable();

        Assert.True(Disagreements(subject, ReflectiveMap(subject)).Count == 0, "the honest oracle agrees, or the controls below prove nothing");
    }

    [Fact]
    public void TheComparisonNoticesAKeyTheOracleLost()
    {
        // Control one: the oracle has LOST a key. The generated dispatch still
        // claims it; the oracle does not. That is the failure that matters most
        // -- a key that quietly stops arriving -- so the comparison must see it.
        var subject = Mutable();

        var lost = new Dictionary<string, PropertyInfo>(ReflectiveMap(subject), StringComparer.OrdinalIgnoreCase);
        string dropped = lost.Keys.First();
        lost.Remove(dropped);

        Assert.True(Disagreements(subject, lost).Count > 0, $"the comparison notices a key the oracle lost ('{dropped}')");
    }

    [Fact]
    public void TheComparisonNoticesAKeyBoundToTheWrongProperty()
    {
        // Control two: the oracle binds a key to the WRONG property. Both sides
        // claim every key and both parse identically; only the member written
        // differs, which is exactly the mistake a hand-written switch makes.
        var subject = Mutable();
        var honest = ReflectiveMap(subject);

        var swapped = new Dictionary<string, PropertyInfo>(honest, StringComparer.OrdinalIgnoreCase);
        var pair = honest.GroupBy(kv => kv.Value.PropertyType, SymbolEq)
                         .First(g => g.Count() >= 2).Take(2).ToArray();

        (swapped[pair[0].Key], swapped[pair[1].Key]) = (pair[1].Value, pair[0].Value);

        Assert.True(Disagreements(subject, swapped).Count > 0, $"the comparison notices a key bound to the wrong property " +
              $"('{pair[0].Key}' and '{pair[1].Key}')");
    }

    private static readonly IEqualityComparer<Type> SymbolEq = EqualityComparer<Type>.Default;

    //-------------------------------------------------------------------------
    // The comparison itself.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every way the generated dispatch and <paramref name="oracle"/> differ on
    /// <paramref name="type"/>, as lines naming the key and the value.
    /// </summary>
    private static List<string> Disagreements(Type type, Dictionary<string, PropertyInfo> oracle)
    {
        var found = new List<string>();

        // Every key either side believes in, so a key only one of them knows is
        // itself a disagreement rather than a key nobody tries.
        var keys = new SortedSet<string>(oracle.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var d in New(type).Descriptors)
            keys.Add(d.Name);

        foreach (string key in keys)
        {
            foreach (string value in Values)
            {
                var generated = New(type);
                var reflected = New(type);

                bool a = generated.OnKeyDataUpdate(key, value);
                bool b = Reflect(reflected, oracle, key, value);

                if (a != b)
                {
                    found.Add($"{type.Name}.{key} = '{value}': generated claimed {a}, reflection {b}");
                    continue;
                }

                string x = State(generated);
                string y = State(reflected);

                if (x != y)
                    found.Add($"{type.Name}.{key} = '{value}': generated left {x}, reflection {y}");
            }
        }

        // Case and near misses: a key must match however a map spells it, and
        // must NOT match when it is a different name. The generated dispatch
        // matches lower-case labels, but nothing outside the registry has to
        // know that — OnKeyDataUpdate folds first — and this is the check that
        // the folding door is the one a caller gets.
        foreach (string key in keys)
        {
            bool up = New(type).OnKeyDataUpdate(key.ToUpperInvariant(), "1");
            bool down = New(type).OnKeyDataUpdate(key.ToLowerInvariant(), "1");
            bool declared = New(type).OnKeyDataUpdate(key, "1");
            bool space = New(type).OnKeyDataUpdate(key + " ", "1");

            if (!up || !down || !declared)
                found.Add($"{type.Name}.{key}: case-insensitive matching failed ({up}/{down}/{declared})");

            if (space)
                found.Add($"{type.Name}.{key}: '{key} ' matched, and it is a different key");
        }

        return found;
    }

    /// <summary>
    /// What the old registry did, reproduced: find the key, parse it, assign
    /// it — and claim it either way, because an unreadable value is still this
    /// class's key.
    /// </summary>
    private static bool Reflect(ScriptedVarSet set, Dictionary<string, PropertyInfo> oracle,
                                string key, string value)
    {
        if (!oracle.TryGetValue(key, out var property))
            return false;

        if (TryParse(property.PropertyType, value, out object? parsed))
            property.SetValue(set, parsed);

        return true;
    }

    private static bool TryParse(Type target, string value, out object? result)
    {
        result = null;

        if (target == typeof(string))
        {
            result = value;
            return true;
        }

        // The one kind whose parse is generic (plan_nettable_generic.md
        // Step 12), so the oracle reaches it the way a runtime oracle has to.
        // Through ScriptedVarParse like every arm here: what this file compares
        // is the mapping from a key NAME to a member, and an oracle with its own
        // parser would be comparing parsers instead.
        if (target.IsEnum)
        {
            object?[] args = { value, null };

            bool parsed = (bool)typeof(ScriptedVarParse)
                .GetMethod(nameof(ScriptedVarParse.Enum))!
                .MakeGenericMethod(target)
                .Invoke(null, args)!;

            if (parsed)
                result = args[1];

            return parsed;
        }

        if (target == typeof(float) && ScriptedVarParse.Float(value, out float f))
        {
            result = f;
            return true;
        }

        if (target == typeof(int) && ScriptedVarParse.Int(value, out int i))
        {
            result = i;
            return true;
        }

        if (target == typeof(bool) && ScriptedVarParse.Bool(value, out bool b))
        {
            result = b;
            return true;
        }

        if (target == typeof(Vector) && ScriptedVarParse.Vector(value, out var v))
        {
            result = v;
            return true;
        }

        if (target == typeof(QAngle) && ScriptedVarParse.QAngle(value, out var a))
        {
            result = a;
            return true;
        }

        if (target == typeof(Color32) && ScriptedVarParse.Color(value, out var c))
        {
            result = c;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Every value a set holds, as text. Values only: IsSet is checked by
    /// <see cref="Stamps"/>, because the oracle assigns through a PropertyInfo
    /// and has no way to reach a stamp — comparing it here would be comparing
    /// the generated half against something reflection was never able to do.
    /// </summary>
    private static string State(ScriptedVarSet set)
        => string.Join("|", set.Descriptors.Select(d => $"{d.Member}={d.Read(set)}"));

    //-------------------------------------------------------------------------
    // Finding what to check.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The key-to-property map reflection would build, by the same rule the
    /// generator uses: a settable instance property, named for itself unless
    /// [VarName] says otherwise.
    /// </summary>
    private static Dictionary<string, PropertyInfo> ReflectiveMap(Type type)
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        // DeclaredOnly, and no walk up the base chain: a var set's only base is
        // ScriptedVarSet, whose properties are the machinery rather than keys.
        // Sets do not inherit from one another — an entity holds its base's set
        // beside its own instead — so the declared type is the whole list.
        foreach (var property in type.GetProperties(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (property.GetMethod is null || property.SetMethod is null
                || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            map[property.GetCustomAttribute<VarNameAttribute>()?.Name ?? property.Name] = property;
        }

        return map;
    }

    /// <summary>Every [ScriptVars] class in every loaded mod assembly.</summary>
    private static IReadOnlyList<Type> SetTypes()
        => LoadedAssemblies.Types(typeof(ScriptedVarDispatchTests).Assembly, new RecordingLog())
               .Where(t => !t.IsAbstract
                           && typeof(ScriptedVarSet).IsAssignableFrom(t)
                           && t.GetCustomAttribute<ScriptVarsAttribute>() is not null)
               .OrderBy(t => t.FullName, StringComparer.Ordinal)
               .ToArray();

    private static ScriptedVarSet New(Type type)
        => (ScriptedVarSet)Activator.CreateInstance(type)!;

    /// <summary>
    /// What a map writes. Readable and unreadable both, since the claim/refuse
    /// verdict is half of what the two sides have to agree about.
    /// </summary>
    private static readonly string[] Values =
    {
        "0", "1", "-1", "2.5", "9.0", "4.9", "1e3", "255",
        "1 -2 3.5", "0 0 0", "1 2", "10 20 30 40", "255.0 0 0",
        "hello", "", "   ", " 7 ", "banana",
    };
}
