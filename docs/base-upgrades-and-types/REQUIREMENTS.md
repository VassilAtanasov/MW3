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

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the
user and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: 4ec5d7b58f7c, issue #30): The developer can cap a base's self-production, give bases a
level, and spend units from a base's own garrison to raise that level, so that growth becomes
finite and investment becomes possible. Core only; draws nothing.
  - Acceptance: the level ladder is named Core constants — three levels, caps 20/35/50, production
    periods 10/7/5 ticks, upgrade costs 6 to reach level 2 and 16 to reach level 3 — read by both
    the simulation and the tests, with no tuning number repeated at a call site (D-22).
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
  - Acceptance: an accepted upgrade subtracts the cost immediately and raises the level by exactly
    one, with the new cap and period effective from that tick.
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
    no cost at level 3; pressing a greyed button submits nothing and leaves the menu open.
  - Acceptance: the menu shows the base's garrison against its cap (`12 / 35`) — the only place the
    cap is legible — and tracks live state while open, flipping between greyed and enabled as the
    garrison crosses the cost, re-queried only when that base's garrison or level actually changes
    and allocating nothing per frame.
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
    and production progress zero at every tick rather than frozen at a value. It still reports a
    garrison cap from its level; the cap simply never binds, and arrivals stack above it exactly as
    D-21 already allows.
  - Acceptance: a tower is a base in every other respect this phase touches — reinforced, attacked,
    captured, upgraded, and sent from — with combat staying phase 2's plain 1:1 arithmetic and no
    defence bonus of any kind (D-15, D-22), and no tower branch added to the send path.
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
  - Acceptance: an accepted convert subtracts 10 immediately, sets the type, and resets the level to
    `LevelTable.MinLevel`, all effective from that tick, and zeroes production progress in both
    directions — a new tower banks nothing, and a base converted back to a producer starts a fresh
    period rather than inheriting progress from before it was a tower.
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

FR-4 (wf: b7427e502078): The developer can have a tower fire on enemy armies passing within its
range, removing units from them in transit and destroying them outright when their count reaches
zero, so that towers do something and armies stop being inert. Core only; this is the phase's
deliberate reversal of phase 2 FR-4, and it touches the elimination rule (D-20).

FR-5 (wf: b6e8bc28daa9): The player can convert a base from the action menu in both directions, see
a tower drawn distinguishably from a producer, see a tower's range on screen, and watch an army's
count shrink as it is shot in transit — so that everything FR-3 and FR-4 added is visible and
reachable. Adds no rule of its own.

FR-6 (wf: 7eea0544b808): The player can face an AI opponent that upgrades its bases, builds and
un-builds towers, and stops pouring production into a capped base, so that the new decisions are
decisions the opponent also makes. Extends phase 2's three-clause brain rather than replacing it.

### Tuning values

The economy columns are **settled** by FR-1's kickoff (28-07-2026), and the conversion cost by
FR-3's (28-07-2026); both are contract, not proposal. The tower fire columns remain a **starting
proposal for FR-4's kickoff to confirm or change**, recorded so build mode never has to invent one
and so the shape of the constant table (D-22) is concrete:

| Level | Garrison cap | Ticks per unit produced | Cost to reach this level | Tower fire period | Tower range (normalized) |
|---|---|---|---|---|---|
| 1 | 20 | 10 | — (starting level) | 12 ticks *(proposed)* | 0.12 *(proposed)* |
| 2 | 35 | 7 | 6 units | 8 ticks *(proposed)* | 0.15 *(proposed)* |
| 3 | 50 | 5 | 16 units | 5 ticks *(proposed)* | 0.18 *(proposed)* |

The first upgrade is deliberately cheap enough (6) to be affordable from the starting garrison of
10 without waiting, so "grow first" is a live option on the opening move rather than something a
player only saves toward. Still proposed: a tower shot removing 1 unit from the army it hits.

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
  phone. It must not.
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

Explicit non-goals for this phase — these are what stop `/autopilot` drifting:

- **A third base type.** Producer and tower, and nothing else. No forge, no watchtower, no wall, no
  building that grants an ability — MW2 has several; this phase earns two.
- **Tribe abilities, a rage meter, and any active power** the player triggers outside a base.
- **Unit types.** One unit, as phase 2 established. Levels change how fast units appear and how many
  fit, never what they are.
- **A send-strength picker.** Phase 2 fixed a send at half the garrison rounded down, minimum 1, and
  scoped a slider out; MW2's `25/50/75/100%` control is deliberately **not** taken this phase, even
  though the menu widget FR-2 introduces would make it easy to add. It is a separate decision about
  how the game plays, and it deserves its own phase rather than riding in on a UI feature.
- **A second map, a map file format, and map selection.** Still one hardcoded six-base layout — now
  with more that can happen on it.
- **Campaign structure**: no level list, progression, stars, score, statistics, or save data.
- **Art, sound, music, and animation.** A tower is a different shape or tint, not a model. Original
  art (D-5) still arrives in its own phase.
- **Anything server, account, login, or multiplayer** (S-7).
- **Randomized combat and difficulty levels** (D-15). Tower fire is deterministic integer damage,
  not a hit chance. AI tuning surfaces stay out, as does a switch to disable the AI — refused in
  phase 2 for the same reason it would be refused now.
- **A defence bonus from levels.** Settled in discovery: a level buys production rate and cap only,
  and combat stays phase 2's plain 1:1 arithmetic. This keeps every existing combat test meaning
  exactly what it meant.
- **Army recall, rally points, and interception by armies.** FR-4 makes armies vulnerable to
  *towers* specifically. Armies still do not fight each other in transit, still cannot be recalled,
  and still travel base-to-base in a straight line — no pathfinding, no fog of war.
- **Repair, decay, and over-cap bleed.** Settled in discovery: the cap is a production ceiling, so
  arrivals stack above it freely and nothing decays back down. Nor is there a refund for converting
  a base back — conversion costs 10 each way with nothing returned.
- **Build time.** Settled at FR-3's kickoff (28-07-2026): upgrading and converting are instant, and
  no base is ever "under construction". A build delay would be a new mechanic, a new state to draw,
  and a new thing for the AI to predict, for a feel benefit this phase cannot yet measure.
- **The cap and the level as numbers on the map.** Settled at FR-2's kickoff (28-07-2026): the map
  circle keeps the bare garrison count, the level is carried non-textually as ring thickness, and
  the cap is legible only inside the action menu — three numbers in one small circle is unreadable
  at 1808x1018. The cap remains a `--dump-state` field regardless, so nothing about it becomes
  unverifiable. If the map needs the cap later that is a deliberate change, not a build-mode
  addition.
- **Nice-to-have, explicitly deferred rather than forgotten**: a HUD totalling a player's units,
  pause, camera pan/zoom, and the app icon still owed from phase 1.

## 7. Open questions

None. Discovery closed with every question resolved.
