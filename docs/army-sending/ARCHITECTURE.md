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
- **FR-3** extends the per-army line with `Send=<id>` and `Wave=<index>/<count>`, read verbatim:
  `Army 3: Owner=Human Source=1 Target=3 Count=8 Launch=120 Arrival=154 Send=2 Wave=1/3`. A
  single-arrival send reads `Wave=1/1`. FR-2's `Strength:` line is untouched, so a script asserting
  "this send arrived as 3 waves, the first captured nothing, the second captured it" has something
  to key on.

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

**The wave interval is a tuning value, settled at FR-3's kickoff.** `MW2-RULES.md` §3.3 states that
waves "don't strike simultaneously" but never publishes the gap between them — §10's known-gaps list
records this. Per `CLAUDE.md`'s tuning-values rule (D-22's routing rule) and following the precedent
FR-4 set for tower range and fire period when MW2 was equally silent (parity **G-13**, **G-22**),
`SendWaveCalculator.WaveIntervalTicks = 5` (250 ms) and `WaveSizeUnits = 8` are recorded in this
phase's `REQUIREMENTS.md` §"Tuning values" table — 8 is MW2's published wave size (`MW2-RULES.md`
§3.3 `[S]`), 5 is MW3's own number, chosen above the fastest tower's 3-tick fire period so every
wave gap admits a fresh shot at any tower level.

**D-35: a pending wave lives outside `ArmiesInFlight` until its own launch tick, not inside it with
a future `LaunchTick`.** Considered: adding every wave to `ArmiesInFlight` at `Execute` with its real
(future) `LaunchTick`, and having `PositionAtTick`/tower fire/`--dump-state` skip anything whose
`LaunchTick` is still ahead. Rejected — every reader of `ArmiesInFlight` (tower fire, the dump line,
determinism tests) would need its own "is this actually launched yet" guard, multiplying one rule
into N call sites, exactly the duplication D-32 already rejected for the strength arithmetic.
Considered also: staggering `ArrivalTick` instead of `LaunchTick`, keeping every wave "launched" at
tick 0 but arriving later — rejected because it would make a wave's position formula diverge from an
ordinary army's (`PositionAtTick` extrapolating from a source it never actually left) and would let
a tower fire on units still standing in the source base, contradicting FR-4's own model of what
"in range" means. Chosen: `Match` holds a private pending-wave list; wave 1 enters `_armies` (and so
`ArmiesInFlight`) immediately at `Execute`, exactly as today's single-army send does; waves 2..N wait
in the pending list and move into `_armies` only when `Advance` reaches their own `LaunchTick` — a
boundary evaluated after construction completion and before tower fire and arrivals, so a wave is a
legitimate tower target from the tick it launches, never before. Once `Match.Outcome` leaves
`InProgress`, no pending wave ever launches and none are reported anywhere — the same freeze phase 2
FR-7 already applies to decision-making, extended to this one remaining source of state change.

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

**D-36: a wave column reads legibly by tapering each wave's marker radius and drawing a shared
spine, not by redesigning what a wave already draws as.** Since D-33 made a wave an ordinary `Army`,
`DrawArmiesInFlight` already rendered each wave as its own circle with its own count - nothing new
needed constructing there. The actual gap D-33 named as a cost: waves of one send are visually
indistinguishable from separate sends, and on this map's geometry they can also overlap each other
outright. The wave spacing along a path (`ArmySpeedUnitsPerTick × SendWaveCalculator.WaveIntervalTicks`
= 0.05 normalized units) is fixed regardless of edge length or direction, while a marker's diameter
(`2 × _armyRadiusFraction (0.08) × min(viewport.Width, viewport.Height)`) is not - at 1280x720 the
worst case (a purely horizontal edge, where the 0.05-unit spacing projects onto the full
`viewport.Width` rather than being foreshortened by a diagonal) still leaves consecutive full-size
markers overlapping by 44%; a purely vertical edge overlaps 69%; the MI Pad 4 at 1808x1018 overlaps
45%. Considered: one composed shape drawing a single aggregate count for the whole column - rejected,
because per-wave counts are what actually resolve against a defender (a 4-unit tail wave about to
bounce needs to read as 4, not be folded into a column total). Considered: staggering markers
perpendicular to the travel line - rejected, because it would put markers off the line
`HitTester`/the range test reason about, and FR-4's acceptance explicitly wants the column to read as
one send *on its path*, not beside it. Considered: index badges alone (small "1", "2", "3" labels)
with no size change - rejected, because it does nothing about the overlap itself; two same-size
circles at 44-69% overlap still occlude each other's badge. Chosen instead: `WaveColumnPresentation`
(pure, headlessly testable, decides and draws nothing - the same division D-25 established for
`SendStrengthSelector`) computes two things from plain `Army` data - `RadiusFraction`, linear from
`_armyRadiusFraction` at wave 1 to a new, smaller `_armyTrailingRadiusFraction` (0.04) at the last
wave (sized so a tail marker's diameter no longer exceeds the 0.05-unit spacing on the worst-case
1280x720 horizontal edge), and `ComputeSpineSegments`, which groups **by `Army.SendId`, never by
adjacency** in `ArmiesInFlight` (a second send launched mid-column interleaves with the first, per
D-35 - pending waves aren't even in the list yet) and returns index pairs for consecutive,
currently-in-flight waves only. `MatchScreen.DrawArmiesInFlight` draws the resulting spine beneath
every marker (a rotated, scaled draw of the same reused 1x1 texture the buttons already use - no new
texture) and each wave's own tapered circle on top, so a count is never obscured by the line beneath
it. `WaveCount == 1` returns `_armyRadiusFraction` unchanged and produces no spine segment at all -
an ordinary send draws bit-identically to before this feature, the same boundary-is-zero pattern
D-33 and D-34 both already follow.

A tower's fire and a hit army are made visible the same way: `WaveColumnPresentation.IsFlashing`
(pure: `elapsedTicks - eventTick < durationTicks`) drives a short, presentation-only brightening
(`Color.Lerp` toward white, mirroring `DarkenOwnerColor`'s existing technique in reverse) of a
tower's fill while `Base.LastFireTick` is recent, and of an army's marker while its `UnitCount` was
recently observed to drop. The drop is detected in `MatchScreen.Update` (a new `RecordArmyHits`,
called once per tick alongside the existing `PruneResolvedArmyText`), never in `Draw`, so the flash
is tied to tick arithmetic rather than frame cadence and two decrements in quick succession are not
collapsed into one. Rejected: a literal tower-to-army tracer line - `MW3.Core` does not expose which
army a tower's shot resolved against (only the aggregate `UnitCount` change), and adding that
exposure is out of scope for a presentation-only feature; the brighten-on-hit flash reads as "a shot
landed on that wave" without needing the target identity at all. Accepted, documented limitation: an
army destroyed outright leaves `ArmiesInFlight` the same tick its count reaches zero, so its final,
fatal hit is never recorded and never flashes - `RecordArmyHits` only ever sees armies still in the
list.

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
