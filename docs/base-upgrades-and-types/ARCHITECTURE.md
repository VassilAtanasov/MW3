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

- per-base: **level**, **type** (producer/tower), **cap**, and **building** (FR-3c: what, if
  anything, the base is under construction into), alongside the existing id, owner, and garrison.
- per-army: **current strength**, which can now be lower than the count it launched with, alongside
  the existing id, owner, source, target, launch tick, and arrival tick.

Each feature's `/kickoff` fixes the exact line format and the scripts that exercise it. The
standing rule from phase 2 holds: a dump is only meaningful while the match screen is showing, and
`--dump-state` writes nothing otherwise.

**FR-2's `--dump-state` line format**, settled at kickoff: the per-base line gains `Level=` and
`Cap=` after the existing `Garrison=`, with every other field unchanged in name, order, and meaning
(`Base 1: Owner=Human Garrison=12 Level=2 Cap=35`). Exactly one further line is always written
while the match screen is showing - `Menu: none`, or
`Menu: Base=1 Garrison=12/35 Upgrade=Affordable Cost=16` with `Upgrade=` one of `Affordable`,
`GarrisonBelowCost`, `AlreadyAtMaxLevel` (`Cost=0` at max level) - and it is written by
`MatchScreen`, never by `MW3.Core`, since menu state is presentation state (D-26).

**FR-3c's `--dump-state` line format**, settled at kickoff: the per-base line gains one further
field, `Building=`, written after `Cap=` with every other field unchanged in name, order, and
meaning - `Building=none`, or `Building=UpgradeToLevel3@1240` / `Building=ConvertToTower@1300` where
the number is the completion tick. `Upgrade=` on the `Menu:` line gains the fourth value
`UnderConstruction` (D-30).

FR-2 adds six scripts under `qa/scripts/`, each opening or driving the action menu through the
existing `down`/`up` vocabulary - no new directive, no new flag:

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/open-action-menu.txt --screenshot open.png --dump-state open.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/dismiss-menu-on-empty-space.txt --screenshot dismiss.png --dump-state dismiss.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/upgrade-from-menu.txt --screenshot upgrade.png --dump-state upgrade.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/greyed-upgrade-does-nothing.txt --screenshot greyed.png --dump-state greyed.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/drag-suppressed-while-menu-open.txt --screenshot suppressed.png --dump-state suppressed.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/menu-clamped-on-top-row-base.txt --screenshot clamp.png --dump-state clamp.txt
```

`open-action-menu.txt` presses and releases on the human base: its dump shows
`Menu: Base=0 Garrison=10/20 Upgrade=Affordable Cost=6`. `dismiss-menu-on-empty-space.txt` opens
that menu, then presses empty space: `Menu: none`, no other state changed.
`upgrade-from-menu.txt` opens the menu and presses its Upgrade button: the dump shows the base's
garrison down by exactly its cost and `Menu: none`. **Superseded by FR-3c**: the level itself no
longer rises on this press - see `upgrade-pending.txt` and `upgrade-completed.txt` below for the
build-time behaviour. `greyed-upgrade-does-nothing.txt`
drains the human base below the level-1 cost first (a send halves it to 5), opens the menu (reading
`Upgrade=GarrisonBelowCost`), then presses the greyed button: the dump shows the level and garrison
unchanged and the menu **still open** (`Menu: Base=0 Garrison=5/20 Upgrade=GarrisonBelowCost
Cost=6`) - pressing a greyed button neither submits a command nor closes the menu.
`drag-suppressed-while-menu-open.txt` opens the menu, then repeats the exact press-drag-release
`send-army.txt` uses (human base to the nearest neutral): with the menu open this sends no army at
all - the down dismisses the menu (it did not land on the button) and the matching release does
nothing, so the dump shows zero armies in flight and the human base's garrison unaffected by any
send. `menu-clamped-on-top-row-base.txt` captures the top-row neutral base at y=0.25 with two
sends launched before the first lands (5 v 5 first drains it to zero without capturing, the second
takes it a couple of ticks later), then opens its menu - exercising the viewport clamp for a base
near the top edge rather than the middle, kept short enough that the AI's first decision (tick 20)
cannot land before the script ends. Re-running any of the six individually reproduces its own
screenshot byte-for-byte.

A `--screenshot` at 1808x1018 - the attached MI Pad 4's viewport, per `docs/core-gameplay-loop/
ARCHITECTURE.md`'s note on Android's chrome - is checked directly on device (D-3, D-8) rather than
by resizing the desktop window, exactly as FR-3 established for the base-layout criterion this
phase reuses for the menu.

**FR-3c adds two further scripts** (D-30), both upgrading the human base from the menu exactly as
`upgrade-from-menu.txt` does, but reading the result at two different moments instead of one:

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/upgrade-pending.txt --screenshot pending.png --dump-state pending.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/upgrade-completed.txt --screenshot completed.png --dump-state completed.txt --time-scale 50
```

`upgrade-pending.txt` presses Upgrade and immediately reopens the menu, all within the 100-tick
build: the dump shows the garrison already reduced by the cost, `Level=1` unchanged,
`Building=UpgradeToLevel2@<tick>` populated, and the menu reading `Upgrade=UnderConstruction` -
`MatchScreen` draws the base with its one deliberate pending treatment, a further ring outside the
level ring in a fixed colour, distinguishable from both the current level and the (undrawn) target
one. `upgrade-completed.txt` repeats the same press, then waits past the build using `--time-scale`
(FR-7) rather than hundreds of real-time frames, and reopens the menu once more: the dump shows
`Level=2`, `Building=none`, and `Upgrade=Affordable` again, and the screenshot shows the pending
ring gone.

**FR-5's `--dump-state` line format**, settled at kickoff 29-07-2026: the per-base line gains one
final field, `Type=`, written after `Building=` with every other field unchanged in name, order, and
meaning - `Base 1: Owner=Human Garrison=12 Level=2 Cap=40 Building=none Type=Producer`, its values
matching the `BaseType` member names (`Producer` / `Tower`). A tower still renders `Cap=none`
(D-28). The `Menu:` line gains three tokens after the existing `Cost=`, which keeps its name and its
meaning of *the Upgrade cost*:
`Menu: Base=1 Garrison=12/40 Upgrade=Affordable Cost=10 Convert=GarrisonBelowCost ConvertCost=30 ConvertTo=Tower`
- `Convert=` one of the four `BaseActionAvailability` names, `ConvertTo=` one of `Tower` /
`Producer`. `Menu: none` is unchanged. The per-**army** line gains nothing: `Count=` has reported
current strength since FR-4 made `Army.UnitCount` mutable, so an army shrinking in transit is
already observable, and FR-5 proves it with two runs dumping at different ticks rather than by adding
a field.

FR-5 adds five scripts under `qa/scripts/` - still no new directive and no new flag:

```powershell
dotnet run --project src/MW3.Desktop -- --script qa/scripts/greyed-convert-does-nothing.txt --screenshot greyed-convert.png --dump-state greyed-convert.txt
dotnet run --project src/MW3.Desktop -- --script qa/scripts/convert-pending.txt --screenshot convert-pending.png --dump-state convert-pending.txt --time-scale 50
dotnet run --project src/MW3.Desktop -- --script qa/scripts/convert-completed.txt --screenshot convert-completed.png --dump-state convert-completed.txt --time-scale 50
dotnet run --project src/MW3.Desktop -- --script qa/scripts/army-shrinking-early.txt --dump-state shrink-early.txt --time-scale 50
dotnet run --project src/MW3.Desktop -- --script qa/scripts/army-shrinking-late.txt --dump-state shrink-late.txt --time-scale 50
```

The two `army-shrinking-*` scripts are one scenario read at two moments, exactly as
`upgrade-pending` / `upgrade-completed` are: the human converts a front base to a tower and the AI's
next attack flies into its range, and the later dump shows the same in-flight AI army with a strictly
lower `Count=`.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  Match.cs                 gains levels, caps, types, tower fire, new commands
  Base.cs                  gains Level, BaseType, and a cap derived from both
  BaseType.cs              producer | tower
  LevelTable.cs            the constant tables: cap, production period, upgrade cost,
                           tower fire period, tower range - per level (D-22).
                           FR-3a splits this per building type (D-28): a village ladder of
                           five levels and a tower ladder of four, each with its own costs,
                           plus the defence percentages FR-3b adds (D-29)
  CombatResolver.cs        FR-3b: MW2's Bu = (a/d) x Wu, with morale and forge terms present
                           and fixed at 1.0 until G-1 and G-6 supply them (D-29)
  UpgradeCommand.cs        spend units from a base's own garrison to raise its level
  ConvertCommand.cs        producer <-> tower, both directions, reversible (D-23)
  BaseAction.cs            one offerable action + its cost + whether it is affordable
  MatchRunner.cs           unchanged in shape; routes the two new command types
  AiBrain.cs               gains a clause for upgrading (FR-6)
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
ticks, no allocation). Two consequences worth knowing before touching this again. First, the
invariant is **at or above the cap, progress is zero** — not merely frozen at whatever it held.
Reaching the cap by *producing* lands on zero for free, because the tick that produced the capping
unit consumed the progress that bought it; reaching it by *arrival* does not, and that path has to
zero it explicitly. Both are enforced at the write site (`ProductionCalculator.Advance` and the
reinforcement branch of `Match.ResolveArrival`) rather than left to whichever runs next, so a base
reinforced to its cap and drained again within one tick cannot smuggle banked progress through.
This was found in review on #30: the first implementation only handled the producing path, so a
base massed to its cap — the very thing D-21 exists to allow — produced early once drained.
Second, a base captured
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

> **Superseded in part, 28-07-2026.** The table-of-constants decision stands and is untouched. The
> two claims that do not survive the MW2 goal are "one table" — split per building type by **D-28**,
> because MW2's ladders differ in length — and "levels buy economy only, never combat strength",
> reversed by **D-29**, which gives levels a defence percentage and moves combat to MW2's ratio
> formula. The reasoning above is retained as the record of why the staging ladder was built that
> way, not as a rule still in force.

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

**D-27: the tick duration is chosen to make MW2's production ladder expressible, not the other way
round.** Added 28-07-2026 for FR-3a. MW2 states village production in units per second — `0.33 ×
level`, so 0.33 / 0.66 / 1.00 / 1.33 / 1.66 — and D-24 keeps all simulation arithmetic on integer
ticks, so a tick duration is only usable if every one of those five periods is a whole number of
ticks. 100 ms fails (level 4's 0.75 s is 7.5 ticks) and so does 20 ms. Considered: keeping 100 ms
and rounding each period to the nearest tick, which changes nothing structurally and costs nothing
today. Rejected because it makes numeric parity permanently unreachable while looking like it
succeeded — the ladder would be *approximately* MW2's forever, with no failing test to say so.
Chosen: **50 ms (20 Hz)**, the longest integer-millisecond tick that makes all five whole, and the
one that renders the ladder as exactly `60 / level` ticks. `ArmySpeedUnitsPerTick` halves from 0.02
to 0.01 so the map still takes five seconds to cross. Consequences to know before touching this
again: **every tick count in the codebase doubles** — tests, `qa/scripts/` budgets, the AI's
decision interval, and FR-4's tower fire periods — and a tick is now half as much wall-clock, so a
full match is twice as many `Advance` calls and the no-allocation rules on per-tick paths get
correspondingly more load-bearing.

**D-28: villages and towers get separate level tables, because MW2's ladders differ in length and in
what a level buys.** Added 28-07-2026 for FR-3a, superseding D-22's single table. Villages have five
levels and towers four; a village upgrade costs 5, 10, then 20 while a tower's is a flat 20; and a
village level buys capacity and production where a tower's buys defence, radius, and rate of fire.
Considered: one table with nullable columns, which is a smaller diff and keeps `LevelTable` as the
one name to know. Rejected — a nullable production column on a tower row is exactly the "model
absence in comments rather than in the type system" that `docs/CONVENTIONS.md` forbids, and the
level *count* differing means a single `MaxLevel` constant would already be wrong. Chosen: two
tables behind whatever the type dispatches on, with `Base` asking for its own type's ladder.
**Level 5 is present and unreachable**: MW2 publishes the tier with no upgrade price and its prose
says a village upgrades three times, so the row exists, `UpgradeCommand` rejects at level 4 with the
existing `AlreadyAtMaxLevel`, and no new rejection reason is invented for it. That asymmetry —
a defined tier no command can reach — is deliberate and must not be "fixed" in build mode.

**A tower has no cap column at all**, settled at FR-3a's kickoff 28-07-2026. MW2's tower table
publishes defence, radius, shooting speed, price, and build time and no unit capacity, so there is
nothing to source. Considered: reusing the village's `20 × level`, which keeps one rule for both
types and is nearly consequence-free because D-21 makes the cap a production ceiling and a tower
never produces. Rejected in favour of modelling the absence — a ceiling on something that cannot
produce is a rule that does nothing, and carrying an inert number invites a later feature to give it
meaning by accident. Chosen: the tower ladder has no cap column and "the cap of this base" is an
optional value, empty for a tower, which every reader handles explicitly. What this forbids: a
sentinel — `0`, `int.MaxValue`, or a magic negative — standing in for "no cap". `Cap=none` is the
`--dump-state` rendering, fixed here so FR-5 inherits it rather than inventing one. This falsifies
FR-3's shipped criterion that a tower "still reports a garrison cap from its level", which FR-3a's
PR corrects in place.

**D-29: levels buy defence, and combat becomes MW2's ratio formula with its later terms present as
identity.** Added 28-07-2026 for FR-3b, and it is a deliberate reversal of **D-22**'s "levels buy
economy only, never combat strength" and of **D-15**'s 1:1 arithmetic — both of which are corrected
in place in the PR that lands it, following the same rule FR-4 follows for phase 2's inertness
claim. MW2 resolves an arriving wave as `Bu = (a/d) × Wu`, where `a` and `d` accumulate the
attacker's and defender's multipliers. Considered: applying the defence percentage directly at the
arrival site as a single multiply, which is the whole of the behaviour this feature needs. Rejected
because morale (**G-1**) and forges (**G-6**) are both known to stack into the same `a` and `d`, and
a direct multiply is precisely the shape that has to be torn out to admit them. Chosen: a combat
resolver taking the full `a` and `d`, with the morale and forge contributions **present in the
signature and fixed at 1.0**, so those systems later supply a term instead of forcing a rewrite. The
resolver is integer arithmetic on whole units — a percentage is a ratio of integers, never a `float`
field on match state, because the first float in the simulation is how D-12 dies quietly.

**The rounding rule, settled at FR-3b's kickoff 28-07-2026: round nothing in the decision.** MW2
computes in real numbers and MW3 cannot, and the naive translation `Bu = floor(Wu × a / d)` has a
sharp failure — one unit attacking an *empty* level-1 tower gives `floor(100/140) = 0`, so
`Du_new = 0` and the defender "holds", making a drained building uncapturable and breaking a rule
FR-1 and FR-3 both shipped. Considered: flooring with a minimum of 1 damage, and rounding to nearest;
both patch the symptom and both need a special case for the empty-garrison boundary anyway. Chosen:
decide capture by **cross-multiplication** — the attacker takes the base iff `Wu × a > Du × d` —
which is algebraically identical to `Du − (a/d) × Wu < 0` and therefore matches MW2 exactly, with no
division and no rounding in the decision at all. The empty-base case then needs no rule: `Du × d` is
zero. Only the remainder rounds — the attacker's surviving garrison is `(Wu × a − Du × d) / d`
floored with a minimum of 1, and a holding defender keeps `Du − (Wu × a) / d` floored. Strictly
greater, so an exact tie leaves the defender holding zero. Worth knowing: at `a = d = 100%` this is
bit-identical to phase 2's arithmetic, which is why level-1 combat tests survive the change
untouched. What this makes true and is worth stating: a level-1 tower and a level-5
village both defend at 140%, so the tower stops being a building that trades production for range
and becomes the defensive structure MW2 has.

**D-30: an under-construction base is a state on the base, not a separate entity, and the recapture
grace is a remembered tick rather than a timer.** Added 28-07-2026 for FR-3c. Considered for build
time: a queue of pending construction jobs on the match, which is how a bigger RTS would do it and
which keeps `Base` unchanged. Rejected — it puts a base's own state somewhere other than the base,
so every read of "what is this building doing" needs two lookups, and a captured or converted base
needs a job cancelled by side effect. Chosen: the base carries the tick its construction completes
and what it is becoming; `Advance` completes it, exactly as it already resolves production and
arrivals, so it inherits D-12's chunk-independence for free. Likewise the grace window is the tick a
base last changed hands, compared against the current tick when demotion is computed — **not** a
countdown that has to be advanced, because a countdown is state that must be stepped and stepping is
what breaks under irregular chunks.

**Settled at FR-3c's kickoff 28-07-2026: a building under construction keeps working.** It produces
at its *current* level's period, defends at its current level, and is reinforced, attacked,
captured, and sent from as normal — build time is a delay on the benefit, not a penalty on the
building. Considered: halting production while building, which makes build time a genuine tempo cost
rather than only a delay; and halting production *and* dropping defence to 100%, which makes
upgrading under pressure a gamble. Both were rejected as inventions filling an MW2 silence at a real
price — each adds a second reason a base can produce zero, threading through the D-21a cap
invariant, `AiBrain`'s `ProductionCalculator` lookahead, and every production test, in exchange for
a feel benefit nothing yet measures. The chosen model is the only one of the three under which this
feature touches no AI code. This is MW3's own answer to an unpublished question, so it belongs in
`MW2-PARITY.md` §4 as a divergence rather than being presented as the reference's behaviour.

Two adjacent questions parked alongside it turned out **not** to need deciding. Construction in
progress is **discarded** on capture, because D-21a already discards a previous owner's partial
production progress for exactly the reason that applies here. And the spend is **not refunded**,
because `MW2-PARITY.md` §1 already records no refund on conversion as at parity in both games — a
refund would have been a new divergence, not a gap closed. Reaching for the existing precedent
before inventing a rule is the general move, not a one-off.

**The completion tick is a segment boundary in `Advance`**, exactly as an arrival tick is. This is
the part most likely to be got wrong: production is computed in closed form across a segment
(D-21a), so a period that changes mid-segment would be credited at one rate for the whole span.
Within a tick, construction completes **before** arrivals resolve, so a base finishing an upgrade on
the tick it is attacked defends at its new level.

**D-31: the AI upgrades a base whose production has *stalled*, and that clause outranks attacking.**
Added 29-07-2026 for FR-6. Two decisions, and they only work together. Clause order becomes **defend
→ upgrade → attack → consolidate**. Considered: appending upgrade as a fourth clause, which is the
tidiest diff — rejected because clause 3 (consolidate) fires whenever the AI holds two or more bases
and its front is not already targeted, so a fourth clause would be close to dead code dressed as a
feature. Considered: placing it after attack — rejected for the same reachability reason, though less
severely. Chosen: second, ahead of attack. The obvious objection is that an AI which grows before it
strikes gives away early tempo and free neutrals, which is the opposite of REQUIREMENTS §3's success
criterion 5 — and that objection is answered by the *gate*, not by the ordering.

The gate is saturation: a base is a candidate only when its garrison is **at or above its cap**,
which means its production has already stopped earning. Upgrading it is the move that literally
un-stalls the economy, and it preempts an attack only in the state where striking is the weaker play
anyway. So "upgrade outranks attack" is narrow in practice rather than passive. Every other condition
is a rejection the brain must not walk into: not under construction, level below the upgradable
maximum, garrison at least the next cost, and **no enemy army in flight to it** — the cost is
deducted immediately while the benefit lands 100+ ticks later (D-30), so a base upgrading under
attack can hand over a capture it would have held.

Two consequences worth stating. A tower is never an upgrade candidate under this clause, and it falls
out of the cap test rather than needing a type check: `GarrisonCap` is *empty* for a tower (D-28), so
"at or above its cap" has no answer — handled as the empty case, never by a sentinel comparison that
happens to be true. And the target rule reuses geometry the brain already has: among candidates, the
one whose nearest not-owned base is **furthest** away, which is the consolidate clause's front
calculation read the other way round. One distance rule, two clauses — consolidate feeds the front,
upgrade develops the rear.

**What this deliberately does not add**: any rule against reinforcing a base at its cap. D-21 makes
the cap a production ceiling and explicitly blesses massing above it as strategy rather than a leak,
so a clause refusing to stage units would contradict a shipped decision. "Respects garrison caps"
means the AI spends a saturated base's surplus and never predicts past a ceiling — not that it avoids
stacking. `BrainDecision` widens to carry either a send or an upgrade, still at most one command per
decision (D-16), in a shape that admits FR-7's convert as a third case without another rewrite.

**Settled at FR-5's kickoff 29-07-2026: every tower's range is drawn on the map at all times, for
both players, and it is drawn as the ellipse the rules actually describe.** MW2 publishes its
shooting radius only as a percentage of an unstated base and says nothing about whether it is shown
(parity **G-22**), so this is MW3's own answer. Considered: revealing a range only while that tower's
action menu is open, which is the cleanest map and the usual selection-driven idiom - rejected
because an *enemy* tower's reach would then never be visible at all, and routing around enemy fire is
the whole premise FR-7 teaches the AI, so the human would be playing blind against the one thing the
opponent can see. Considered: always-on for the human's towers only, which is a deliberate
hidden-information position rather than a display choice, and one no source supports. Chosen:
always, both owners, an outline in the owner's tint - the state of the board is public, as every
garrison count already is.

The second half is the part most likely to be got wrong. `Match` measures range as plain Euclidean
distance in normalized `MapPoint` units, while `MatchScreen` maps X by viewport width and Y by
viewport height (D-14). On a non-square viewport those disagree: the set of points actually within
range is an **ellipse** on screen, and a circle sized from the smaller dimension - the obvious
implementation - would draw fire reaching where it does not and failing to reach where it does. The
drawn shape is derived from the same normalized radius the simulation uses, scaled per axis, so the
picture cannot drift from the rule. If the two ever need to agree exactly, it is the drawing that
follows Core, never Core that is bent to suit the drawing.

**A tower is drawn as a square and a producer as a circle**, also settled at that kickoff: shape
rather than tint, because owner is already carried by tint and a screenshot must distinguish the two
at 1280x720 and 1808x1018. The level ring, the selection highlight, and FR-3c's construction ring
follow the base's shape. **Hit-testing does not**: `HitTester` keeps one radius for both types, so
which base a tap lands on never depends on what shape it happens to be drawn as (D-18).

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
