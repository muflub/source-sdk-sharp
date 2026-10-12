using System.Text;

using SourceSharp.Client.Voice;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed ban list replays the voice ban oracle's script
/// (src/test/oracle/native/voiceban_oracle_test.cpp, stock CVoiceBanMgr) and
/// must write exactly the golden it recorded: the same answers and the same
/// voice_ban.dt bytes after every step.
/// </summary>
public class VoiceBanOracleTests
{
    private static readonly string[] Ids = ["STEAM_1:0:1001", "STEAM_1:0:1002", "STEAM_1:0:1010", "STEAM_1:1:77", "BOT"];

    private static void Ban(VoiceBanList l, int i, bool b) => l.SetBanned(VoiceBanList.IdOf(Ids[i]), b);

    private static void Query(StringBuilder log, string step, VoiceBanList l)
    {
        foreach (var id in Ids)
            log.Append($"ban\t{step}\t{id}\t{(l.IsBanned(VoiceBanList.IdOf(id)) ? 1 : 0)}\n");
    }

    private static byte[] Save(StringBuilder log, string step, VoiceBanList l)
    {
        byte[] file = l.Save();
        log.Append($"save\t{step}\t{VoiceBanList.FileName}\t{Convert.ToHexString(file).ToLowerInvariant()}\n");
        return file;
    }

    private static string RunScenario()
    {
        var log = new StringBuilder();

        var m1 = new VoiceBanList();
        Ban(m1, 0, true); Ban(m1, 1, true); Ban(m1, 2, true); Ban(m1, 1, false);
        Ban(m1, 3, true); Ban(m1, 1, true); Ban(m1, 0, true);
        Query(log, "fresh", m1);
        byte[] saved = Save(log, "fresh", m1);

        var m2 = new VoiceBanList();
        m2.Load(saved);
        Query(log, "loaded", m2);
        Ban(m2, 4, true); Ban(m2, 0, false);
        Query(log, "edited", m2);
        Save(log, "edited", m2);

        var m3 = new VoiceBanList();
        m3.Load([.. BitConverter.GetBytes(2), .. saved.AsSpan(4).ToArray()]);
        Query(log, "version2", m3);
        Save(log, "version2", m3);

        var m4 = new VoiceBanList();
        m4.Load([.. saved, .. Enumerable.Repeat((byte)'x', 10)]);
        Query(log, "partial", m4);
        Save(log, "partial", m4);

        var m5 = new VoiceBanList();
        m5.Load(BitConverter.GetBytes(1));
        Query(log, "versiononly", m5);
        Save(log, "versiononly", m5);

        return log.ToString();
    }

    [Fact]
    public void The_managed_ban_list_writes_stocks_golden()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "voiceban-stock.tsv");
        Assert.True(File.Exists(path), $"{path} is missing: the oracle writes it");
        string golden = File.ReadAllText(path);
        Assert.True(golden.Length > 1000, "the golden is empty or truncated");
        Assert.Equal(golden, RunScenario());
    }
}
