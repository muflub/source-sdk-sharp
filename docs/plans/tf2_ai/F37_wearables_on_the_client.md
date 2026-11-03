# F37: wearables drawn on the client (hats; not for a lane)

Base: `dae055998`. Size L. Fit: **stronger model**. Depends on nothing;
the looks-by-difficulty draw (main plan C.3.3) shows nothing until this
lands, and F38 is independent of it.

## Why not a lane

Rendering with visual proof: a hat is a bone-merged model drawn on the
player (`EF_BONEMERGE`/`FASTCULL` in stock's `CEconWearable`; the deferred
row under area `wearables` in `docs/plans/tf2/deferred.tsv` names "client
drawing: the bone-merge render of a tf_wearable (EF_BONEMERGE/FASTCULL),
the per-item team skin override"). The server side exists
(`src/tf2/server/Player/TFWearables.cs:58`, `TFWearableEntity.cs`,
`src/tf2/shared/Player/TFWearableNetState.cs`); the client has nothing
(`grep -rln Wearable src/tf2/client src/tf2/ui` is empty). Lanes have
misdescribed what their screenshots show (memory
`lane-jpeg-claims-unreliable`); the proof here is a picture compared
with stock's, opened by a person or a stronger model, plus the
command-readable half below.

## What it must deliver

- A client entity for `tf_wearable` that bone-merges its model to its
  owner, with the team skin, drawn in the owner's render pass and hidden
  with the owner (the deferred row's items), and gone when the owner dies
  (the body keeps its own look: F08) or the item is removed.
- Command-readable proof: the dev API's entity list for a client shows
  the wearable with its model name and owner index; the render trace
  (`SOURCESHARP_RENDER_TRACE_MESHES=1`, which `lane-boot.sh` sets) counts
  one mesh draw per wearable per frame. Then the picture: a Scout with a
  stock-schema hat, ours beside stock's (`stock_tf.sh`), as a montage.
- Facts: the client entity composes (`TF2MenuCompositionTests`-style over
  the real `ClientModule`, memory `tf2-carrier-pitfalls` item 3); the
  wearable's net state fits the tier; `MISPREDICTION` count unchanged.
- The `deferred.tsv` row deleted in the landing commit.
