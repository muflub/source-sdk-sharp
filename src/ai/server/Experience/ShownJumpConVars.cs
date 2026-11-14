using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// <c>ai_experience_jump_standin</c> (lane G6a): a bot's name, or <c>*</c> for every bot, whose jumps the experience feed
/// takes as a person's. For proving and measuring the jump watch with no person at the keyboard; a bot is never the source
/// of a learned jump otherwise. Off (empty) by default; a cheat.
/// </summary>
[ConVar("ai_experience_jump_standin", "", Flags = ConVarFlags.Cheat,
        Help = "A bot's name, or * for every bot, whose jumps the experience feed takes as a person's (proof and measurement only); empty for none")]
public sealed class AiExperienceJumpStandInConVar : StringConVar { }

/// <summary>
/// <c>ai_experience_jump_standin_links</c> (lane G6a): 1 judges a stand-in's jump against the level's links as a person's
/// is (one the level already has is thrown away); 0, the default, as on a level whose finder found none of its jumps,
/// since a bot can fly only the level's links. A cheat.
/// </summary>
[ConVar("ai_experience_jump_standin_links", "0", Flags = ConVarFlags.Cheat,
        Help = "1: a stand-in's jump is judged against the level's links as a person's is; 0: as on a level with none")]
public sealed class AiExperienceJumpStandInLinksConVar : BoolConVar { }
