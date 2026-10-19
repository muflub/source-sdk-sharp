# `savegame-stock.tsv`: where it came from

Stock's save field codec: `CSave::WriteFields` and `CRestore::ReadFields`
(game/shared/saverestore.cpp), which encoded every block the engine hands a
game DLL through `SaveWriteFields` / `SaveReadFields`: the client state file's
`DECALLIST` and `MUSICLIST`, and the server's `GameHeader`, `Save Header`,
`ADJACENCY` and `LIGHTSTYLE`. Written by
`src/test/oracle/native/savegame_codec_oracle_test.cpp`, which compiles the
field path cut verbatim from the pristine `saverestore.cpp` (the oracle CMake
names the markers) over the pristine `saverestore.h`, `isaverestore.h`,
`saverestoretypes.h` and `datamap.h`. `SaveCodecOracleTests` replays it
against the managed codec (`src/shared/interop/SaveBlockCodec.cs`).

The maps are the engine's own (`M` rows) plus writer variants of
`decallist_t` and `musicsave_t`, each written by stock and read back by stock
under the real map. The values are seeded (xorshift32, seed `0x5A7E0C0D`).

To regenerate (only when the scenario changes, and say why in the commit):
touch `src/test/oracle/native/savegame_golden.regen`, run the oracle test,
delete the marker.

## The rows

Bytes are lowercase hex. A level is three columns: the base time's IEEE bits
(`%08x`), whether the landmark is used (`1`/`0`), and the landmark's three
components' bits, comma separated. A token table is `slot:name` pairs, comma
separated, in slot order (0xfff slots).

| tag | columns |
|---|---|
| `M` | map, struct bytes, fields as `name:fieldtype:count:offset` separated by `;` |
| `W` | case, map, block, level, the struct's bytes, the bytes stock wrote, the token table after the write |
| `R` | case, reader map, block, level, the struct's bytes before the read, stock's return (1, or 0 for a refused block), the cursor after it, the struct's bytes after it. It reads the bytes and table of the `W` row of the same case. |
| `S` | stream case, the write level, the read level |
| `SW` | stream case, map, block, the struct's bytes: one write, in order, into one buffer and table |
| `SO` | stream case, every byte the stream wrote, the token table |
| `SR` | stream case, map, block, stock's return, the cursor after it, the struct's bytes after it (prefilled `cd`): the reads, in order |
