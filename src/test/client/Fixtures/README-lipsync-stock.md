# `lipsync-stock.tsv`: where it came from

Stock's viseme pass, the arm of `public/sentence.cpp` the client lost when
C_BaseFlex left with the managed entities (docs/client-novalve-rulings.tsv rules
sentence.cpp `port`): `C_BaseFlex::ProcessVisemes` and what it calls
(`AddVisemesForSentence`, `AddViseme`, `SetupEmphasisBlend`,
`ComputeBlendedSetting`, `AddGlobalFlexController`, `SetupMappings`), over
`CSentence::GetIntensity` and the runtime phonemes `CSentence::
CacheRestoreFromBuffer` fills. Written by
`src/test/oracle/native/lipsync_oracle_test.cpp`, which compiles pristine
`sentence.cpp` whole and the viseme functions cut verbatim from pristine
`c_baseflex.cpp` (the oracle CMake names the markers). `LipSyncOracleTests`
replays it against `src/client/managed/LipSync`.

The scenario is seeded (xorshift32, seed `0x1F5A3C07`): 120 entities, 48
frames each. What the harness stands in for (the entity's members, the model's
flags, the scene file lookup, the engine's sentence answers) is listed in the
test's header; none of it is a decision of the pass.

To regenerate (only when the scenario changes, and say why in the commit):
touch `src/test/oracle/native/lipsync_golden.regen`, run the oracle test,
delete the marker.

## The rows

Floats are their IEEE bits in hex (`%08x`). Rows of one entity follow its `C`.

| tag | columns |
|---|---|
| `C` | case, has a model (0: GetModelPtr is NULL), the model's flags, the accumulated bone mask, phonemesnap, phonemedelay, phonemefilter |
| `F` | a phoneme class file the scene lookup finds: its name (`phonemes_weak`, `phonemes`, `phonemes_strong`) and the `.vfe` image in hex; a class with no row is not found |
| `V` | voice source slot, ignores phonemes, has a sentence (0: GetSentence answers NULL), the sentence length GetSentenceLength answers, the elapsed time at frame 0 |
| `P` | one runtime phoneme of the voice above: code, start, end |
| `E` | one emphasis sample of the voice above: time, value |
| `T` | frame, then each voice's elapsed time this frame |
| `I` | voice, `GetIntensity( elapsed - phonemedelay, length )` for each voice the pass reaches GetIntensity with (diagnostic) |
| `B` | the first 12 global flex weights before the pass (what the frame's earlier work left) |
| `W` | global flex controllers registered so far, then the first 12 global flex weights after the pass |
