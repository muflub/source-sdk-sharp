# Bot chat with personality: the plan

Status: committed at the user's word, not started (2026-11-03). A plan only:
nothing in it is implemented, and the open questions in 4.2 are still open. The user: "Make a plan for this for now, ill implement
later". Read at main `2576cdd7a`.

**The goal.** Today a bot has a tone from its most distinctive trait, and all bots still
sound like one person. After this, a player can tell bots apart by how they talk, and bots
talk about what really happened this match. The request, 2026-11-03: "Is there more we can
do to make the chat more personified".

**What must stay true** (rulings in `plan_hl2mp_ai.md` §12.1, design in §3.6):

- A bot says only what it knows. `ChatFacts` (`src/ai/core/Chat/ChatPrompts.cs`) has no
  position, health or other player's state, and a fact holds that by reflection. Everything
  here adds to it in the same shape.
- Factual answers stay checked (`ChatAnswerBook`, `ChatAnswers.cs:123`). A persona changes how
  an answer is worded, never its facts. The `chat-truth` table must stay at zero wrong.
- Every line goes through the output filter (`ChatOutputFilter`), including anything this plan
  adds. Player text stays delimited, capped and untrusted.
- Nothing the model says reaches a brain, a decision or a command. The game never waits.
  Off by default (`ai_chat 0`). Opponent memory is in memory only, never on disk.
- Bot names keep coming from their traits (`plan_hl2mp_ai` §3.5.1). A persona must not
  contradict a name: "Tommy Camps Alot" isn't the loud rusher.
- No human recordings or chat logs in the repo. Reports with raw model output stay in scratch,
  because main is public and raw output can hold slurs.

## 1. Where it stands

| Piece | Today | File |
|---|---|---|
| Persona | name, first name, one tone string | `ChatPersona`, `ChatPrompts.cs`; built in `Hl2mpBotChat.PersonaOf` (`Hl2mpBotChat.cs:377`) |
| Tones | 10 skills, a high and a low phrase each | `tones` in `game/mod_test/scripts/ai/hl2mp_chat.json` |
| Triggers | Kill, Death, Suicide, Revenge, Streak, Leader, Pickup, Reply, Spot, Hot, Idle, Cold, Answer | `ChatTriggerKind`, `ChatPrompts.cs:7` |
| Pace | one server-wide interval and one per-bot cooldown, the same for every bot | `ChatDirector.cs:10`, `:13`, `:48`, `:51` |
| Memory | the last few lines of a conversation; grudges and habits exist but chat only gets `spot` and `revenge` moments | `BotMoment`, `Hl2mpBots.cs:10`; `OpponentHabits.cs` |
| Evaluation | lines from real kills per model; a hundred graded questions | `tools/ai-replay chat-eval`, `chat-truth` |
| The model | `llama3.2:3b`, about 250 ms a line, on the AMD card, with a 4 GB ceiling set by us | `plan_hl2mp_ai` §3.6, `tools/ai-chat-model` |

## 2. Stages

Each stands alone and lands by itself, in this order. Each ends with `make test`, zero warnings,
its facts shown red once with `tools/ai-mutations` arms, and a sample report. Sizes are
rough guesses.

### S0 A way to measure "sounds like different people" (small)

Personality is judged by reading, so make reading easy and add one number.

- `tools/ai-replay chat-voices <console.log|eval>`: lines grouped per bot, side by side, into a
  scratch report.
- A distinctiveness score: train a trivial word and character n-gram classifier on half a bot's
  lines, then see how often it names the bot from the other half. Chance is 1/N, and today's
  score is the baseline. It's a guide, not a gate: a lower number with better lines still wins.
- `chat-eval` gains a mode that plays several personas through the same events.

### S1 Persona cards (medium; the biggest gain)

Small models copy examples better than they follow descriptions.

- New data file `game/mod_test/scripts/ai/hl2mp_personas.json`: about 10–12 hand-written
  personas (the friendly veteran, the sore loser, the keen new player, the quiet one, the
  tryhard, the joker, the grump, ...), each with a one-line character, what it cares about, and
  **5–8 sample lines** that go in the prompt as examples. Lines are ours: reviewed, clean and
  short.
- A bot draws a persona from its seed at roster entry, so it's stable across map changes and
  replays, weighted by its traits so it can't contradict its name. No two bots share one while the
  pool lasts. `ai_bot_reroll_on_map` redraws it with the bot's name.
- `ChatPersona` gains the card. `Hl2mpBotChat.PersonaOf` builds it. The prompt builder puts it in
  the system text.
- A saved custom profile may name a persona (`cfg/ai/bots/<name>.json`). The editor shows it.
- Facts: same seed, same persona; a persona's traits fit the name; no repeats while the pool
  lasts; a card's text never reaches the filter's echo check as a false refusal.
- The sample lines mustn't be spat back: a line that copies a sample is refused, as the echo rule
  already does for prompts.

### S2 A typing style, applied by code (small)

Consistent in a way a model isn't, and it also applies to factual answers.

- Part of the card: case (all lowercase, normal, the odd ALL CAPS when angry), punctuation (none,
  "...", "!!"), shortenings ("u", "ur", "tho"), habits ("lol", "bro", "tbh", at a rate), and a typo rate
  with a sometimes-sent correction line ("*shotgun").
- A pure function in core, seeded, run after the model and **before** the filter, so a style can't
  smuggle a word past it. Numbers, item names and places in a fact answer are protected from typos:
  `chat-truth` must grade the styled line.
- Facts: same seed, same text; protected words survive; the line stays under the filter's limit
  (`ChatOutputFilter.cs:56`); a correction line is one line and goes through the normal rate limit.

### S3 Some bots talk more than others (small)

- The card gives a chattiness from 0.2 to 2, scaling that bot's reaction chance and cooldown in
  `ChatDirector`. Answers to a person aren't affected, though a quiet bot is picked last.
- One chatterbox, one nearly silent, the rest between, as far as the draw allows. The server-wide
  interval still rules, so overall rate doesn't rise.
- Facts: a chatty bot speaks more over a fixed scene; the total stays within the interval; a quiet
  bot still answers when asked by name.

### S4 This match's facts (medium)

- A per-bot `ChatMatchLog` in memory: kills and deaths by opponent, last killer, streaks, weapons
  used on it, "got me with the same gun", and the grudge and habit figures that already exist.
- `ChatFacts` gains a few worded facts, chosen per trigger: "lodle has killed you 3 times", "you
  and lodle are 2-2". Only from what this bot experienced or the scoreboard shows, with a census
  naming the new reader.
- Prompt rule: use the fact if it's there. Check: a number in a line must be one we gave it, or the
  line is refused; same approach as `ChatAnswerCheck`.
- New triggers: a third death to the same person, a kill on your nemesis, the end of a long streak.
- Facts: counts are right; a stale opponent after they leave isn't used; a spectator never appears; a
  reset on map change, unless S7 lands.

### S5 Mood and social moments (medium)

- A mood per bot from its own match: cocky, fine, grumpy, resigned; moved by rank, recent deaths and
  streaks, slowly, with hysteresis. One worded line in the prompt, and it nudges S2's style (angry caps).
- New triggers: greeting a player who joins, "gg" and a word at map end, a remark on being benched and
  coming back, a player's streak or a nice kill.
- At map end the rate limit relaxes for a moment, as real players do. This fires while the intermission
  shows, so check `Intermission.cs`'s timing.
- Facts: mood moves the right way and doesn't flip per kill; one greeting per join; a bot being kicked
  says nothing after it's gone.

### S6 Real exchanges (medium)

- Today a bot may answer a bot once. Allow a short scene: two bots, up to 3 lines, sharing the topic and
  a running joke held for the match, with a low chance and a server-wide cooldown (say every 2–3 minutes).
- A human's line ends any scene at once and takes priority. Never over a person's question.
- Facts: a scene stops at its limit; a human interrupts it; no loops; the rate stays under S3's total.

### S7 Remembering across maps (small, optional)

- A roster bot keeps `ChatMatchLog` totals across a map change, in memory only, cleared on a server
  restart or when it leaves the roster: "you again", "got you last map". Fits with the roster that
  already survives map changes. A person is matched as rejoin scores will be (SteamID, name on LAN).
- Facts: survives a map change, not a restart, never on disk (a census), a re-rolled bot forgets.

### S8 A larger model, tested (small)

- On the AMD card with S1's prompts, run `chat-eval` and `chat-voices` on `llama3.2:3b` and a few 8B
  models, up to about 6–8 GB, reading the lines. Measure latency and `chat-truth`. Change the default
  only if it reads clearly better, with latency under about 1 s. Note a small `tools/ai-chat-model`
  download. **The 4 GB ceiling was our own choice, not the user's.** Don't touch the system ollama service.
- It's last on purpose: S1–S5 decide how much it's still needed. It could run as early as S1, as
  a quick check.

## 3. Risks

- **More rude lines.** Personas with attitude make models ruder, and the filter is a word list. Keep
  personas playful, not cruel; read a large sample at S1. Bots are already trash-talking.
- **Chat crowds out the player.** Everything stays under the interval; a human's line takes priority.
- **Styling hurts truth.** Protected tokens and grading the styled line cover that.
- **Prompt length and latency.** Sample lines lengthen the prompt: measure at S1. Hold first-token
  latency near today's, and trim samples first.
- **Model nonsense stays.** Personas make it charming or more obvious. Reading is the only real test.
- **TF2 later.** Keep cards, style and logs in core and mod-neutral, with data per mod. TF2's own
  lines (classes, voices) stay for its plan.

## 4. Decisions

### 4.1 Rulings (dated)

- 2026-11-03: "Is there more we can do to make the chat more personified", then "Make a plan for this
  for now, ill implement later". Scope: items 1–8 as proposed, S1–S5 first.

### 4.2 Open questions, with a recommendation

| # | Question | Recommendation |
|---|---|---|
| Q1 | Can a persona's attitude go past mild trash talk? | Mild only. Rude to play with, never cruel, and the filter's list stays. |
| Q2 | Should bots make typos and send corrections? | Yes, at a low rate for some bots. It reads as human. |
| Q3 | Can a player switch persona chat off but keep answers? | Yes: `ai_chat_style 0` gives plain answers only. |
| Q4 | Should the scoreboard or AI tab show a bot's persona? | The web AI tab yes, the scoreboard no: a player finds out by reading. |
| Q5 | Keep S7 (memory across maps)? | Yes if S4 reads well. It's cheap and very personal. |
| Q6 | Is a larger model allowed if it reads better, up to 8 GB on the AMD card? | Yes, if the game isn't affected and latency stays under 1 s. |

Citations were grepped at `2576cdd7a`, except new files and names this plan proposes (`hl2mp_personas.json`,
`ChatMatchLog`, `chat-voices`, `ai_chat_style`), and `Intermission.cs`'s timing, which was not read.
