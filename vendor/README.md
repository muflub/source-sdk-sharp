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
