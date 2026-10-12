# The icon set

Original vector art for the UI (user ruling 2026-10-05): the kill feed's weapons
and the HUD's marks. Nothing here is traced from Valve's glyphs or sprites.

## Style rules

- **One file per icon**, named as the code names it (`smg1.svg`, `skull.svg`).
- **The height grid is 24 units**: `viewBox="0 0 W 24"`, any width. The art
  sits between y 2 and y 22; long weapons are wider, never shorter.
- **Silhouettes, side on, facing right**: a muzzle points right, as the kill
  feed reads killer → victim. Symbols (skull, explosion, ball) face the viewer.
- **One fill, no strokes**: a single `<path fill="#fff" fill-rule="evenodd">`.
  Colour is the UI's (the backend tints the white).
- **Details are cut-outs 2 units wide** (even-odd holes): a trigger guard, an
  ejection port, a barrel's hoops. Nothing thinner, so the 16 px copy still
  reads.
- **Straight edges on whole or half units**; curves only where the object is
  round (a revolver's grip, a crowbar's hook, a ball).

## The atlas

`make ui-icons` rasterises every SVG with SkiaSharp at each of
`UiIconSet.PixelHeights` into `atlas/icons.a8z` (alpha, zlib) and
`atlas/icons.tsv` (name, height, x, y, w, h), and a PNG of it under
`bin/<config>/ui-preview/icons-atlas.png`. Commit the atlas with the art;
`UiIconAtlasTests` fails when it is stale. The toolkit embeds both: the SVG
backend draws the art, the Skia and vgui backends draw the atlas.

Add an icon: draw the SVG to these rules, `make ui-icons`, look at the
`Icons` gallery sheet (`make ui-preview PNG=1`, Mockups tab), commit all three.
