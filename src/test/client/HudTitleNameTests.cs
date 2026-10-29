using System.Text.RegularExpressions;

using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// A HudText name reaches its titles.txt entry with or without a leading '#'
/// (stock CHudMessage::MessageAdd, message.cpp:729-736), decided in C# since
/// lane M1-B.
/// </summary>
public class HudTitleNameTests
{
    private sealed class Event<T> : IUserMessageEvent<T>
    {
        public event Action<T>? Fired;
        public void Fire(T args) => Fired?.Invoke(args);
    }

    [Fact]
    public void A_leading_hash_is_dropped_before_the_lookup()
        => Assert.Equal("GAMETITLE", HudMessages.TitleName("#GAMETITLE"));

    [Fact]
    public void A_plain_name_is_looked_up_as_it_is()
        => Assert.Equal("GAMETITLE", HudMessages.TitleName("GAMETITLE"));

    [Fact]
    public void Only_one_hash_is_dropped()
        => Assert.Equal("#X", HudMessages.TitleName("##X"));

    [Fact]
    public void An_empty_name_stays_empty()
        => Assert.Equal("", HudMessages.TitleName(""));

    [Fact]
    public void A_HudText_title_asks_the_engine_for_the_name_without_its_hash()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("Hud.TextMessageGet", null);   // titles.txt has none: the log says so
        var hud = new HudMessages(new RecordingLog(), ClientDoubles.Fake(), engine);
        var titles = new Event<string>();
        hud.Subscribe(new Event<HudMessageArgs>(), titles);

        titles.Fire("#CHAPTER1_TITLE");

        EngineCall call = Assert.Single(engine.Calls, c => c.Member == "Hud.TextMessageGet");
        Assert.Equal("CHAPTER1_TITLE", call.Arg(0));
    }

}
