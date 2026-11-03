using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Chat;

/// <summary><c>ai_chat</c> (plan_hl2mp_ai §3.6): bots write chat lines with a local model. Off by default; nothing runs while it is off.</summary>
[ConVar("ai_chat", "0", Help = "1: bots chat, with a local model (start one: tools/ai-chat-model); 0: they say nothing")]
public sealed class AiChatConVar : BoolConVar { }

/// <summary><c>ai_chat_url</c>: the model server, on this machine only. Its default is the private instance <c>tools/ai-chat-model</c> starts.</summary>
[ConVar("ai_chat_url", "http://127.0.0.1:11435", Help = "The local model server bot chat asks, on this machine: 127.0.0.1:11435, or a URL in quotes")]
public sealed class AiChatUrlConVar : StringConVar { }

/// <summary><c>ai_chat_model</c>: the model it asks for.</summary>
[ConVar("ai_chat_model", OllamaChatWriter.DefaultModel, Help = "The model bot chat asks for")]
public sealed class AiChatModelConVar : StringConVar { }

/// <summary><c>ai_chat_interval</c>: the server's one unprompted line per this many seconds. A reply to a player has its own, shorter limit.</summary>
[ConVar("ai_chat_interval", "30", Help = "Seconds between the bots' unprompted chat lines, over the whole server", Min = 1, Max = 600)]
public sealed class AiChatIntervalConVar : IntConVar { }

/// <summary><c>ai_chat_cooldown</c>: one bot's silence after an unprompted line.</summary>
[ConVar("ai_chat_cooldown", "60", Help = "Seconds a bot stays quiet after an unprompted chat line", Min = 0, Max = 600)]
public sealed class AiChatCooldownConVar : IntConVar { }

/// <summary><c>ai_chat_reply_chance</c>: percent chance one bot answers a line in general chat that named nobody.</summary>
[ConVar("ai_chat_reply_chance", "10", Help = "Percent chance one bot answers a general chat line that names no bot", Min = 0, Max = 100)]
public sealed class AiChatReplyChanceConVar : IntConVar { }

/// <summary><c>ai_chat_timeout</c>: how long a line may take before it is dropped.</summary>
[ConVar("ai_chat_timeout", "5", Help = "Seconds a model may take over one chat line before it is dropped", Min = 1, Max = 30)]
public sealed class AiChatTimeoutConVar : IntConVar { }
