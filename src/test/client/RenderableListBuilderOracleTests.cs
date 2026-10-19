using System.Globalization;

using SourceSharp.Client.Render.LeafSystem;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed leaf-system decisions (<see cref="RenderableListBuilder"/>)
/// against stock's CClientLeafSystem, run offline from the pristine
/// clientleafsystem.cpp by src/test/oracle/native/leafsystem_oracle_test.cpp
/// (<c>Fixtures/leafsystem-stock.tsv</c>, README beside it).
///
/// <para>
/// Every view's buffer (the VB row, as the client's gather writes it) is
/// handed to the builder, and its answer is held to what stock did with the
/// same registry and the same engine answers: each group's entries in order
/// (decision 1, and decision 2's back-to-front sort), the render frame,
/// render leaf and render frame 2 stock left each renderable with (decision
/// 2's translucent leaf, decision 1's dedupe), the groups stock warned had
/// overflowed, and the view models' split (decision 5).
/// </para>
/// </summary>
public class RenderableListBuilderOracleTests
{
    private sealed class View
    {
        public string Tag = "";
        public int[] Words = [];
        public readonly List<string[]> Outs = [];
        public readonly List<string[]> State = [];
        public readonly List<string> Overflows = [];
    }

    private sealed class ViewModels
    {
        public int[] Words = [];
        public int[] Ids = [];
        public int[] Opaque = [];
        public int[] Translucent = [];
    }

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int[] Hex(string[] row) =>
        row.Skip(1).Select(h => unchecked((int)uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray();

    private static List<string[]> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "leafsystem-stock.tsv");
        Assert.True(File.Exists(path), path + " is missing");
        return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
    }

    private static List<View> Views()
    {
        var views = new List<View>();
        string tag = "";
        View? open = null;
        foreach (string[] r in Rows())
        {
            switch (r[0])
            {
                case "V": tag = r[1]; break;
                case "VB": open = new View { Tag = tag, Words = Hex(r) }; views.Add(open); break;
                case "O" when open != null: open.Outs.Add(r); break;
                case "ST" when open != null: open.State.Add(r); break;
                case "NP" when open != null: open.Overflows.Add(r[2]); break;
                case "VE": open = null; break;
            }
        }
        Assert.True(views.Count >= 12, $"the golden holds {views.Count} views");
        return views;
    }

    private static List<ViewModels> ViewModelCalls()
    {
        var calls = new List<ViewModels>();
        foreach (string[] r in Rows())
        {
            switch (r[0])
            {
                case "VMB": calls.Add(new ViewModels { Words = Hex(r) }); break;
                case "VMI": calls[^1].Ids = r.Skip(1).Select(I).ToArray(); break;
                case "VML" when r[1] == "o": calls[^1].Opaque = r.Skip(2).Select(I).ToArray(); break;
                case "VML" when r[1] == "t": calls[^1].Translucent = r.Skip(2).Select(I).ToArray(); break;
            }
        }
        Assert.True(calls.Count >= 3, $"the golden holds {calls.Count} view model splits");
        return calls;
    }

    // The answer's size, as leaf_view_gather.h sizes it.
    private static int AnswerWords(int[] w) => 3 + 3 * w[11] + 4 * (2 * w[10] + 2 * w[11] + w[13]);

    private static int[] Answer(View v)
    {
        var answer = new int[AnswerWords(v.Words)];
        int n = new RenderableListBuilder().BuildRenderableList(v.Words, answer);
        Assert.True(n > 0, $"view {v.Tag}: the builder refused the buffer ({n})");
        Assert.Equal(n, answer[0]);
        return answer;
    }

    private static string Source(int source) => source >= 0 ? $"r{source}" : source == -1 ? "-" : $"d{-2 - source}";

    [Fact]
    public void EveryViewsListsAreStocks()
    {
        int compared = 0;
        foreach (View v in Views())
        {
            int[] answer = Answer(v);
            int at = 2 + 3 * v.Words[11];
            int n = answer[at++];
            var managed = new List<string>();
            for (int e = 0; e < n; e++, at += 4)
                managed.Add($"{answer[at]}\t{Source(answer[at + 1])}\t{answer[at + 2]}\t{answer[at + 3]}");
            var stock = v.Outs.Select(o => $"{o[1]}\t{(o[3] == "-" ? "-" : o[3] + o[4])}\t{o[6]}\t{o[7]}").ToList();
            // Stock's rows are by group then position; the answer is in group
            // order too, each group in its own order.
            Assert.True(stock.Count == managed.Count, $"view {v.Tag}: stock listed {stock.Count} entries, managed {managed.Count}");
            for (int i = 0; i < stock.Count; i++)
                Assert.True(stock[i] == managed[i], $"view {v.Tag} entry {i}: stock [{stock[i]}], managed [{managed[i]}]");
            compared += stock.Count;
        }
        Assert.True(compared > 1000, $"only {compared} entries compared");
    }

    [Fact]
    public void EveryRenderablesFramesAndRenderLeafAreStocks()
    {
        int compared = 0;
        foreach (View v in Views())
        {
            int[] answer = Answer(v);
            Assert.Equal(v.Words[11], v.State.Count);
            foreach (string[] st in v.State)
            {
                int row = I(st[1]);
                int[] stock = [I(st[3]), I(st[4]), I(st[5])];
                int[] managed = [answer[2 + 3 * row], answer[2 + 3 * row + 1], answer[2 + 3 * row + 2]];
                Assert.True(stock.SequenceEqual(managed),
                            $"view {v.Tag} row {row} (id {st[2]}): stock frame/leaf/frame2 {string.Join(",", stock)}, managed {string.Join(",", managed)}");
                compared++;
            }
        }
        Assert.True(compared > 200, $"only {compared} rows compared");
    }

    [Fact]
    public void TheOverflowedGroupsAreTheOnesStockWarnedAbout()
    {
        int warned = 0;
        foreach (View v in Views())
        {
            int[] answer = Answer(v);
            int stock = 0;
            foreach (string text in v.Overflows)
            {
                const string prefix = "Warning: overflowed CClientRenderablesList group ";
                Assert.StartsWith(prefix, text);
                stock |= 1 << I(text[prefix.Length..]);
            }
            Assert.True(stock == answer[1], $"view {v.Tag}: stock overflowed groups {stock:x}, managed {answer[1]:x}");
            warned += v.Overflows.Count;
        }
        Assert.True(warned > 0, "no view overflowed: the overflow arm is not compared");
    }

    [Fact]
    public void TheViewModelSplitIsStocks()
    {
        foreach (ViewModels call in ViewModelCalls())
        {
            var answer = new int[call.Words[0]];
            Assert.Equal(call.Words[0], new RenderableListBuilder().SplitViewModels(call.Words, answer));
            Assert.Equal(call.Opaque, call.Ids.Where((_, i) => answer[i] == 0).ToArray());
            Assert.Equal(call.Translucent, call.Ids.Where((_, i) => answer[i] == 1).ToArray());
        }
    }

    [Fact]
    public void ABufferWhoseLengthDisagreesIsRefused()
    {
        View v = Views()[0];
        var answer = new int[AnswerWords(v.Words)];
        Assert.True(new RenderableListBuilder().BuildRenderableList(v.Words, answer) > 0);
        Assert.Equal(-1, new RenderableListBuilder().BuildRenderableList(v.Words.AsSpan(0, v.Words.Length - 1), answer));
        Assert.Equal(-1, new RenderableListBuilder().BuildRenderableList(v.Words, answer.AsSpan(0, 3)));
    }
}
