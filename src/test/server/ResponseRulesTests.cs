using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The response rule search, ported from CResponseSystem
/// (game/server/AI_ResponseSystem.cpp): the script language and its
/// tokeniser, the criterion matcher, the best-rule score, and the
/// weighted/sequential response selection with its filters.
/// </summary>
public class ResponseRulesTests
{
    private const string ScriptName = "scripts/talker/test.txt";

    /// <summary>Draws handed to the engine in order; an int-less draw answers 0.</summary>
    private sealed class ScriptedRandom : IUniformRandom
    {
        private readonly Queue<float> _floats = new();
        private readonly Queue<int> _ints = new();

        public void Floats(params float[] draws)
        {
            foreach (float d in draws)
                _floats.Enqueue(d);
        }

        public void Ints(params int[] draws)
        {
            foreach (int d in draws)
                _ints.Enqueue(d);
        }

        public float RandomFloat(float min, float max)
        {
            // unstaged draws answer a clamped half: facts that care stage
            // their sequence and assert InRange on every staged value
            if (_floats.Count == 0)
            {
                return Math.Min(Math.Max(0.5f, min), max);
            }

            float v = _floats.Dequeue();
            Assert.InRange(v, min, max);
            return v;
        }

        public int RandomInt(int min, int max) => _ints.Count > 0 ? _ints.Dequeue() : 0;
    }

    private sealed class Files : IEngineFiles
    {
        public readonly Dictionary<string, byte[]> Bytes = new(StringComparer.OrdinalIgnoreCase);

        public int GameFileSize(string path) => Bytes.TryGetValue(path, out var b) ? b.Length : -1;

        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path) => Bytes.TryGetValue(path, out var b) ? b : null;

        public byte[]? ReadFileOnPath(string path, string pathId)
            => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;
    }

    private sealed class Filter(Func<ResponseKind, string, bool> accept) : IResponseFilter
    {
        public bool IsValidResponse(ResponseKind kind, string value) => accept(kind, value);
    }

    private static byte[] B(string text) => Encoding.Latin1.GetBytes(text);

    private static ResponseRuleSet Engine(ScriptedRandom random, List<string>? warnings = null)
    {
        Action<string>? sink = warnings is null ? null : warnings.Add;
        return new ResponseRuleSet(random, sink);
    }

    private static bool Load(ResponseRuleSet rules, Files files, string script, string name = ScriptName)
    {
        files.Bytes[name] = B(script);
        return rules.Load(name, files);
    }

    /// <summary>One criterion on <paramref name="key"/> matched by <paramref name="matcher"/>, one response, one rule.</summary>
    private static ResponseRuleSet Matching(string key, string matcher, ScriptedRandom? random = null)
    {
        random ??= new ScriptedRandom();
        ResponseRuleSet rules = new(random);
        var files = new Files();
        Load(rules, files,
            "criterion C " + key + " " + matcher + "\n" +
            "response G\n{\n speak a\n}\n" +
            "rule R\n{\n criteria C\n response G\n}\n");
        return rules;
    }

    private static MatchedResponse One(ResponseRuleSet rules, string value = "x", string key = "Concept",
        IResponseFilter? filter = null)
    {
        var set = new ResponseCriteriaSet();
        set.Append(key, value);
        Assert.True(rules.FindBestResponse(set, out MatchedResponse response, filter),
            $"no rule matched {key}={value}");
        return response;
    }

    private static void Miss(ResponseRuleSet rules, string value = "x", string key = "Concept",
        IResponseFilter? filter = null)
    {
        var set = new ResponseCriteriaSet();
        set.Append(key, value);
        Assert.False(rules.FindBestResponse(set, out _, filter), $"{key}={value} matched, a miss was expected");
    }

    // -----------------------------------------------------------------
    // the loader
    // -----------------------------------------------------------------

    [Fact]
    public void AMissingScriptIsAWarningAndNoRules()
    {
        var warnings = new List<string>();
        var rules = Engine(new ScriptedRandom(), warnings);
        var files = new Files();

        Assert.False(rules.Load(ScriptName, files));
        Assert.Equal(0, rules.RuleCount);
        Assert.Null(rules.LoadedScript);
        Assert.Contains(warnings, w => w.Contains("failed to load", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLoadReceiptCountsRulesCriteriaAndResponses()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();

        Assert.True(Load(rules, files, """
            criterion Concept Concept medic
            response Group
            {
                speak one
                speak two
            }
            rule Rule
            {
                criteria Concept
                response Group
            }
            """));

        Assert.Equal((1, 1, 1), (rules.RuleCount, rules.CriterionCount, rules.ResponseCount));
        Assert.Equal(ScriptName, rules.LoadedScript);
    }

    [Fact]
    public void AnIncludeLoadsOnceAndAMissIsWarned()
    {
        var warnings = new List<string>();
        var rules = Engine(new ScriptedRandom(), warnings);
        var files = new Files();
        files.Bytes[ScriptName] = B("""
            #include talker/sub.txt
            #include talker/sub.txt
            rule Root
            {
                criteria SubConcept
                response SubGroup
            }
            """);
        files.Bytes["scripts/talker/sub.txt"] = B("""
            #include talker/test.txt
            #include talker/absent.txt
            criterion SubConcept Concept sub
            response SubGroup
            {
                speak sub_response
            }
            """);

        Assert.True(rules.Load(ScriptName, files));
        Assert.Equal((1, 1, 1), (rules.RuleCount, rules.CriterionCount, rules.ResponseCount));
        Assert.Contains(warnings, w => w.Contains("scripts/talker/absent.txt", StringComparison.Ordinal));

        // the root rule reached the sub's response: the include pool is
        // shared across the stack, and the second include was a pool hit
        Assert.Equal("sub_response", One(rules, "sub").Name);
    }

    [Fact]
    public void QuotedTokensKeepTheirSpacesAndCommentsAreSkipped()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();

        Assert.True(Load(rules, files, """
            // the concept, commented above as the shipped files are
            criterion Concept Concept "hello world"
            response Group
            {
                speak "two words"
            }
            rule Rule
            {
                criteria Concept
                response Group
            }
            """));

        Assert.Equal("two words", One(rules, "hello world").Name);
    }

    [Fact]
    public void AnUnknownEntryEndsTheParseWithAWarning()
    {
        var warnings = new List<string>();
        var rules = Engine(new ScriptedRandom(), warnings);
        var files = new Files();

        Assert.True(Load(rules, files, """
            whatever
            rule TooLate
            {
                criteria Nothing
            }
            """));

        Assert.Equal(0, rules.RuleCount);
        Assert.Contains(warnings, w => w.Contains("Unknown entry type 'whatever'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.StartsWith($"{ScriptName}(token 1)", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------
    // the criterion matcher
    // -----------------------------------------------------------------

    [Fact]
    public void ACriterionMatchesItsValueCaseInsensitively()
        => One(Matching("Concept", "medic"), "MeDIC");

    [Fact]
    public void ANumericCriterionComparesByValueNotSpelling()
        => One(Matching("health", "100"), "100.0", "health");

    [Fact]
    public void ANumberNeverMatchesAMissingKey()
        => Miss(Matching("health", "100"), "100", "other");

    [Fact]
    public void GreaterThanIsStrict()
    {
        var rules = Matching("count", ">2");

        One(rules, "3", "count");
        Miss(rules, "2", "count");
    }

    [Fact]
    public void GreaterThanOrEqualsTakesTheBound()
        => One(Matching("count", ">=2"), "2", "count");

    [Fact]
    public void ALessThanHoldsItsBoundToo()
    {
        var rules = Matching("count", "<10");

        One(rules, "9.5", "count");
        Miss(rules, "10", "count");
    }

    [Fact]
    public void ARangeHoldsBothOfItsEnds()
    {
        var rules = Matching("count", ">2,<10");

        One(rules, "5", "count");
        One(rules, "2.5", "count");
        Miss(rules, "2", "count");
        Miss(rules, "12", "count");
    }

    [Fact]
    public void ANumericNotEqualExcludesOnlyItsValue()
    {
        var rules = Matching("count", "!=3");

        One(rules, "4", "count");
        Miss(rules, "3.0", "count");
    }

    [Fact]
    public void AStringNotEqualIsCaseInsensitive()
    {
        var rules = Matching("Concept", "!=medic");

        One(rules, "soldier");
        Miss(rules, "Medic");
    }

    [Fact]
    public void AnEnumerationFeedsTheMatcherAsItsNumber()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            enumeration Mood
            {
                "Angry" "1"
                "Happy" "2"
            }
            criterion Mood Mood "[Mood::Happy]"
            response G
            {
                speak a
            }
            rule R
            {
                criteria Mood
                response G
            }
            """);

        One(rules, "2", "Mood");
        Miss(rules, "angry", "Mood");
    }

    // -----------------------------------------------------------------
    // the best-rule score
    // -----------------------------------------------------------------

    [Fact]
    public void TheRuleWithMoreMatchesWins()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion One Concept medic
            criterion Two Health 100
            response GA
            {
                speak a
            }
            response GB
            {
                speak b
            }
            rule Weak
            {
                criteria One
                response GA
            }
            rule Strong
            {
                criteria One Two
                response GB
            }
            """);

        var set = new ResponseCriteriaSet();
        set.Append("Concept", "medic");
        set.Append("Health", "100");
        Assert.True(rules.FindBestResponse(set, out var response));
        Assert.Equal("Strong", response.Rule);
        Assert.Equal("b", response.Name);
    }

    [Fact]
    public void CriterionWeightScalesTheScore()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Heavy Concept medic weight 3
            criterion Light Concept medic weight 2
            response GA
            {
                speak a
            }
            response GB
            {
                speak b
            }
            rule LightRule
            {
                criteria Light
                response GA
            }
            rule HeavyRule
            {
                criteria Heavy
                response GB
            }
            """);

        Assert.Equal("HeavyRule", One(rules, "medic").Rule);
    }

    [Fact]
    public void ARequiredMissExcludesTheWholeRule()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept medic
            criterion Mood Mood happy required
            response GA
            {
                speak a
            }
            response GB
            {
                speak b
            }
            rule Moody
            {
                criteria Concept Mood
                response GA
            }
            rule Plain
            {
                criteria Concept
                response GB
            }
            """);

        // Moody matches more, but its required mood is absent: Plain wins
        Assert.Equal("Plain", One(rules, "medic").Rule);
    }

    [Fact]
    public void AnOptionalMissIsOnlyAZeroScore()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Mood Mood happy
            response G
            {
                speak a
            }
            rule R
            {
                criteria Mood
                response G
            }
            """);

        Miss(rules, "sad", "Mood");
    }

    [Fact]
    public void TiedScoresAreDrawnFromTheBucket()
    {
        Assert.Equal("RuleA", TiedPick(0).Rule);
        Assert.Equal("RuleB", TiedPick(1).Rule);
    }

    private static MatchedResponse TiedPick(int draw)
    {
        var random = new ScriptedRandom();
        random.Ints(draw);
        var rules = Engine(random);
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept medic
            response GA
            {
                speak a
            }
            response GB
            {
                speak b
            }
            rule RuleA
            {
                criteria Concept
                response GA
            }
            rule RuleB
            {
                criteria Concept
                response GB
            }
            """);

        return One(rules, "medic");
    }

    [Fact]
    public void MatchOnceRetiresTheRuleForTheRestOfTheLevel()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept medic
            response G
            {
                speak a
            }
            rule R
            {
                matchonce
                criteria Concept
                response G
            }
            """);

        One(rules, "medic");

        var again = new ResponseCriteriaSet();
        again.Append("Concept", "medic");
        Assert.False(rules.FindBestResponse(again, out _));
    }

    // -----------------------------------------------------------------
    // response selection
    // -----------------------------------------------------------------

    [Fact]
    public void SequentialHandsOutInOrderAndExhaustesUnderNoRepeat()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                sequential
                norepeat
                speak a
                speak b
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        Assert.Equal("a", One(rules).Name);
        Assert.Equal("b", One(rules).Name);

        // spent: the search still succeeds, the response is just empty
        var third = One(rules);
        Assert.Equal(ResponseKind.None, third.Kind);
        Assert.Equal("R", third.Rule);

        // and a disabled group keeps answering that shape
        Assert.Equal(ResponseKind.None, One(rules).Kind);
    }

    [Fact]
    public void TheWeightedDrawPicksAmongTheWeights()
    {
        Assert.Equal("b", WeightedPick(0.9f, 2.5f));
        Assert.Equal("a", WeightedPick(0.9f, 3.5f));
    }

    private static string WeightedPick(params float[] draws)
    {
        var random = new ScriptedRandom();
        random.Floats(draws);
        var rules = Engine(random);
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                speak a weight 1
                speak b weight 3
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        return One(rules).Name;
    }

    [Fact]
    public void DisplayFirstGoesFirstWhileItIsUndepleted()
    {
        var random = new ScriptedRandom();
        random.Floats(0.5f);
        var rules = Engine(random);
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                speak a displayfirst
                speak b
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        Assert.Equal("a", One(rules).Name);
        Assert.Equal("b", One(rules).Name);
    }

    [Fact]
    public void DisplayLastWaitsUntilItIsTheOnlyOneLeft()
    {
        var random = new ScriptedRandom();
        random.Floats(0.5f);
        var rules = Engine(random);
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                speak a
                speak z displaylast
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        Assert.Equal("a", One(rules).Name);
        Assert.Equal("z", One(rules).Name);
    }

    [Fact]
    public void TheFilterDepletesForItsOwnChoiceAndIsReverted()
    {
        var random = new ScriptedRandom();
        random.Floats(0.5f, 0.5f, 0.5f);
        var rules = Engine(random);
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                speak a
                speak b
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        var onlyA = new Filter((_, value) => value == "a");
        var onlyB = new Filter((_, value) => value == "b");

        Assert.Equal("a", One(rules, filter: onlyA).Name);
        Assert.Equal("b", One(rules, filter: onlyB).Name);

        // both used, the rejections reverted: the plain search starts a fresh
        // depletion round rather than finding the pool empty (draws 0.5, 0.5
        // land on the later slot each time)
        Assert.Equal("b", One(rules).Name);
    }

    [Fact]
    public void AFullyRejectedGroupRetiresUnderNoRepeat()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                norepeat
                speak a
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        var none = new Filter((_, _) => false);

        var first = One(rules, filter: none);
        Assert.Equal(ResponseKind.None, first.Kind);
        Assert.Equal("R", first.Rule);

        // the group is disabled, not empty: the same shape again
        Assert.Equal(ResponseKind.None, One(rules, filter: none).Kind);
    }

    [Fact]
    public void AReferenceResolvesThroughTheNamedGroup()
    {
        var random = new ScriptedRandom();
        random.Floats(0.5f);
        var rules = Engine(random);
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response Other
            {
                speak b
            }
            response G
            {
                response Other
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        var response = One(rules);
        Assert.Equal(ResponseKind.Speak, response.Kind);
        Assert.Equal("b", response.Name);
    }

    [Fact]
    public void AReferenceToAMissingGroupAnswersNoneButValid()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                response Ghost
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        var response = One(rules);
        Assert.Equal(ResponseKind.None, response.Kind);
        Assert.Equal("R", response.Rule);
    }

    [Fact]
    public void NoMatchAtAllIsFalseWithAnEmptyResponse()
    {
        var rules = Engine(new ScriptedRandom());

        Assert.False(rules.FindBestResponse(new ResponseCriteriaSet(), out var response));
        Assert.Equal(ResponseKind.None, response.Kind);
        Assert.Equal(string.Empty, response.Name);
        Assert.Equal(string.Empty, response.Rule);
    }

    [Fact]
    public void TheWinnerIsNamedInTheResponseAt63Characters()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        string rule = new('r', 80);
        string value = new('v', 100);
        Load(rules, files,
            "criterion Concept Concept x\n" +
            "response G\n{\n speak " + value + "\n}\n" +
            "rule " + rule + "\n{\n criteria Concept\n response G\n}\n");

        var response = One(rules);
        Assert.Equal(new string('r', 63), response.Rule);
        Assert.Equal(new string('v', 63), response.Name);
    }

    [Fact]
    public void TheGroupParamsRideTheResponse()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G defaultdelay odds 40 respeakdelay 3,4
            {
                speak a
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        ResponseParams p = One(rules).Params;
        Assert.True(p.Has(ResponseParamFlags.DelayAfterSpeak));
        Assert.True(p.Has(ResponseParamFlags.Odds));
        Assert.True(p.Has(ResponseParamFlags.RespeakDelay));
        Assert.Equal((short)40, p.Odds);
        Assert.InRange(p.Delay.Start, 2.8f, 2.81f);
        Assert.InRange(p.Delay.Range, 0.39f, 0.41f);
        Assert.Equal(3f, p.RespeakDelay.Start);
        Assert.Equal(1f, p.RespeakDelay.Range);
    }

    [Fact]
    public void ApplyContextJoinsAndTheWorldFlagRideToo()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                speak a
            }
            rule R
            {
                applyContext ctxone
                applyContext ctxtwo
                applyContextToWorld
                criteria Concept
                response G
            }
            """);

        var response = One(rules);
        Assert.Equal("ctxone,ctxtwo", response.Context);
        Assert.True(response.ApplyContextToWorld);
    }

    [Fact]
    public void TheCriteriaRideBackUnchanged()
    {
        var rules = Engine(new ScriptedRandom());
        var files = new Files();
        Load(rules, files, """
            criterion Concept Concept x
            response G
            {
                speak a
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);

        var set = new ResponseCriteriaSet();
        set.Append("Concept", "x");
        rules.FindBestResponse(set, out var response);
        Assert.Same(set, response.Criteria);
    }

    // -----------------------------------------------------------------
    // the criteria set (AI_CriteriaSet)
    // -----------------------------------------------------------------

    [Fact]
    public void AnAppendReplacesTheSameNameCaseInsensitively()
    {
        var set = new ResponseCriteriaSet();
        set.Append("key", "one");
        set.Append("KEY", "two", 3);

        Assert.Equal(1, set.Count);
        Assert.Equal(0, set.Find("key"));
        Assert.Equal("two", set.GetValue(0));
        Assert.Equal(3f, set.GetWeight(0));
    }

    [Fact]
    public void ASetValueIsTruncatedTo63Characters()
    {
        var set = new ResponseCriteriaSet();
        set.Append("key", new string('v', 100));

        Assert.Equal(63, set.GetValue(set.Find("key")).Length);
    }

    [Fact]
    public void MissingLookupsAnswerTheirDefaults()
    {
        var set = new ResponseCriteriaSet();
        set.Append("key", "value");

        Assert.Equal(-1, set.Find("absent"));
        Assert.Equal(string.Empty, set.GetName(9));
        Assert.Equal(string.Empty, set.GetValue(9));
        Assert.Equal(1f, set.GetWeight(9));
    }

    // -----------------------------------------------------------------
    // the service: the container, the load-once level init
    // -----------------------------------------------------------------

    [Fact]
    public void TheLazyRegistrationServesBothTheContractAndTheLifecycle()
    {
        var services = new ServiceContainer();
        services.AddInstance<ILog>(new RecordingLog());
        services.AddInstance<IEngineFiles>(new Files());
        services.AddInstance<IUniformRandom>(new ScriptedRandom());

        // the one line the module carries: the contract registration is also
        // the lifecycle registration, ResolveAll walking concrete types
        services.Add<IResponseRules, ResponseRules>();

        var participant = Assert.Single(services.ResolveAll<IGameLifecycle>());
        Assert.IsType<ResponseRules>(participant);
        Assert.Same(participant, services.Require<IResponseRules>());
    }

    [Fact]
    public void TheLevelInitLoadsOnceAndResetsTheGroupsEveryTime()
    {
        var log = new RecordingLog();
        var services = new ServiceContainer();
        services.AddInstance<ILog>(log);
        services.AddInstance<IEngineFiles>(LevelScript());
        services.AddInstance<IUniformRandom>(new ScriptedRandom());
        services.Add<IResponseRules, ResponseRules>();

        IResponseRules rules = services.Require<IResponseRules>();
        IGameLifecycle lifecycle = Assert.Single(services.ResolveAll<IGameLifecycle>());

        lifecycle.OnLevelInitPreEntity("dm_a");
        Assert.Equal("a", Say(rules).Name);
        Assert.Equal("b", Say(rules).Name);
        Assert.Equal(ResponseKind.None, Say(rules).Kind);

        // a second level: no second load, the groups spent no longer
        lifecycle.OnLevelInitPreEntity("dm_b");
        Assert.Equal("a", Say(rules).Name);

        Assert.Equal(1, LoadReceipts(log));
        Assert.Contains("(1 rules, 1 criteria, and 1 responses)", log.Text, StringComparison.Ordinal);
    }

    private static MatchedResponse Say(IResponseRules rules)
    {
        var set = new ResponseCriteriaSet();
        set.Append("Concept", "x");
        Assert.True(rules.FindBestResponse(set, out MatchedResponse response), "the level script did not match");
        return response;
    }

    private static int LoadReceipts(RecordingLog log)
    {
        int receipts = 0;
        foreach (var (text, _) in log.Lines)
        {
            if (text.Contains("rules,", StringComparison.Ordinal))
                receipts++;
        }

        return receipts;
    }

    private static Files LevelScript()
    {
        var files = new Files();
        files.Bytes[ResponseRules.DefaultScript] = B("""
            criterion Concept Concept x
            response G
            {
                sequential
                norepeat
                speak a
                speak b
            }
            rule R
            {
                criteria Concept
                response G
            }
            """);
        return files;
    }

    // -----------------------------------------------------------------
    // the shipped rules
    // -----------------------------------------------------------------

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void TheShippedResponseRulesMatchAMedicConceptWithAScene()
    {
        string tf = Path.Combine(RepoLayout.Repo("bin", "debug", "content"), "tf");
        Assert.True(Directory.Exists(tf),
            $"no {tf}: make setup links bin/<config>/content to a Team Fortress 2 install (TF2)");

        TfvPks files = new(tf);
        var warnings = new List<string>();
        var rules = new ResponseRuleSet(new ScriptedRandom(), warnings.Add);

        Assert.True(rules.Load(ResponseRules.DefaultScript, files),
            "the shipped response_rules.txt did not load; warnings:\n" + string.Join('\n', warnings));

        Assert.True(rules.RuleCount > 16,
            $"the shipped script parsed {rules.RuleCount} rules, {rules.CriterionCount} criteria and "
            + $"{rules.ResponseCount} responses — the parse saw too little");

        // the shipped per-class medic rule (Rule PlayerMedicMedic) is
        // `criteria ConceptPlayerMedic IsMedic`, both required
        var criteria = new ResponseCriteriaSet();
        criteria.Append("Concept", "TLK_PLAYER_MEDIC");
        criteria.Append("playerclass", "Medic");
        Assert.True(rules.FindBestResponse(criteria, out var response),
            "no rule matched the TLK_PLAYER_MEDIC medic-class concept");
        Assert.True(
            response.Kind == ResponseKind.Scene
            && response.Name.Contains("medic", StringComparison.OrdinalIgnoreCase),
            $"the medic concept matched rule '{response.Rule}' with a {response.Kind} '{response.Name}'");
    }

    /// <summary>
    /// The game-file seam over the tree's mounted Team Fortress 2 content
    /// (bin/&lt;config&gt;/content → the install), extracting VPK entries on
    /// demand: every <c>*_dir.vpk</c> is indexed once, bodies are read from
    /// the numbered archives or the directory's own preload bytes.
    /// </summary>
    private sealed class TfvPks : IEngineFiles
    {
        private sealed class Entry(string root, ushort archive, uint offset, uint length, byte[]? preload)
        {
            public string Root = root;
            public ushort Archive = archive;
            public uint Offset = offset;
            public uint Length = length;
            public byte[]? Preload = preload;
        }

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _bodies = new(StringComparer.OrdinalIgnoreCase);

        public TfvPks(string tfDir)
        {
            foreach (string dirVpk in Directory.GetFiles(tfDir, "*_dir.vpk"))
            {
                Index(dirVpk);
            }
        }

        private void Index(string dirVpk)
        {
            using BinaryReader r = new(File.OpenRead(dirVpk));
            r.ReadUInt32();                        // signature
            uint version = r.ReadUInt32();
            r.ReadUInt32();                        // tree size
            if (version == 2)
                r.ReadBytes(16);

            string Z()
            {
                var bytes = new List<byte>();
                byte c;
                while ((c = r.ReadByte()) != 0)
                    bytes.Add(c);
                return Encoding.Latin1.GetString(bytes.ToArray());
            }

            string root = dirVpk.EndsWith("_dir.vpk", StringComparison.Ordinal) ? dirVpk[..^8] : dirVpk;

            for (string ext; (ext = Z()).Length > 0;)
            for (string dir; (dir = Z()).Length > 0;)
            for (string name; (name = Z()).Length > 0;)
            {
                r.ReadUInt32();                    // crc
                ushort preload = r.ReadUInt16();
                ushort archive = r.ReadUInt16();
                uint offset = r.ReadUInt32();
                uint length = r.ReadUInt32();
                r.ReadUInt16();                    // terminator
                byte[]? inlineBytes = archive == 0xFFFF ? r.ReadBytes(preload) : null;
                if (archive != 0xFFFF && preload > 0)
                    r.ReadBytes(preload);

                _entries[$"{dir}/{name}.{ext}"] = new Entry(root, archive, offset, length, inlineBytes);
            }
        }

        public byte[]? ReadGameFileBytes(string path)
        {
            if (_bodies.TryGetValue(path, out byte[]? cached))
                return cached;

            if (!_entries.TryGetValue(path, out Entry? entry))
                return null;

            byte[] body;
            if (entry.Archive == 0xFFFF)
            {
                body = entry.Preload!;
            }
            else
            {
                using BinaryReader archive = new(File.OpenRead($"{entry.Root}_{entry.Archive:D3}.vpk"));
                archive.BaseStream.Seek(entry.Offset, SeekOrigin.Begin);
                body = archive.ReadBytes((int)entry.Length);
            }

            _bodies[path] = body;
            return body;
        }

        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path)
            => ReadGameFileBytes(path) is byte[] b ? Encoding.Latin1.GetString(b) : null;

        public byte[]? ReadFileOnPath(string path, string pathId)
            => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;

        public IEnumerable<string> Entries => _entries.Keys;
    }
}
