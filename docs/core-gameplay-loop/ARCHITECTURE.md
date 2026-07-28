# Architecture — Core gameplay loop (phase 2)

> Records what this phase adds or changes; the repo-wide `docs/ARCHITECTURE.md` holds the system
> baseline shared by every phase, and `docs/welcome-screen/ARCHITECTURE.md` holds phase 1's
> reasoning. Decision numbering **continues** phase 1's sequence (D-11 onward) so that a `D-n`
> reference is unambiguous anywhere in the repo.

## 1. Overview

No new project, no new dependency, no new platform. This phase fills the two boxes phase 1 left
almost empty: the match simulation inside `MW3.Core`, and the screens that draw and drive it inside
`MW3.Game`.

```
  MW3.Game (presentation)                    MW3.Core (rules, no engine)
  +-------------------------+                +-----------------------------+
  |  ScreenManager          |                |  Match                      |
  |   +-- WelcomeScreen     |   commands     |   Advance(ticks)            |
  |   +-- MatchScreen ------|--------------->|   Execute(SendArmyCommand)  |
  |        reads state,     |                |   Players, Bases, Armies    |
  |        never mutates it |<---------------|   Outcome                   |
  +-------------------------+   state read   |  MapLayout (normalized)     |
                                             |  HitTest, Combat            |
                                             |  IPlayerBrain -> AiBrain    |
                                             +-----------------------------+
                                                          ^
                                             MW3.Core.Tests drives whole
                                             matches headlessly (no device)
```

The one-way arrow from phase 1 still holds, and the new arrow that matters is the narrow one:
presentation talks to the simulation **only** by advancing ticks and submitting commands. It never
reaches in and moves a unit.

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds **no** package reference, no content-pipeline asset beyond the existing SpriteFont,
and no platform capability. See `docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a is complete and current for build, run, smoke,
screenshot, Android install/launch, and the gate. Everything there still applies verbatim; note the
repo-wide rule that the solution is built with `-m:1` (see CLAUDE.md).

FR-2 adds exactly one command to the desktop head's smoke path — `MW3.Android` accepts no CLI args
and stays verified over `adb` (D-3, D-8), so this exists to give `qa-verifier` an unattended way to
drive screen navigation without synthetic OS events (D-17):

```powershell
dotnet run --project src/MW3.Desktop -- --script <commands.txt> --screenshot out.png
```

Replays the directives in `<commands.txt>` against a fresh app (no `--smoke` needed alongside it —
`--script` has its own exit rule), then writes one frame and exits 0.

**FR-5 adds no directive.** Phase 2 discovery expected it to extend this vocabulary for sending
armies; settling the feature showed there is nothing to add — the send-army interaction is a drag,
and a drag is already `down` at the source followed by `up` at the target. The scripted input
source teleports the pointer between the two, which the model is indifferent to because only the
press-start and release positions decide anything.

**File format** — one directive per line, `<frame> <directive> [args]`. Blank lines are skipped;
`#` starts a full-line comment. `<frame>` is a non-negative integer frame index (the first `Update`
call is frame 0). Directives:

- `down <x> <y>` — pointer press at normalized `0..1` coordinates.
- `up <x> <y>` — pointer release at normalized `0..1` coordinates.
- `back` — a back request (Escape on desktop, the hardware back button on Android).
- `wait` (FR-3) — a timeline marker with no effect, letting a script extend to a chosen frame
  without a fake pointer event. Useful once a screen has live state (the match screen) that a
  script wants to hold on for a while before ending.

Coordinates are normalized so the same script behaves identically at any window size or device
aspect ratio (D-14's precedent, applied to input instead of the map).

**Exit rule** — playback ends a fixed 10 frames after the highest frame number in the file
(`10 up 0.2 0.2` ends the run at frame 20), then the screenshot is written if `--screenshot` was
given, and the process exits 0. The fixed frame count is what keeps the check from flaking on
timing. An unparseable script (unknown directive, wrong argument count, non-numeric frame or
coordinate) exits non-zero with a message naming the offending line, before any graphics device is
created.

**`--dump-state <path>` (FR-3)** — at the same final frame the screenshot is taken, writes the
match's total elapsed ticks and one line per base (id, owner - the human player, the AI player, or
`Neutral` - and garrison count), then the process still exits 0. Independent of `--screenshot`:
works with or without it, and omitting `--dump-state` writes no file. Only meaningful once the
match screen is showing - reads nothing if `--script` never navigated past the welcome screen.
This is how `qa-verifier` asserts exact model numbers instead of inferring them from pixels; FR-4,
FR-6, and FR-7 all reuse it rather than inventing a second state-inspection mechanism.

**FR-5 extends the dump with in-flight armies** — after the per-base lines, one line per army
reporting its id, owner, source base id, target base id, unit count, launch tick, and arrival tick.
The elapsed-ticks and per-base lines are unchanged, and a dump taken with nothing in flight lists no
army at all. This is what makes transit assertable from the model: a screenshot can show that *a*
circle is somewhere between two bases, but only the dump says which army it is and when it lands.

**Desktop window size** — `MW3Game` sets the desktop head's `PreferredBackBufferWidth`/`Height` to
`1280x720`, the same reference resolution every screen's layout already scales from. This is one of
the two viewports FR-3's circle-layout criterion is checked at; the other is the attached device's
own screen, checked there directly (D-3, D-8) rather than by resizing the desktop window to match.
That device viewport is **not** the panel's full `1920x1200` - `MainActivity` requests no
fullscreen/immersive theme, so Android draws the status and soft-navigation bars as chrome on top
of the surface, shrinking what MonoGame actually receives to roughly `1808x1018` (measured on the
attached MI Pad 4; see follow-up #15 for correcting this repo-wide rather than only here). D-14's
viewport-derived layout adapts to whatever the real value is regardless, so this doesn't affect
correctness - only any future arithmetic that assumes the panel's advertised resolution literally.

Scripts backing the FR-2 acceptance criteria are committed under `qa/scripts/` (`play.txt`,
`play-then-back.txt`, `press-then-drag-off.txt`, `back-and-forth.txt`), so the commands below are
reproducible on a clean clone:

```powershell
dotnet run --project src/MW3.Desktop -- --smoke --screenshot welcome.png
dotnet run --project src/MW3.Desktop -- --script qa/scripts/play.txt --screenshot match.png
dotnet run --project src/MW3.Desktop -- --script qa/scripts/play-then-back.txt --screenshot back.png
dotnet run --project src/MW3.Desktop -- --script qa/scripts/press-then-drag-off.txt --screenshot drag.png
dotnet run --project src/MW3.Desktop -- --script qa/scripts/back-and-forth.txt --screenshot cycles.png
```

`match.png` is not byte-identical to `welcome.png` (a different screen is showing); `back.png`,
`drag.png`, and `cycles.png` all are (navigation returned to, or never left, the welcome screen).

FR-3 adds two more, holding the match screen for different lengths of time before ending
(`qa/scripts/match-early.txt`, `qa/scripts/match-late.txt` — the latter long enough for at least 40
ticks to elapse):

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/match-early.txt --screenshot early.png --dump-state early.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/match-late.txt --screenshot late.png --dump-state late.txt
```

`late.png` is not byte-identical to `early.png` (the garrison numbers changed); running either
script again reproduces its own screenshot byte-for-byte. Each dump reports elapsed ticks and, for
every owned base, a garrison of exactly `10 + elapsedTicks / 10`; neutral bases stay at exactly 5
in both.

**Corrected by phase 3 FR-1 (#30)**: that garrison formula is now bounded by the base's production
cap — `min(20, 10 + elapsedTicks / 10)` for an untouched level-1 base (D-21). Neither script gets
near the ceiling (`match-late.txt` ends at tick 64 with the human's base at 16), so both dumps read
exactly as before; the general rule is what changed. Production is also per base now rather than
credited from global tick boundaries, so a base captured mid-match produces one period after it
changed hands rather than on the match's own multiples of 10.

FR-5 adds six more, exercising the drag interaction (D-18) through the same `down`/`up`
vocabulary — a drag is just `down` at the source followed by `up` at the target, so no directive
changed:

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/send-army.txt --screenshot send.png --dump-state send.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/army-arrival.txt --screenshot arrival.png --dump-state arrival.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/cancel-on-empty-space.txt --screenshot cancel.png --dump-state cancel.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/drag-from-unowned-base.txt --screenshot unowned.png --dump-state unowned.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/hold-selection.txt --screenshot hold-selection.png
dotnet run --project src/MW3.Desktop -- --script qa/scripts/hold-empty-space.txt --screenshot hold-empty-space.png
```

`send-army.txt` drags from the human base to the nearest neutral immediately and ends before the
army lands: its dump shows the human base at half its pre-send garrison and exactly one army in
flight, with an arrival tick later than its launch tick. `army-arrival.txt` holds first so the
human base's garrison outgrows twice the neutral's before dragging, then waits long enough (~17
ticks) for the army to land: its dump shows the target owned by the human with a garrison
consistent with FR-4's 1:1 arithmetic. `cancel-on-empty-space.txt` presses on the human base and
releases over empty space; `drag-from-unowned-base.txt` presses starting on the AI's base — both
dumps show zero armies in flight, every base under its starting owner, and garrisons consistent
with production alone. `hold-selection.txt` presses on the human base and never releases, so its
screenshot captures the selection highlight while held; `hold-empty-space.txt` is otherwise
identical but presses over empty space, so the two screenshots are **not** byte-identical (the
highlight is what differs), and re-running either individually reproduces its own screenshot
byte-for-byte.

FR-6 adds the AI opponent, which decides on a fixed 20-tick interval independently of any screen or
script — the first decision tick any script can reach is baked into its total frame count (`match-
late.txt` and `army-arrival.txt`, both holding for hundreds of frames, now run well past it). This
changes what two pre-existing dumps show, corrected here and in `REQUIREMENTS.md`'s FR-3/FR-5
entries rather than dodged: **`match-late.txt`**'s dump no longer has every owned base holding
exactly `10 + elapsedTicks / 10` — only the human's base and any base the AI has not acted on still
do; the AI's own bases reflect its sends and captures instead. **`army-arrival.txt`**'s dump may
show the AI's own army still in flight elsewhere on the map, so "zero armies in flight" no longer
holds by itself — only the human's captured base and its own zero-contest guarantee stand.
`match-early.txt` (elapsed tick 4) and the other FR-5 scripts all end well before the AI's first
decision (tick 20) and are unaffected.

Two more scripts exercise the AI directly, holding no human input at all:

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/ai-first-strike.txt --screenshot first-strike.png --dump-state first-strike.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/ai-expansion.txt --screenshot expansion.png --dump-state expansion.txt
```

`ai-first-strike.txt` holds just long enough for the AI's first decision (tick 20) to fire: its
dump lists exactly one army, owned by the AI, and none owned by the human. `ai-expansion.txt` holds
for several decision ticks (~64): its dump shows the AI owning at least two bases, one of which
started neutral. Neither adds a script directive or a `--dump-state` field — the AI needs nothing
`--script` doesn't already expose, since owner and army-in-flight lines already say everything these
checks need. Re-running either individually reproduces its own screenshot byte-for-byte, and the two
are not byte-identical to each other.

FR-7 closes the loop: `Match` gains an `Outcome` (in progress / human victory / human defeat),
`--dump-state` gains one more line reporting it, and the desktop head gains `--time-scale <n>`, a
positive-integer multiplier on the fixed per-frame elapsed milliseconds handed to `Update`:

```powershell
dotnet run --project src/MW3.Desktop -- --script <commands.txt> --time-scale <n> --screenshot out.png --dump-state out.txt
```

It changes no rule and no behaviour — the tick sequence is exactly the one real-time play produces,
delivered sooner — and defaults to 1 (matching every prior script's timing) when omitted. A
non-numeric, zero, or negative value exits non-zero naming the problem, before any graphics device
is created, the same way an unparseable `--script` file does. `MW3.Android` accepts no command-line
arguments at all (D-3, D-8) and stays real-time only; `--time-scale` is a desktop QA lever, needed
because a full match is thousands of ticks — over eight minutes of wall clock at the default scale,
against the 30-second budget every earlier script fits comfortably (REQUIREMENTS.md §5).

Three scripts exercise the ending, each a **documented exception to the 30-second script
budget: up to 60 seconds**, since reaching an ending is a full match rather than a few drags:

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/defeat.txt --time-scale 125 --screenshot defeat.png --dump-state defeat.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/victory.txt --time-scale 25 --screenshot victory.png --dump-state victory.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/dismiss-ending.txt --time-scale 125 --screenshot dismiss.png
```

`defeat.txt` presses nothing after `Play`, at a time scale sufficient for the passive-human match to
reach defeat well within FR-6's 5000-tick budget: its dump reports human defeat with the AI owning
all six bases. `victory.txt` drives the exact fixed, hand-authored command sequence covered by
`MatchOutcomeTests` in `MW3.Core.Tests` — every drag's timing is chosen so it lands on the intended
tick precisely under `--time-scale 25` (each frame after `Play`'s release advances the match by
exactly 4 ticks): its dump reports human victory with the human owning all six bases.

**Corrected by phase 3 FR-1 (#30)**: both sequences were re-derived, because garrison caps changed
the arithmetic they were tuned around. The frame↔tick mapping (`frame = 5 + tick/4`, `down` two
frames before its `up`) is unchanged, and so is everything else about the format — only the drags
themselves differ. Defeat now lands at tick 377 rather than deep into the thousands, because a cap
stops the passive human's capital out-growing the AI's expansion; victory lands at tick 556.
`dismiss-ending.txt` waits past defeat, then presses back: its final screenshot is byte-identical to
the FR-2 welcome baseline, proving the return is a real pop rather than a redrawn lookalike (and
`--dump-state`, given alongside it, would write nothing, since the final screen showing is
`WelcomeScreen`, not `MatchScreen` — consistent with FR-3's existing rule that the dump only ever
reads match state when the match screen is what is actually showing).

Re-running any of the three individually reproduces its own screenshot byte-for-byte; the victory
and defeat screenshots are not byte-identical to each other. A short pre-existing script
(`match-early.txt`) run with `--time-scale 1` reports the identical elapsed ticks and per-base
numbers it always has — the new `Outcome:` dump line is the only addition, proving the default path
is untouched. Every pre-existing script (FR-2 through FR-6) still exits 0 within its original
30-second budget and still reproduces its own screenshot byte-for-byte; `--smoke` alone still exits
0 within 30 seconds writing no file.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  FixedStepClock.cs        (phase 1) drives Match.Advance - unchanged
  Match.cs                 the aggregate: players, bases, armies, outcome
  MapLayout.cs             the one hardcoded map, in normalized coordinates
  MapPoint.cs              Core-side normalized position (no Vector2 - D-14)
  Player.cs                rules-level owner: id + controller kind (D-11)
  SendArmyCommand.cs       the only mutation input (D-12)
  HitTester.cs             "which base is at this point" - pure, unit tested (D-18)
  IPlayerBrain.cs          AI seam, implemented by AiBrain (D-16)
  BrainDecision.cs         "no command" vs exactly one SendArmyCommand, in the type system
  AiBrain.cs               the three-clause heuristic: defend, attack, consolidate
  MatchRunner.cs           owns Match + the AI brain; the only thing that Advance()s and Execute()s
  MatchOutcome.cs          in progress / human victory / human defeat (D-13)
src/MW3.Game/
  ScreenManager.cs         minimal screen stack (D-16)
  WelcomeScreen.cs         (phase 1) Play now pushes MatchScreen
  MatchScreen.cs           draws match state, turns input into commands
```

File names above are the intent, not a contract — `/kickoff` and `/implement` may split or rename
them. What **is** a contract: which project each concern lives in.

## 4. Key decisions

**D-11: `Player` is a rules-level owner, not an account.** Considered: modelling a profile now — a
persisted id, display name, avatar — "because multiplayer will need one". Rejected: there is no
server to authenticate against (S-7), so an identity model would be structure built on a guess, and
the wrong guess is expensive to unpick. Chosen: a `Player` is a stable in-match id plus a
controller kind (`Human` / `Ai`), and nothing else. What this protects: a future authoritative
server maps its own accounts onto in-match player ids, which forecloses nothing. What it forbids:
any Core type learning about names, logins, or persistence this phase.

**D-12: the match is a deterministic, command-driven simulation.** Considered: updating the model
directly from the MonoGame update loop with `GameTime` deltas — the obvious MonoGame way.
Rejected: it makes the rules untestable without a device, frame-rate-dependent, and impossible for
a server to re-run. Chosen: the match changes state in exactly two ways — `Advance(ticks)` for
whole ticks produced by the existing `FixedStepClock`, and `Execute(command)` for explicit
commands. No wall-clock read, no `GameTime`, no ambient randomness inside `MW3.Core`. Consequences:
a match is reproducible from a starting state plus a command log, which is what buys headless
tests (success criterion 2), unattended QA of interactive features (D-17), and a future server.
Human input and the AI produce the *same* command type, so neither can do anything the other
cannot — and a server could validate both with one code path.

**D-13: `Match` is an encapsulated mutable aggregate, not a per-tick immutable snapshot.**
Considered: the immutable style `FixedStepClock` already uses (new state returned from every
call), which would make replay, diffing, and undo trivial. Rejected at this scale: the simulation
advances every frame on a phone, and allocating a fresh graph of bases and armies per tick trades a
real GC cost on the target platform for a convenience nothing this phase needs. Chosen: `Match`
mutates in place, exposes no public setters, and permits state changes only through `Advance` and
`Execute`. Determinism (D-12) is what actually delivers replay, and it survives mutability. If a
later phase needs snapshots (rewind, netcode prediction), it adds an explicit serializer rather
than reversing this.

**D-14: map positions are normalized `MapPoint` values in `MW3.Core`, resolved to pixels only in
presentation.** Considered: pixel coordinates in Core (Core would then know the screen size), or
`Microsoft.Xna.Framework.Vector2` (banned by D-2 — it is exactly the kind of engine type whose
first appearance ends portability). Chosen: a Core-side point with X and Y in `0..1`, multiplied by
the viewport when drawn. This continues FR-3's viewport-derived layout precedent, so one map reads
correctly on the desktop window and on a landscape-locked device (D-10) with no per-platform
layout code.

**D-15: combat is deterministic integer arithmetic — no randomness.** Considered: Mushroom Wars
2-style randomized outcomes, which are more authentic and more fun. Rejected for this phase:
random outcomes make acceptance criteria probabilistic and tests flaky by construction, at the
moment when proving the loop matters more than tuning it. Chosen: an army of N attacking a base
holding M resolves by fixed arithmetic. If randomness is wanted later it enters as a seeded PRNG
owned by the match state and advanced only inside `Advance`/`Execute`, so D-12's replay guarantee
survives.

**D-16: screen management is a minimal `IScreen` plus a manager in `MW3.Game` — no library, and the
AI sits behind a Core-side seam.** Considered for screens: a third-party scene/state library, or a
single `Game` class with an enum switch. Chosen: `WelcomeScreen` already has the
LoadContent/Update/Draw/Dispose shape, so the smallest honest step is to name that shape as
`IScreen` and add a manager owning the current one — presentation-only code, assumed disposable in
an engine migration (D-2). Considered for the AI: putting it in `MW3.Game` next to the input that
resembles it. Rejected: the AI is rules-adjacent and must be testable headlessly, so it implements
a Core-side `IPlayerBrain` that observes the match and returns commands. Consequence: the AI is
exercised in unit tests over hundreds of ticks in milliseconds, with no head running at all.

**D-17: interactive and AI features are verified by scripted commands, not synthetic input.**
Considered: injecting synthetic mouse/touch events into the running head, and an image-diff
harness over recorded frames. Rejected: MonoGame reads input from the OS device state, so faking it
means OS-level automation — slow, flaky, and platform-specific, exactly what D-3 exists to avoid.
Chosen: because commands are data (D-12), smoke mode gains `--script <file>` which replays commands
against a fresh match and then honours `--screenshot`. `qa-verifier` asserts the resulting frame,
reusing the D-9 screenshot mechanism instead of inventing a second one. Accepted limitation, stated
plainly: this verifies the rules end-to-end and the drawing of their result, but **not** the step
that converts a physical tap into a command — which is what D-18 shrinks to near-nothing, with the
remainder confirmed by one real tap on hardware at feature boundaries (D-8).

**D-18: hit-testing is a pure function in `MW3.Core`.** Considered: hit-testing inside
`MatchScreen` against pixel rectangles, the natural place for it. Rejected because it would put the
one genuinely untestable part of the game — "which base did the player mean?" — in the one place
tests cannot reach. Chosen: Core answers "which base, if any, is at this normalized point", unit
tested including the miss and the ambiguous-overlap cases. What remains in the head is the
conversion of device coordinates to a normalized point: a couple of lines, verified once on real
hardware rather than mocked forever.

**D-19: Android's hardware back button is intercepted in `MainActivity.DispatchKeyEvent`, never
`OnBackPressed` or MonoGame's `Keyboard` state.** Discovered building FR-2 (#9): the initial
assumption that MonoGame surfaces the hardware back button as `Keys.Back` in `Keyboard.GetState()`
proved false on a physical MI Pad 4 (Android 11) - the check never fired. The next attempt,
overriding `Activity.OnBackPressed()`, *also* never fired: MonoGame's own view consumes the
`KEYCODE_BACK` key event during `Window.superDispatchKeyEvent` - the step `Activity.dispatchKeyEvent`
runs *before* falling back to `onKeyDown`/`onBackPressed` - so the event never reaches that
fallback path at all. What works: overriding `DispatchKeyEvent` itself, the activity's first look
at any key event, ahead of the view hierarchy; checking for `Keycode.Back` with
`KeyEventActions.Down` there and returning `true` (without calling `base.DispatchKeyEvent` for it)
both guarantees the handler runs and keeps Android's default back-stack handling from finishing the
activity. Binds every later feature that reads Android back/hardware-key input (FR-5's tap input,
FR-7's return-to-welcome): don't reach for `OnBackPressed` or a `Keyboard` check first, go straight
to `DispatchKeyEvent`.

**D-20: elimination requires zero bases *and* zero armies in flight, evaluated once per tick right
after that tick's arrivals resolve - not a snapshot check at arbitrary moments.** Considered:
declaring a player eliminated the instant their base count hits zero. Rejected: an army already
launched toward a base it will recapture would make a match end (and freeze) before that army ever
lands, contradicting "attainable" victories that route through a temporary all-bases-lost swing.
Considered for the tie-break: no precedence rule, introducing a draw outcome instead. Rejected:
REQUIREMENTS §6 explicitly scopes draws out, and a fixed, documented precedence (defeat first) is
simpler to state and test than a third outcome value threading through every screen and script that
reads `Outcome`. Consequence worth recording: ordinary play can never actually produce the
simultaneous-elimination state this rule exists for - a capture always transfers ownership to the
attacker, never to neither player, so the combined human-plus-AI owned-base count is monotonically
non-decreasing from its starting value of 2 and can never reach 0 for both at once. The test covering
the precedence rule constructs that state directly (reflection into `Base.Owner`'s internal setter)
rather than reaching it through `SendArmyCommand`s, which is expected, not a workaround.

## 5. Cross-cutting conventions

Build-mode Ivan applies these without being asked:

- **Presentation reads, commands write.** A screen may read match state to draw it and may submit
  commands; it may never mutate the model. A reviewer should treat a public setter on a Core match
  type as a defect.
- **One command type for humans and AI.** If input can do something the AI cannot express as a
  command, the command model is wrong — fix the model, do not add a side channel.
- **No engine type in `MW3.Core`, and that includes `Vector2`** (D-2, D-14). No wall-clock read and
  no `Random` in `MW3.Core` either (D-12, D-15).
- **Every rules feature lands with headless tests over whole ticks**, not just over single methods:
  a test that advances a match hundreds of ticks and asserts the outcome is worth more than one
  asserting a getter.
- **One map, one unit type, hardcoded in code.** No config file, no content asset, no map format
  until a phase actually needs a second map (REQUIREMENTS §6).
- **Presentation is verified by screenshot** (D-9) and, for interactive behaviour, by scripted
  commands (D-17) — never by eye as a routine step.
- **The gate is the standard** (`./gate.ps1`, built `-m:1`), and `MW3.Core` staying engine-free is
  checked the same way phase 1 checked it: no `Microsoft.Xna` or `MonoGame` text under
  `src/MW3.Core`.
