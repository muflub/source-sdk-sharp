using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Sounds;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed client sound emitter (<see cref="ClientSoundEmitter"/>, over
/// the shared <see cref="SoundEmitterCore"/>) and its system
/// (<see cref="ClientSoundEmitterSystem"/>) against stock's client
/// CSoundEmitterSystem, run offline from the pristine SoundEmitterSystem.cpp
/// by src/test/oracle/native/soundemitter_client_oracle_test.cpp
/// (<c>Fixtures/soundemitter-client-stock.tsv</c>, README beside it).
///
/// <para>
/// Each recorded call is made as the oracle made it, through the entry point
/// the client called it by. Every question the port asks the engine or the
/// script table must be the one stock asked, in the same place, and is
/// answered from the recording; every emit, stop, ambient sound, caption,
/// console line and result is compared with stock's, floats by their bits.
/// </para>
///
/// <para>
/// Two differences are the port's on purpose, and the replay states them:
/// the script trace counts the recipients (plan_entity.md step 4b, which
/// tools/7e-particle-sound-gate reads), and <c>GameSound_PrecacheScript</c>,
/// which had no caller on the client's link line, is not ported -- its rows
/// are skipped.
/// </para>
/// </summary>
public class ClientSoundEmitterOracleTests
{
    private static readonly Regex ScriptTrace =
        new(@"^\(cl\) EmitSound:  '(.*)' emitted as '(.*)' \(ent (-?\d+)\)\n$", RegexOptions.Singleline);

    private static float H(string hex) => BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private static string X(float f) => unchecked((uint)BitConverter.SingleToInt32Bits(f)).ToString("x8", CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>A TSV cell as text: "-" is null; "\n", "\t", "\\" are escapes.</summary>
    private static string? Text(string cell)
    {
        if (cell == "-")
            return null;
        var builder = new StringBuilder(cell.Length);
        for (int i = 0; i < cell.Length; i++)
        {
            if (cell[i] == '\\' && i + 1 < cell.Length)
            {
                char next = cell[++i];
                builder.Append(next switch { 'n' => '\n', 't' => '\t', _ => next });
            }
            else
            {
                builder.Append(cell[i]);
            }
        }

        return builder.ToString();
    }

    private static string Cell(string? text)
    {
        if (text is null)
            return "-";
        return text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
                   .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static Vector? V(string cell)
    {
        if (cell == "-")
            return null;
        string[] p = cell.Split(',');
        return new Vector(H(p[0]), H(p[1]), H(p[2]));
    }

    private static string VCell(Vector? v) => v is { } at ? $"{X(at.X)},{X(at.Y)},{X(at.Z)}" : "-";

    private static ClientRecipients Filter(string cell)
    {
        string[] halves = cell.Split('/');
        int[] slots = halves[0] == "-" ? [] : halves[0].Split(',').Select(I).ToArray();
        return ClientRecipients.Of(slots, halves[1] == "1", false);
    }

    private static string FilterCell(IRecipientList filter)
    {
        var slots = new List<string>();
        for (int i = 0; i < filter.Count; i++)
            slots.Add(filter.IndexAt(i).ToString(CultureInfo.InvariantCulture));
        return (slots.Count == 0 ? "-" : string.Join(",", slots)) + (filter.IsReliable ? "/1" : "/0");
    }

    private static string ParamsCells(bool ok, in SoundParametersLayout p)
        => $"{(ok ? 1 : 0)}\t{p.Channel}\t{X(p.Volume)}\t{p.SoundLevel}\t{p.Pitch}\t{p.PitchLow}\t{p.PitchHigh}\t{p.DelayMsec}";

    //-------------------------------------------------------------------------
    // The world and the recording, as the port's engine, context and HUD.
    //-------------------------------------------------------------------------

    private sealed class Entity
    {
        public string? Model;
        public bool IsPlayer;
        public int Owner = -1;
    }

    private sealed class Replay : ISoundEmitterEngine, IClientSoundContext, ICloseCaptionHud, IClientSoundSystemEngine, IGameClock
    {
        public readonly Dictionary<int, Entity> Entities = [];
        public readonly Dictionary<string, string> Vars = new(StringComparer.Ordinal);
        public int MaxClientsValue = 1;
        public float Curtime;
        public int Local = 1;
        public bool Hud = true;

        public readonly Queue<string[]> Expected = new();
        public int FilterCount;
        public string Call = string.Empty;
        public int Answered;

        private string[] Next(string kind, string what)
        {
            if (Expected.Count == 0)
                Assert.Fail($"{Call}: the port did {kind} {what}, and stock did nothing more");
            string[] row = Expected.Dequeue();
            Assert.True(row[0] == kind && (what.Length == 0 || row[1] == what),
                        $"{Call}: the port did {kind} {what}; stock's next was {string.Join(" ", row)}");
            return row;
        }

        private void Same(string expected, string actual, string what)
            => Assert.True(expected == actual, $"{Call}: {what}: stock {expected}, the port {actual}");

        // ISoundEmitterEngine
        public int SoundIndex(string name)
        {
            string[] r = Next("A", "SoundIndex");
            Same(r[2], Cell(name), "SoundIndex's name");
            Answered++;
            return I(r[4]);
        }

        public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender)
        {
            string[] r = Next("A", "Params");
            Same($"{r[2]}\t{r[3]}\t{r[4]}", $"{Cell(name)}\t{gender}\t0", "GetParametersForSound's question");
            Answered++;
            return Answer(r, 6, ref parameters);
        }

        public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters, int gender,
                                         bool beingEmitted)
        {
            string[] r = Next("A", "ParamsEx");
            Same($"{r[2]}\t{r[3]}\t{r[4]}\t{r[5]}", $"{Cell(name)}\t{handle}\t{gender}\t{(beingEmitted ? 1 : 0)}",
                 "GetParametersForSoundEx's question");
            handle = (short)I(r[6][2..]);
            Answered++;
            return Answer(r, 8, ref parameters);
        }

        private static bool Answer(string[] r, int at, ref SoundParametersLayout p)
        {
            if (r[at] != "1")
                return false;
            p.Channel = I(r[at + 1]);
            p.Volume = H(r[at + 2]);
            p.SoundLevel = I(r[at + 3]);
            p.Pitch = I(r[at + 4]);
            p.PitchLow = I(r[at + 5]);
            p.PitchHigh = I(r[at + 6]);
            p.DelayMsec = I(r[at + 7]);
            p.Name = Text(r.Length > at + 8 ? r[at + 8] : string.Empty) ?? string.Empty;
            return true;
        }

        public SoundScriptEntry? Entry(int index)
        {
            string[] r = Next("A", "Internal");
            Same(r[2], index.ToString(CultureInfo.InvariantCulture), "InternalGetParametersForSound's index");
            Answered++;
            if (r[4] == "-")
                return null;
            return new SoundScriptEntry(I(r[4]), r[5] == "-" ? [] : r[5].Split(','));
        }

        public string? WavFileForSound(string name, int gender)
        {
            string[] r = Next("A", "WavFile");
            Same($"{r[2]}\t{r[3]}", $"{Cell(name)}\t{gender}", "GetWavFileForSound's question");
            Answered++;
            return Text(r[5]);
        }

        public int LookupSoundLevel(string name) => throw new InvalidOperationException("stock's client emit never asks LookupSoundLevel");

        public int ActorGender(string? model)
        {
            string[] r = Next("A", "ActorGender");
            Same(r[2], Cell(model), "GetActorGender's model");
            Answered++;
            return I(r[4]);
        }

        public void EmitSound(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                              int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime,
                              int speakerEntity)
        {
            string[] r = Next("O", "EmitSound");
            Same(string.Join("\t", r[2..]),
                 $"{FilterCell(recipients)}\t{entIndex}\t{channel}\t{Cell(sample)}\t{X(volume)}\t{soundLevel}\t{flags}\t{pitch}\t{specialDsp}\t{VCell(origin)}\t-\t{X(soundTime)}\t{speakerEntity}",
                 "the emit");
        }

        public void StopSound(int entIndex, int channel, string sample)
        {
            string[] r = Next("O", "StopSound");
            Same(string.Join("\t", r[2..]), $"{entIndex}\t{channel}\t{Cell(sample)}", "the stop");
        }

        public float SoundDuration(string sample)
        {
            string[] r = Next("A", "Duration");
            Same(r[2], Cell(sample), "GetSoundDuration's sample");
            Answered++;
            return H(r[4]);
        }

        public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel,
                                     int flags, int pitch, float soundTime)
        {
            // The client's EmitAmbientSound takes no entity, origin or level.
            string[] r = Next("O", "Ambient");
            Same(string.Join("\t", r[2..]), $"{Cell(sample)}\t{X(volume)}\t{pitch}\t{flags}\t{X(soundTime)}", "the ambient sound");
        }

        public int SentenceIndexFromName(string name)
        {
            string[] r = Next("A", "Sentence");
            Same(r[2], Cell(name), "SentenceIndexFromName's name");
            Answered++;
            return I(r[4]);
        }

        // IClientSoundContext
        public int MaxClients => MaxClientsValue;

        public bool CloseCaptionOn => Vars.TryGetValue("closecaption", out string? v) && v != "0";

        public int LocalPlayerSlot => Local;

        public bool TraceOn => Vars.TryGetValue("sv_soundemitter_trace", out string? v) && v != "0";

        public string? SpeakerModel(int entIndex) => Entities.TryGetValue(entIndex, out Entity? e) ? e.Model ?? string.Empty : null;

        public bool FromPlayer(int entIndex)
        {
            for (int at = entIndex; Entities.TryGetValue(at, out Entity? e); at = e.Owner)
            {
                if (e.IsPlayer)
                    return true;
            }

            return false;
        }

        // ICloseCaptionHud
        public bool Present => Hud;

        public void ProcessCaption(string token, float duration, bool fromPlayer)
        {
            string[] r = Next("O", "Caption");
            Same(string.Join("\t", r[2..]), $"{Cell(token)}\t{X(duration)}\t{(fromPlayer ? 1 : 0)}\t0", "the caption");
        }

        // IClientSoundSystemEngine
        public bool ModInit()
        {
            string[] r = Next("A", "ModInit");
            Answered++;
            return r[3] == "1";
        }

        public void ModShutdown() => Next("O", "ModShutdown");

        public string LevelName()
        {
            string[] r = Next("A", "LevelName");
            Answered++;
            return Text(r[3]) ?? string.Empty;
        }

        public bool FileExists(string name, string pathId)
        {
            string[] r = Next("A", "FileExists");
            Same($"{r[2]}\t{r[3]}", $"{Cell(name)}\t{Cell(pathId)}", "FileExists's question");
            Answered++;
            return r[5] == "1";
        }

        public void AddSoundOverrides(string scriptFile, bool preload)
        {
            string[] r = Next("O", "AddOverrides");
            Same($"{r[2]}\t{r[3]}", $"{Cell(scriptFile)}\t{(preload ? 1 : 0)}", "the overrides");
        }

        public void ClearSoundOverrides() => Next("O", "ClearOverrides");

        // IGameClock
        public float Now => Curtime;

        // The console.
        public void Msg(string text)
        {
            string[] r = Next("M", string.Empty);
            string expected = Text(r[1])!;
            Match m = ScriptTrace.Match(expected);
            if (m.Success)
            {
                // The port's script trace counts the recipients (plan_entity.md 4b).
                expected = $"(cl) EmitSound:  '{m.Groups[1].Value}' emitted as '{m.Groups[2].Value}' (ent {m.Groups[3].Value}, {FilterCount} recipients)\n";
            }

            Same(Cell(expected), Cell(text), "the console line");
        }

        public void DevMsg(string text)
        {
            string[] r = Next("D", string.Empty);
            Same(r[1], Cell(text), "the developer console line");
        }
    }

    private sealed record Run(int Calls, int Answers, int Results, HashSet<string> Ops);

    private static List<string[]> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "soundemitter-client-stock.tsv");
        Assert.True(File.Exists(path), path + " is missing");
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(rows.Count > 400, "the golden is empty or truncated");
        Assert.Equal("END", rows[^1][0]);
        return rows;
    }

    private static bool IsEvent(string kind) => kind is "A" or "O" or "M" or "D" or "W" or "R";

    private static Run ReplayAll()
    {
        List<string[]> rows = Rows();
        var replay = new Replay();
        var emitter = new ClientSoundEmitter(replay, replay, replay, replay, replay.Msg, replay.DevMsg);
        var system = new ClientSoundEmitterSystem(replay);
        int calls = 0, results = 0;
        var ops = new HashSet<string>(StringComparer.Ordinal);

        for (int at = 0; at < rows.Count; at++)
        {
            string[] row = rows[at];
            switch (row[0])
            {
                case "G":
                    replay.MaxClientsValue = I(row[1]);
                    replay.Curtime = H(row[2]);
                    continue;
                case "V":
                    replay.Vars[row[1]] = row[2];
                    continue;
                case "HUD":
                    replay.Hud = row[1] == "1";
                    continue;
                case "LOCAL":
                    replay.Local = I(row[1]);
                    continue;
                case "ENT":
                    replay.Entities[I(row[1])] = new Entity { Model = Text(row[2]), IsPlayer = row[3] == "1", Owner = I(row[4]) };
                    continue;
                case "C":
                    break;
                default:
                    continue;
            }

            // The call's own rows: what it asked and did, then its results.
            var events = new List<string[]>();
            while (at + 1 < rows.Count && IsEvent(rows[at + 1][0]))
                events.Add(rows[++at]);

            string op = row[1];
            if (op == "precachescript")
                continue;

            calls++;
            ops.Add(op);
            replay.Call = string.Join(" ", row[1..]);
            replay.Expected.Clear();
            foreach (string[] e in events.Where(e => e[0] != "R"))
                replay.Expected.Enqueue(e);
            var expectedResults = events.Where(e => e[0] == "R").Select(e => string.Join("\t", e)).ToList();
            var actualResults = new List<string>();

            switch (op)
            {
                case "emitname":
                {
                    ClientRecipients filter = Filter(row[2]);
                    replay.FilterCount = filter.Count;
                    emitter.Emit(filter, I(row[3]), Text(row[4]), V(row[5]), H(row[6]));
                    break;
                }
                case "emitnamehandle":
                {
                    ClientRecipients filter = Filter(row[2]);
                    replay.FilterCount = filter.Count;
                    short handle = (short)I(row[5]);
                    emitter.EmitByHandle(filter, I(row[3]), Text(row[4])!, ref handle, V(row[6]), H(row[7]));
                    actualResults.Add($"R\thandle\t{handle}");
                    break;
                }
                case "emitparams":
                case "emitparamshandle":
                {
                    ClientRecipients filter = Filter(row[2]);
                    replay.FilterCount = filter.Count;
                    string[] e = row[4..];
                    var ep = new EmitSoundParams
                    {
                        Channel = I(e[0]),
                        SoundName = Text(e[1]),
                        Volume = H(e[2]),
                        SoundLevel = I(e[3]),
                        Flags = I(e[4]),
                        Pitch = I(e[5]),
                        SpecialDsp = I(e[6]),
                        Origin = V(e[7]),
                        SoundTime = H(e[8]),
                        WantDuration = e[9] == "1",
                        Duration = -1.0f,
                        EmitCloseCaption = e[10] == "1",
                        WarnOnMissingCloseCaption = e[11] == "1",
                        WarnOnDirectWaveReference = e[12] == "1",
                        SpeakerEntity = I(e[13]),
                        ScriptHandle = (short)I(e[14]),
                    };
                    if (op == "emitparams")
                    {
                        emitter.Emit(filter, I(row[3]), ep);
                        if (ep.WantDuration)
                            actualResults.Add($"R\tduration\t{X(ep.Duration)}");
                        actualResults.Add($"R\tephandle\t{ep.ScriptHandle}");
                    }
                    else
                    {
                        short handle = (short)I(row[19]);
                        emitter.EmitByHandle(filter, I(row[3]), ep, ref handle);
                        if (ep.WantDuration)
                            actualResults.Add($"R\tduration\t{X(ep.Duration)}");
                        actualResults.Add($"R\thandle\t{handle}");
                    }

                    break;
                }
                case "stopname":
                    emitter.Stop(I(row[2]), Text(row[3])!);
                    break;
                case "stopchannel":
                    emitter.Stop(I(row[2]), I(row[3]), Text(row[4]));
                    break;
                case "ambient":
                {
                    bool want = row[10] == "1";
                    bool written = emitter.UtilEmitAmbient(I(row[2]), V(row[3])!.Value, Text(row[4]), H(row[5]), I(row[6]),
                                                           I(row[7]), I(row[8]), H(row[9]), want, out float duration);
                    if (want)
                        actualResults.Add($"R\tduration\t{X(written ? duration : -1.0f)}");
                    break;
                }
                case "caption":
                {
                    ClientRecipients filter = Filter(row[2]);
                    replay.FilterCount = filter.Count;
                    emitter.EmitCloseCaption(filter, I(row[3]), Text(row[4])!, H(row[5]), row[6] == "1");
                    break;
                }
                case "params":
                {
                    SoundParametersLayout? got = emitter.Parameters(Text(row[2])!, Text(row[3]));
                    SoundParametersLayout p = got ?? SoundParametersLayout.Stock();
                    actualResults.Add($"R\tparams\t{ParamsCells(got.HasValue, p)}\t{Cell(p.Name)}");
                    break;
                }
                case "init":
                    actualResults.Add($"R\tinit\t{(system.Init() ? 1 : 0)}");
                    break;
                case "shutdown":
                    system.Shutdown();
                    break;
                case "levelinit":
                    system.LevelInitPreEntity();
                    break;
                case "levelinitpost":
                    // Stock's LevelInitPostEntity is empty, and the port has none.
                    break;
                case "levelshutdown":
                    system.LevelShutdownPostEntity();
                    break;
                default:
                    Assert.Fail("the golden makes a call the replay does not know: " + op);
                    break;
            }

            if (replay.Expected.Count > 0)
                Assert.Fail($"{replay.Call}: stock went on to {string.Join(" ", replay.Expected.Peek())}, and the port did not");
            Assert.Equal(expectedResults, actualResults);
            results += actualResults.Count;
        }

        return new Run(calls, replay.Answered, results, ops);
    }

    [Fact]
    public void ThePortDoesWhatStockDidForEveryRecordedCall()
    {
        Run run = ReplayAll();

        // The replay reached the whole recording: every call but the two
        // unported precaches, every question they asked and every result.
        List<string[]> rows = Rows();
        int precaches = rows.Count(r => r[0] == "C" && r[1] == "precachescript");
        Assert.Equal(2, precaches);
        Assert.Equal(rows.Count(r => r[0] == "C") - precaches, run.Calls);
        Assert.Equal(rows.Count(r => r[0] == "A") - precaches, run.Answers);
        Assert.Equal(rows.Count(r => r[0] == "R") - precaches, run.Results);
        Assert.True(run.Calls > 80 && run.Answers > 120 && run.Results > 30,
                    $"the replay is too small to be the recording: {run}");
    }

    [Fact]
    public void TheReplayMakesEveryKindOfCallTheGoldenRecords()
    {
        Run run = ReplayAll();

        string[] expected =
        [
            "emitname", "emitnamehandle", "emitparams", "emitparamshandle", "stopname", "stopchannel", "ambient",
            "caption", "params", "init", "shutdown", "levelinit", "levelinitpost", "levelshutdown",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), run.Ops.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FixEscapeCharsMakesOnlyBackslashNANewline()
    {
        Assert.Equal("a\nb\\tc\\", ClientSoundEmitter.FixEscapeChars("a\\nb\\tc\\"));
        Assert.Equal("\n\n", ClientSoundEmitter.FixEscapeChars("\\n\\n"));
    }

    [Fact]
    public void TheLevelSoundsScriptIsTheMapsLoweredWithoutItsExtension()
    {
        Assert.Equal("maps/dm_lockdown_level_sounds.txt", SoundScriptFiles.LevelSoundsScript(SoundScriptFiles.FileName("maps/DM_Lockdown.bsp")));
        Assert.Equal("maps/dm_overrides_level_sounds.txt", SoundScriptFiles.LevelSoundsScript(SoundScriptFiles.FileName("maps\\dm_Overrides.bsp")));
        Assert.Equal("maps/dm_lockdown_level_sounds.txt", SoundScriptFiles.LevelSoundsScript("dm_lockdown"));
        // Q_FixSlashes: a separator in the name itself is made the platform's.
        Assert.Equal("maps/sub/map_level_sounds.txt", SoundScriptFiles.LevelSoundsScript("sub\\Map.bsp"));
    }

    [Fact]
    public void TheLocalPlayerFilterHoldsTheViewerOrNobody()
    {
        Assert.Equal("3/0", FilterCell(ClientRecipients.LocalPlayer(3)));
        Assert.Equal("-/0", FilterCell(ClientRecipients.LocalPlayer(0)));
        Assert.Equal("-/1", FilterCell(ClientRecipients.LocalPlayer(-1).MakeReliable()));
    }
}
