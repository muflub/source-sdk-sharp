# `vstdlib-draws-stock.tsv`: where it came from

What the game's own `libvstdlib.so` (Source SDK Base 2013 Multiplayer,
`bin/linux64/libvstdlib.so`, sha1 prefix `d32356cdbeff3cbf`) draws after
`RandomSeed(seed)`: eight `RandomInt(0, 9999)`, eight `RandomFloat(0, 1)` (as
IEEE bits), and a mixed run (`RandomInt(-3, 3)`, `RandomFloat(-10, 10)`,
`RandomInt(0, 1)`, `RandomFloat(0.5, 0.75)`), each run from a fresh reseed.
`UniformStreamOracleTests` requires both sides' `UniformStream` to draw the
same values.

The seeds are edge cases (0 and 1 must agree: stock clamps a non-positive
state to 1; a negative; `INT_MAX`) and six of TF's crit-roll shape,
`MD5_PseudoRandom(command_number) & 0x7fffffff ^ (weapon entindex << 8 |
player entindex)` (`CTFWeaponBase::CalcIsAttackCriticalHelper`,
`src/game/shared/tf/tf_weaponbase.cpp`; `WEAPON_RANDOM_RANGE` is 10000).

Recorded 2026-10-25 by loading the library with Python's ctypes (it exports
`RandomSeed`, `RandomInt` and `RandomFloat`, the symbols
`vstdlib_random_oracle_test` loads):

```python
import ctypes, hashlib, struct, sys
lib = ctypes.CDLL(sys.argv[1])   # with LD_LIBRARY_PATH at its bin/linux64
lib.RandomSeed.argtypes = [ctypes.c_int]
lib.RandomInt.argtypes = [ctypes.c_int, ctypes.c_int]; lib.RandomInt.restype = ctypes.c_int
lib.RandomFloat.argtypes = [ctypes.c_float, ctypes.c_float]; lib.RandomFloat.restype = ctypes.c_float
def cmd_seed(n):
    d = hashlib.md5(struct.pack('<I', n & 0xffffffff)).digest()
    return struct.unpack('<I', d[6:10])[0] & 0x7fffffff
def bits(f): return struct.unpack('<I', struct.pack('<f', f))[0]
seeds = [0, 1, -5, 12345, 0x7fffffff]
for cmd, weapon, player in [(1, 0x500, 1), (2, 0x500, 1), (7, 0x500, 1), (7, 0x501, 1), (7, 0x500, 2), (1000, 0x1ff, 24)]:
    seeds.append(cmd_seed(cmd) ^ ((weapon << 8) | player))
print("# seed\tkind\tmin\tmax\tdraws (ints, or float bits in hex), each after RandomSeed(seed)")
for s in seeds:
    lib.RandomSeed(s)
    print(f"{s}\tint\t0\t9999\t" + " ".join(str(lib.RandomInt(0, 9999)) for _ in range(8)))
    lib.RandomSeed(s)
    print(f"{s}\tfloat\t0\t1\t" + " ".join(f"{bits(lib.RandomFloat(0.0, 1.0)):08x}" for _ in range(8)))
    lib.RandomSeed(s)
    m = [lib.RandomInt(-3, 3), lib.RandomFloat(-10.0, 10.0), lib.RandomInt(0, 1), lib.RandomFloat(0.5, 0.75)]
    print(f"{s}\tmixed\t-\t-\t{m[0]} {bits(m[1]):08x} {m[2]} {bits(m[3]):08x}")
```
