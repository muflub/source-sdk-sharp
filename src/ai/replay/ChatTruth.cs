using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SourceSharp.Ai.Hl2mp.Chat;
using SourceSharp.Ai.Hl2mp.Knowledge;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Replay;

/// <summary>
/// <c>tools/ai-replay chat-truth</c> (plan_hl2mp_ai §3.6, §12.3): a hundred questions a player
/// might ask, half about the map and half about how to play, put to a model as bot chat puts them,
/// and each result graded against the map's lump and the question's own truth. Reports, for each
/// half, what the model's raw line would have said wrong, what the check refused, and what
/// actually went out wrong. The grader is deliberately not the check: it reads the final line.
/// </summary>
public static partial class ChatTruth
{
    private sealed record Question(string Group, string Text, Regex? Must, Regex? MustNot);

    [GeneratedRegex(@"\d")]
    private static partial Regex AnyDigit();

    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static List<Question> Questions()
    {
        var q = new List<Question>();
        void Map(string text, string? must = null, string? mustNot = null) => q.Add(new Question("map", text, must is null ? null : R(must), mustNot is null ? null : R(mustNot)));
        void How(string text, string? must = null, string? mustNot = null) => q.Add(new Question("howto", text, must is null ? null : R(must), mustNot is null ? null : R(mustNot)));
        const string NoSpot = @"\d|\bof you\b|\bby\b";

        // What is on it: yes, no, how many (dm_lockdown's lump, checked by DmLockdownsItemSpawnsAreExactlyTheLumps).
        Map("is there an rpg?", @"\brpg\b", @"\b(no|not|nah|none)\b|\d");
        Map("is there a rocket launcher?", @"\brpg\b", @"\b(no|not|nah|none)\b|\d");
        Map("is there a crossbow on this map?", @"\bcrossbow\b", @"\b(no|not|nah|none)\b|\d");
        Map("any ar2 on the map?", @"\bpulse rifle\b", @"\b(no|not|nah|none)\b|\d");
        Map("is there a magnum?", @"\b3\b.*\bmagnum", @"\b(no|not|nah|none)\b");
        Map("got any shotguns?", @"\b4\b.*\bshotgun", @"\b(no|not|nah|none)\b");
        Map("are there grenades?", @"\b8\b.*\bgrenade", @"\b(no|not|nah|none)\b");
        Map("is there health?", @"\b22\b.*\bhealth", @"\b(no|not|nah|none)\b");
        Map("is there any armour?", @"\b5\b.*\bbatter", @"\b(no|not|nah|none)\b");
        Map("is there a health charger?", @"\bhealth charger\b", @"\b(no|not|nah|none)\b");
        Map("is there a suit charger?", @"\b2\b.*\bsuit charger", @"\b(no|not|nah|none)\b");
        Map("is there a slam?", @"\b(no|not|nah|nope)\b", @"\byeah\b");
        Map("any tripmines?", @"\b(no|not|nah|nope)\b", @"\byeah\b");
        Map("is there a stun stick?", @"\b(no|not|nah|nope)\b", @"\byeah\b");
        Map("is there a bfg?", @"\b(no|not|nah|nope)\b", @"\byeah\b");
        Map("is there a railgun on this map?", @"\b(no|not|nah|nope)\b", @"\byeah\b");
        Map("is there a crowbar?", @"\bspawn\b", @"\byeah\b");
        Map("is there a pistol?", @"\bspawn\b", @"\byeah\b");
        Map("is there a smg?", @"\bspawn\b", @"\byeah\b");
        Map("is there a gravity gun?", @"\bspawn\b", @"\byeah\b");

        // How many.
        Map("how many kits are there?", @"\b22\b");
        Map("how many health kits?", @"\b22\b");
        Map("how many batteries?", @"\b5\b");
        Map("how many shotguns are there", @"\b4\b");
        Map("how many magnums", @"\b3\b");
        Map("how many grenades?", @"\b8\b");
        Map("how many rpgs?", @"\bone\b|\b1\b");
        Map("how many crossbows?", @"\bone\b|\b1\b");
        Map("how many spawns?", @"\b9\b");
        Map("how many spawn points are there?", @"\b9\b");

        // Where, exact for what is not a good weapon, vague for what is.
        Map("where's the health?", @"health");
        Map("wheres the armour?", @"batter");
        Map("where can i find a battery?", @"batter");
        Map("where's the health charger?", @"health charger");
        Map("where's the suit charger?", @"suit charger");
        Map("where are the grenades?", @"grenade");
        Map("where can i get ammo for the rpg?", @"rpg round");
        Map("where's the buckshot?", @"shells");
        Map("where is the rpg?", @"\brpg\b", NoSpot);
        Map("where's the crossbow?", @"\bcrossbow\b", NoSpot);
        Map("where is the ar2?", @"\bpulse rifle\b", NoSpot);
        Map("where can i find a shotgun?", @"\bshotgun", NoSpot);
        Map("where's the magnum?", @"\bmagnum", NoSpot);
        Map("where do i find the 357?", @"\bmagnum", NoSpot);

        // The map itself.
        Map("whats the map?", @"dm_lockdown");
        Map("what map is this?", @"dm_lockdown");
        Map("what weapons are on the map?", @"\brpg\b.*\bmagnum|\bmagnum.*\brpg\b");
        Map("how long does the rpg take to respawn?", @"\b20\b");
        Map("how long do health kits take to respawn?", @"\b30\b");
        Map("does the health kit respawn?", @"\b30\b");

        // How to play: each must hold the true key or number, from StockKeyActions and the tables.
        How("how do i climb a ladder?", @"\be\b");
        How("how do ladders work?", @"\be\b");
        How("how do i use the charger?", @"\be\b");
        How("how do i heal?", @"\be\b|kits");
        How("how do i vote?", @"\bo\b");
        How("how do i kick someone?", @"\bo\b|\bf4\b");
        How("how do i open the admin menu?", @"\bf4\b");
        How("how do i change the map?", @"\bf4\b");
        How("how do i add bots?", @"\bf4\b|create server");
        How("how do i add more bots?", @"\bf4\b|create server");
        How("how do i remove a bot?", @"\bf4\b|create server");
        How("whats the alt fire?", @"\bright\b");
        How("what does right click do?", @"\bright\b");
        How("how do i use the smg grenade launcher?", @"\bright\b");
        How("how do i use the ar2?", @"\bright\b");
        How("how do i use the shotgun?", @"\bright\b");
        How("how do i use the crossbow?", @"\bright\b");
        How("how do i zoom?", @"\bright\b");
        How("how do i use the rpg?", @"\blaser\b");
        How("how do i use the magnum?", @"\b6\b");
        How("how do i throw a grenade?", @"\bleft\b");
        How("how do i use the gravity gun?", @"\bright\b");
        How("how do i pick up props?", @"\bright\b");
        How("how do i crouch jump?", @"\bctrl\b");
        How("how do i jump higher?", @"\bctrl\b");
        How("what difficulties are there?", @"\b20\b");
        How("what does hard mode mean?", @"\b50\b");
        How("how good are the bots?", @"\b20\b");
        How("how does recoil work?", @"\bkick\b");
        How("why does my gun kick?", @"\bkick\b");
        How("how do i stop missing?", @"\bkick\b");
        How("what is spawn frag?", @"\bspawn\b");
        How("how do i sprint?", @"\bshift\b");
        How("how do i run faster?", @"\bshift\b");
        How("how do i use the flashlight?", @"\bf\b");
        How("how do i reload?", @"\br\b");
        How("how do i see the scoreboard?", @"\btab\b");
        How("how do i chat?", @"\by\b");
        How("how do i team chat?", @"\bu\b");
        How("how do i switch weapons?", @"\bq\b");
        How("how do i drop my gun?", @"\bg\b");
        How("what are the controls?", @"\bwasd\b");
        How("how do i play?", @"\bwasd\b");
        How("how much health does a kit give?", @"\b25\b");
        How("how much armour does a battery give?", @"\b15\b");
        How("how long until items respawn?", @"\b30\b");
        How("whats the goal?", @"\bkills\b");
        How("how do i win?", @"\bkills\b");
        How("where is north?", @"\bnorth\b");
        How("what does the minimap show?", @"\bnorth\b");
        return q;
    }

    public static int Run(string[] args)
    {
        // chat-truth <url> <nav3d> <bsp> <help.json> <chat.json> <out.md> <model>
        if (args.Length < 7)
        {
            Console.Error.WriteLine("chat-truth <url> <nav3d> <bsp> <help.json> <chat.json> <out.md> <model>");
            return 2;
        }

        string nav3d = args[1], bsp = args[2], model = args[6];
        string mapName = Path.GetFileNameWithoutExtension(nav3d);
        NavLoadResult loaded = NavLoader.Load(mapName, p => p.EndsWith(".nav3d", StringComparison.Ordinal) ? File.ReadAllBytes(nav3d) : File.ReadAllBytes(bsp), null);
        if (loaded.Nav is not { } nav)
            throw new InvalidDataException(loaded.Refusal);
        WeaponCatalog catalog = WeaponCatalog.Stock();
        var weapons = new WeaponKnowledge(Enumerable.Range(0, catalog.Count).Select(i => catalog.ByIndex(i)!.Value).Select((w, i) => new WeaponFacts(i, w.Name, w.ClipSize, w.Interval, 0f, false, false, 0f)));
        MapKnowledge map = MapKnowledgeBuilder.Build(mapName, nav.Entities, catalog, weapons, 20f, 30f);
        ChatHelp help = ChatHelp.Parse(File.ReadAllText(args[3]));
        ChatWords words = ChatWords.Parse(File.ReadAllText(args[4]));
        IReadOnlySet<string> good = SessionMapFacts.GoodOf(weapons.All.Select(w => w.Name)).ToHashSet(StringComparer.Ordinal);
        var places = new ChatPlaces(map, nav.Nav.Graph);
        var book = new ChatAnswerBook(help, map, places, good, 0xC4A7);
        var prompts = new ChatPromptBuilder(words);
        var filter = new ChatOutputFilter(words);
        var people = new HashSet<string>(StringComparer.Ordinal) { "lodle", "ravi", "pia", "kofi", "ines" };
        var persona = new ChatPersona("Pia Bullseye", "Pia", "cocky about your aim");
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        var target = new ChatTarget(args[0], model);
        var report = new StringBuilder();
        var rows = new List<string>();
        var totals = new Dictionary<string, int[]>(StringComparer.Ordinal);
        int draw = 0;
        foreach (Question q in Questions())
        {
            int[] t = totals.TryGetValue(q.Group, out int[]? have) ? have : totals[q.Group] = new int[7];
            t[0]++;
            ChatTopic? topic = book.Read(q.Text, false, people);
            if (topic is null)
            {
                t[6]++;
                rows.Add($"| {q.Group} | {q.Text} | no topic | | | NOT READ |");
                continue;
            }

            ChatAnswer a = book.Answer(topic, new ChatAsked(q.Text, new ChatScore(3, 2, 2, 4, "Kofi", 5), null, null, null, draw++));
            string raw = "";
            string final = a.Line;
            string how = "template";
            if (a.Check is { } check)
            {
                using var timed = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                ChatPrompt prompt = prompts.BuildAnswer(persona, "lodle", q.Text, a.Line);
                ChatWriterReply reply = writer.Write(prompt, target, timed.Token);
                raw = reply.Text ?? "";
                ChatVerdict v = filter.Check(raw, prompt.System, persona.FirstName);
                if (v.Line is { } line && check.Holds(line))
                {
                    final = line;
                    how = "model";
                    t[1]++;
                }
                else
                {
                    how = v.Line is null ? "refused " + v.Refusal : "refused Unverified";
                    t[2]++;
                }

                if (!Grade(q, filter.Check(raw, "", persona.FirstName).Line ?? raw))
                    t[3]++;
            }

            bool right = Grade(q, final);
            if (!right)
                t[4]++;
            if (filter.Check(final, "", persona.FirstName).Line is null)
                t[5]++;
            rows.Add($"| {q.Group} | {q.Text} | {topic.Kind} | {Esc(raw)} | {how}: {Esc(final)} | {(right ? "ok" : "WRONG")} |");
        }

        report.Append("# Bot chat: questions, answered by ").Append(model).Append("\n\n| group | asked | model's words went out | model refused | model's raw line wrong | final line wrong | final refused by the filter | topic not read |\n|---|---|---|---|---|---|---|---|\n");
        foreach ((string group, int[] t) in totals)
            report.Append(CultureInfo.InvariantCulture, $"| {group} | {t[0]} | {t[1]} | {t[2]} | {t[3]} | {t[4]} | {t[5]} | {t[6]} |\n");
        report.Append("\n| group | question | topic | model wrote | went out | grade |\n|---|---|---|---|---|---|\n");
        foreach (string row in rows)
            report.Append(row).Append('\n');
        File.WriteAllText(args[5], report.ToString());
        Console.WriteLine($"chat-truth: wrote {args[5]}");
        return 0;
    }

    /// <summary><c>chat-places &lt;nav3d&gt; &lt;bsp&gt; &lt;help.json&gt;</c>: every item's place as chat words it, the map's floors and how much floor stands at each height, to read by eye.</summary>
    public static int Places(string[] args)
    {
        string nav3d = args[0], bsp = args[1];
        string mapName = Path.GetFileNameWithoutExtension(nav3d);
        NavLoadResult loaded = NavLoader.Load(mapName, p => p.EndsWith(".nav3d", StringComparison.Ordinal) ? File.ReadAllBytes(nav3d) : File.ReadAllBytes(bsp), null);
        if (loaded.Nav is not { } nav)
            throw new InvalidDataException(loaded.Refusal);
        WeaponCatalog catalog = WeaponCatalog.Stock();
        var weapons = new WeaponKnowledge(Enumerable.Range(0, catalog.Count).Select(i => catalog.ByIndex(i)!.Value).Select((w, i) => new WeaponFacts(i, w.Name, w.ClipSize, w.Interval, 0f, false, false, 0f)));
        MapKnowledge map = MapKnowledgeBuilder.Build(mapName, nav.Entities, catalog, weapons, 20f, 30f);
        ChatHelp help = ChatHelp.Parse(File.ReadAllText(args[2]));
        IReadOnlySet<string> good = SessionMapFacts.GoodOf(weapons.All.Select(w => w.Name)).ToHashSet(StringComparer.Ordinal);
        NavGraph g = nav.Nav.Graph;
        var places = new ChatPlaces(map, g);
        var book = new ChatAnswerBook(help, map, places, good, 0xC4A7);
        var bins = new SortedDictionary<int, float>();
        var parts = new HashSet<int>();
        foreach (AiVector spawn in map.Spawns)
        {
            CellId cell = g.FindCell(spawn);
            if (cell.IsValid)
                parts.Add(g.ComponentOf(cell.Index));
        }

        for (int i = 0; i < g.CellCount; i++)
        {
            if (!parts.Contains(g.ComponentOf(i)))
                continue;
            NavCell c = g.Cell(i);
            int bin = (int)MathF.Floor(c.FloorZ / 16f) * 16;
            bins[bin] = bins.GetValueOrDefault(bin) + (c.MaxX - c.MinX) * (c.MaxY - c.MinY);
        }

        float total = bins.Values.Sum();
        foreach ((int bin, float area) in bins)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"z {bin,6}: {100f * area / total,5:F1} %"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"floors {places.FloorCount}, long side {places.LongSide:F0} u, {g.CellCount} cells"));
        foreach (ChatItem item in help.Items)
        {
            foreach (ItemSpawn spawn in map.Items.Where(i => item.Classes.Contains(i.ClassName, StringComparer.Ordinal)))
            {
                ChatAnswer a = book.Answer(new ChatTopic(ChatTopicKind.Where, item), new ChatAsked("", default, null, null, spawn.Position, spawn.Index));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{item.Say,-20} {spawn.Position.X,7:F0} {spawn.Position.Y,7:F0} {spawn.Position.Z,6:F0}  {a.Line}"));
            }
        }

        return 0;
    }

    private static string Esc(string s) => s.Replace("|", "/", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static bool Grade(Question q, string line)
        => (q.Must is null || q.Must.IsMatch(line)) && (q.MustNot is null || !q.MustNot.IsMatch(line));
}
