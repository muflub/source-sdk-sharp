# `usercmd-decode-stock.txt`: where it came from

550 packets of user commands and what stock `ReadUsercmd` (usercmd.cpp) decoded
from each one, with the stock move-magnitude clamp applied in the delta chain.
It is the acceptance for plan_server_novalve.md P2.2, where the decode moved to
C#. `UserCmdCodecTests.EveryRecordedPacketDecodesAsStockDid` decodes every
packet with `UserCmdCodec.Decode` and requires every field of every command, and
the bit the packet ends at, to match **bit for bit** (floats are compared as
their bit patterns).

## The two sections

- **real**, 250 packets of live traffic, recorded by a temporary probe in
  `CServerGameClients::ProcessUsercmds` during a `make live-gate` run: every
  packet whose commands carry an impulse, plus a seeded sample of the rest
  (14,289 were recorded). The gate's bot client sends no weapon selection or
  mouse deltas, which is why the second section exists.
- **synthetic**, 300 seeded random bitstreams (xorshift32, seed 0x2545F491)
  decoded by the same stock `ReadUsercmd` and clamp, at a limit of 1000. Random
  bits set every presence bit sooner or later, so these reach the weapon
  select, weapon subtype, mouse deltas, impulse and clamp paths real traffic did
  not, with extreme float values as well.

Both probes were temporary and never committed: they ran against the build
whose ProcessUsercmds still decoded natively, and the commit that switched the
decode to C# is the first one with this file in it. `ReadUsercmd` itself left
in the commit after.

## Format

```
P numcmds totalcmds startbit endbit maxmove packetbytes
C command tick pitch yaw roll fwd side up buttons impulse weaponselect subtype seed mousedx mousedy
```

`maxmove` and the six float fields are IEEE-754 bit patterns in hex.
`packetbytes` starts at byte 0 of the engine buffer and runs to the byte holding
`endbit`. The `C` lines follow their `P` line in array order, newest command
first, as the packet orders them.
