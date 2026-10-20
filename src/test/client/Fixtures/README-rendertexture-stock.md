# `rendertexture-stock.tsv`: where it came from

Stock's client hooks into the material system's named render targets, the
whole of `game/client/rendertexture.cpp` (C7): every getter in
`rendertexture.h` (`GetPowerOfTwoFrameBufferTexture` .. `GetTeenyTexture`),
the name and group each asks `FindTexture` for, the reference it takes
(`CTextureReference`, from the pristine `public/materialsystem/
MaterialSystemUtil.cpp`), the release function it registers on the first
find, and which references that function drops. Written by
`src/test/oracle/native/rendertexture_oracle_test.cpp`, which compiles both
pristine files from the clone at `valve-sdk.lock` over a recording
material-system double. `RenderTargetCatalogOracleTests` replays it against
the managed catalog (`src/client/managed/Render/RenderTargets.cs`).

The script: every getter in the header's order (the frame buffers at indices
0 to 3, the teeny textures at 0 to 2), the same again, the release function,
every getter again, the release function again.

Rows, one per line, tab-separated:

| row | meaning |
|---|---|
| `phase NAME` | the script's step: first, again, release, after, release2 |
| `G GETTER ARG` | a getter was called; ARG is its index or `-` |
| `findtexture NAME GROUP COMPLAIN FLAGS` | `IMaterialSystem::FindTexture`'s arguments |
| `inc #N` / `dec #N` | a texture's reference count, by the order its name was first found |
| `addreleasefunc N` | `IMaterialSystem::AddReleaseFunc`, the Nth time |
| `holds #N` | the texture the getter answered |

To regenerate (only when the script changes, and say why in the commit):
touch `src/test/oracle/native/rendertexture_golden.regen`, run
`farm/debug/src/buildcpptests --oracle rendertexture_oracle_test`, delete the
marker.
