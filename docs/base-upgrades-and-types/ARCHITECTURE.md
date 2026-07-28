# Architecture — Base upgrades and types (phase 3)

> Records what this phase adds or changes; the repo-wide `docs/ARCHITECTURE.md` holds the system
> baseline shared by every phase, `docs/welcome-screen/ARCHITECTURE.md` holds phase 1's reasoning,
> and `docs/core-gameplay-loop/ARCHITECTURE.md` holds phase 2's. Decision numbering **continues**
> that sequence (D-21 onward) so a `D-n` reference is unambiguous anywhere in the repo.

## 1. Overview

No new project, no new dependency, no new platform — the third phase in a row that adds none. What
changes is inside the two boxes phase 2 filled: `Match` learns that bases have levels, caps, and
types and that armies can be shot down; `MatchScreen` learns to draw those and to host the first UI
widget the game has ever had.

```
  MW3.Game (presentation)                     MW3.Core (rules, no engine)
  +----------------------------+              +---------------------------------+
  |  ScreenManager             |              |  Match                          |
  |   +-- WelcomeScreen        |  commands    |   Advance(ticks)                |
  |   +-- MatchScreen ---------|------------->|   Execute(SendArmyCommand)      |
  |        +-- BaseActionMenu  |              |   Execute(UpgradeCommand)   NEW |
  |            lays out and    |              |   Execute(ConvertCommand)   NEW |
  |            draws only      |<-------------|   AvailableActions(baseId)  NEW |
  +----------------------------+  state read  |   Bases: level, cap, type   NEW |
                                              |   Armies: mutable strength  NEW |
                                              |   TowerFire (per tick)      NEW |
                                              |  AiBrain (+ new clauses)        |
                                              +---------------------------------+
```

The narrow arrow phase 2 established still holds and is the thing this phase is most at risk of
breaking: presentation talks to the simulation **only** by advancing ticks and submitting commands.
A menu is exactly the kind of feature that tempts a screen to decide something — "can this base
afford an upgrade?" — and D-25 exists to stop that.

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds **no** package reference, no content-pipeline asset beyond the existing SpriteFont,
and no platform capability. See `docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a (build, run, smoke, screenshot, Android install/launch,
gate) and `docs/core-gameplay-loop/ARCHITECTURE.md` §2a (`--script`, `--dump-state`,
`--time-scale`, the committed `qa/scripts/`) are both complete and current. Everything there
applies verbatim, including the repo-wide rule that the solution is built with `-m:1`.

This phase is expected to add **no new script directive and no new command-line flag**. The action
menu is opened and used through the `down` / `up` vocabulary that already exists — press and release
on a base to open it, press and release on a button to choose — which is the whole reason FR-2's
widget must be laid out from the viewport in a way a normalized coordinate can address (D-14, D-17).

What it does extend is `--dump-state`, which is the mechanism every rules feature this phase is
verified through. Each feature adds fields to the existing per-base and per-army lines rather than
inventing a second inspection path:

- per-base: **level**, **type** (producer/tower), and **cap**, alongside the existing id, owner, and
  garrison.
- per-army: **current strength**, which can now be lower than the count it launched with, alongside
  the existing id, owner, source, target, launch tick, and arrival tick.

Each feature's `/kickoff` fixes the exact line format and the scripts that exercise it. The
standing rule from phase 2 holds: a dump is only meaningful while the match screen is showing, and
`--dump-state` writes nothing otherwise.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  Match.cs                 gains levels, caps, types, tower fire, new commands
  Base.cs                  gains Level, BaseType, and a cap derived from both
  BaseType.cs              producer | tower
  LevelTable.cs            the constant tables: cap, production period, upgrade cost,
                           tower fire period, tower range - per level (D-22)
  UpgradeCommand.cs        spend units from a base's own garrison to raise its level
  ConvertCommand.cs        producer <-> tower, both directions, reversible (D-23)
  BaseAction.cs            one offerable action + its cost + whether it is affordable
  MatchRunner.cs           unchanged in shape; routes the two new command types
  AiBrain.cs               gains clauses for upgrading and converting (FR-6)
src/MW3.Game/
  MatchScreen.cs           draws level, cap, type, range, army strength; hosts the menu
  BaseActionMenu.cs        layout, drawing, and button hit-testing - decides nothing (D-25)
```

File names are intent, not contract — `/kickoff` and `/implement` may split or rename them. What
**is** contract: which project each concern lives in.

## 4. Key decisions

**D-21: the garrison cap is a production ceiling, not a storage limit.** Considered: a hard cap
that destroys units arriving above it (simplest to state), and MW2's over-cap bleed, where a base
can be stuffed past its cap and decays back down. Rejected the hard cap because an evaporating
reinforcement is a punishing, illegible moment for the player, and rejected the bleed because it is
a second attrition rule threading through every test, dump, and AI prediction for a feel benefit
this phase cannot yet measure. Chosen: a base stops *producing* at its cap, and arriving armies
stack above it freely with nothing decaying. What this protects: production is the only source of
units in the game, so capping production still throttles the economy exactly as the phase goal
requires — the cap's job is to force upgrading and expansion, and it does that whether or not
reinforcements can exceed it. What it enables, deliberately: massing a strike force at a staging
base. That is strategy, not a leak.

**D-21a: production is per-base state, not a global count of periods crossed.** Discovered building
FR-1 (#30), and recorded because it is the structural change the cap actually forced. Phase 2's
`Match.ApplyProduction` credited every owned base with `(toTick / 10) - (fromTick / 10)` — one
global figure, correct only while every base shares one period and no base can stop. A cap makes
bases stop independently, and levels give them different periods, so neither premise survives.
Chosen: each `Base` carries its own `ProductionProgressTicks`, advanced per segment by
`ProductionCalculator` in closed form (no tick-by-tick loop over spans that reach thousands of
ticks, no allocation). Two consequences worth knowing before touching this again. First, reaching
the cap leaves progress at exactly zero — the tick that produced the capping unit consumed the
progress that bought it, and every later tick at the cap is discarded — which is *why* "held at cap,
then drained, produces a full period later" needs no special case. Second, a base captured
mid-match now produces one period after **it** changed hands rather than on the match's global
multiples of 10, and its previous owner's partial progress is discarded rather than inherited; the
phase-2 documents that stated the old rule are corrected in the same PR.

**D-22: levels are a short fixed ladder defined by constant tables in `MW3.Core`, and they buy
economy only — never combat strength.** Considered: a formula (`cap = 20 * level`), and a content
file so levels could be tuned without a rebuild. Rejected the formula because every interesting
ladder is non-linear and a formula invites tuning by exponent; rejected the content file because
this phase has one map hardcoded in code and a tuning file would be the first crack in that
(REQUIREMENTS §6). Chosen: a table of per-level constants — cap, production period, upgrade cost,
tower fire period, tower range — read by both the simulation and the tests. Levels raising *defence*
was considered and rejected in discovery: it would reopen D-15's 1:1 combat arithmetic, invalidate
the meaning of every existing combat test, and complicate every capture prediction the AI makes,
in exchange for authenticity this phase does not need. Combat stays exactly as phase 2 left it.

**D-23: base type is reversible, and a capture keeps the type while dropping one level.**
Considered for conversion: one-way (cheapest, and it makes the choice weightier). Rejected: it
turns a mistap into a permanent loss in a game with no undo. Considered on capture: keeping the
level intact (maximum drama), and resetting to a level-1 producer (protects against one lucky
capture deciding the match). Chosen: the structure survives the fighting but one level of
investment is burned. Consequence worth stating: a level-1 base that changes hands stays level 1 —
the demotion floors rather than destroying — and a captured tower stays a tower, so taking an
enemy tower hands you a working defence, which is a real reason to attack a defended position
rather than route around it.

**D-24: armies are no longer inert in flight, and tower fire is deterministic discrete integer
damage.** This is the phase's deliberate reversal. Phase 2's FR-4 states that armies are inert once
launched — no interception, no recall, no change of owner — and its §6 scopes interception out;
both are corrected in place, in the PR that lands this, rather than left reading as still-true.
Considered for the damage model: continuous per-tick attrition proportional to time spent in range,
which feels smoother. Rejected: the damage taken then depends on the whole path geometry, so every
test expectation becomes a computed float rather than a stated integer, and the first float
accumulator in the simulation is how determinism dies quietly. Chosen: while an enemy army is
within a tower's range, the tower removes a fixed number of units every N ticks, N and the range
coming from the level table (D-22). An army whose strength reaches zero is destroyed and never
arrives. Consequences: army strength becomes mutable state inside the aggregate (consistent with
D-13, not a new pattern); tower fire is evaluated once per tick inside `Advance`, so it inherits
D-12's determinism and chunk-independence for free; and it touches **D-20**, since elimination
requires zero bases *and* zero armies in flight — a tower kill can now be the event that eliminates
a player, which phase 2's rule already handles correctly but never had a way to trigger.

**D-25: the action menu's options, costs, and affordability are a pure `MW3.Core` query; the
widget lays out, draws, and hit-tests, and decides nothing.** Considered: `BaseActionMenu` reading
the base's garrison and level and working out for itself which buttons to grey — the obvious way to
build a menu, and the way that puts "can I upgrade this?" in the one place headless tests cannot
reach. Rejected for exactly the reason D-18 rejected hit-testing in the screen. Chosen: Core answers
"what actions does this base offer its owner right now, what does each cost, and which are
affordable", unit-tested with no graphics device; the widget renders that answer. What this forbids:
a screen computing a cost, comparing it to a garrison, or constructing a command it has not been
told is available. What it protects: the phase-2 convention that a screen submits no command it can
determine will be rejected now has a Core-side source of truth instead of duplicated arithmetic —
and phase 2's #24 review finding (a value computed from live state used in a command without being
re-validated against that same live state, now a standing note in `docs/CONVENTIONS.md`) applies
directly here, because an affordable action can stop being affordable between the menu opening and
the button being pressed.

**D-26: an open menu makes the match screen modal, and that state lives in the screen.** The menu
is the first thing in the game that can swallow input. Considered: letting map drags continue to
work underneath it. Rejected: a press that begins on a menu button and drifts onto a base would
otherwise send an army the player never asked for. Chosen: while the menu is open, the match
screen's map input — drag-to-send and its selection highlight — is suppressed entirely; a press
outside the menu dismisses it and does nothing else. This is presentation state, not match state:
`MW3.Core` never learns that a menu exists, and a script that opens a menu and a script that does
not produce the same simulation. Phase 2's press-began-before rule for dismissing the outcome
screen (FR-7) is the precedent — a release from a press that began before the menu opened must not
activate a button under it.

## 5. Cross-cutting conventions

Build-mode Ivan applies these without being asked. Phase 2's conventions all still hold; these are
the additions and the ones this phase makes newly load-bearing:

- **Presentation reads, commands write** — unchanged, and now including the menu. A public setter
  on a Core match type is still a defect, and so is a screen doing affordability arithmetic (D-25).
- **One command type family for humans and AI.** Upgrade and convert are commands submitted through
  the runner exactly as sends are. If the AI can upgrade in a way the human's menu cannot express,
  or vice versa, the command model is wrong — fix the model, do not add a side channel.
- **Every tuning number lives in the level table** (D-22), never inline at a call site and never in
  a screen. A magic `20` in `MatchScreen` is a defect.
- **A phase-2 document that this phase makes untrue is corrected in the same PR**, not left to be
  discovered. FR-4's reversal of army inertness is the known case; anything else found gets the
  same treatment, following the precedent phase 2's FR-6 set for FR-3 and FR-5.
- **Tower fire allocates nothing per tick.** It runs every tick for every tower against every
  in-flight army on a phone (REQUIREMENTS §5); the obvious LINQ implementation is not acceptable
  here even though it would be elsewhere in Core.
- **No engine type in `MW3.Core`, no wall-clock read, no `Random`** (D-2, D-12, D-14, D-15). Tower
  geometry is computed on normalized `MapPoint` values with integer tick arithmetic.
- **Every rules feature lands with headless tests over whole ticks**, including at least one that
  advances a full match and asserts a board state, not only unit-level assertions.
- **Presentation is verified by screenshot and scripted commands** (D-9, D-17) — never by eye as a
  routine step — and this phase adds no new QA mechanism (§2a).
- **The gate is the standard** (`./gate.ps1`, built `-m:1`), and `MW3.Core` staying engine-free is
  checked the same way every phase has checked it.
