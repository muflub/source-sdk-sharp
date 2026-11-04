using System.Reflection;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Chat;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// Bot chat knows what a player knows and changes nothing (plan_hl2mp_ai §3.6),
/// from the IL: the chat types read no position and no other player's state,
/// call nothing of a brain, a command, the senses or a recording; and nothing
/// that decides anything calls them.
/// </summary>
public sealed class ChatCensusTests
{
    private static readonly Assembly Binding = typeof(Hl2mpBotChat).Assembly;
    private static readonly Assembly Core = typeof(ChatDirector).Assembly;

    private static bool IsChatType(string? fullName)
        => fullName is not null && (fullName.StartsWith("SourceSharp.Ai.Chat", StringComparison.Ordinal) || fullName.StartsWith("SourceSharp.Ai.Hl2mp.Chat.", StringComparison.Ordinal));

    /// <summary>The core's chat types, by name: they sit in the core's one namespace.</summary>
    private static readonly string[] CoreChat =
    [
        nameof(ChatDirector), nameof(ChatWorker), nameof(ChatPromptBuilder), nameof(ChatOutputFilter), nameof(ChatWords), nameof(ChatAddress),
        nameof(ChatCounters), nameof(ChatSettings), nameof(ChatRefused), nameof(ChatLine), nameof(ChatFacts), nameof(ChatPersona), nameof(ChatPrompt), nameof(ChatHeard),
        nameof(ChatAnswerBook), nameof(ChatAnswerCheck), nameof(ChatHelp), nameof(ChatPlaces), nameof(ChatLineRules), nameof(ChatOpenings), nameof(ChatText),
    ];

    private static bool IsCoreChat(string caller) => CoreChat.Any(n => caller == "SourceSharp.Ai." + n);

    private static string Who(MethodBase m) => $"{m.DeclaringType?.Name}::{m.Name}";

    /// <summary>Every call the chat types make out of themselves into the game's core: what a player's screen shows, and its say path.</summary>
    private static readonly string[] Allowed =
    [
        "ConVar`1::get_Value", "IEngineFiles::ReadGameFile", "IEngineFrame::TickCount", "ILog::Msg",
        "IEngineEntities::GetIndex", "IClientInfo::ClientConVar", "IPlayerDirectory::get_All", "PlayerRecord::get_Kills", "PlayerRecord::get_Deaths",
        "HostSay::CanHear", "HostSay::Say", "EntityHandle::get_None", "EntityHandle::op_Equality", "EntityHandle::op_Inequality",
        "DeathNoticeFacts::get_Victim", "DeathNoticeFacts::get_Scorer", "DeathNoticeFacts::get_Weapon",
        "PlayerHurt::get_Player", "PlayerHurt::get_Attacker", "PlayerChat::get_Speaker", "PlayerChat::get_Text", "PlayerChat::get_TeamOnly",
        "BoolConVar::.ctor", "IntConVar::.ctor", "StringConVar::.ctor",
        // The marker on the asker's own screen (§3.6): a generic seam, a place from the map's own list.
        "IPlayerMarkers::Show", "Vector::.ctor",
    ];

    private static bool IsGameCore(MethodBase m)
    {
        string name = m.DeclaringType?.Assembly.GetName().Name ?? "";
        return name.StartsWith("SourceSharp.", StringComparison.Ordinal) && !name.StartsWith("SourceSharp.Ai", StringComparison.Ordinal) && name != "SourceSharp.MapFormats";
    }

    private static string[] ChatCallsIntoTheCore()
        => [.. IlCallCensus.Calls(m => IsGameCore(m), Binding).Where(c => IsChatType(c.Caller)).Select(c => c.Callee).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void ChatCallsOnlyWhatAPlayersScreenShowsAndTheSayPath()
    {
        var calls = new SortedSet<string>(StringComparer.Ordinal);
        foreach (MethodBase m in ChatMethods())
        {
            foreach (MethodBase callee in IlCallCensus.CalleesOf(m))
            {
                if (IsGameCore(callee))
                    calls.Add(Who(callee));
            }
        }

        Assert.Contains("HostSay::Say", calls);
        Assert.Empty(calls.Except(Allowed));
    }

    private static IEnumerable<MethodBase> ChatMethods()
    {
        const BindingFlags every = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (Type t in Binding.GetTypes().Where(t => t.FullName!.StartsWith("SourceSharp.Ai.Hl2mp.Chat.", StringComparison.Ordinal)))
        {
            foreach (MethodBase m in t.GetMethods(every).Cast<MethodBase>().Concat(t.GetConstructors(every)))
                yield return m;
        }
    }

    /// <summary>What decides, senses, applies or records: chat calls none of it, on either side of the core.</summary>
    [Fact]
    public void ChatCallsNoBrainCommandSenseOrRecording()
    {
        string[] forbidden = ["Brain", "AiSession", "BotCommandApplier", "Hl2mpSenses", "VisionPass", "SnapshotBuilder", "AiRecorder", "Hl2mpAiSession", "PerceivedView", "BotMemory"];
        var hits = new List<string>();
        foreach (MethodBase m in ChatMethods())
        {
            if (m.DeclaringType == typeof(ChatAskerEyes))
                continue;
            foreach (MethodBase callee in IlCallCensus.CalleesOf(m))
            {
                if (callee.DeclaringType?.Name is { } name && forbidden.Contains(name) && !(name == "BotCommandApplier" && callee.Name == "PlayerOf"))
                    hits.Add(Who(callee));
            }
        }

        Assert.Empty(hits);
    }

    /// <summary>
    /// The one reader of vision in chat (§3.6): whether this bot sees the asker, and where the
    /// asker stands, and nothing else a vision pass answers. Only the director's seam reaches it.
    /// </summary>
    [Fact]
    public void TheAskersEyesReadOnlyTheAsker()
    {
        const BindingFlags every = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        string[] calls = [.. typeof(ChatAskerEyes).GetMethods(every).SelectMany(IlCallCensus.CalleesOf).Where(c => c.DeclaringType == typeof(VisionPass)).Select(c => c.Name).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(["ReadOf", "SightOf"], calls);
        string[] callers = IlCallCensus.Callers(m => m.DeclaringType == typeof(IChatAskerEyes), Binding);
        Assert.Equal([typeof(Hl2mpBotChat).FullName!], callers);
    }

    /// <summary>The control for the two facts above: the same read sees a call the chat type really makes.</summary>
    [Fact]
    public void TheCensusSeesThePlayerLookUp()
    {
        Assert.Contains(ChatMethods(), m => IlCallCensus.CalleesOf(m).Any(c => c.DeclaringType == typeof(BotCommandApplier) && c.Name == nameof(BotCommandApplier.PlayerOf)));
    }

    /// <summary>The core's prompt builder and director have no way to the engine at all, and call no reader of the core's own truth.</summary>
    [Fact]
    public void ThePromptBuilderReadsNothingButItsFacts()
    {
        const BindingFlags every = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Type[] types = [typeof(ChatPromptBuilder), typeof(ChatFacts), typeof(ChatPersona)];
        var calls = new List<string>();
        foreach (Type t in types)
        {
            foreach (MethodBase m in t.GetMethods(every).Cast<MethodBase>().Concat(t.GetConstructors(every)))
            {
                foreach (MethodBase callee in IlCallCensus.CalleesOf(m))
                {
                    if (callee.DeclaringType?.Assembly == Core && !types.Contains(callee.DeclaringType) && callee.DeclaringType != typeof(ChatWords)
                        && callee.DeclaringType != typeof(ChatTrigger) && callee.DeclaringType != typeof(ChatHeard) && callee.DeclaringType != typeof(ChatTurn)
                        && callee.DeclaringType != typeof(ChatPrompt) && !callee.DeclaringType!.Name.StartsWith('<'))
                        calls.Add(Who(callee));
                }
            }
        }

        Assert.Empty(calls);
    }

    /// <summary>Nothing that decides reads chat: the only callers outside the chat types are the routes that show it.</summary>
    [Fact]
    public void NothingOutsideChatCallsIntoIt()
    {
        string[] callers = IlCallCensus.Callers(m => m.DeclaringType is { } t && (IsChatType(t.FullName) || IsCoreChat(t.FullName ?? "")), Binding, Core);
        string[] outside = [.. callers.Where(c => !IsChatType(c) && !IsCoreChat(c) && !c.StartsWith("SourceSharp.Ai.Chat", StringComparison.Ordinal))];
        Assert.Equal([typeof(AiBotRoutes).FullName!], outside);
    }

    /// <summary>A recording's events have no chat kind: chat is not in <c>.airec</c> (by ruling).</summary>
    [Fact]
    public void ARecordingsEventsHaveNoChat()
        => Assert.DoesNotContain(Enum.GetNames<AiEventKind>(), n => n.Contains("Chat", StringComparison.OrdinalIgnoreCase) || n.Contains("Say", StringComparison.OrdinalIgnoreCase));

    /// <summary>The chat stream is the director's own: it is built from a salted seed and never draws from a bot's.</summary>
    [Fact]
    public void ChatDrawsFromItsOwnStream()
    {
        string[] draws = IlCallCensus.Callers(m => m.DeclaringType == typeof(AiRandom) && m.IsConstructor, Binding);
        Assert.DoesNotContain(typeof(Hl2mpBotChat).FullName!, draws);
        Assert.NotEqual(Hl2mpBots.NameSalt, Hl2mpBotChat.ChatSalt);
        Assert.NotEqual(Hl2mpAiSession.BrainSalt, Hl2mpBotChat.ChatSalt);
    }

    /// <summary>The setting is off by default: nothing about chat runs until someone asks.</summary>
    [Fact]
    public void ChatIsOffByDefault()
    {
        var declared = typeof(AiChatConVar).GetCustomAttribute<SourceSharp.Abi.ConVarAttribute>()!;
        Assert.Equal(("ai_chat", "0"), (declared.Name, declared.DefaultValue));
    }
}
