using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SourceSharp.Ai.Hl2mp.Knowledge;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// The help file says only what is true of this mod (§3.6): each claim against the table it
/// comes from, the binding table above all. A wrong answer here would be wrong with confidence.
/// </summary>
public sealed partial class ChatHelpTests
{
    private static readonly Lazy<ChatHelp> Help = new(ChatAnswerRig.Help);
    private static readonly Lazy<ChatOutputFilter> Filter = new(() => new ChatOutputFilter(ChatRig.Words()));

    private static string Answer(string id) => Help.Value.HowTo.Single(h => h.Id == id).Answer;

    [GeneratedRegex("""\[KeyAction\("(?<action>[^"]+)"[^\]]*Keys = \[(?<keys>[^\]]*)\]""")]
    private static partial Regex KeyAction();

    /// <summary>The default binding table, read from <c>StockKeyActions.cs</c>: action to keys, lower case.</summary>
    private static Dictionary<string, string[]> Bindings()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Match m in KeyAction().Matches(File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "Input", "StockKeyActions.cs"))))
            map[m.Groups["action"].Value] = [.. m.Groups["keys"].Value.Split(',').Select(k => k.Trim().Trim('"').ToLowerInvariant())];
        return map;
    }

    /// <summary>Which key an answer names for which action: the table's keys, and the word in the line.</summary>
    [Theory]
    [InlineData("ladder", "+use", "e")]
    [InlineData("chargers", "+use", "e")]
    [InlineData("vote", "callvote", "o")]
    [InlineData("admin", "adminmenu", "f4")]
    [InlineData("addbots", "adminmenu", "f4")]
    [InlineData("crouchjump", "+jump", "space")]
    [InlineData("crouchjump", "+duck", "ctrl")]
    [InlineData("sprint", "+speed", "shift")]
    [InlineData("flashlight", "impulse 100", "f")]
    [InlineData("reload", "+reload", "r")]
    [InlineData("scoreboard", "+showscores", "tab")]
    [InlineData("chat", "messagemode", "y")]
    [InlineData("chat", "messagemode2", "u")]
    [InlineData("switch", "lastinv", "q")]
    [InlineData("switch", "dropprimary", "g")]
    [InlineData("controls", "+forward", "w")]
    [InlineData("controls", "+moveleft", "a")]
    [InlineData("controls", "+back", "s")]
    [InlineData("controls", "+moveright", "d")]
    [InlineData("controls", "+reload", "r")]
    [InlineData("controls", "lastinv", "q")]
    public void AKeyInAnAnswerIsTheTablesKey(string id, string action, string key)
    {
        Assert.Contains(key, Bindings()[action]);
        string[] words = ChatText.Words(Answer(id).ToLowerInvariant());
        Assert.True(words.Contains(key, StringComparer.Ordinal) || (key.Length == 1 && words.Contains("wasd", StringComparer.Ordinal)), $"{id}: '{Answer(id)}' names no {key}");
    }

    /// <summary>The mouse's sides are the table's.</summary>
    [Fact]
    public void RightMouseIsAltFire()
    {
        Assert.Contains("mouse2", Bindings()["+attack2"]);
        Assert.Contains("mouse1", Bindings()["+attack"]);
        Assert.Contains("right", Answer("altfire"), StringComparison.Ordinal);
    }

    /// <summary>The loadout the file claims is the game's own, in full.</summary>
    [Fact]
    public void TheLoadoutIsTheGames()
        => Assert.Equal(GameRules.Loadout.Order(StringComparer.Ordinal), Help.Value.Loadout.Order(StringComparer.Ordinal));

    /// <summary>The numbers: what a kit and a battery give, the magnum's clip, the crossbow's, the respawns, the difficulties' points.</summary>
    [Fact]
    public void TheNumbersAreTheGames()
    {
        Assert.Contains("HealthGiven: 25", File.ReadAllText(RepoLayout.Repo("src", "server", "managed", "Pickups.cs")), StringComparison.Ordinal);
        Assert.Contains("TopUp(p.Armour, 15,", File.ReadAllText(RepoLayout.Repo("src", "server", "managed", "Entities", "Items", "ItemBattery.cs")), StringComparison.Ordinal);
        Assert.Contains("25 health", Answer("healthkit"), StringComparison.Ordinal);
        Assert.Contains("15 armour", Answer("healthkit"), StringComparison.Ordinal);

        WeaponCatalog catalog = WeaponCatalog.Stock();
        Assert.Equal(6, catalog.ByIndex(catalog.IndexOf("weapon_357"))!.Value.ClipSize);
        Assert.Contains("6 shots", Answer("magnum"), StringComparison.Ordinal);
        Assert.Equal(1, catalog.ByIndex(catalog.IndexOf("weapon_crossbow"))!.Value.ClipSize);
        Assert.Contains("one bolt", Answer("crossbow"), StringComparison.Ordinal);

        Assert.Equal("20", ConVarDefault(typeof(WeaponRespawnTimeConVar)));
        Assert.Equal("30", ConVarDefault(typeof(ItemRespawnTimeConVar)));
        Assert.Contains("about 20 s", Answer("weaponrespawn"), StringComparison.Ordinal);
        Assert.Contains("about 30 s", Answer("weaponrespawn"), StringComparison.Ordinal);

        int[] points = [.. new[] { "easy", "normal", "hard", "expert" }.Select(p => SkillPresets.TryGet(p, out SkillProfile? s) ? s!.Spent : -1)];
        Assert.Equal([20, 35, 50, 60], points);
        Assert.Contains("20, 35, 50 and 60", Answer("difficulty"), StringComparison.Ordinal);
    }

    private static string ConVarDefault(Type t) => ((SourceSharp.Abi.ConVarAttribute)Attribute.GetCustomAttribute(t, typeof(SourceSharp.Abi.ConVarAttribute))!).DefaultValue;

    /// <summary>The menus exist: the admin menu has a map, a mode, a kick and bots; the vote menu a kick.</summary>
    [Fact]
    public void TheMenusHoldWhatTheFileSays()
    {
        string tokens = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(RepoLayout.Repo("src", "client", "ui", "resource", "sourcesharp_ui_english.txt")));
        foreach (string token in (string[])["SS_UI_Admin_Map", "SS_UI_Admin_Mode", "SS_UI_Admin_Kick", "SS_UI_Admin_BotAdd", "SS_UI_Vote_KickPlayer"])
            Assert.Contains(token, tokens, StringComparison.Ordinal);
    }

    /// <summary>The thing the file says of an item is on an item the game has: weapon classes in the catalog or the ranks, others a real pickup class.</summary>
    [Fact]
    public void EveryItemIsARealClass()
    {
        WeaponCatalog catalog = WeaponCatalog.Stock();
        foreach (string c in Help.Value.Items.SelectMany(i => i.Classes))
        {
            if (c.StartsWith("weapon_", StringComparison.Ordinal))
                Assert.True(catalog.Contains(c), $"{c} is no weapon the game has");
            else
                Assert.NotEqual(ItemKind.Other, MapKnowledgeBuilder.KindOf(c));
        }
    }

    /// <summary>Every line a player could be sent passes the filter whole, under the line's cap: a line refused would answer nothing.</summary>
    [Fact]
    public void EveryPlainAnswerPassesTheFilter()
    {
        foreach (string line in Help.Value.HowTo.Select(h => h.Answer).Concat(Help.Value.Shrugs).Concat(Help.Value.PeopleShrugs))
        {
            ChatVerdict v = Filter.Value.Check(line, "", "Pia");
            Assert.True((v.Line, v.Refusal) == (line, ChatRefusal.None), $"'{line}': {v.Refusal}");
            Assert.True(line.Length <= ChatOutputFilter.MaxLine, $"'{line}' is {line.Length} long");
        }
    }

    /// <summary>What an answer must keep is in the answer: a must that is not there could never pass.</summary>
    [Fact]
    public void EveryMustIsInItsAnswer()
    {
        foreach (ChatHowTo h in Help.Value.HowTo)
        {
            Assert.All(h.Must, m => Assert.Contains(m, ChatText.Words(h.Answer.ToLowerInvariant())));
            Assert.True(new ChatAnswerCheck(h.Answer, h.Must, Help.Value.Filler, Help.Value.Negations, Help.Value.Agreements).Holds(h.Answer), $"{h.Id}: its own answer does not hold");
        }
    }

    /// <summary>Filler has no word that carries a claim: a negation, a number, an item or a place in it would let a model add one.</summary>
    [Fact]
    public void FillerCarriesNoClaim()
    {
        string[] place = ["north", "south", "east", "west", "upstairs", "downstairs", "ladder", "spawn", "middle", "floor", "roof", "basement"];
        string[] items = [.. Help.Value.Items.SelectMany(i => i.Words).Where(w => !w.Contains(' ', StringComparison.Ordinal))];
        Assert.Empty(Help.Value.Filler.Intersect(Help.Value.Negations));
        Assert.Empty(Help.Value.Filler.Intersect(place));
        Assert.DoesNotContain(Help.Value.Filler, w => w.Any(char.IsDigit));
        Assert.Empty(Help.Value.Filler.Intersect(items));
    }

    /// <summary>On the real map: every "where" line passes the filter, an exact marker sits on a real item's place, and a good weapon's area on none.</summary>
    [PerTreeNavFact("dm_lockdown")]
    public void DmLockdownsAnswersAreTrueAndPassTheFilter()
    {
        NavLoadResult loaded = NavLoader.Load("dm_lockdown", p => File.ReadAllBytes(p.EndsWith(".nav3d", StringComparison.Ordinal) ? PerTreeNav.NavPath("dm_lockdown") : PerTreeNav.BspPath("dm_lockdown")), null);
        LoadedNav nav = loaded.Nav!;
        WeaponCatalog catalog = WeaponCatalog.Stock();
        var weapons = new WeaponKnowledge(Enumerable.Range(0, catalog.Count).Select(i => catalog.ByIndex(i)!.Value).Select((w, i) => new WeaponFacts(i, w.Name, w.ClipSize, w.Interval, 0f, false, false, 0f)));
        MapKnowledge map = MapKnowledgeBuilder.Build("dm_lockdown", nav.Entities, catalog, weapons, 20f, 30f);
        var book = new ChatAnswerBook(Help.Value, map, new ChatPlaces(map, nav.Nav.Graph), ChatAnswerRig.Good, 7);
        int exact = 0, areas = 0;
        foreach (ChatItem item in Help.Value.Items)
        {
            foreach (ItemSpawn spawn in map.Items.Where(i => item.Classes.Contains(i.ClassName, StringComparer.Ordinal)))
            {
                ChatAnswer a = book.Answer(new ChatTopic(ChatTopicKind.Where, item), new ChatAsked("", default, null, null, spawn.Position, spawn.Index));
                Assert.True(Filter.Value.Check(a.Line, "", "Pia").Line == a.Line, $"'{a.Line}' does not pass");
                ChatMark mark = a.Mark!.Value;
                if (mark.Kind == ChatMarkKind.Exact)
                {
                    Assert.Equal(spawn.Position, mark.At);
                    exact++;
                }
                else
                {
                    Assert.True(ChatPlaces.Distance(mark.At, spawn.Position) < mark.Radius);
                    Assert.NotEqual(spawn.Position, mark.At);
                    Assert.DoesNotMatch(@"\d", a.Line);
                    areas++;
                }
            }
        }

        Assert.True(exact > 40 && areas == 10, $"exact {exact}, areas {areas}");
        ChatAnswer ladder = book.Answer(new ChatTopic(ChatTopicKind.WhereLadder), new ChatAsked("", default, null, null, new AiVector(-2865f, 3418f, 3f), 0));
        Assert.True(Filter.Value.Check(ladder.Line, "", "Pia").Line == ladder.Line, $"'{ladder.Line}' does not pass: {Filter.Value.Check(ladder.Line, "", "Pia").Refusal}");
        Assert.Contains("ladder", ladder.Line, StringComparison.Ordinal);
        Assert.True(ChatPlaces.Distance(ladder.Mark!.Value.At, new AiVector(-2865f, 3418f, 3f)) < 300f, "the ladder bots climb at (-2865 3418) is the nearest to it");
        Assert.Equal(3, new ChatPlaces(map, nav.Nav.Graph).FloorCount);
    }
}
