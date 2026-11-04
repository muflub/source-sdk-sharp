# The admin menu

`adminmenu` (F4 by default) opens a radial menu of what an admin may do on the
server: change the map, the game mode and the game's settings (friendly fire, time
limit and so on), kick a player, restart the game, and what mods add (`IAdminMenuProvider`: the AI's bots). Menus nest; the server builds them
and a choice goes back as an id, so nothing a client sends is ever a command.

## Who is an admin

- The host of a listen server (slot 1, never on a dedicated server).
- A player who proves a key listed in the server's `cfg/admins.yaml`:

```yaml
admins:
  - name: alice
    key: MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...
```

The file is read at each use: an edit needs no restart, and taking a key off ends
its rights at once. A file that does not read grants nobody, and the server's log
says why.

A player's key is ECDSA P-256, made under Options, Advanced or with
`ss_admin_keygen`, kept at `~/.local/share/SourceSharp/admin_key.pem` (mode 0600;
refused if others can read it). `ss_admin_pubkey` prints the public half, which is
also in `admin_key.pem.pub`.

A server behind NAT, whose players connect to an address its interfaces do not
show, sets `sv_admin_address` to the `ip` or `ip:port` they use.

## The challenge

1. The player presses the key; the client asks the server.
2. The server sends a 32-byte random nonce, for this slot, good for 30 seconds.
3. The client signs `"SourceSharp admin v1\n" + nonce + "\n" + address`, the address
   being the one it connected to, and sends the public key, signature and address.
4. The server checks the key is listed, the address is its own and the signature
   verifies, and sends the menu. Rights last for the connection.

What this stops:

- **Replay.** A nonce answers once, right or wrong, and leaves with its player, so
  a recorded proof names a nonce the server no longer holds: on this connection, a
  later one, another slot, or after a restart.
- **A malicious server relaying a challenge.** The signature names the address the
  victim connected to, which is not the real server's.
- **A server collecting signatures.** The client signs only just after its player
  pressed the key, and once per press.
- **Guessing.** Three failures lock the slot out of trying for 60 seconds, across
  reconnects.

What it does not stop: two servers that share an address (the same private LAN
address and port on two networks) cannot be told apart by the address alone. Traffic
after the proof is protected only as the engine's connection is.
