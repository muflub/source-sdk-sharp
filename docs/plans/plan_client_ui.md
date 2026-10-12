# plan_client_ui.md: the managed UI framework, forms, and mock-ups first

Status: APPROVED by the user 2026-10-04 ("UI plan is good"), decisions 1–7 as
written. This expands `plan_client_novalve.md` §3.2 and phase C3 into a
buildable plan. It changes the C3 order so that **the first thing you get is
a way to mock up screens offline** (scoreboard, HUD, team menu), before any
engine wiring. Where this plan and §3.2 differ, the difference is named and
needs your ruling (§9).

## 0. What you can do after each milestone

| milestone | you can |
|---|---|
| **U1 (mock-ups)** | write a screen in C# under `src/client/ui/`, run `make ui-preview WATCH=1`, and see it in the browser (dev web UI, *Mockups* tab) at 4:3, 16:9, 21:9 and portrait with sample data, re-rendered on every save in about a second. No game, no native build. |
| U2 | the same screens drawn in game by the existing HUD panel; the current HUD (health, crosshair, weapons, damage, messages, hints, build stamp) re-written on the toolkit with its SVGs as goldens |
| U3 | menus: mouse/keyboard input, focus, the scoreboard on TAB, the team menu clickable |
| U4 | forms: options-style dialogs bound to convars with Apply/OK/Cancel |
| U5 | UI-only hot reload: save a screen file, see it change in the running game in under a second, open panels kept |

## 1. Where it stands (main c6c53802)

- The whole managed HUD is `Hud.cs` (315 lines), `HudMessages.cs` (411),
  `HudHints.cs` (190), `HudBuildStamp.cs` (213): immediate-mode, pixel
  coordinates, drawn through `IEngineHud` (`DrawRect`, `DrawText`,
  `MeasureText`) over six `IShimHudClient` slots.
- Native: `shell_hud.cpp` (505 lines) is one `CHudElement`+`vgui::Panel` whose
  `Paint` calls `IManagedHudClient.OnHudPaint(w, h)`. **One font**: the first
  of a fallback list (`Trebuchet24`, …) from the scheme; no font choice, no
  textures, no scissor, no input.
- No retained UI state, no layout engine, no widgets, no tests that look at
  what is drawn (tests read seams like `DamageFraction`).
- Scoreboard data exists on the client: `Standalone/ClientPlayerResource.cs`
  (names, frags, deaths, ping, team) from `PlayerResourceNetState`.
- The dev web UI (`src/webui`) is React + TS with tabs and a vitest leg: the
  natural home for the mock-up viewer.

## 2. Shape in one paragraph

Screens are C# classes that, each frame, **describe** themselves into a
`UiFrame` builder (immediate mode: no widget objects to keep in sync, state
lives in the screen's own fields). The builder runs layout (anchors + design
units, stacks, grids, tables) and emits a flat **render list** of value-type
primitives. A backend draws the list: `VguiBackend` in game, `SvgBackend` in
tests and mock-ups. Input arrives as events into the same frame (hit-test
against last frame's layout ids). Forms are a thin layer over widgets that
binds fields to a model (convars, settings) with dirty tracking and
Apply/Cancel. Everything above the backend is pure C# with no engine, so it
is unit-testable and previewable.

```
 screen (IUiScreen)  ─describe→  UiFrame (layout, ids, hit-test)  ─→  RenderList
        ↑ input events                                                  │
        └──────────── UiLayer (open screens, focus, z-order) ───────────┤
                                                   VguiBackend ◄────────┤  (game)
                                                   SvgBackend  ◄────────┘  (preview, goldens, /ui)
```

## 3. Ruled: our own toolkit, not VellumUI

**User ruling, 2026-10-04: "make our own framework, make sure it's easy for
agents to make great looking UIs."** The C3 spike's VellumUI arm is dropped.
§5.8 is the second half of that ruling. The reasons it was recommended:

| need | VellumUI 1.1.0 (nuget, 2026-05-09, 2.2K downloads) | in-house |
|---|---|---|
| anchors + height-scaled design units (§3.2 ruling: no pixel API for mods) | pixel/panel-stacking layout; would be wrapped | the core of the layout |
| vgui fonts (scheme fonts, engine rasterised) | ships its own TrueType rasteriser + atlas; routing text to vgui means replacing its text path | text is a primitive with a font id from day one |
| zero allocation per frame (the HUD rule today) | lambda-per-panel API (`static` lambdas avoid captures, but state is threaded through generics) | ref-struct builder, ids by hash, no closures |
| no statics surviving a UI-ALC reload | unknown, would need an audit per version | ours, gated by the existing statics-zero tooling |
| render list we can serialise for `/ui`, goldens, fidelity | its `IRenderer` is an immediate callback interface | the list *is* the contract |
| maintenance | one maintainer, young | ~3–4K lines we own |

Vellum's **shape** (immediate-mode, `Ui` → `IRenderer`) is kept.

Also ruled out of the first milestones: the **web arm** (§3.2's Steam HTML
surface). It stays a later spike for menus only; nothing in U1–U5 depends on
it, and the render list keeps the option open (a page could report its DOM
as the same `/ui` JSON).

## 4. Assemblies and where code lives

As §3.2 rules (interface assemblies are data-only):

| assembly | path | contains | reloads |
|---|---|---|---|
| `SourceSharp.Client.Interface` | `src/client/interface` | `IUiLayer`, `IUiBackend`, `IUiScreen`, `ScreenInfo`, render-list records (`UiQuad`, `UiTexturedQuad`, `UiGlyphRun`, `UiLine`, `UiScissor`), `UiAnchor`, `UiUnit` enums, `UiInputEvent` | never |
| `SourceSharp.Client.Ui.Toolkit` | `src/client/ui/toolkit` (new) | `UiFrame` builder, layout, widgets, forms, theme, `SvgBackend` | with UI |
| `SourceSharp.Client.Ui` | `src/client/ui/screens` (new) | the game's screens + each screen's sample states | with UI |
| `SourceSharp.Client` | `src/client/managed` | `VguiBackend`, `UiHost` (implements `IManagedUiClient`, owns the ALC, forwards to the current layer), data sources (`IScoreboardSource` over `ClientPlayerResource`) | full reload |
| tool | `tools/ui-preview` + `src/tools/UiPreview` (new console app) | loads Toolkit + Ui, renders every screen × sample × size to SVG, `--watch`, serves to the web UI | n/a |

`SvgBackend` lives in the Toolkit (not a test project) because the
`/client/{slot}/ui` route and the preview tool both need it; it has no
engine dependency.

Screens never see the engine. What a screen needs from the game comes in by
constructor injection as a **data interface** declared in
`Client.Interface` (e.g. `IScoreboardSource`, `ILocalPlayerHud`), which the
core implements in game and the sample states implement in preview. This is
the rule that makes mock-ups possible and is the one most worth your review.

## 5. The toolkit

### 5.1 Units, anchors, screen

- `ScreenInfo { int W, H; float Scale; Insets Safe; }`, read each frame,
  never cached. `Scale = min(H / 1080, W / 1280) × ui_scale` (`ui_scale`
  convar, default 1). **User ruling 2026-10-05** (the U1 review: "skinny
  resolution should have smaller panels laid out properly"): pure height
  scaling made the portrait HUD huge; the width term keeps 4:3 and wider
  height-driven and shrinks skinnier screens. It is W / 1280, not W / 1920,
  because W / 1920 left portrait body text at 10 px and never crossed a
  breakpoint (the design width stayed at 1920 du); against 1280 portrait
  keeps 0.84× text and a 1280 du design width. Screens also get a layout
  **breakpoint** from the design width (`W / Scale`): below a threshold
  (`NarrowBelowDu`, 1440 du; 1024x768 is exactly 1440 and keeps the regular
  arrangement) a screen switches to its narrow arrangement (stacked HUD
  groups, one-column scoreboard), chosen in the screen, never by stretching.
- **Design units** (`du`): 1 du = 1 px at 1080p, ×`Scale` otherwise. Every
  public size/offset is du. Fonts are du heights. There is no pixel overload
  (§3.2 ruling); the backend rounds to whole pixels at emit time, snapping
  rect edges (not sizes) so neighbours do not gap or overlap.
- **Anchors**: 9 points (`TopLeft` … `Centre` … `BottomRight`) on the parent
  and on the element. `Place(parentAnchor, selfAnchor, offset, size)`. A
  wider screen adds space between anchored groups; nothing stretches unless
  asked (`Size.Fill`, `Size.Percent(n)` of the parent).

### 5.2 Layout containers (enough for scoreboard, HUD, menus, forms)

| container | for |
|---|---|
| `Anchored` | HUD corners, crosshair, centre print |
| `Stack(Vertical/Horizontal, gap, align)` | menus, button rows, hint lines |
| `Grid(columns as du / fr / auto)` | forms (label column + field column) |
| `Table<TRow>(columns, rows, sort, group)` | scoreboard (per-team groups, header row, sorted by frags), server browser later |
| `Scroll(id)` | long lists; the scroll offset is per-id state kept by the layer |
| `Panel(style)` | background, border, padding, title bar |

Layout is two passes inside one frame (measure, then arrange) over a
per-frame node arena (pooled `struct` nodes, reset each frame: zero GC).
`auto` sizes measure text through the backend's font metrics.

### 5.3 Widgets

Label, Icon (texture id + sub-rect), Image, ProgressBar/Meter (health,
armour, ammo), Button, Toggle/Checkbox, Slider, Dropdown, TextField
(single-line, caret, selection, IME later), KeyBind capture, Tabs, Tooltip,
Avatar (Steam avatar via the managed flat API, a later data source; preview
draws a placeholder). Each returns a small result struct
(`.Clicked`, `.Changed`, `.Hovered`, `.Focused`).

### 5.4 Text and fonts

- A `UiFont` is `(family, du height, weight, flags)` from the theme. The
  backend maps it to a real font: `VguiBackend` asks the scheme surface for a
  font at `round(du × Scale)` px and caches by (name, px), invalidated on
  `ScreenSizeChanged`; `SvgBackend` uses the recorded metrics.
- **Metrics in preview** (from §3.2): `/client/{slot}/ui/fonts` records each
  font's height, ascent and advance table from the running game into
  `src/test/client/Fixtures/ui-fonts.tsv`. Until that is recorded (it needs
  U2's native slots), the preview uses metrics measured from the same TTF
  files the scheme names (`resource/*.ttf` / the system Trebuchet), so mock-ups
  are close from day one and exact after the first recording.
- Localisation: `#Token` strings resolve through `HudLocalise` in game and
  through `mod_sharp_english.txt` (parsed, same file) in preview.

### 5.5 Theme

One `UiTheme` record: colours, font roles (`Title`, `Body`, `Number`,
`Small`), paddings, corner radius (drawn as textured quads with a 9-slice
texture, since vgui has no rounded rects), opacity for HUD-over-world.
Code-defined with a data override: `game/mod_test/resource/ui-theme.yaml`
read on load and on change, so colour/size tweaks need no rebuild. Mods
supply their own theme through DI.

### 5.6 Input, ids, focus

- Ids: each interactive widget has an id = hash(parent id, label or explicit
  key). Hover/active/focus live in the layer by id between frames; nothing
  else does.
- Events (`MouseMove`, `MouseDown/Up`, `Wheel`, `Key`, `Char`,
  `FocusLost`) queue between frames and are consumed during describe, hit
  tested against **last frame's** arranged rects (standard immediate-mode
  lag of one frame, invisible at 60 fps).
- Keyboard/controller navigation: Tab/Shift-Tab and arrows walk focusable
  ids in describe order; Enter = click; Esc = the screen's `Back`.
- Mouse capture: a screen declares `WantsMouse`; the layer turns the cursor
  on/off through the shim (`SetMouseInputEnabled`) only on change.

### 5.7 Screens and the layer

```csharp
public interface IUiScreen            // Client.Interface
{
    string Name { get; }               // "scoreboard", used by /ui and previews
    UiLayerKind Layer { get; }         // Hud (no input) | Menu (input)
    bool Visible { get; }
    bool WantsMouse { get; }
}
// Toolkit: what a screen implements to draw
public abstract class UiScreen : IUiScreen
{
    public abstract void Describe(ref UiFrame ui);
}
```

`UiLayer` owns the screens (from DI), sorts by layer and z, runs describe for
visible ones, gives input to the top menu screen, and hands the list to the
backend. Opening/closing is by name (`ui.Open("scoreboard")`), so console
commands (`+showscores`) and the server's VGUI user messages
(`VGUIMenu` "team", "info"/MOTD) map one to one.

### 5.7a Sub-panels (user question, 2026-10-04)

Every screen is a tree of sub-panels, and a sub-panel is the unit of reuse,
preview, toggling and mod extension.

- **Nesting.** Any container nests to any depth. A child lays out in its
  parent's rect: anchors, `Fill` and `Percent` are relative to the parent.
  `Panel` and `Scroll` push a clip rect intersected with the parent's
  (emitted as `UiScissor` push/pop), so a child never draws outside its
  ancestors.
- **Reusable sub-panels.** `UiPanel<TProps>` is a class with its own
  fields and `Describe(ref UiFrame ui, in TProps props)`. A parent embeds it
  with `ui.Sub(key, panel, props)`, which pushes an **id scope**, so two
  instances (two team tables, two player cards) never share hover, focus or
  scroll state. Parent-owned instances keep state in their fields; for lists
  of anonymous instances, `ui.State<T>(id)` gives pooled per-id state held
  by the layer. A sub-panel has its own samples, its own goldens and its own
  Gallery entry, so it is previewed and linted alone before it is placed.
- **Windows** (user ruling 2026-10-05: "the console and server browser are
  also on user resizable windows", as stock's vgui Frames are). `ui.Window`
  on the overlay machinery: a title bar to drag, a close button, resize from
  every edge and corner (grip at the bottom right), min and max in du, raise
  on click with the z-order kept by the layer, Esc closes the window on top,
  pointer shapes in the render list. Placements in du from the screen's
  top-left, kept by `UiWindows` (carried across a UI-only reload) and saved
  through `IUiWindowStore`; clamped so the title bar stays reachable after a
  resolution change. Layout inside answers to the window's size: `ui.Narrow`
  is per window, against the window's own breakpoint.
- **Overlays.** Dropdown lists, tooltips, context menus and modal dialogs
  escape their parent's clip and z-order: `ui.Overlay(key, …)` defers them to
  an overlay pass drawn after the screen. Input hit-tests overlays first,
  top-down, and a modal blocks everything under it.
- **Page stacks.** A dialog can push and pop sub-panels (options → video →
  advanced) with Back/Esc wired. `Tabs` swaps sub-panels in place.
- **The HUD is sub-panels.** Health, armour, ammo, crosshair, damage,
  messages, hints and build stamp are each a sub-panel of the HUD screen.
  Each one can be toggled by its own convar (and stock's `hidehud` bits),
  and the theme file can re-anchor it.
- **Extension slots for mods.** A screen declares named slots
  (`ui.Slot("scoreboard.columns")`, `ui.Slot("hud.bottom-left")`). A mod
  contributes through DI (`IUiSlotContributor`, ordered by `mods.yaml`
  listing order) and describes into the slot's rect. Descent can then add a
  HUD element or a scoreboard column without forking the screen. The slot
  list is part of each screen's public surface, documented in the authoring
  guide.
- **Seen by the instruments.** `/client/{slot}/ui` reports the sub-panel
  tree (key, type, rect, clip, state ids). `ui-lint`'s overlap rule compares
  siblings within one parent only. A UI-only reload carries per-id state for
  every sub-panel.
- **Facts.** A nested clip fact (a child drawn past its parent's edge is
  clipped in every backend). An id-collision fact (two instances of one
  sub-panel keep separate hover and scroll). An overlay fact (a dropdown over
  a scroll region gets the click; a modal blocks the screen below). A slot
  fact (a test contributor's quad lands in the slot's rect, in listing
  order).

### 5.8 Built so agents make great-looking UI (user ruling)

Most screens will be written by agents. An agent cannot judge a design by
taste over many iterations; it can follow strong defaults, look at a picture,
and fix a list of named defects. So the toolkit makes the good choice the
easy one, makes a bad choice visible as text, and gives the agent eyes.

1. **Tokens, not values.** The public API takes design tokens: spacing
   (`Space.XS…XXL` on a 4-du scale: 4, 8, 12, 16, 24, 32, 48), type roles
   (`Text.Display, Title, Heading, Body, Label, Number, Caption`), colour
   roles (`Tone.Surface, SurfaceRaised, Text, TextMuted, Accent, Positive,
   Warning, Danger, TeamA, TeamB`), radius, elevation and motion durations.
   Raw colours and du numbers are reachable only through `Raw.*`, which the
   lint flags so a reviewer sees every escape. One curated default theme
   (`SourceSharpTheme`, designed once with you in U1, in Source's own
   vernacular: high-contrast numerals, translucent dark panels over the world,
   amber accent) carries the look; mods swap the theme, not the screens.
2. **Composites with good defaults.** Agents assemble screens from parts
   that already look right, rather than from rects: `Panel`, `Dialog`
   (title, body, button row, Esc/Enter wired), `TeamTable` (header, per-team
   groups, sorted, local player highlighted, ping colouring), `PlayerRow`,
   `HudMeter` (icon + big number + bar, low-value tone switch), `StatBlock`,
   `KeyHint` (renders the bound key as a keycap), `Toast`, `Tabs`,
   `FormGrid` (§6), `EmptyState`. Each composite has a short XML doc saying
   when to use it, and every parameter has a default that works.
3. **Animation as a value.** `ui.Animate(id, target, Motion.Quick)` eases
   to targets (health draining, panels sliding in, hover fades) with the
   theme's curves, so motion is consistent without per-screen tuning.
4. **Eyes: pictures an agent can read.** `make ui-preview PNG=1` draws
   each render list to PNG with a third backend, `SkiaBackend` (SkiaSharp,
   MIT, in-process; user ruling 2026-10-04 over headless Chrome): the
   render list straight to pixels, no SVG step, milliseconds per image, text
   through FreeType from the TTF files the scheme names, so glyphs are close
   to vgui's on Linux. Because it is in-process and deterministic, a few
   **pixel goldens** (scoreboard, HUD) sit beside the SVG goldens with a
   small per-pixel tolerance; SVG stays for readable diffs and the web
   Mockups tab. HUD screens composite over **world backdrops**: three
   checked-in dm_lockdown stills (bright, dark, busy), so contrast is judged
   over the game, not over white. An agent writes a screen, looks at the
   PNGs with its image reader, and iterates. From U2 an in-game route
   (`/ui/render?screen=&sample=` + `jpeg`) is the final check at review time,
   not part of the edit loop.
5. **A design lint that reports defects as text** (`tools/ui-lint`, also run
   as a fact over every screen × sample × size):
   text clipped or overflowing its box (unless the widget elides); element
   off screen or inside the safe insets; text contrast below 4.5:1 (3:1 for
   ≥ 24 du) against its computed backdrop, HUD text against each world
   backdrop; siblings whose edges are within 3 du but not equal (near-miss
   alignment); unintended overlap between siblings; more than 4 type roles or
   6 tones on one screen; click targets under 32 du; literal user-facing
   strings not localised (`#Token`); `Raw.*` uses. Each rule has a fact that
   plants the defect and sees it reported (mutation proof), and an output
   line an agent can act on: `scoreboard/12-players@2560x1080: TeamTable
   "Rebels" row 7 text "xXx_LongName…" clipped by 38 du; use Elide or a
   wider column`.
5a. **A Roslyn analyzer for the code shapes** (user, 2026-10-05:
   "add analysers"). `ui-lint` judges what a sample drew; the analyzer
   judges every code path at build time, and its errors land in the build
   output an agent already reads. `SourceSharp.Client.Ui.Analyzers`, applied
   to the Toolkit, the screens and every mod UI assembly, warnings as errors:
   - allocation inside `Describe` (capturing lambdas, LINQ, boxing, `new` of a
     class, `string +`, `ToString()`), aware of the toolkit's interpolated
     string handler, which does not allocate;
   - raw values in screens (literal `UiColor`, numeric literals into du
     parameters) instead of tokens; code fix: the nearest token;
   - user-facing string literals that are not `#Token`s;
   - a widget key built inside a loop that does not use the loop variable
     (rows sharing hover/focus state);
   - mutable `static` fields in the UI assemblies (they break the UI-only
     reload).
   Assembly-level rules stay facts: UI assemblies reference no core,
   engine or interop type; every screen has a `[UiSample]`; a panel's
   injected source interfaces are all read. Each analyzer rule has a
   planted-defect test and a clean case that must stay silent. Rule ids sit
   beside the `ui-lint` ones in the guide and the skill.
6. **A gallery and exemplars.** Every composite × state × theme renders into
   the Mockups tab's *Gallery* and to PNG, so an agent picks parts by
   looking. The U1 scoreboard and HUD, once you approve them, are marked
   **exemplars**: the screens new work is told to copy.
7. **A guide and a skill.** `docs/ui/authoring.md`: the rules above, recipes
   (a HUD element, a table screen, a dialog, a form), do/don't pairs with
   PNGs from the gallery, and the lint rule list. A project skill
   `.claude/skills/ui-screen/SKILL.md` encodes the loop: write the screen and
   its samples → `make ui-preview PNG=1` → look at every PNG → `ui-lint`
   clean → golden → commit with the PNG paths in the message. CLAUDE.md gets
   one line pointing at both.
8. **Measured, not assumed.** U1 closes with a trial: a fresh agent given
   only the skill and a one-line brief ("a spectator target panel: name,
   health, weapon, team colour") builds a screen; the result, its lint
   output and the iterations it needed are recorded in this plan. If it
   needed a human to look good, the defaults or the guide get fixed, not the
   screen.

## 6. Forms

A form is a screen whose body is a **model with fields**:

```csharp
sealed class VideoOptions : UiForm<VideoOptions.Model>
{
    public sealed class Model
    {
        [Field("#GameUI_Resolution")] public Resolution Mode;   // dropdown
        [Field("#GameUI_FOV", Min = 75, Max = 110)] public int Fov; // slider
        [Field("#GameUI_VSync")] public bool VSync;               // toggle
    }
    // binding: where the values come from and go to
    protected override IFormBinding<Model> Bind(IFormBindings b) =>
        b.ConVar(m => m.Fov, "fov_desired")
         .ConVar(m => m.VSync, "mat_vsync")
         .Custom(m => m.Mode, _video.Current, _video.Apply);
}
```

- Field kinds come from the C# type (bool→toggle, enum→dropdown, int/float
  with Min/Max→slider, string→text field, `KeyBinding`→capture). Attributes
  are read **once** by a source generator (no reflection per frame, no
  static caches surviving reload); a field can always be drawn by hand
  instead.
- Lifecycle: open → load from bindings into a working copy → edit (dirty
  per field) → **Apply** (write changed fields only), **OK** (apply+close),
  **Cancel** (discard). Validation per field returns a localised message
  drawn under the field; Apply is disabled while any field is invalid.
- Layout is a `Grid` of label/field rows with sections; a form can override
  the layout and still get binding, dirty tracking and validation.
- Preview: the sample state for a form is a filled `Model` plus an
  in-memory `IFormBindings`, so the mock-up shows real fields; the form's
  facts drive Apply/Cancel against a fake convar store and assert the
  writes.

Forms are what C4's GameUI dialogs (options, create server, player list)
use; U4 builds the layer and one dialog to prove it.

## 7. Mock-ups (U1): how you will use it

1. Add `src/client/ui/screens/Scoreboard.cs` (`UiScreen`) and
   `Scoreboard.Samples.cs`:
   ```csharp
   [UiSample("12 players, two teams")] static IScoreboardSource TwelvePlayers()
       => FakeScoreboard.Teams(("Combine", 6), ("Rebels", 6), seed: 1);
   [UiSample("empty server")]          static IScoreboardSource Empty() => ...;
   ```
2. `make ui-preview WATCH=1` builds the two UI assemblies (no native build),
   renders every screen × sample × size to
   `bin/<config>/ui-preview/<screen>/<sample>-<w>x<h>.svg`, and on each save
   rebuilds the UI assemblies and re-renders (target: < 2 s from save to
   picture, measured and recorded in U1's closing commit).
3. The dev web UI's **Mockups** tab (`src/webui/src/tabs/Mockups.tsx`) shows
   the grid: screen picker, sample picker, size row (1024×768, 1920×1080,
   2560×1080, 3440×1440, 1080×1920), a game-screenshot backdrop toggle (a
   jpeg from `/screenshot` or a checked-in still) so HUD mock-ups are judged
   over the world, hover to see each element's id and bounds, live reload
   over a websocket from the watcher.
4. `make ui-preview` without WATCH writes the files once; it also runs in
   `make test` as a fact (every screen × sample renders without exception, no
   quad off screen, no text overflowing its box unless the widget elides).

Mock-ups in U1: **scoreboard**, **HUD** (current elements re-done: health and
armour meters, ammo, crosshair, damage indicator, messages, hints, build
stamp), **team menu**, **centre print/chat** — enough to settle the theme
and the API before the in-game work.

## 8. Phases (replaces C3 steps 0–3 of plan_client_novalve.md)

One worktree: `../ss-client-ui`, branch `client-ui`, cut from
`client-novalve` after this review; rebased (never merged) onto main as
main moves. Per commit: build + touched tests; `make check` at each phase
close (user rule: full check only at the merge).

**U0: contract (no behaviour change)**
1. `Client.Interface` additions: render-list records, `ScreenInfo`,
   `UiAnchor`, `IUiScreen`, `IUiBackend`, `UiInputEvent`; covered by the C0
   interface-purity fact (lane *routes* is adding it).
2. Empty `Client.Ui.Toolkit` and `Client.Ui` projects in the solution, test
   project wiring (`src/test/client` gains `Ui/`), ALC-safe (no statics; the
   statics-zero tool covers both).

**U1: toolkit + SVG + mock-ups**
1. `SvgBackend` and the render list → SVG; fact: a hand-built list round
   trips to expected SVG text (golden).
2. Layout: units, anchors, Stack, Grid, Table, Panel, Scroll, nesting with
   clip intersection and id scopes (§5.7a); facts per container at the five
   sizes (positions proportional, nothing off screen,
   font du heights scale with H).
3. Text: font roles, metrics source (TTF-measured), elision, localisation
   from `mod_sharp_english.txt`.
4. Tokens and the default theme (§5.8.1); widgets display-only first
   (Label, Icon, Meter, Panel, Table), then the composites (§5.8.2) and
   `Animate`; `UiPanel<TProps>`, overlays, page stacks and extension
   slots (§5.7a), with the HUD built as sub-panels.
5. `src/tools/UiPreview` + `tools/ui-preview` + `make ui-preview [WATCH=1]
   [PNG=1]`; the sample attribute and discovery; world backdrops; the "all
   samples render" fact.
6. `tools/ui-lint` and its fact, one rule per commit, each with its planted
   defect (§5.8.5).
6a. The UI analyzer (§5.8.5a), one rule per commit with its tests.
7. Mockups tab and Gallery in `src/webui` with vitest coverage.
8. `docs/ui/authoring.md`, the `ui-screen` skill, the CLAUDE.md line.
9. Mock-ups: scoreboard, HUD, team menu, centre print, all lint-clean.
   **Checkpoint: you review the mock-ups, the theme and the authoring API;
   U2 starts on your word.**
   **PASSED** (user, 2026-10-05: "ui is good to go", after two review rounds;
   the lane's three calls stand: `Size.Du` literals are layout, SSUI002 flags
   only off-grid values, scrollbar thumbs are exempt from the target rule). The approved ones become exemplars.
10. The fresh-agent trial (§5.8.8), recorded here.
    **Done 2026-10-05** (efba7a4b). A fresh general-purpose agent got only the
    ui-screen skill, docs/ui/authoring.md and the brief "a spectator target
    panel: name, health, weapon, team colour". It wrote `SpectatorTarget`
    (+ `ISpectatorTargetSource`, a fake, five samples, one token) with no
    outside help: three preview/look/lint iterations (a floating meter, a
    dead state that changed the panel's height, both fixed by looking at the
    PNGs), every lint run `0 finding(s)`, no analyzer errors, then 174/174 UI
    facts after removing a sample that drew nothing. Its screen is committed
    unchanged. What it found, fixed in the defaults and the guide rather than
    in its screen:
    - `ui.Data` upper-cased names in Title/Heading/Label: data now keeps its
      case in every role (the scoreboard's team and server names changed;
      goldens regenerated);
    - a sample that draws nothing fails `UiSamplesTests`: the guide now says
      so, and when an empty sample is right;
    - the guide and skill listed five preview sizes, not nine;
    - pictures of a removed sample lingered: a full preview run starts clean;
    - no rule for team number to tone: `UiTeams.ToneOf` (2 Combine = TeamA,
      3 Rebels = TeamB);
    - interface granularity, screen registration, `#Token`s held in data, and
      that the analyzer runs on preview builds: now in the guide;
    - an allocation fact flaked (3 runs in 10 of the full UI suite):
      65a0dc03 measures in windows, 10/10 since.

**U2: in game, HUD layer**
1. `IShimUiClient` slots (≈14, §3.2 list: filled/outlined/textured rect,
   textured poly, line, glyph run with font, measure, scissor push/pop,
   texture create/upload RGBA, font lookup by scheme name + px, screen size,
   play UI sound) + `IManagedUiClient` (Paint per layer,
   `ScreenSizeChanged`), bound interfaces with `chain=` rows; the existing
   `shell_hud.cpp` panel becomes the HUD root panel calling the layer. The
   six `IShimHudClient` slots retire after step 3.
2. `VguiBackend` + its **draw ledger**; the `ui-fidelity` fact (same list →
   ledger vs SVG elements one to one).
3. The current HUD re-written as screens on the toolkit; old `Hud.cs`
   drawing deleted in its own commit; goldens from U1 become the check.
4. `/client/{slot}/ui` and `/ui/fonts`; record `ui-fonts.tsv`; preview
   metrics switch to it. Live: full join, `first paint`, `/ui` shows the
   HUD screens with non-zero quads; `jpeg` screenshot compared by eye with
   the SVG.

**U3: input and menus**
1. The menu root panel (focusable layer); input events forwarded;
   `SetMouseInputEnabled`/cursor slots; `ui/event` route (write) for tests.
2. Interactive widgets (Button, Toggle, Slider, Dropdown, TextField, Tabs).
3. Scoreboard on `+showscores`, team menu on the server's `VGUIMenu`,
   MOTD as a text window (HTML later, web arm). Live steps `ui-scoreboard`,
   `ui-teammenu` (inject a click through `/ui/event`, see the `jointeam`
   command arrive on the server), `ui-resize` (three `mat_setvideomode`s with
   the scoreboard open).

**U4: forms**
Source generator, `UiForm<T>`, bindings (convar, custom), dirty/Apply/
Cancel/validation; one real dialog (video or mouse options) to prove it;
facts against a fake convar store; a live step that changes a convar through
the form and reads it back through `/convars`.

**U5: UI-only reload**
`UiHost` loads Toolkit + Ui into their own collectible ALC; the file watcher
classifies the deploy; open screens, focus and scroll carried as bytes;
`/reload` reports `ui-only` with its time; the `hot-reload` step's `ui-only`
arm (scoreboard open, reload, still open, same rows, old ALC collected).

After U5 the rest of C3 (spectator GUI, loading disc, voice indicator,
debug overlay text, chat, `vgui_controls` leaving the link) continues in the
client port plan on this toolkit, and C4's GameUI dialogs are forms.

## 9. Decisions for your review

1. ~~In-house toolkit instead of a VellumUI spike~~ **RULED** (user,
   2026-10-04): our own framework, built so agents make great-looking UI
   (§5.8). The web arm stays a later spike for menus only.
   Decisions 2–7 below: **APPROVED as written** (user, 2026-10-04).
2. **Screens get game data only through data interfaces** in
   `Client.Interface`, so every screen is previewable (§4).
3. **Mock-ups before engine wiring** (U1 before U2), with a review
   checkpoint at the end of U1.
4. **Theme as code + a YAML override file** in the mod's `resource/` (§5.5).
   Alternative: code only.
5. **Forms by attributes + a source generator** (§6). Alternative: forms
   built by hand with a fluent field API only (less magic, more code per
   dialog).
6. **Preview fonts from TTF metrics until recorded from the game** (§5.4).
7. **Mock-up sizes**: 1024×768, 1920×1080, 2560×1080, 3440×1440, 1080×1920
   (adds 3440×1440 to §3.2's four).

## 10. Risks

| risk | measurement that retires it |
|---|---|
| per-frame cost of describe + layout for the HUD | U2: ms per frame for the HUD layer from `/client/{slot}/frame`, against today's `Hud.Paint`; budget row in `live-gate-budget.tsv` |
| crossings: one shim call per primitive | U2: crossings per frame on dm_lockdown; if over budget, a batched `DrawList(ptr, n)` slot (boundary design, not a retreat) |
| vgui text differs from SVG text | `ui-fidelity` ledger + recorded metrics; glyph shape remains eyeballed via `jpeg` |
| textures uploaded by `DrawSetTextureRGBA` lost on `mat_setvideomode` | C0 probe (plan_client_novalve §6 C0.7); the backend re-uploads on `ScreenSizeChanged` either way |
| an ALC reload leaks the old UI | U5's leak arm: old context collected, by `WeakReference` |
