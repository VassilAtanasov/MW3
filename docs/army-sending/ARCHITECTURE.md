# Architecture — Sending armies the MW2 way (phase 4)

> Records what this phase adds or changes; the repo-wide `docs/ARCHITECTURE.md` holds the system
> baseline shared by every phase, and `docs/welcome-screen/`, `docs/core-gameplay-loop/`, and
> `docs/base-upgrades-and-types/ARCHITECTURE.md` hold phases 1-3's reasoning. Decision numbering
> **continues** that sequence (D-32 onward) so a `D-n` reference is unambiguous anywhere in the repo.

## 1. Overview

No new project, no new dependency, no new platform — the fourth phase in a row that adds none. The
change is narrower than phase 3's: `Match.Execute(SendArmyCommand)` learns to split one send into
several staggered `Army` objects instead of one; `Army` gains grouping metadata so those objects are
recognizable as one send; and `MatchScreen` gains a persistent strength control and a column
renderer. Nothing about `Base`, upgrades, conversion, or the combat resolver changes.

```
  MW3.Game (presentation)                     MW3.Core (rules, no engine)
  +----------------------------+              +---------------------------------+
  |  MatchScreen                |              |  Match                          |
  |   strength selector    NEW |  commands    |   Execute(SendArmyCommand)      |
  |   column rendering     NEW |------------->|     splits into wave Armies NEW |
  |                              |<-------------|   SendStrengthCalculator    NEW |
  |                              |  state read  |   Army: SendId/WaveIndex   NEW |
  +----------------------------+              |  AiBrain (uses calculator) NEW |
                                              +---------------------------------+
```

The key architectural finding this phase (see D-33): a wave is not a new kind of thing. It is an
ordinary `Army`, launched a few ticks after its siblings. Every downstream mechanism that already
resolves an `Army` — tower fire, arrival combat, capture, the recapture grace, determinism — needs
**no change** to handle waves correctly, because it never assumed there was only one `Army` per
send in the first place. This is the reason the phase is scoped the way §"Project layout" below
shows: the wave feature (FR-3) touches `Match` and `Army`, not `CombatResolver`, `Base`, or
`HitTester`.

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds no package reference, no content-pipeline asset, and no platform capability. See
`docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a, `docs/core-gameplay-loop/ARCHITECTURE.md` §2a, and
`docs/base-upgrades-and-types/ARCHITECTURE.md` §2a are all complete and current; everything there
applies verbatim, including the repo-wide `-m:1` build rule and the `down`/`up` scripted-input
vocabulary.

**Correction (FR-2):** no new script directive was added, superseding this section's original
expectation of one. `ScriptParser` is unchanged — a QA script selects a strength by pressing the
new `SendStrengthSelector` control with the existing `down`/`up` directives, exactly as it already
presses an action-menu button. This also exercises the control's real hit-testing and layout, which
a dedicated directive would have bypassed entirely.

`--dump-state` gains fields at two features, each fixed exactly at that feature's kickoff rather
than here:

- **FR-2** adds the currently-selected strength to the screen's own state line (alongside `Menu:`),
  since it is presentation state the human controls, not simulation state — following D-26's
  precedent that menu state lives in `MatchScreen` and is dumped by it, never by `MW3.Core`.
- **FR-3** extends the per-army line. An army today reports id, owner, source, target, launch tick,
  arrival tick, and current strength (`Count=`, mutable since phase 3 FR-4). It gains three fields
  identifying which send it belongs to and where in that send's wave sequence it sits, so a script
  asserting "this send arrived as 3 waves, the first captured nothing, the second captured it" has
  something to key on.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  SendStrength.cs           NEW - enum: Quarter=25, Half=50, ThreeQuarters=75, Full=100 (D-32)
  SendStrengthCalculator.cs NEW - pure function: (garrison, SendStrength) -> unit count,
                             floor(garrison * pct / 100) clamped to a minimum of 1 - the one
                             place this arithmetic exists (D-32)
  Army.cs                   gains SendId, WaveIndex, WaveCount - grouping metadata only,
                             no change to how an Army resolves (D-33)
  Match.cs                  Execute(SendArmyCommand) splits a send into 1..N wave Armies,
                             staggering LaunchTick by the wave interval (D-33); ResolveArrival,
                             tower fire, and the recapture grace are unchanged
  AiBrain.cs                ClampedSendSize's inline `garrison / 2` is replaced by
                             SendStrengthCalculator.Compute(garrison, SendStrength.Half) -
                             same behaviour, one fewer copy of the arithmetic
src/MW3.Game/
  MatchScreen.cs            gains a persistent strength selector read before a send drag
                             resolves, and draws a multi-wave send as a tapered column
  SendStrengthSelector.cs   NEW - layout, drawing, and hit-testing for the strength control;
                             decides nothing, exactly as BaseActionMenu does (D-25's pattern)
```

File names are intent, not contract — `/kickoff` and `/implement` may split or rename them. What
**is** contract: which project each concern lives in.

## 4. Key decisions

**D-32: send strength is one shared Core calculator, and reading the current garrison at send time
gives snaking away for free.** Considered: computing the unit count inline at each call site
(`MatchScreen`'s drag-release handler and `AiBrain`'s `ClampedSendSize`), which is the smallest diff
and is exactly what phase 2 already does for the fixed-half rule. Rejected — it is the same
arithmetic duplicated twice today and would become the same arithmetic duplicated twice with a
percentage parameter, which is precisely the shape `docs/CONVENTIONS.md`'s "model absence and
failure in the type system" spirit warns against for anything more than a one-line trivial repeat.
Chosen: `SendStrengthCalculator.Compute(garrison, SendStrength)` as the one place the
floor-and-clamp-to-1 arithmetic exists, called by both the human path and `AiBrain`. Consequence
worth stating: because the calculator is given the garrison *at the moment of the call* rather than
some earlier snapshot, snaking needs no explicit representation anywhere in `MW3.Core` — a player
tapping the same target three times at 25% just submits three ordinary `SendArmyCommand`s, each
computed against whatever the garrison is by then. There is no `SnakeCommand` and no snake-detection
logic; MW2's snaking is a player technique that falls out of the rules, not a rule of its own
(`MW2-RULES.md` §3.3 confirms this reading — "set sending to 25% and tap the target repeatedly").

**D-33: a wave is an ordinary `Army` with a staggered launch tick, not a new kind of aggregate.**
Considered: giving `Army` an internal sequence of sub-arrivals — closer to how the reference
describes a "wave" as a property of one send — and teaching `Match.ResolveArrival` to walk that
sequence. Rejected because it means rewriting the one piece of the simulation phase 3 spent the most
care on (D-24's tower fire, D-29's combat resolver, D-30's recapture grace) to understand a new
shape, and because determinism (D-12) would then depend on a second, wave-internal notion of
"tick" nested inside the existing one. Chosen: `Match.Execute(SendArmyCommand)` splits a send of `n`
units into `ceil(n / 8)` ordinary `Army` objects — full 8-unit waves plus a final wave carrying the
remainder — each launched `waveInterval` ticks after the previous, each computing its own
`ArrivalTick` from its own `LaunchTick` exactly as today's single-army send does. A send of 8 units
or fewer produces exactly one `Army`, launched on the submission tick — **bit-identical to today's
behaviour**, the same "collapses to the old rule at the boundary" pattern D-29's combat resolver
uses at `a = d = 100%`. What this buys for free: tower fire already evaluates every in-flight army
every tick (D-24), so a tower now naturally gets one shot per wave instead of one shot at a single
large blob; `ResolveArrival` already resolves each army independently, so a defender that falls to
wave 2 can still repel wave 4 if it was reinforced between them, with no new code path. What it
costs: multiple armies from one send are otherwise indistinguishable from several coincidental
separate sends, which the drawn column (FR-4) and any wave-aware test need to tell apart — so `Army`
gains `SendId` (shared by every wave from one `Execute` call), `WaveIndex` (1-based), and
`WaveCount`, metadata only, read by nothing in `ResolveArrival` or `CombatResolver`.

**The wave interval is a tuning value, not an architecture decision, and is deliberately not fixed
here.** `MW2-RULES.md` §3.3 states that waves "don't strike simultaneously" but never publishes the
gap between them — §10's known-gaps list is amended to say so. The only thing known is that a
passive skill shortens it (out of scope, parity **G-20**, §2 of `MW2-ITEMS-AND-PROGRESSION.md`),
which confirms a baseline interval exists but supplies no value. Per `CLAUDE.md`'s tuning-values
rule (D-22's routing rule) and following the precedent FR-4 set for tower range and fire period
when MW2 was equally silent (parity **G-13**, **G-22**), the number is derived and recorded in this
phase's `REQUIREMENTS.md` §"Tuning values" table at FR-3's `/kickoff`, calibrated against MW3's own
`Match.TickDurationMilliseconds` (50 ms) and `ArmySpeedUnitsPerTick` (0.01) rather than guessed.
Guidance for that kickoff, not a decision made here: the interval should be small enough that even a
maximum send (a capped level-5 village's 100 units, 13 waves) finishes launching well inside the
shortest inter-base travel time (30 ticks between the closest pair of bases, per
`docs/base-upgrades-and-types/REQUIREMENTS.md`'s tuning narration), so a large attack still reads as
one attack rather than a slow trickle.

**D-34: the strength control is a persistent, standing selection, not a per-drag modifier.**
Considered: a radial or press-and-hold gesture layered onto the existing drag-to-send, chosen at the
moment of each send. Rejected — MW2 models this as a mode: "the game has a 25/50/75/100% setting"
(`MW2-RULES.md` §3.3), a control the player sets once and that then applies to every subsequent send
until changed, which is also the only reading under which repeated-tap snaking (leave it at 25%,
tap several times) makes sense as a technique rather than requiring the same gesture repeated with
the same modifier held each time. Chosen: `MatchScreen` carries the currently-selected `SendStrength`
as its own presentation state (the same category as D-26's menu-open flag), read by the drag-release
handler when it builds a `SendArmyCommand`; a small always-visible control sets it. **Defaults to
`Half`**, so every existing `qa/scripts/` drag-to-send script keeps producing the same command it
does today without being touched — this phase's only behavioural change for an untouched script is
therefore zero, matching the "bit-identical at the boundary" pattern D-33 also follows.

## 5. Cross-cutting conventions

Build-mode Ivan applies these without being asked. Phases 2 and 3's conventions all still hold;
these are the additions:

- **One arithmetic site per rule.** `SendStrengthCalculator` is the only place a percentage becomes
  a unit count; a second inline computation anywhere (a screen, a test helper, `AiBrain`) is a
  defect, following the same discipline `LevelTable` established for tuning numbers (D-22).
- **A wave is not a special case.** Code that resolves an `Army` — combat, tower fire, capture,
  determinism — must not branch on `WaveIndex` or `WaveCount`; those fields exist for grouping and
  display only. If resolution logic ever needs to know a wave is a wave, that is a sign D-33's
  design has been violated.
- **One command type family for humans and AI**, extended: the AI's sends go through the same
  `SendStrengthCalculator` the human's do, so if the AI can express a strength the human's control
  cannot, or vice versa, the model is wrong — the same rule phase 3 stated for upgrade and convert.
- **No allocation per tick.** Wave splitting happens once, at `Execute`, not per tick — but the
  waves it creates then live in `ArmiesInFlight` and are walked by tower fire every tick exactly as
  any other army is (phase 3's standing no-LINQ-on-this-path rule still applies, now against a list
  that can be up to 13× longer for one large send).
- **A phase-2 or phase-3 document that this phase makes untrue is corrected in the same PR**, not
  left to be discovered — following the precedent every prior phase's FR set for the one before it.
- **No engine type in `MW3.Core`, no wall-clock read, no `Random`** (D-2, D-12, D-14, D-15). Wave
  timing is expressed in ticks, computed from the submission tick and the tuning constant.
- **Every rules feature lands with headless tests over whole ticks**, including at least one that
  advances a full match containing a multi-wave send and asserts a board state.
- **Presentation is verified by screenshot and scripted commands** (D-9, D-17); the one new script
  directive (§2a) follows `ScriptParser`'s existing pattern rather than a second mechanism.
- **The gate is the standard** (`./gate.ps1`, built `-m:1`), and `MW3.Core` staying engine-free is
  checked the same way every phase has checked it.
