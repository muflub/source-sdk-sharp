# vendor/

The Valve Source SDK 2013 files that server.so and client.so build from. They
came from upstream ValveSoftware/source-sdk-2013 at the commit in
`valve-sdk.lock`, under the Source 1 SDK License (`LICENSE-VALVE-SDK`), and
are this repo's own from then on: trimmed to what the modules use.

| Directory | Holds |
|---|---|
| `shared/` | files both sides read |
| `server/` | files only the server side reads (server.so, libss_fwd_server) |
| `client/` | files only the client side reads (client.so, libss_fwd_client, libss_particles) |

Below each directory a file keeps its Valve path, less the leading `src/`.
The tests may still build against the fork (`farm/<config>/src`).

`tools/vendor` measures what the module builds read (the build's dependency
logs, a Windows preprocessing pass and the link lines) and fails on a vendored
file nothing reads or a file under the wrong side. `tools/vendor --write`
moves or deletes files to match.

## Edited files

Most files here are Valve's text as the pristine SDK has it, trimmed. These
carry SourceSharp's edits, because the stock versions build the client's
entity class (`c_baseentity.h`) into every translation unit that reads them,
and client.so has no entity class (its entity face is `client_entity.h`, our
own). Each is the fork's edited copy, moved here 2026-10-25 when client.so
stopped reading the fork; a line at its top says so.

| File | What differs from stock |
|---|---|
| `client/game/client/tempent.h` | `C_LocalTempEntity` over our `CShellRenderable`, not `C_BaseEntity` |
| `client/game/client/c_te_legacytempents.h` | the temp entity manager's interface without the entity types |
| `client/game/client/prediction.h` | `CPrediction` with the managed prediction's state, no entity includes |
| `client/game/client/c_recipientfilter.h` | the recipient filters over `IClientEntity`, not `C_BaseEntity` |
| `client/game/client/c_rope.h` | `C_RopeKeyframe` over our `CShellRenderable` |
| `client/game/client/c_env_fog_controller.h` | `DECLARE_CLASS_GAMEROOT` in place of `DECLARE_CLASS`: its base, `C_BaseEntity`, is our entity face, which declares no `CheckDeclareClass` for an MSVC debug build to validate against |
| `shared/public/tier0/platform.h` | `SS_ABI_PICK( msvc, itanium )`, for the byte stubs `tools/vendor-fields` writes where the two record layouts disagree |
