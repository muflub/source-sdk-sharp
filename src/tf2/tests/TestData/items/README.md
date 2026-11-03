# Item schema fixtures

- `items_game.trimmed.txt`: trimmed from the shipped
  `tf/scripts/items/items_game.txt` (loose file in the Team Fortress 2 install,
  8,245,712 bytes, sha256 prefix `d590563c7fae73ab`, mtime 2026-10-23), taken
  2026-10-25. Kept: the `qualities` block whole; the `default` item and every
  item whose own block says `"baseitem" "1"` (37 with `default`), plus item 441
  (a `static_attrs` block with `stored_as_integer` attributes); the prefabs
  those items name, transitively (28); the attribute definitions their
  `static_attrs`/`attributes` blocks name (27). Each kept block is copied
  verbatim; line endings are LF. Everything else (game_info, rarities, loot
  lists, recipes, ...) is dropped.
- `prefab_order.txt`: synthetic, not from the game. Three items over the same
  two prefabs in both orders, one key per layer, for the prefab merge order.
