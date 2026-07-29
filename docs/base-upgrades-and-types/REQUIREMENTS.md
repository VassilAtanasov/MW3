# Requirements — Base upgrades and types

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`1dd3b0f977af`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 3 is the first real decision. Phase 2 made the game a loop, but every match it produces is
the same match, because the only choice a player ever makes is *where* to send half a garrison.
This phase adds the choice Mushroom Wars 2 is actually built on: spending units on your own bases
instead of at your enemy.

Three things arrive together, and none of them works without the others. Garrisons gain a
**capacity cap**, so a base stops growing on its own and the economy stops being a free ramp. Bases
gain **levels**, bought with units from that base's own garrison, raising both its production rate
and its cap. And a second **base type** appears — a tower, which produces nothing and shoots enemy
armies passing within its range. Because units are finite and can be burned on infrastructure,
"attack now or grow first" becomes a genuine trade-off rather than a foregone conclusion.

The tower deliberately reverses a phase 2 rule: armies are no longer inert once launched (D-15,
phase 2 FR-4). An army can lose units in transit and be destroyed before it ever arrives. That is
the single largest change to the simulation this phase makes, and it is made on purpose rather than
discovered mid-build.

**A correction arrives mid-phase** (added 28-07-2026, after FR-1, FR-2, and FR-3 merged). Phase 3
was designed before `docs/reference/` existed, so its ladder — three levels, caps 20/35/50, upgrades
at 6 and 16, conversion at 10 — was invented to be testable rather than sourced. The reference now
documents what Mushroom Wars 2 actually does, and the project's goal is to be as close to it as
possible, which makes those numbers not conservative but wrong. FR-3a, FR-3b, and FR-3c realign the
shipped code: MW2's literal economy on a tick rate that can express it, levels that buy defence with
combat resolved by MW2's ratio formula, and build time with a recapture grace window. They sit
between FR-3 and FR-4 in dependency order, and FR-4, FR-5, and FR-6 are re-discovered on top of
them rather than built against numbers that are about to change.

The AI learns to make the same trade-offs, so a match is won or lost for a reason rather than by
arithmetic. Rules stay in the engine-free `MW3.Core` and stay headlessly testable; presentation
stays deliberately plain — shapes and numbers, plus the minimum that makes a level, a cap, a type,
a range, and a transit loss legible. This phase adds **depth to the one match, not breadth**: no
second map, no campaign, no art.

## 2. Target users

- **The player** — the developer, on their own Android device, playing a match that now has more
  than one kind of decision in it. The question this phase answers for them is no longer "can I
  finish a match" (phase 2 settled that) but "did I lose because of something I chose".
- **The developer** — still the implementer, and still the reason every rule must be verifiable
  headlessly. No second human enters the picture this phase either.

## 3. Success criteria

Observable outcomes, not features:

1. A match can be played on a physical Android device in which the player upgrades a base, converts
   a base to a tower and back, and sees an enemy army shot down in transit — with no crash and no
   dead end.
2. Two matches played from the identical starting state diverge because of investment choices
   alone: a player who upgrades early and one who attacks early reach measurably different board
   states, provable headlessly.
3. The whole of the new simulation runs headlessly in tests — caps, production rates, upgrade and
   convert commands, tower fire, transit losses, army destruction, capture demotion, and the
   extended AI — with no graphics device and no wall-clock dependency.
4. Determinism (D-12) survives the reversal: replaying the same commands against the same starting
   state produces the same outcome, every time, including which armies were shot down and when.
5. The AI is not trivially beatable by upgrading — a headless match against a human that upgrades
   its base and turtles does not hand the human a free win.
6. `qa-verifier` confirms each feature unattended through the existing `--script` / `--dump-state` /
   `--screenshot` mechanisms (D-17), without a new verification mechanism being invented.
7. `./gate.ps1` passes locally and in CI throughout, and `MW3.Core` still contains no engine type.
8. **The correction leaves nothing half-migrated.** After FR-3a, FR-3b, and FR-3c, no tuning number
   in `MW3.Core`, in a test, or in a `qa/scripts/` budget still comes from the staging ladder, and
   every value that a table row claims is MW2's is traceable to a cited row in
   `docs/reference/MW2-RULES.md`. Re-authoring tests against the new numbers is expected work for
   these three features, not evidence of a defect — but a test *weakened* rather than re-authored
   is a defect, and the distinction is that a re-authored test still asserts the same behaviour.

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the
user and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: 4ec5d7b58f7c, issue #30): The developer can cap a base's self-production, give bases a
level, and spend units from a base's own garrison to raise that level, so that growth becomes
finite and investment becomes possible. Core only; draws nothing.
  - Acceptance: the level ladder is named Core constants — three levels, caps 20/35/50, production
    periods 10/7/5 ticks, upgrade costs 6 to reach level 2 and 16 to reach level 3 — read by both
    the simulation and the tests, with no tuning number repeated at a call site (D-22).
    **Superseded by FR-3a** (29-07-2026): this was the staging ladder invented before
    `docs/reference/` existed; it is replaced wholesale by MW2's literal numbers — see "Tuning
    values" below for the ladder actually in force.
  - Acceptance: every base starts at level 1, so a fresh match is unchanged from phase 2 (10/10/5,
    one unit per 10 ticks); level and current cap are readable with no public setter (D-13).
  - Acceptance: an owned level-1 base reaches exactly 20 and stops — still exactly 20 after 1000
    ticks; neutral bases still never produce, cap or no cap.
  - Acceptance: at or above cap, production progress does not accumulate **at all** — a base held
    at cap for 500 ticks then dropped below it by a send produces its next unit a full period
    later, not immediately (D-21).
  - Acceptance: a garrison may exceed its cap with nothing destroyed; the base simply does not
    produce until it is back under.
  - Acceptance: `UpgradeCommand` carries the issuing player and base id, is submitted only through
    `Match.Execute`, and returns acceptance or each rejection reason in the type system, mirroring
    `SendArmyCommand` — never a bool, an exception for an ordinary rejection, or a silent no-op.
  - Acceptance: rejected leaving all state untouched — unknown base id, base not owned by the
    issuer (neutral included), already at max level, garrison below the cost, or the match outcome
    already decided (phase 2 FR-7's freeze covers this command too).
  - Acceptance: an accepted upgrade subtracts the cost immediately. **Superseded by FR-3c**: the
    level itself, and the new cap and period, no longer take effect from that tick — they wait for
    the build's completion tick (D-30).
  - Acceptance: upgrading down to zero garrison is legal — the base stays owned, resumes producing,
    and can be taken by one unit, exactly as a base emptied by a send does.
  - Acceptance: production progress carries across an upgrade — 6 ticks into a 10-tick period
    becomes 6 ticks into the new 7-tick period and produces on the next tick; progress frozen at
    the cap resumes rather than banking units.
  - Acceptance: a base changing owner through combat drops one level, flooring at 1 (the level half
    of D-23); reinforcing a base its owner already holds never changes its level; demotion may
    leave the garrison above the new lower cap, which is legal and merely blocks production.
  - Acceptance: determinism (D-12) — identical levels, garrisons, caps, and production progress
    whether `Advance` runs in one call or irregular chunks, proved on a run containing a capped
    base, an upgrade, and a capture.
  - Acceptance: `MW3.Core` still targets `netstandard2.1`, contains no `Microsoft.Xna` or
    `MonoGame` text, and no `DateTime`, `DateTimeOffset`, `Stopwatch`, `Environment.TickCount`, or
    `Random`; tests advance whole matches rather than asserting getters.
  - Acceptance: every pre-existing test and committed `qa/scripts/` script still passes within its
    documented budget, and where the cap legitimately invalidates a phase-2 expectation it is
    corrected in place in the same PR — never weakened or dodged by raising the cap.
    `qa/scripts/victory.txt` and `MatchOutcomeTests`' hand-authored victory sequence are the known
    casualties and are re-authored so victory is still reached and still proved attainable under
    the cap; the passive-human defeat test still reaches defeat with the AI owning all six bases,
    its tick budget re-stated in phase 2's docs if capped garrisons genuinely change it.
  - Acceptance: `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-2 (wf: bea15b8431a8, issue #32): The player can tap a base they own to open an action menu laid
out on an arc above it, see each action's unit cost, see an unaffordable action greyed out, dismiss
the menu with a press elsewhere, and upgrade the base from it — with the base's level visible on the
map as its ring thickness and its cap legible in the menu. This is the game's first real UI widget;
the convert options join it in FR-5.
  - Acceptance: a press and release on the same base the human owns opens the menu anchored there
    (phase 2's silent cancel on that gesture is gone); the same gesture on a neutral or AI base does
    nothing; a press released over a *different* base still sends an army exactly as phase 2 FR-5
    defined, and opens no menu.
  - Acceptance: while the menu is open, a press — the down, not the release — anywhere outside it
    dismisses it and does nothing else: no army sent, no highlight, no second menu opened, including
    on another owned base; the matching release does nothing either.
  - Acceptance: a button activates on release only when the press that began the interaction landed
    on that same button (D-26, following FR-7's press-began-before precedent).
  - Acceptance: the menu dismisses itself with no player input if its base stops being owned by the
    human, or if the match outcome is decided.
  - Acceptance: while open, drag-to-send and the gold selection highlight are suppressed entirely,
    but the match keeps advancing — opening a menu is not a pause — and a `--script` run that opens
    and dismisses a menu reaches the same simulation state at the same tick as one that does not.
  - Acceptance: `MW3.Core` answers "what actions does this base offer its owner now, at what cost,
    and is it available" for a player and base id, unit-tested with no graphics device; `MW3.Game`
    computes no cost, compares nothing against a garrison, and constructs no command it was not told
    is available (D-25). The query returns nothing for a base the issuer does not own.
  - Acceptance: exactly one action this phase — Upgrade — its cost read from `LevelTable` and never
    named by the caller; availability is three distinct states in the type system (affordable,
    garrison below cost, already at max level), never a bool and never an exception.
  - Acceptance: the Upgrade button is always present on an owned base's menu — enabled with its cost
    when affordable, greyed with its cost when the garrison is below it, greyed reading `Max` with
    no cost at level 3 (staging ladder; **superseded by FR-3a**, which moves the village menu's `Max`
    to level 4 — see "Tuning values"); pressing a greyed button submits nothing and leaves the menu
    open.
  - Acceptance: the menu shows the base's garrison against its cap (`12 / 35`) — the only place the
    cap is legible — and tracks live state while open, flipping between greyed and enabled as the
    garrison crosses the cost, re-queried only when that base's garrison or level actually changes
    and allocating nothing per frame. (The example figures are the retired staging ladder's; see
    "Tuning values" for the caps actually shown today.)
  - Acceptance: releasing on an enabled Upgrade submits `UpgradeCommand` through the runner and
    dismisses the menu; Core's outcome is authoritative, so an upgrade that stopped being affordable
    between opening and release leaves all match state untouched (phase 2 #24's finding); an
    accepted one drops the garrison by exactly the cost and thickens the ring in the next frame.
  - Acceptance: every base draws its level as outline ring thickness — three thicknesses
    distinguishable in a screenshot at both 1280x720 and 1808x1018, tinted by owner as the fill is,
    sized from the viewport and the level table with no magic number in `MatchScreen` (D-14, D-22).
    The map still draws the bare garrison count: no cap, no level numeral.
  - Acceptance: no new script directive and no new command-line flag. `--dump-state`'s per-base line
    gains `Level=` and `Cap=` with existing fields unchanged in name, order, and meaning
    (`Base 1: Owner=Human Garrison=12 Level=2 Cap=35`), and one presentation line is added, written
    by the screen and never by Core: `Menu: none`, or
    `Menu: Base=1 Garrison=12/35 Upgrade=Affordable Cost=16` with `Upgrade=` one of `Affordable`,
    `GarrisonBelowCost`, `AlreadyAtMaxLevel` and `Cost=0` at max level.
  - Acceptance: new `qa/scripts/` covering open, dismiss by tapping empty space, upgrade, a greyed
    Upgrade doing nothing, and a drag suppressed while a menu is open — each asserted through dumps;
    plus screenshots at both sizes showing the arc fully inside the viewport for the map's top base
    row at y=0.25, so the clamp is exercised rather than merely written.
  - Acceptance (device, blocking): on the MI Pad 4 (`43e75e5`, viewport ~1808x1018),
    `adb shell input tap` on the human base opens the menu, a second tap on Upgrade upgrades it, and
    a tap on empty space dismisses it — verified against a freshly built and installed APK whose
    `lastUpdateTime` is newer than the branch build.
  - Acceptance: every pre-existing test and committed script still passes in its budget. No
    committed script releases on its source base, so none is expected to change; the phase-2 *test*
    asserting release-on-source cancels is corrected in place in the same PR to assert the menu
    opens instead, as is `docs/core-gameplay-loop/REQUIREMENTS.md`'s FR-5 line stating that rule.
    Release over *no* base still cancels; only release over the source changes meaning.
  - Acceptance: `MW3.Core` still targets `netstandard2.1` with no `Microsoft.Xna` or `MonoGame`
    text, and `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-3 (wf: ace16ed72ce6, issue #34): The developer can convert an owned base between producer and
tower in either direction for 10 units, where a tower holds and defends a garrison but produces
nothing, so that the second base type exists as rules. Core only; shooting is FR-4's job.
  - Acceptance: every base carries a type in the type system — producer or tower, an enum, never a
    bool or a string — readable with no public setter (D-13) and changed only through
    `Match.Execute(ConvertCommand)`, never through `Advance`. Every base starts a producer and
    neutral bases are producers, so a fresh match is exactly the match FR-1 left behind.
  - Acceptance: an owned tower never produces — garrison unchanged after 1000 ticks at any level,
    and production progress zero at every tick rather than frozen at a value. **Corrected by FR-3a**
    (28-07-2026): a tower does not report a garrison cap at all — MW2 publishes no capacity column
    for one, so `GarrisonCap` is absent (`null`) rather than present and inert. This shipped feature
    originally claimed a tower "still reports a garrison cap from its level"; that was FR-1's staging
    ladder, which gave every base type the same cap column. Arrivals still stack above any garrison
    total exactly as D-21 already allows, cap or no cap.
  - Acceptance: a tower is a base in every other respect this phase touches — reinforced, attacked,
    captured, upgraded, and sent from — and no tower branch is added to the send path.
    **Corrected by FR-3b** (29-07-2026): a tower does not fight with no defence bonus. Combat stayed
    phase 2's plain 1:1 arithmetic only until FR-3b shipped; a tower now defends at its level's
    percentage (140→200%) exactly as D-29 describes, which is the whole reason a tower is a
    defensive structure rather than one that merely trades production for range.
  - Acceptance: `UpgradeCommand` is accepted on a tower at the same costs from the same table: the
    level rises by one and the base still produces nothing. For a tower a level buys fire period and
    range, which FR-4 reads; here it is asserted as the level changing while production stays zero.
  - Acceptance: `ConvertCommand` carries the issuing player, the base id, and the **target type** —
    not a toggle — is submitted only through `Match.Execute`, and returns acceptance or each
    rejection reason in the type system, mirroring `UpgradeCommand` and `SendArmyCommand`.
  - Acceptance: rejected leaving all state untouched — unknown base id, base not owned by the issuer
    (neutral included), base already of the target type, garrison below the cost, or the match
    outcome already decided (phase 2 FR-7's freeze covers this command too).
  - Acceptance: the cost is one named Core constant — 10 units, identical in both directions — read
    from the tuning table by both the simulation and the tests, with no tuning number at a call site
    (D-22). The level table gains only this constant; the tower fire period and range columns arrive
    with FR-4, the feature that reads them.
  - Acceptance: an accepted convert subtracts the cost immediately. **Superseded by FR-3c**: setting
    the type, resetting the level to `LevelTable.MinLevel`, and zeroing production progress in both
    directions no longer happen from that tick — they wait for the build's completion tick (D-30). A
    new tower still banks nothing, and a base converted back to a producer still starts a fresh
    period rather than inheriting progress from before it was a tower, once the build completes.
  - Acceptance: converting down to exactly zero garrison is legal, as upgrading or sending to zero
    already is; converting a producer at or above its cap is legal and the resulting tower keeps
    that garrison.
  - Acceptance: capture keeps the type while dropping one level (D-23, whose level half FR-1
    implemented) — a captured tower is a tower one level lower that still produces nothing for its
    new owner, and a captured producer is a producer.
  - Acceptance: elimination and outcome are unchanged (D-20) — a tower is an owned base and counts
    as one. A player holding only towers is not eliminated and simply cannot produce: a legal board
    state, asserted as such rather than treated as a defect.
  - Acceptance: determinism (D-12) — identical types, levels, garrisons, and production progress
    whether `Advance` runs in one call or irregular chunks, proved on a run containing a conversion,
    the upgrade of a tower, and the capture of a tower. The type check on the production path
    allocates nothing per tick.
  - Acceptance: `MW3.Core` still targets `netstandard2.1`, contains no `Microsoft.Xna` or `MonoGame`
    text, and no `DateTime`, `DateTimeOffset`, `Stopwatch`, `Environment.TickCount`, or `Random`;
    tests advance whole matches, including one that converts, upgrades, captures, and asserts a
    board state.
  - Acceptance (scope guard): the Core action query still offers exactly one action, Upgrade —
    convert is not offered until FR-5, so `MW3.Game` is untouched, the action menu gains no button,
    and no screenshot changes. `--dump-state` gains no `Type=` field, which arrives with FR-5 exactly
    as FR-1 deferred `Level=` and `Cap=` to FR-2. No new script directive and no new flag.
  - Acceptance: every pre-existing test and committed `qa/scripts/` script still passes unchanged
    within its budget — every base starts a producer, so nothing about an existing match differs —
    and `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-3a (wf: f5f3320ec408, issue #38): The developer can play the match on MW2's literal economy —
five village levels and four tower levels, MW2's caps and production rates, upgrade costs of 5/10/20
and a flat 20 for towers, and a conversion price of 30 — on a 50 ms tick that can express them, so
that phase 3's staging numbers are replaced by the reference's. Closes parity **G-8**, **G-14**, and
§3's tick-rate decision. Core, plus the FR-2 presentation the new ladder invalidates.
  - Acceptance: `LevelTable` splits into a village ladder and a tower ladder (D-28); a `Base` reads
    the ladder for its own `BaseType` and no caller selects a table by hand.
  - Acceptance: villages have five levels, caps 20/40/60/80/100, production periods 60/30/20/15/12
    ticks, and upgrades costing 5/10/20 to reach levels 2/3/4 (`MW2-RULES.md` §2.2); towers have
    four levels at a flat 20 per upgrade (§2.3); conversion costs 30 both directions and still
    resets to level 1 (§2.1). Every value read from its table with none repeated at a call site.
  - Acceptance: village level 5 is defined and not reachable by upgrading — `UpgradeCommand` on a
    level-4 village is rejected with the existing `AlreadyAtMaxLevel` and no new rejection reason is
    invented. A level-4 tower rejects the same way.
  - Acceptance: **a tower has no garrison cap** — absent in the type system rather than present and
    inert, because MW2 publishes no capacity column for towers. Every reader handles the empty case
    explicitly and none substitutes a sentinel; `Cap=none` is the dump rendering, defined here so
    FR-5 inherits it. Nothing about a tower's behaviour changes, since a tower never produces.
  - Acceptance: `Match.TickDurationMilliseconds` is 50 and `ArmySpeedUnitsPerTick` is 0.01, with the
    map still taking five seconds to cross, asserted directly. `MatchRunner.DecisionIntervalTicks`
    becomes 40, preserving the AI's two-second cadence rather than silently doubling how often it
    acts.
  - Acceptance: five village and four tower ring thicknesses, adjacent ones distinguishable at
    1280x720 and 1808x1018; the action menu reads `Max` at village level 4, not 3, and shows the new
    caps; `--dump-state` is unchanged in field name, order, and meaning with only the values moving.
    No new field, script directive, or command-line flag.
  - Acceptance: behaviour survives the re-tuning — a level-1 village stops at exactly 20 and stays
    there; progress does not accumulate at or above cap (D-21, D-21a); a garrison may exceed its cap
    with nothing destroyed; progress carries across an upgrade; capture drops one level flooring at
    1 (D-23); the opening move is unchanged in kind (garrison 10, first upgrade 5).
  - Acceptance: **a level-1 base cannot be converted at all** — cap 20 against a cost of 30 — so a
    tower requires reaching level 2 first. MW2's actual shape, asserted as a board state so it
    cannot regress silently.
  - Acceptance: determinism (D-12) across single-call and irregular-chunk advances, proved on a run
    containing a capped base, an upgrade, a conversion, and a capture.
  - Acceptance (corrections in the same PR): `LevelTable`'s XML doc, FR-3's claim below that a tower
    "still reports a garrison cap from its level", and any doc naming the staging numbers as
    current. `MW2-PARITY.md` moves **G-8** and **G-14** out of §2 and records §3's tick rate as
    shipped.
  - Acceptance: every pre-existing test and committed script still passes; where the new numbers
    invalidate an expectation it is re-authored in place to assert the same behaviour — never
    weakened, deleted, or dodged by adjusting a tuning value to suit a test. Budgets are re-authored
    for the roughly 3× slower economy; `qa/scripts/victory.txt` and `MatchOutcomeTests`' victory
    sequence are the known casualties again.
  - Acceptance (device, blocking): on the MI Pad 4 (`43e75e5`), a base upgraded twice shows three
    visibly distinct ring thicknesses and a menu reading the new caps, against a freshly installed
    APK whose `lastUpdateTime` is newer than the branch build.
  - Acceptance: `MW3.Core` still targets `netstandard2.1` and is engine-free;
    `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-3b (wf: f585a0868ecc, issue #39): The developer can have a base's level buy defence as well as
economy, with combat resolved by MW2's `Bu = (a/d) × Wu` rather than 1:1, so that a level-1 tower is
as defensible as a level-5 village and the formula morale and forges later feed already exists.
Closes parity **G-9**, **G-10**, and most of **G-7**. Deliberately reverses D-15 and D-22's "levels
buy economy only, never combat strength"; **depends on FR-3a (#38)** for the tables the defence
column hangs on, and must not be started before it merges.
  - Acceptance: the village ladder gains defence 100/110/120/130/140 (`MW2-RULES.md` §2.2) and the
    tower ladder 140/170/190/200 (§2.3), as integer percentages in the tables with none repeated at
    a call site. A level-1 tower and a level-5 village defend identically, asserted directly. A
    neutral base is a level-1 producer at 100%, so taking neutrals is unchanged.
  - Acceptance: one Core resolver takes `a`, `d`, `Wu`, and `Du` — no arithmetic inline in
    `ResolveArrival` (D-29). `a` and `d` are composed from named contributions: the building's own
    defence, plus a morale and a forge term on both sides, each a named constant fixed at 100 with a
    comment naming parity **G-1** and **G-6**. A later feature supplies a value; it does not add a
    parameter.
  - Acceptance: **no stacking rule is chosen.** `MW2-RULES.md` §4.3 marks multiplicative-versus-
    additive stacking `[?]`, and with exactly one live term it is unobservable — resolving it here
    would be an unsourced divergence.
  - Acceptance: the attacker captures **iff `Wu × a > Du × d`**, as integer cross-multiplication
    with no division and no rounding in the decision — algebraically identical to MW2's
    `Du − (a/d) × Wu < 0` (§4.1). Strictly greater, so an exact tie leaves the defender holding zero.
  - Acceptance: on a capture the attacker's surviving garrison is `(Wu × a − Du × d) / d` floored,
    minimum 1; on a hold the defender keeps `Du − (Wu × a) / d` floored, never negative.
    Reinforcement is untouched — defence never applies to your own arriving units — and no
    floating-point value appears anywhere on the path (D-12, D-24).
  - Acceptance (worked cases, exact integers): 100% is unchanged from today (10 v 10 holds at 0, 11
    captures with 1); a level-1 tower holding 10 survives a 14-unit wave at zero and falls to 15 with
    1; a single unit captures an empty level-4 tower and arrives with 1, which falls out of the exact
    comparison rather than needing a special case; a level-3 village holding 10 survives 12 and falls
    to 13.
  - Acceptance: capture demotion (D-23), elimination and outcome (D-20), and the freeze once decided
    are all unchanged. Determinism (D-12) across chunked advances on a run containing an attack on an
    upgraded base, an attack on a tower, and a capture. The resolver allocates nothing per arrival.
  - Acceptance (corrections in the same PR): `ResolveArrival`'s XML doc; **D-15** and **D-22**
    annotated as superseded with the reason rather than deleted; FR-3's claim that a tower fights
    with no defence bonus of any kind; any other doc stating combat is 1:1. `MW2-PARITY.md` moves
    **G-9** and **G-10** out of §2 and records **G-7** as partially closed.
  - Acceptance: level-1 combat tests pass **unchanged**; where an upgraded base or a tower is
    involved the expectation is re-authored in place to assert the same behaviour under the new
    arithmetic — never weakened, deleted, or dodged by lowering a defence percentage to suit a test.
    `MW3.Core` stays `netstandard2.1` and engine-free; `dotnet build MW3.slnx -warnaserror -m:1` and
    `./gate.ps1` both pass.

FR-3c (wf: a4c8cacb426a, issue #40): The developer can have an upgrade or a conversion take MW2's
build time instead of completing instantly, and a building retaken within one second not demote a
further level, so that infrastructure costs time as well as units and thrash is not rewarded. Closes
parity **G-11** and **G-12**, and introduces this phase's first genuinely new state — a base under
construction. **Depends on FR-3a (#38)** for the tick rate every duration is expressed in; FR-3b
(#39) is a soft dependency only.
  - Acceptance: durations are named table constants, never literals (D-22), from `MW2-RULES.md`
    §2.2 and §2.3 — whose Time columns are identical for both building types. At 50 ms: conversion
    100 ticks, upgrade to level 2/3/4 costing 100/200/300 ticks. The recapture grace is 20 ticks
    (§2.5), likewise named and derived from the tick duration.
  - Acceptance: cost is still deducted immediately on acceptance; only the benefit is delayed. An
    accepted command records a completion tick and a target, readable with no public setter. A base
    already under construction rejects both `UpgradeCommand` and `ConvertCommand` with a new distinct
    reason — no queue, no second concurrent build, and no cancel. Every existing rejection reason
    still applies and still takes precedence where it does.
  - Acceptance: **a base under construction keeps working** — it produces at its *current* level's
    period with the cap and D-21a progress invariant unchanged, defends at its current level, and is
    reinforced, attacked, captured, and sent from as normal. Type changes on completion, never on
    command. Settled at kickoff 28-07-2026: MW2 does not publish this, and the alternatives (halting
    production, or halting production and dropping defence) were rejected as inventions that would
    thread a second reason-for-zero through the production path, the AI's lookahead, and every
    production test.
  - Acceptance: completion happens inside `Advance` at the exact recorded tick, and **a completion
    tick is a segment boundary** exactly as an arrival tick is, so production is never credited
    across a period change at the wrong rate. Within a tick, construction completes **before**
    arrivals, so a base finishing an upgrade on the tick it is attacked defends at its new level. An
    upgrade carries production progress; a conversion zeroes it. Nothing completes once the outcome
    is decided.
  - Acceptance: a base captured mid-build has its construction **discarded** and the spend is **not
    refunded** — both by existing precedent rather than new rules (D-21a already discards a previous
    owner's partial progress; `MW2-PARITY.md` §1 already records no refund on conversion as at
    parity). Capture demotion (D-23) still applies on top.
  - Acceptance: each base remembers its last owner-change tick and the owner it had immediately
    before it. Demotion is skipped when the base changed owner within 20 ticks **and** the capturing
    player is that previous owner — a true *retake* per §2.5's wording, inclusive at exactly 20. The
    distinction is asserted in the one observable case: neutral → human → AI within 20 ticks does
    **not** grant the AI the grace. The grace suppresses only the demotion; it restores nothing and
    does not touch conversion's independent level reset.
  - Acceptance (the one deliberate presentation change): a base under construction is drawn
    distinguishably from both its current and its completed target level, visible at 1280x720 and
    1808x1018 and provable by screenshot comparison. This breaks the phase's Core-only-then-FR-5
    rhythm on purpose — FR-2's Upgrade button already ships, so deferring all feedback would mean a
    shipped control that appears to do nothing for 5-15 seconds. The Core action query gains a
    fourth availability state so the button greys while a build runs; FR-2's three existing states
    keep their names and meanings.
  - Acceptance: `--dump-state` gains exactly one per-base field with every other field unchanged in
    name, order, and meaning — `Building=none`, or `Building=UpgradeToLevel3@1240` /
    `Building=ConvertToTower@1300` where the number is the completion tick. The `Menu:` line's
    `Upgrade=` token gains the new availability value. No new script directive and no new flag. New
    `qa/scripts/` dumping both before and after completion.
  - Acceptance: determinism (D-12) across single-call and irregular-chunk advances for construction
    state and owner-change ticks, proved on a run containing a completed build, a build lost to a
    capture, and a recapture inside the grace window. Nothing allocated per tick.
  - Acceptance (corrections in the same PR): §6's build-time bullet, FR-3's kickoff statement that
    upgrading and converting are instant, and the XML docs on `UpgradeCommand`, `ConvertCommand`,
    and `Match.Execute`. `MW2-PARITY.md` moves **G-11** and **G-12** out of §2.
  - Acceptance: every pre-existing test and committed script passes; one that assumed an instant
    upgrade is re-authored in place to advance past the completion tick — never dodged by shortening
    a build time to suit it. Device criterion blocking on the MI Pad 4. `MW3.Core` stays
    `netstandard2.1` and engine-free; `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1`
    both pass.

> **FR-4 has been re-kicked-off** (28-07-2026) and its entry below is current. Issue #36 was updated
> in place rather than replaced, since it was still Todo with no branch and no code. It has since
> merged (29-07-2026, issue #36 / PR #45).
>
> **FR-5 and FR-6 were re-discovered** (29-07-2026), after FR-3a/b/c and FR-4 merged. FR-5's slice
> was confirmed unchanged — everything FR-3a/b/c added (the five/four-level ladder, the deliberate
> under-construction ring, the fourth menu availability state) was already drawn by the feature that
> introduced it, so FR-5 still owes only the convert button, tower/range visuals, and the shrinking
> army count. FR-6 was found to be oversized — "upgrades, converts, respects caps" bundled a pure
> economy decision with the AI's spatial reasoning about enemy tower positions — and was split in
> two: **FR-6** now covers only upgrading and respecting caps, and the AI building/converting towers
> and routing around enemy ranges moves to a new **FR-7**, which depends on FR-6.

FR-4 (wf: b7427e502078, issue #36): The developer can have a tower fire on enemy armies passing
within its range, removing units from them in transit and destroying them outright when their count
reaches zero, so that towers do something and armies stop being inert. Core only; this is the
phase's deliberate reversal of phase 2 FR-4, and it touches the elimination rule (D-20).
**Re-kicked-off 28-07-2026** against FR-3a's tick rate and four-level tower ladder; issue #36 was
updated in place. **Depends on FR-3a (#38)**; FR-3b (#39) and FR-3c (#40) are soft dependencies.
  - Acceptance (re-tuned at the 28-07-2026 re-kickoff): the tower ladder gains a range and a fire
    period per level, read by both the simulation and the tests with no tuning number at a call site
    (D-22). Ranges are **0.20 / 0.22 / 0.25 / 0.28** — MW2's published radius *ratios*
    (100/110/125/140%, `MW2-RULES.md` §2.3) applied to a level-1 anchor of 0.20 chosen by MW3,
    because MW2 gives the radius only as a percentage of an unstated base (**G-22**). Fire periods
    are **6 / 5 / 4 / 3** ticks at one unit per shot — MW3's own numbers, since MW2 never publishes
    tower damage (**G-13**) and §2.3's "shooting speed" column is marked `[?]` and explicitly
    unusable as a tuning input. The village ladder is untouched and a tower still produces nothing.
  - Acceptance: every range stays at or below 0.30 (the closest pair of bases) and below 0.34 (a
    home base to its nearest neutral), asserted against `MapLayout` rather than assumed, so a tower
    guards its own approach rather than reaching a neighbouring base.
  - Acceptance: only an **owned** tower fires, and only at armies whose owner is not the tower's
    owner; a player's own armies fly through their own towers untouched. A tower fires regardless of
    its own garrison — drained to zero by a send it still shoots, and can still be taken by a single
    unit, since a garrison is not ammunition (FR-3).
  - Acceptance: each tower tracks its own last-fire tick and fires on the first tick at which an
    enemy army is in range **and** at least its period has elapsed since its previous shot. An idle
    tower is therefore always ready and its first shot lands on the tick the enemy enters range, so
    damage taken is a function of time spent in range rather than of which tick the army entered on.
  - Acceptance: on a firing tick a tower hits exactly one army — the closest enemy army, ties broken
    by lowest army id. One tower is one gun, so a coordinated multi-army push genuinely overwhelms
    it; that is the intended counterplay, not a gap.
  - Acceptance: an army is in range when the distance from the tower to the army's current position
    is less than or equal to the level's range, inclusive at exactly the range. The army's position
    is computed each tick by interpolating source to target on
    `(tick - LaunchTick) / (ArrivalTick - LaunchTick)`, clamped to 0..1 — recomputed from those
    values every time and never accumulated across ticks, which is what keeps D-12 free.
  - Acceptance: tower fire is evaluated once per tick inside `Advance`, on **every** tick — `Advance`
    may not skip ticks for fire the way it computes production in closed form over a span; production
    stays closed-form and fire does not. The within-tick order is **construction completion → tower
    fire → arrivals → outcome**: fire before arrivals gives a tower a final shot at an army landing
    on it that tick, so an army reduced to zero on its arrival tick is destroyed and never lands, and
    construction before fire means a base whose conversion to a tower completes on tick T fires on
    tick T. If FR-3c (#40) has not merged there is no construction step and the rest of the order is
    unchanged. No tower fires once the outcome is decided (phase 2 FR-7).
  - Acceptance: a shot removes exactly one unit; strength never goes negative and never rises. An
    army at zero strength is destroyed that tick — removed from `ArmiesInFlight`, never arriving,
    delivering neither reinforcement nor attack. A survivor arrives with its **current** strength and
    resolves under whatever arrival arithmetic is in force — phase 2's 1:1 today, FR-3b's
    `Bu = (a/d) × Wu` once #39 has merged. This feature adds no combat rule of its own. Army strength
    is mutable state inside the aggregate with no public setter, changed only by `Advance` (D-13),
    consistent with `Base`.
  - Acceptance (tuning sanity, re-tuned 28-07-2026): roughly **3 / 4 / 6 / 9** units are removed from
    a full-strength army flying straight at a level 1 / 2 / 3 / 4 tower and arriving at it. Tests
    assert exact integers for constructed scenarios rather than these approximations, which vary by a
    shot with tick alignment. An army merely *passing* a tower crosses a chord rather than a radius
    and can take up to double, which is the intended reason to route around a defended position.
  - Acceptance (scope guard): this feature does **not** close parity **G-13** or **G-22**. The range
    anchor and the damage per shot are MW3's own and must be recorded as such; neither gap leaves
    `MW2-PARITY.md` §2.4 on the strength of this feature.
  - Acceptance: elimination (D-20) — a player owning zero bases whose last in-flight army is shot
    down is eliminated on that tick and the outcome decided accordingly. This is the case phase 2's
    rule always described but nothing could reach, and it is asserted directly.
  - Acceptance: determinism (D-12) — identical army strengths, tower last-fire ticks, garrisons,
    owners, and outcome whether `Advance` runs in one call or irregular chunks, including which
    armies were shot down and on which tick.
  - Acceptance: tower fire allocates nothing per tick — it runs every tick for every tower against
    every in-flight army on a phone, so the obvious LINQ implementation is not acceptable here even
    though it would be elsewhere in Core (§5). `MW3.Core` stays `netstandard2.1` and engine-free,
    with tower geometry computed on normalized `MapPoint` values.
  - Acceptance (corrections in the same PR): `Army`'s XML doc no longer claims armies are inert or
    undamageable, and `docs/core-gameplay-loop/REQUIREMENTS.md` is corrected at both sites — FR-4's
    "Armies are inert in flight: no interception, recall, or change of owner" and §6's "Rally points,
    army recall, and interception in flight". Recall and change-of-owner remain true and stay scoped
    out; only interception and damage change.
  - Acceptance (the one deliberate `MW3.Game` change): `MatchScreen` caches each army's count text by
    army id, justified by a comment stating that an army's unit count never changes in flight. That
    premise is now false and the cache would silently draw the launch count forever, so it and its
    comment are corrected here so the drawn number tracks current strength while still allocating
    nothing per frame. No other presentation change.
  - Acceptance (scope guard): `--dump-state` gains no army-strength field — it arrives with FR-5,
    exactly as FR-1 deferred `Level=` and `Cap=` to FR-2 and FR-3 deferred `Type=` to FR-5. No new
    script directive, no new flag, and the Core action query is untouched.
  - Acceptance: every pre-existing test and committed `qa/scripts/` script still passes within its
    budget — no script builds a tower, so no existing match is affected — and where a phase-2 test
    asserts inertness it is corrected in place to assert the new rule, never weakened or deleted.
    `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-5 (wf: b6e8bc28daa9, issue #48): The player can convert a base from the action menu in both
directions, see a tower drawn distinguishably from a producer, see a tower's range on screen, and
watch an army's count shrink as it is shot in transit — so that everything FR-3 and FR-4 added is
visible and reachable. Adds no rule of its own: the only `MW3.Core` change is `AvailableActions`
offering the second action it was always going to offer (D-25), and the simulation is identical
whether or not a menu is opened (D-26). Kicked off 29-07-2026.
  - Acceptance: `BaseActionKind` gains `Convert` and `AvailableActions` returns exactly two actions
    for an owned base, always in the order Upgrade, Convert, so button indices, arc layout, and dump
    output are stable. Their availability is computed **independently** — the current early return at
    max level goes, so a level-4 village offers `Upgrade=AlreadyAtMaxLevel Cost=0` alongside a live
    Convert, and a base under construction offers both as `UnderConstruction`.
  - Acceptance: the Convert cost is the existing conversion constant (30, identical both directions)
    read from the tuning table with none named at a call site (D-22), and the action carries the
    **target type** — the opposite of the base's current type — so the widget never picks one.
  - Acceptance: no new availability state is invented. A level-1 base whose cap (20) is permanently
    below the cost (30) reads `GarrisonBelowCost` like any other unaffordable action; that
    permanent-until-upgraded shape is MW2's, already settled in "Tuning values", and is asserted as a
    board state rather than special-cased.
  - Acceptance: no simulation rule changes at all — garrisons, production, combat, tower fire,
    capture, and outcome untouched, proved by every pre-existing Core test passing unchanged.
  - Acceptance: two buttons fit the existing arc fully inside the viewport at 1280x720 and 1808x1018
    including the top base row at y=0.25; labels come from the action rather than a hardcoded string
    (`Convert: 30`, `Convert: Building`), correcting `FormatLabel`'s assumption that every action is
    an Upgrade; releasing on an enabled Convert submits `ConvertCommand` and dismisses the menu, a
    greyed one submits nothing and leaves it open, and `BaseActionMenu` still compares no cost to a
    garrison (D-25).
  - Acceptance: a conversion completing while its menu is open relabels Convert to the opposite
    direction and flips the header's cap to or from `none` in the next frame, re-queried only on a
    real change and allocating nothing per frame. A tower's header reads `<garrison> / none` (D-28),
    with no sentinel standing in.
  - Acceptance: a tower is drawn as a **square** and a producer as a circle, tinted by owner and
    distinguishable in a screenshot at both sizes; the level ring, the selection highlight, and
    FR-3c's construction ring follow the shape. Core's `HitTester` keeps one radius for both types,
    so which base a tap lands on never depends on shape (D-18).
  - Acceptance: **every tower's range is drawn at all times, for both players**, as an outline in the
    owner's tint — settled at this kickoff (29-07-2026) because routing around enemy fire is the
    premise FR-7 will teach the AI, so the human must see the same board, and because an always-on
    outline is directly provable by screenshot. Considered and rejected: revealing range only while
    that tower's menu is open (leaves enemy reach permanently invisible) and only for the human's own
    towers (a design position about hidden information, not just a display choice).
  - Acceptance: the drawn range matches Core's in-range test rather than approximating it. `Match`
    measures distance in normalized `MapPoint` units while the screen maps X by viewport width and Y
    by height, so the true reach is an **ellipse** on a non-square viewport; a circle sized from the
    smaller dimension would show fire reaching where it does not. Extent read from the tower's own
    level — four visibly different sizes — with no magic number in `MatchScreen` (D-22), its texture
    created once and disposed with the others, and nothing allocated per frame.
  - Acceptance: a base under construction *into* a tower draws no range and stays a circle until its
    completion tick; a tower converting back draws its range up to the moment it completes.
  - Acceptance: the drawn army count already tracks current strength (FR-4 corrected the per-army
    text cache), so this feature adds no drawing code for it and instead **proves** it — two runs of
    one scenario dumping at different ticks show the same in-flight army with a strictly lower
    `Count=` in the later one. The per-army dump line gains no field.
  - Acceptance: `--dump-state`'s per-base line gains exactly one field, `Type=`, appended after
    `Building=` with every other field unchanged in name, order, and meaning
    (`Base 1: Owner=Human Garrison=12 Level=2 Cap=40 Building=none Type=Producer`, values matching
    the `BaseType` names). The `Menu:` line gains three tokens after the existing `Cost=`, which
    keeps its name and meaning:
    `Menu: Base=1 Garrison=12/40 Upgrade=Affordable Cost=10 Convert=GarrisonBelowCost ConvertCost=30 ConvertTo=Tower`.
    `Menu: none` is unchanged and the line is still written by `MatchScreen` (D-26). No new script
    directive and no new command-line flag.
  - Acceptance: new `qa/scripts/` — `greyed-convert-does-nothing.txt` (menu stays open, nothing
    changes), `convert-pending.txt` (garrison down 30, `Type=Producer` still,
    `Building=ConvertToTower@<tick>`, `Convert=UnderConstruction`), `convert-completed.txt`
    (`Type=Tower Cap=none Building=none`, `ConvertTo=Producer`, square with range outline), and the
    `army-shrinking-early.txt` / `army-shrinking-late.txt` pair. Screenshots at both sizes.
  - Acceptance (device, blocking): on the MI Pad 4 (`43e75e5`), a tap opens an owned base's menu, a
    tap on Convert is accepted, and after the build the base is visibly a square with a range
    outline — against a freshly installed APK whose `lastUpdateTime` is newer than the branch build.
  - Acceptance (corrections in the same PR): `BaseActionKind`'s XML doc, `BaseActionMenu`'s class and
    `Activate` docs, `MatchScreen`'s class doc ("one circle per base"), and FR-3's line deferring
    `Type=` to FR-5. `ARCHITECTURE.md` §2a and §4 already carry this kickoff's dump format, script
    list, and the range/shape decisions, so the implementation matches them rather than restating
    them. **No parity gap closes** — G-13 and G-22 in particular stay open.
  - Acceptance: every pre-existing test and committed script still passes in its budget; a test
    asserting the one-action menu is re-authored in place to assert two, never weakened. `MW3.Core`
    stays `netstandard2.1` and engine-free; `dotnet build MW3.slnx -warnaserror -m:1` and
    `./gate.ps1` both pass.

FR-6 (wf: 7eea0544b808, issue #49): The developer can face an AI opponent that upgrades its own bases
and stops pouring production into a capped one, so that the economy decision this phase adds is a
decision the opponent also makes. Extends phase 2's three-clause brain rather than replacing it.
Core-only; no new drawing, no new script directive. Pure economy — does not need to reason about
enemy positions. Building or converting towers, and routing around an enemy tower's range, is FR-7's
job, split out 28-07-2026 in discovery because it is a materially different kind of reasoning
(spatial awareness of enemy defences) than upgrading is, and bundling both risked an oversized PR.
Kicked off 29-07-2026.
  - Acceptance: `BrainDecision` carries **either** a `SendArmyCommand` **or** an `UpgradeCommand`,
    still at most one command per decision (D-16), distinguished in the type system — never a null, a
    sentinel, a list, a bool discriminant, or a second `IPlayerBrain` method. FR-7 extends the same
    seam with convert, so the shape must admit a third case without another rewrite. `MatchRunner`
    dispatches to the matching `Match.Execute` overload and remains the only submitter; `AiBrain`
    still never executes and never mutates.
  - Acceptance: clause order becomes **defend → upgrade → attack → consolidate** (D-31). Placing
    upgrade after consolidate would make it unreachable, since consolidate fires whenever the AI
    holds two or more bases and its front is untargeted.
  - Acceptance: a base is an upgrade candidate only when all of these hold, each asserted separately
    — owned by this brain's player; garrison **at or above its garrison cap**; it has a cap at all;
    not under construction; level below `MaxUpgradableLevel`; garrison at least the next level's
    cost; and no enemy army in flight to it. The "has a cap at all" test is explicit: `GarrisonCap`
    is empty for a tower (D-28), so a tower is never a candidate by the empty case rather than by a
    sentinel comparison that happens to be true.
  - Acceptance: the threatened-base guard stands alone — the cost is deducted immediately while the
    benefit lands 100+ ticks later (FR-3c), so a base upgrading under attack can hand over a capture
    it would have held. A capped, affordable, otherwise-valid base with an incoming enemy army
    produces no upgrade that decision.
  - Acceptance: among candidates the AI upgrades the **safest** — the one whose nearest not-owned
    base is furthest away, ties by lowest id. This is the consolidate clause's own
    nearest-not-owned-base distance read the other way round, so the brain carries one distance rule
    rather than two: consolidate feeds the front, upgrade develops the rear.
  - Acceptance: over a full headless match **every** command the brain produces is accepted by
    `Match.Execute` — no rejection of any kind, asserted by a test that fails on any non-acceptance
    outcome. The AI-side counterpart of D-25 and of phase 2 #24's standing note in
    `docs/CONVENTIONS.md`.
  - Acceptance: `PredictGarrison` respects base type — a tower never produces, so its predicted
    garrison is its current garrison. A live defect the moment FR-5 (#48) merges and the human can
    build towers: the brain calls the village-only `ProductionCalculator` for every owned base, which
    would inflate a tower defender and make the AI refuse attacks it would win. The window between
    the two merges is accepted, exactly as FR-4 accepted the AI flying through tower fire until FR-7.
    Prediction still shares one copy of the production arithmetic with the simulation.
  - Acceptance: **no rule forbidding the AI to reinforce a capped base.** D-21 makes the cap a
    production ceiling and blesses massing above it as strategy; a clause refusing to stage units
    would contradict a shipped decision. "Respects garrison caps" means spending a saturated base's
    surplus on a level and never predicting past a ceiling — not avoiding stacking.
  - Acceptance: an AI base reaching its cap upgrades on the next decision tick — command accepted,
    garrison down by exactly the cost, `Building=UpgradeToLevel2@<tick>` recorded, and after
    completion the base is level 2 at cap 40 on a 30-tick period, asserted as a board state. In a
    long match at least one AI base reaches level 3, proving the clause re-fires as each new cap
    saturates rather than firing once.
  - Acceptance (success criterion 5 gets its test): a headless match in which the human upgrades its
    home base and then issues no further command does not hand the human a win — the AI upgrades at
    least one base and the outcome is an AI victory. Phase 2's passive-human defeat test is extended
    rather than duplicated, its budget re-stated if this clause changes it.
  - Acceptance: determinism (D-12) across single-call and irregular-chunk advances for levels,
    garrisons, construction, owners, and outcome, on a run where one decision tick issues an upgrade
    and another completes it. Nothing allocated per tick.
  - Acceptance: no new dump field, script directive, flag, or presentation change — an AI upgrade is
    already visible through `Level=`, `Cap=`, `Building=`, and FR-2's ring thickness. One new
    `qa/scripts/` script in which the human issues nothing and the dump shows an AI base at `Level=2`
    or better with the human's still at `Level=1`. Device criterion blocking on the MI Pad 4: an AI
    base's ring visibly thickens against a freshly installed APK.
  - Acceptance (corrections in the same PR): `AiBrain`'s class doc, `BrainDecision`'s doc ("exactly
    one `SendArmyCommand`"), `ProductionCalculator`'s doc where it describes the brain's prediction,
    `docs/core-gameplay-loop/ARCHITECTURE.md`'s "three-clause heuristic: defend, attack, consolidate",
    and this phase's ARCHITECTURE §3 line claiming `AiBrain.cs` gains converting here — that is
    FR-7's. **No parity gap closes**; **G-21** stays open and is not weakened.
  - Acceptance: every pre-existing test and committed script passes in its budget; a phase-2 brain
    expectation genuinely invalidated is re-authored in place, never weakened. `MW3.Core` stays
    `netstandard2.1` and engine-free with no `Random` and no lookahead beyond one decision (D-15,
    D-16); `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-7 (wf: 8804e5cd75c4): The developer can face an AI opponent that values a tower highly enough to
convert into one, and that prefers a path or timing avoiding a costly pass through an enemy tower's
range when a cheaper option exists, so that towers are a real threat against the AI and not just the
human. **Depends on FR-6.** Core-only; no new drawing, no new script directive. Until this ships the
AI is expected to fly armies straight through tower fire — accepted through FR-4, not a defect.

### Tuning values

**FR-3a and FR-3b have both merged (29-07-2026): the table below is now the one in force.** The
staging ladder that shipped with FR-1, FR-2, and FR-3 is retired — kept below only as a historical
record of what FR-3a replaced and why it was tuned that way, not as anything a caller may still read
from. Every tuning number in `MW3.Core`, in a test, and in a `qa/scripts/` budget now comes from the
table this section documents as current, including FR-3b's defence percentages and its
`Bu = (a/d) × Wu` combat formula.

#### Superseded by FR-3a on 29-07-2026 (the former staging ladder)

Settled by FR-1's kickoff (economy), FR-3's (conversion), and FR-4's (tower columns), all
28-07-2026. No longer in force — retained only for the "why it was tuned as it was" narration below
and for anyone diffing against the merged history. `Match.TickDurationMilliseconds` was 100 ms and
`ArmySpeedUnitsPerTick` was 0.02.

| Level | Garrison cap | Ticks per unit produced | Cost to reach this level | Tower fire period | Tower range (normalized) |
|---|---|---|---|---|---|
| 1 | 20 | 10 | — (starting level) | 4 ticks | 0.20 |
| 2 | 35 | 7 | 6 units | 3 ticks | 0.25 |
| 3 | 50 | 5 | 16 units | 2 ticks | 0.30 |

#### What FR-3a shipped (MW2's literal numbers) — in force

Settled in discovery 28-07-2026, merged 29-07-2026, sourced from `docs/reference/MW2-RULES.md` §2.2
and §2.3. The tick duration is **50 ms** and `ArmySpeedUnitsPerTick` is **0.01**, preserving the
5-second map crossing; every tick budget in the tests and in `qa/scripts/` doubled accordingly. The
single `LevelTable` split, because MW2's two building types have different ladders of different
lengths.

Villages — capacity is `20 × level`, production `0.33 × level` units/sec, which at 50 ms is exactly
`60 / level` ticks. The **Defence** column is FR-3b's (merged 29-07-2026, `MW2-RULES.md` §2.2):

| Village level | Garrison cap | Ticks per unit produced | Cost to reach this level | Defence |
|---|---|---|---|---|
| 1 | 20 | 60 | — (starting level) | 100% |
| 2 | 40 | 30 | 5 units | 110% |
| 3 | 60 | 20 | 10 units | 120% |
| 4 | 80 | 15 | 20 units | 130% |
| 5 | 100 | 12 | **not reachable by upgrading** | 140% |

Towers — four levels, a flat 20 units per upgrade, **no garrison cap at all**, and (FR-3b, §2.3) a
defence percentage that already matches or beats a fully upgraded village at level 1:

| Tower level | Cost to reach this level | Garrison cap | Defence |
|---|---|---|---|
| 1 | — (arrived at by conversion) | none | 140% |
| 2 | 20 units | none | 170% |
| 3 | 20 units | none | 190% |
| 4 | 20 units | none | 200% |

**Combat resolves by `Bu = (a/d) × Wu`** (FR-3b, merged 29-07-2026), not phase 2's plain 1:1: the
attacker captures iff `Wu × a > Du × d`, integer cross-multiplication with no rounding in the
decision. `a` (attacker) and `d` (defender) each compose the defence percentage above with a morale
and a forge contribution, both fixed at 100 until parity gaps **G-1** and **G-6** exist. At `a = d =
100%` (every level-1 producer) this is bit-identical to phase 2's 1:1 arithmetic.

The tower's **gunnery columns arrive with FR-4**, the feature that reads them, exactly as FR-3
deferred them before: ranges 0.20 / 0.22 / 0.25 / 0.28 and fire periods 6 / 5 / 4 / 3 ticks at one
unit per shot, settled at FR-4's re-kickoff 28-07-2026 and recorded in full in that FR's entry
above. FR-3a itself adds only the upgrade cost and the ring thickness.

The absent cap is a decision, settled at FR-3a's kickoff 28-07-2026. MW2's tower table publishes
defence, radius, shooting speed, price, and build time — **no unit-capacity column** — and no other
source supplies one, so there is nothing to copy. Rather than carry a cap that is present and inert,
a tower's cap is **absent in the type system**: the tower ladder has no cap column and "the cap of
this base" is an optional value that is empty for a tower, handled explicitly by every reader with
no sentinel like `0` or `int.MaxValue` standing in. Nothing about a tower's behaviour changes, since
D-21 makes the cap a production ceiling and a tower never produces. This falsified FR-3's shipped
criterion that a tower "still reports a garrison cap from its level"; that line was corrected in
place by FR-3a's PR (see FR-3's own acceptance list above).

**Conversion costs 30 units** in either direction, still resetting the base to level 1.

Three consequences that are behaviour, not arithmetic, and that FR-4/5/6's re-discovery inherits:

- **A level-1 base cannot be converted at all.** Its cap is 20 and conversion costs 30, so a player
  must upgrade to level 2 (cap 40) before a tower is even possible. FR-3's kickoff chose 10
  precisely to make towers cheap early; MW2's number makes them a mid-game investment. This is the
  reference's actual behaviour, so it is a gap closed rather than a regression — but it changes the
  opening of every match and the AI's first decision.
- **Level 5 is defined and unreachable.** MW2 publishes the tier (cap 100, 1.66 units/sec) with no
  upgrade price, and prose says a village upgrades three times — `[?]` on how level 5 is reached at
  all. Settled in discovery: the table carries all five rows exactly as published, `UpgradeCommand`
  rejects at level 4 with `AlreadyAtMaxLevel`, and the menu reads `Max` there. Whatever later grants
  level 5 — map setup, a passive, a hero — finds the tier already modelled.
- **Tower range and damage per shot stay MW3's own numbers.** MW2 publishes shooting radius only as
  a percentage of an unstated base and never publishes damage at all (parity **G-13**, **G-22**), so
  there is nothing to copy. FR-4's re-discovery recalibrates them against the 50 ms tick, where an
  army covers half the distance per tick that it does today.

#### Why the staging ladder was tuned as it was

Retained because it is the reasoning FR-3a is overwriting, and because the tower recalibration below
is the standing warning about copying MW2 numbers across without checking them against MW3's speeds.

The first upgrade is deliberately cheap enough (6) to be affordable from the starting garrison of
10 without waiting, so "grow first" is a live option on the opening move rather than something a
player only saves toward. A tower shot removes 1 unit.

**The tower columns were recalibrated at FR-4's kickoff, and the reason matters.** Discovery had
proposed ranges of 0.12 / 0.15 / 0.18 with fire periods of 12 / 8 / 5. Armies travel at
`ArmySpeedUnitsPerTick` = 0.02, so an army flying at a tower is inside range for only `range × 50`
ticks — 6, 7, and 9 respectively. Against those periods a level-1 and level-2 tower would **never**
fire on an army attacking it and a level-3 tower would remove a single unit: the proposal was
non-functional, not merely weak. The settled figures give roughly **3 / 5 / 8** units removed from a
full-strength army flying straight at a level 1 / 2 / 3 tower, on top of the garrison standing in it.
Two constraints anchored the choice. Ranges stay at or below 0.30 because the closest pair of bases
on the map is exactly 0.30 apart, so a tower guards its own approach rather than reaching a
neighbour; and the home bases are 0.76 apart, so even a level-3 tower covers well under half the
board. An army merely *passing* a tower crosses a chord rather than a radius and can take up to
double that damage, which is the intended reason to route around a defended position.

**Conversion costs 10 units in either direction and resets the base to level 1** — settled at FR-3's
kickoff. The reset is the load-bearing half. It makes a tower cheap to build early, since a level-1
base loses nothing but the 10, and expensive to repurpose late, since a level-3 producer burns its
whole 22-unit ladder with it; towers therefore get built deliberately rather than by cashing a
developed economy into defence at a flat price. Two consequences to know before touching FR-4 or
FR-6. A tower never produces, so **upgrading one means shipping units to it** — a level-3 tower
costs 10 + 6 + 16 = 32 against a level-1 cap of 20 and cannot be self-funded. And the reset is
deliberately *independent* of D-23's capture demotion: a capture drops one level, a conversion drops
all of them.

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Determinism remains a hard requirement** (D-12, S-8), and this phase is where it is hardest to
  keep. Tower fire happens continuously against a moving target, which is exactly the shape of
  problem that invites a wall-clock read or a float accumulator. It resolves as integer arithmetic
  on whole ticks like everything else (D-24), or it is wrong.
- **The reversal is a reversal, and must be recorded as one.** Phase 2's FR-4 states plainly that
  armies are inert in flight, and its `REQUIREMENTS.md` §6 scopes interception out. FR-4 of this
  phase contradicts both deliberately. The phase-2 documents are **corrected in place**, in the
  same PR, the way FR-6 corrected FR-3 and FR-5 last phase — never left to read as still-true.
- **Unattended verifiability without new mechanisms.** `--script`, `--dump-state`, and
  `--screenshot` (D-17) already carry everything this phase needs: an action menu is opened by
  `down`/`up` on a base and then on a button, and levels, types, caps, and army counts are dump
  fields. If a feature seems to need a new QA mechanism, that is a signal the design put a decision
  somewhere untestable — fix the design.
- **The engine-free rules layer still binds** (S-2, D-2). Levels, caps, types, tower geometry, and
  the AI's new clauses all live in `MW3.Core` with no `Microsoft.Xna` type, `Vector2` included.
- **Per-frame allocation still matters** (D-13). Tower fire evaluates every tower against every
  in-flight army each tick; the naive implementation allocates a collection per tick per tower on a
  phone. It must not. Settled at FR-4's kickoff: because an army's position changes every tick,
  `Advance` can no longer satisfy fire by working in closed form over a span the way it does for
  production — it must visit **every** tick. Production stays closed-form; fire does not. That makes
  the no-allocation rule on this path load-bearing rather than aspirational.
- **Cost and speed of the build/run loop** remain the primary constraint (S-5): `dotnet` commands
  only, free CI, no engine binary, no paid runner.
- **Device QA is available and device criteria are blocking.** Follow-up #28 (the MI Pad 4 showing
  as `unauthorized` in `adb`, which left every device criterion on phase 2's #24 and #25 reported
  *not verifiable*) was resolved and closed on 28-07-2026; `adb devices` now shows
  `43e75e5 device`. Device-dependent criteria are therefore verified per feature and block the PR,
  as they have since the device arrived. One lesson from clearing #28 binds every feature this
  phase: an `adb install` attempted while the device was unauthorized **silently no-opped**, so
  device checks ran against a stale APK and produced a convincing false defect. Rebuild and
  reinstall from `main` before trusting any device observation that contradicts a passing headless
  test.
- **A full match is still thousands of ticks**, so `--time-scale` (phase 2 FR-7) remains the lever
  that keeps desktop scripts inside their budget, and Android device checks remain real-time.
- No auth, no persistence, no network, and no accessibility or performance targets this phase.

## 6. Out of scope

Explicit non-goals for this phase — these are what stop `/autopilot` drifting.

> **Read these as sequencing, not as design positions** (corrected 28-07-2026). The project's goal
> is a game as close as possible to Mushroom Wars 2, shipping as Bug Wars with only the IP layer
> reskinned. So where a bullet below excludes something MW2 has, it is excluded **from this phase**
> and owed by a later one — not rejected. Each such item is tracked as a numbered gap in
> `docs/reference/MW2-PARITY.md` §2, cross-referenced below. Every exclusion still binds phase 3 in
> full; none of them may be closed in build mode.

- **A third base type.** Producer and tower this phase, and nothing else. No forge, no watchtower,
  no wall, no building that grants an ability. MW2's forge is owed (parity **G-6**) and brings the
  attack/defence multipliers with it; this phase earns two types and stops there.
- **Tribe abilities, a rage meter, and any active power** the player triggers outside a base. Owed
  as MW2's hero and energy systems (parity **G-4**, **G-5**).
- **Unit types.** One unit, as phase 2 established. Levels change how fast units appear and how many
  fit, never what they are.
- **A send-strength picker.** Phase 2 fixed a send at half the garrison rounded down, minimum 1, and
  scoped a slider out; MW2's `25/50/75/100%` control is **not** taken this phase, even though the
  menu widget FR-2 introduces would make it easy to add. It changes how the game plays and needs its
  own phase rather than riding in on a UI feature — and that phase is owed rather than optional
  (parity **G-3**), since the picker is also the precondition for MW2's snaking technique.
- **A second map, a map file format, and map selection.** Still one hardcoded six-base layout — now
  with more that can happen on it.
- **Campaign structure**: no level list, progression, stars, score, statistics, or save data.
- **Art, sound, music, and animation.** A tower is a different shape or tint, not a model. Original
  art (D-5) still arrives in its own phase.
- **Anything server, account, login, or multiplayer** (S-7).
- **Randomized combat and difficulty levels** (D-15). Tower fire is deterministic integer damage,
  not a hit chance. AI tuning surfaces stay out, as does a switch to disable the AI — refused in
  phase 2 for the same reason it would be refused now.
- ~~**A defence bonus from levels.**~~ **No longer excluded — this is FR-3b** (added 28-07-2026).
  It was scoped out to keep every phase-2 combat test meaning what it meant; under the MW2 goal that
  reads as sequencing rather than design, and the sequence has arrived. Levels buy defence (villages
  100→140%, towers 140→200%) and combat becomes `Bu = (a/d) × Wu`, closing parity **G-9**, **G-10**,
  and most of **G-7**. The combat tests are re-authored deliberately, which is the cost this
  exclusion was originally deferring.
- **Army recall, rally points, and interception by armies.** FR-4 makes armies vulnerable to
  *towers* specifically. Armies still do not fight each other in transit, still cannot be recalled,
  and still travel base-to-base in a straight line — no pathfinding, no fog of war.
- **Repair, decay, and over-cap bleed.** Settled in discovery: the cap is a production ceiling, so
  arrivals stack above it freely and nothing decays back down. Nor is there a refund for converting
  a base back — conversion costs the same each way with nothing returned (10 under the retired
  staging ladder, 30 now that FR-3a has shipped), which matches MW2 and is already recorded as at
  parity.
- ~~**Build time.**~~ **No longer excluded — this is FR-3c**, shipped (added 28-07-2026, closed
  29-07-2026). Settled at FR-3's kickoff as instant, on the grounds that a build delay buys a feel
  benefit the phase could not measure; the MW2 goal makes it owed rather than optional, so it
  arrived with MW2's own 5/5/10/15 seconds and the 1-second recapture grace (parity **G-11**,
  **G-12**, both now closed). It really does bring everything that kickoff warned of — a new state
  to draw (`Base.Construction`, D-30) and a new thing the AI will need to predict once FR-6 lets it
  build — which is why it landed as its own feature rather than a rider on FR-3a. Upgrading and
  converting are therefore **no longer instant**: `UpgradeCommand` and `ConvertCommand` still deduct
  their cost immediately, but the level, type, and progress changes wait for the build's completion
  tick.
- **The cap and the level as numbers on the map.** Settled at FR-2's kickoff (28-07-2026): the map
  circle keeps the bare garrison count, the level is carried non-textually as ring thickness, and
  the cap is legible only inside the action menu — three numbers in one small circle is unreadable
  at 1808x1018. The cap remains a `--dump-state` field regardless, so nothing about it becomes
  unverifiable. If the map needs the cap later that is a deliberate change, not a build-mode
  addition.
- **Nice-to-have, explicitly deferred rather than forgotten**: a HUD totalling a player's units,
  pause, camera pan/zoom, and the app icon still owed from phase 1.

## 7. Open questions

None. Every question this phase raised is resolved.

The three under-construction questions parked here on 28-07-2026 were **closed at FR-3c's kickoff
the same day**, and are recorded rather than deleted because two of them turned out not to need a
decision at all:

1. *Does a base under construction keep producing?* **Yes**, at its current level's period — the one
   genuine choice of the three, settled with the user. Build time is a delay on the benefit, not a
   penalty on the building.
2. *What happens to construction in progress on capture?* **Discarded**, by precedent rather than by
   decision — D-21a already discards a previous owner's partial production progress for exactly the
   reason that applies here.
3. *Is the spend refunded if it falls?* **No**, likewise by precedent — `MW2-PARITY.md` §1 already
   records "refund on conversion: none" as at parity in both games, so a refund would have been a
   new divergence rather than a gap closed.

Only (1) is an MW3 invention filling an MW2 silence; it is recorded in D-30 with the alternatives
that were rejected.
