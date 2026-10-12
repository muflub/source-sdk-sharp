# Test evictions

Test targets, and sources in a test target, that left src/test/native/CMakeLists.txt,
with the reason. This file is the only record: CMakeLists.txt keeps no comment for
what left. (Comments that explain a test still in the list stay beside it.)

## Moved from src/test/native/CMakeLists.txt's comments (2026-10-12)

Reasons that lived only in a comment at the file's old place in the source list,
moved here verbatim with the date the comment was written (git blame). Newest first.

| date | file | reason |
|---|---|---|
| 2026-10-01 | ss_ammodef_test.cpp | ss_ammodef_test.cpp rode here until the server's ammo table was deleted (plan_server_novalve.md P5): server.so read nothing from it. |
| 2026-09-29 | soundscape_registry_dict_test.cpp | soundscape_registry_dict_test.cpp WENT with its subject in plan_server_novalve.md P2.4: the server's soundscape registry (a CUtlDict<int> after CStringRegistry) is deleted and the index is the shared C# SoundscapeLibrary. |
| 2026-09-28 | mapentities_shared.cpp | game/shared/mapentities_shared.cpp STOOD HERE and is gone: plan_shared.md Phase 5 took it out of both projects and its bodies are `inline` in game/shared/mapentities_shared.h, which the test already includes. The stub cbase.h stays on the include path -- see its own header for what it is and why one translation unit needs it. |
| 2026-09-28 | entity_tools_server.cpp, shell_tools_absent.cpp, tools_absent_test.cpp | game/server/entity_tools_server.cpp is out of the server build, and the four interfaces it published are published by nothing now: Phase 1b deleted sourcesharp/game/server/shell_tools_absent.cpp, and tools_absent_test.cpp went with it in the same commit.  That test asserted four things about a file that no longer exists -- that the four EXPOSE_SINGLE_INTERFACE_GLOBALVAR lines were present, that every stub carried its TOOLS_ABSENT, that the vpc row was there, and that cs_sv_tools_absent existed. None of them is a claim about this build any more.  What the stub was hedging against was that "nothing calls IServerTools" rested on a grep over the engine's tool .dlls. It now rests on a measurement of the engine this mod loads:  strings '.../Source SDK Base 2013 Multiplayer/bin/linux64/engine.so' \ \| grep -cx VSERVERTOOLS001    -> 0   (002, 003, VSERVERCHOREOTOOLS001: 0) \| grep -cx VSERVERENGINETOOLS001 -> 1   (the control: this one IS there)  An engine cannot ask for an interface whose version string it does not contain, so there is no future caller to crash in engine code with no SourceSharp frame in the backtrace. |
