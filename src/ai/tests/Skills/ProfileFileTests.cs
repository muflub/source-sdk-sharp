using System.Collections.Immutable;
using System.Text;
using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

/// <summary>A custom bot's profile file (plan_hl2mp_ai §3.5, §8.2 bot-setup facts).</summary>
public class ProfileFileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static SkillPool Pool => StandardSkills.StandardPool();

    private static BotProfileDocument Deadeye()
    {
        ImmutableArray<int> now = [8, 7, 6, 6, 6, 6, 7, 5, 5, 4];
        ImmutableArray<int> first = [6, 6, 5, 5, 5, 5, 6, 4, 5, 3];
        return new BotProfileDocument(new SkillProfile("Deadeye", StandardSkills.Ten, now), StandardSkills.Pool,
                                      [new ProfileRevision(1, T0, "first", first), new ProfileRevision(2, T0.AddMinutes(5), "restored #1", first),
                                       new ProfileRevision(3, T0.AddMinutes(9), "", now)]);
    }

    private static string Json(string skills = "\"accuracy\": 8, \"alertness\": 7, \"aggression\": 6, \"cover\": 6, \"tenacity\": 6, \"cunning\": 6, \"discipline\": 7, \"agility\": 5, \"nerve\": 5, \"headhunter\": 4",
                               int format = 1, string name = "Deadeye", int pool = 60)
        => $$"""{ "format": {{format}}, "name": "{{name}}", "pool": {{pool}}, "skills": { {{skills}} }, "revisions": [] }""";

    private static SkillVerdict Read(string json, string name = "Deadeye")
        => ProfileFile.Read(Encoding.UTF8.GetBytes(json), $"cfg/ai/bots/{name}.json", name, Pool, out _);

    [Fact]
    public void AProfileWithRevisionsRoundTripsByteForByte()
    {
        byte[] bytes = ProfileFile.Write(Deadeye());
        Assert.True(ProfileFile.Read(bytes, "Deadeye.json", "Deadeye", Pool, out BotProfileDocument? doc).Accepted);
        Assert.Equal(bytes, ProfileFile.Write(doc!));
    }

    [Fact]
    public void TheRoundTripKeepsEveryRevision()
    {
        ProfileFile.Read(ProfileFile.Write(Deadeye()), "Deadeye.json", "Deadeye", Pool, out BotProfileDocument? doc);
        Assert.Equal(["1 first 6", "2 restored #1 6", "3  8"],
                     doc!.Revisions.Select(r => $"{r.Number} {r.Note} {r.Levels[0]}"));
        Assert.Equal(T0.AddMinutes(5), doc.Revisions[1].At);
    }

    [Fact]
    public void TheFileIsThePlansShape()
    {
        string text = Encoding.UTF8.GetString(ProfileFile.Write(Deadeye()));
        Assert.Contains("\"format\": 1,", text);
        Assert.Contains("\"name\": \"Deadeye\",", text);
        Assert.Contains("\"pool\": 60,", text);
        Assert.Contains("\"at\": \"2026-11-01T10:05:00Z\"", text);
    }

    [Fact]
    public void AnotherFormatIsRefusedByNumber()
        => Assert.Equal("cfg/ai/bots/Deadeye.json is format 2; this build reads format 1", Read(Json(format: 2)).Refusal);

    [Fact]
    public void AnUnknownSkillIsRefusedByName()
        => Assert.Equal("cfg/ai/bots/Deadeye.json: unknown skill 'soloness'",
                        Read(Json("\"accuracy\": 8, \"soloness\": 1")).Refusal);

    [Fact]
    public void ALevelElevenIsRefusedNamingTheSkill()
        => Assert.Equal("cfg/ai/bots/Deadeye.json: accuracy is 11, outside 0..10",
                        Read(Json("\"accuracy\": 11, \"alertness\": 7, \"aggression\": 6, \"cover\": 6, \"tenacity\": 6, \"cunning\": 6, \"discipline\": 7, \"agility\": 5, \"nerve\": 5, \"headhunter\": 4")).Refusal);

    [Fact]
    public void AMissingSkillIsRefusedByName()
        => Assert.Equal("cfg/ai/bots/Deadeye.json: no level for 'headhunter'",
                        Read(Json("\"accuracy\": 8, \"alertness\": 7, \"aggression\": 6, \"cover\": 6, \"tenacity\": 6, \"cunning\": 6, \"discipline\": 7, \"agility\": 5, \"nerve\": 5")).Refusal);

    [Fact]
    public void ANameWithASlashIsRefusedByName()
        => Assert.Equal("cfg/ai/bots/a/b.json: 'a/b' has '/': a profile's name is letters, digits, _ and -",
                        Read(Json(name: "a/b"), "a/b").Refusal);

    [Fact]
    public void AFileHoldingAnotherNameIsRefused()
        => Assert.Equal("cfg/ai/bots/Deadeye.json holds 'Sniper', not 'Deadeye'", Read(Json(name: "Sniper")).Refusal);

    [Fact]
    public void AnOverspentFileIsRefusedWithTheSum()
        => Assert.Equal("cfg/ai/bots/Deadeye.json: profile 'Deadeye' spends 70 of a 60-point pool",
                        Read(Json("\"accuracy\": 10, \"alertness\": 10, \"aggression\": 10, \"cover\": 10, \"tenacity\": 10, \"cunning\": 10, \"discipline\": 10, \"agility\": 0, \"nerve\": 0, \"headhunter\": 0", pool: 100)).Refusal);

    [Fact]
    public void AProfileFromASmallerPoolLoads()
        => Assert.True(Read(Json("\"accuracy\": 2, \"alertness\": 2, \"aggression\": 3, \"cover\": 1, \"tenacity\": 2, \"cunning\": 1, \"discipline\": 2, \"agility\": 2, \"nerve\": 3, \"headhunter\": 2", pool: 40)).Accepted);

    [Fact]
    public void AnOverspentRevisionIsRefusedNamingIt()
    {
        string json = """{ "format": 1, "name": "Deadeye", "pool": 60, "skills": { "accuracy": 1, "alertness": 0, "aggression": 0, "cover": 0, "tenacity": 0, "cunning": 0, "discipline": 0, "agility": 0, "nerve": 0, "headhunter": 0 }, "revisions": [ { "n": 4, "at": "2026-11-01T10:00:00Z", "note": "", "skills": { "accuracy": 10, "alertness": 10, "aggression": 10, "cover": 10, "tenacity": 10, "cunning": 10, "discipline": 10, "agility": 0, "nerve": 0, "headhunter": 0 } } ] }""";
        Assert.Equal("cfg/ai/bots/Deadeye.json (revision #4): profile 'Deadeye' spends 70 of a 60-point pool", Read(json).Refusal);
    }

    [Fact]
    public void NotJsonIsRefusedNamingTheFile()
        => Assert.StartsWith("cfg/ai/bots/Deadeye.json is not JSON", Read("{ format").Refusal);

    [Theory]
    [InlineData("Deadeye", true)]
    [InlineData("dead_eye-2", true)]
    [InlineData("", false)]
    [InlineData("dead eye", false)]
    [InlineData("..", false)]
    [InlineData("a\\b", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)]
    public void AProfilesNameIsLettersDigitsUnderscoreAndDash(string name, bool valid)
        => Assert.Equal(valid, ProfileFile.NameRefusal(name) is null);
}
