# Requirements — Sending armies the MW2 way

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`6557880e12f5`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 4 is how you attack. Phases 2 and 3 made a match a loop and then a decision, but sending is
still the crudest verb in the game: one tap sends exactly half a garrison, and it lands as a single
object resolving in one arithmetic step. Mushroom Wars 2's sending is two mechanics MW3 does not
have yet.

First, a send arrives as successive 8-unit **waves** rather than all at once. Waves "don't strike
simultaneously" (`MW2-RULES.md` §3.3): the defender regenerates between waves, an owned tower keeps
firing into the column, and reinforcements can land mid-fight. This is what makes defending a
larger attack viable at all, and closing it is the precondition for everything else this phase does.

Second, the attacker chooses **how much** to send — 25/50/75/100% of the garrison — instead of a
fixed half. On top of that sits **snaking**: setting the picker to 25% and tapping repeatedly
produces a tapered column, used for deception ("make a 35-unit attack look like a 15-unit attack")
and for spreading a garrison defensively across several threatened bases.

Together these close parity gaps **G-2** and **G-3** — the two highest-leverage gaps left after
morale (`MW2-PARITY.md` §2.1) — and G-3 is the gap phase 3 explicitly deferred to "its own phase"
(`docs/base-upgrades-and-types/REQUIREMENTS.md` §6).

Rules stay in the engine-free `MW3.Core` and stay headlessly testable; presentation stays
deliberately plain — the minimum that makes a wave, a column, and a chosen strength legible. This
phase adds **depth to how you attack, not breadth**: no morale, no forges, no new modes, no second
map.

## 2. Target users

- **The player** — the developer, on their own Android device. The question this phase answers for
  them is no longer "can I win" but "how do I want to spend this garrison" — snake it thin, commit
  it all at once, or hold some back knowing the defender gets time to react between waves.
- **The developer** — still the implementer, and still the reason every rule must be verifiable
  headlessly.

## 3. Success criteria

Observable outcomes, not features:

1. A match on a physical Android device shows a send at each of the four strengths, a snaked column
   from repeated 25% taps, and a large send visibly arriving in waves with the defender's garrison
   changing between them — with no crash and no dead end.
2. A defender with a tower survives an attack it would have lost as a single arrival, because the
   tower gets multiple shots into the column — provable headlessly as a specific board-state
   comparison, not merely asserted.
3. The whole of the new simulation runs headlessly in tests — strength-to-count conversion, wave
   splitting, per-wave combat, capture and the recapture grace mid-column — with no graphics device
   and no wall-clock dependency.
4. Determinism (D-12) survives waves: replaying the same commands against the same starting state
   produces the same outcome every time, including which wave captured a building and on which tick.
5. A send of 8 units or fewer behaves identically to today's single-arrival send — bit-for-bit —
   so this phase does not regress any existing test or `qa/scripts/` budget at the small-send edge.
6. `qa-verifier` confirms each feature unattended through the existing `--script` / `--dump-state` /
   `--screenshot` mechanisms (D-17), without a new verification mechanism being invented.
7. `./gate.ps1` passes locally and in CI throughout, and `MW3.Core` still contains no engine type.

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the
user and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: fa6d69f05f9d): The developer can have a send computed as an explicit 25/50/75/100% of the
source garrison rather than a fixed half, so that both the human path and the AI share one strength
calculation instead of duplicated arithmetic. `MW3.Core` only; `SendArmyCommand` already carries an
explicit `UnitCount` — "choosing a count is the caller's policy, not a rule" per its own doc — so
no command shape changes. The AI's own
strength choice does not change this phase — it keeps using Half through the new shared
calculation — varying AI strength is left to a later AI feature (parity **G-21** territory).
Kicked off 30-07-2026.
  - Settled at kickoff: scope is `MW3.Core` only, this feature — `MatchScreen`'s human send path
    keeps its own inline half-arithmetic until FR-2 wires in the picker, rather than swapping that
    call site now.
  - Acceptance: `SendStrength` enum exists in `MW3.Core` with exactly four members: `Quarter = 25`,
    `Half = 50`, `ThreeQuarters = 75`, `Full = 100`.
  - Acceptance: `SendStrengthCalculator.Compute(int garrison, SendStrength strength)` exists in
    `MW3.Core`, is a pure function (no engine type, no I/O, no randomness), and returns
    `Math.Max(1, garrison * (int)strength / 100)` — floor division, clamped to a minimum of 1.
  - Acceptance: a headless parameterized unit test covers all four strengths against a
    representative range of garrison sizes, including small garrisons where flooring would
    otherwise yield 0 (e.g. garrison 1-3 at `Quarter`), asserting the floor-and-clamp-to-1 result.
  - Acceptance: `AiBrain`'s send-size call sites (`ClampedSendSize` and the `unclampedHalf`
    computation) no longer contain their own `garrison / 2` / `Math.Max(1, ...)` arithmetic; they
    call `SendStrengthCalculator.Compute(garrison, SendStrength.Half)` instead.
  - Acceptance: every existing test asserting the AI sends half its garrison still passes with no
    expected-value changes — this feature is a zero-observable-behavior-change refactor for the AI.
  - Acceptance: `MatchScreen.cs`'s human send path (`Math.Max(1, source.GarrisonCount / 2)`) is
    left untouched this feature; FR-2 owns wiring it to the picker and the shared calculator.
  - Acceptance: `SendArmyCommand`'s shape (`IssuingPlayer, SourceBaseId, TargetBaseId, UnitCount`)
    does not change.
  - Acceptance: `./gate.ps1` passes locally, and `MW3.Core` still contains no engine type.
  - Out of scope: the strength picker UI/gesture and snaking (FR-2); wiring `MatchScreen`'s human
    send path to the shared calculator (FR-2); the AI varying its own strength choice (later AI
    feature, parity G-21); wave splitting and per-wave combat (FR-3); any new drawing (FR-4).

FR-2 (wf: 4d4a9bac3f90): The player can choose a send strength from a persistent 25/50/75/100%
control on both input heads before dragging to send, and can snake a garrison by repeating a 25%
send. Reads FR-1's command shape; adds no rule of its own. `MW3.Game` only — `MW3.Core` is not
modified, since FR-1 (#54) already shipped the enum and the calculator. Kicked off 30-07-2026
(issue #58).
  - Settled at kickoff: the control is a **bottom-left vertical strip**, four buttons stacked
    upward with `25` at the bottom and `100` at the top — 25% is the repeatedly-tapped snaking
    option and so sits nearest a resting thumb on the MI PAD 4 in landscape. A vertically-centred
    left strip was rejected: the human's home base sits at `(0.12, 0.50)` with
    `HitTester.SelectionThresholdUnits = 0.1`, so a centred strip would have covered the
    most-pressed base in the game.
  - Settled at kickoff: **no new script directive.** `ScriptParser` is unchanged; a QA script
    selects a strength by pressing the control with the existing `down`/`up`, exactly as it presses
    an action-menu button — which also verifies the widget's real hit-testing, where a directive
    would bypass it entirely. This supersedes `ARCHITECTURE.md` §2a's expectation of one new
    directive; that section is corrected in this feature's PR.
  - Settled at kickoff: layout constants are presentation (`minDimension` fractions in the widget,
    as `BaseActionMenu`'s are), not a §"Tuning values" entry — D-22 governs simulation numbers.
  - Settled at kickoff: **snaking uses the existing drag gesture, repeated.** MW2's sticky
    selection — source stays selected, tap the *target* repeatedly (`MW2-RULES.md` §3.3) — is not
    adopted; §6's "exactly one drag gesture per send" holds. The tapering that makes snaking worth
    having is present either way, and D-32 means it needs no code at all: this feature's obligation
    is to *demonstrate* snaking under script, not to implement it.
  - Acceptance: a new `SendStrengthSelector` widget in `MW3.Game` owns the control's layout,
    drawing, and hit-testing and decides nothing itself (D-25); it draws four buttons in a
    bottom-left vertical strip ordered `25`, `50`, `75`, `100` upward, sized and spaced as
    fractions of the viewport's smaller dimension.
  - Acceptance: every button lies at least `HitTester.SelectionThresholdUnits` (0.1 normalized)
    from every base position in `MapLayout`, asserted by a headless test against the real map, so
    no press is ever contested between the control and a base.
  - Acceptance: the selected strength is drawn visibly differently from the other three (selected
    affordable-coloured, the rest greyed, reusing the menu's palette and button texture), so a
    screenshot alone shows which is active.
  - Acceptance: the selection defaults to `SendStrength.Half` when a match screen opens and a fresh
    match starts at `Half` again; press-then-release on a button sets the strength, matching
    `BaseActionMenu`'s activation pattern; and the selection persists across sends until changed.
  - Acceptance: a press beginning on the control never selects a base and never starts a send drag
    — the control is hit-tested before `HitTester.FindBaseAt`, and a press it consumes leaves the
    selected source null.
  - Acceptance: a completed drag from an owned source to a different base issues a
    `SendArmyCommand` whose `UnitCount` equals
    `SendStrengthCalculator.Compute(source.GarrisonCount, <selected strength>)`, garrison read at
    release, and `MatchScreen.cs` is left with no `GarrisonCount / 2` or other inline percentage
    arithmetic anywhere.
  - Acceptance: while an action menu is open, a press on the control only dismisses the menu — no
    strength change, no selection, release swallowed — exactly as D-26 specifies for a press
    outside the menu's buttons.
  - Acceptance: once the outcome is decided the control accepts no presses and does not intercept
    the dismiss gesture; a press-and-release after the outcome still pops back to the welcome
    screen, including on the control, and `qa/scripts/dismiss-ending.txt` still passes.
  - Acceptance: `--dump-state` gains a screen-owned `Strength: 50` line alongside `Menu:`, written
    by `MatchScreen` and never by `MW3.Core` (D-26).
  - Acceptance: headless tests in `MW3.Game.Tests` cover the selector's layout and hit-testing
    without a graphics device, mirroring `BaseActionMenuTests` — a press on each of the four
    buttons, and a press in the gap between two resolving to none.
  - Acceptance: a new `qa/scripts/` script selects 25% and drags from the human base to the nearest
    neutral, with `--dump-state` showing `Strength: 25` and one army whose `Count=` is a quarter of
    the source garrison rather than half.
  - Acceptance: a new `qa/scripts/` script demonstrates snaking — 25% selected, the same drag
    repeated three times, `--dump-state` showing three in-flight armies from that source with
    strictly decreasing `Count=`.
  - Acceptance: every existing `qa/scripts/` drag-to-send script is unchanged and still produces
    the same command, because the default is `Half`.
  - Acceptance: a `--screenshot` run shows the control with one option highlighted, and the same
    run on the MI PAD 4 in landscape shows it fully inside the viewport, tappable, and overlapping
    no base — rebuilding and reinstalling from the branch first, per follow-up #28's lesson.
  - Acceptance: `ARCHITECTURE.md` §2a is corrected in this PR to record that no new script
    directive was added, and `docs/reference/MW2-PARITY.md`'s **G-3** row is updated to record the
    picker as closed by FR-1/FR-2, noting that MW2's sticky tap-the-target selection is not adopted.
  - Acceptance: `./gate.ps1` passes locally, CI is green, and `MW3.Core` still contains no engine
    type.
  - Out of scope: any `MW3.Core` change (FR-1, #54); wave splitting and the wave interval (FR-3);
    the drawn tapered column and visible tower fire (FR-4); a new `--script` directive of any kind;
    the AI varying its own strength (parity G-21); MW2's sticky selection and multiselect/converging
    attacks (§6); morale, energy, heroes, forges (G-1, G-4, G-5, G-6).

FR-3 (wf: ed9c0ead836c, issue #61): The developer can have a send split into successive 8-unit waves
that arrive and resolve independently — so the defender regenerates, an owned tower gets multiple
shots, and capture (with the recapture grace) is decided per wave rather than for the whole send at
once. This is the phase's structural feature and closes parity **G-2** at the rules level. Every
rule lives in `MW3.Core`; the one presentation edit is the per-army `--dump-state` line, which
`MatchScreen` writes because Core never formats output — so the "`MW3.Core` only" shorthand this
entry originally used is corrected here. Independent of FR-2 (#58) in both directions: different
projects, colliding only on separate lines of `DumpState`. Kicked off 30-07-2026.
  - Settled at kickoff: the wave interval is **5 ticks (250 ms)** — MW3's own number, since MW2
    publishes none (`MW2-RULES.md` §3.3, §10). Above the fastest tower's 3-tick fire period so every
    wave gap admits a fresh shot at any tower level, and low enough that an ordinary 40-unit send
    finishes launching in 20 ticks — inside every travel edge on the map, whose shortest is 30 —
    while only a maxed 80-unit commitment (10 waves) stretches to 45. Same "MW3's own number where
    MW2 is silent" treatment FR-4 gave tower range and fire period (parity G-13, G-22). See
    "Tuning values" below.
  - Settled at kickoff (**D-35**): waves 2..N wait in a private pending list inside `Match` and
    enter `ArmiesInFlight` on their own launch tick; wave 1 enters at `Execute` as today. D-33 had
    not considered where an unlaunched wave lives, and `PositionAtTick` would have extrapolated one
    to a point *behind* the source base while `EvaluateTowerFireAtTick` walked it every tick.
    Rejected: clamping `PositionAtTick` to the source, which lets a forward tower shred queued waves
    — a strong emergent effect nobody designed; and staggering arrival instead of launch, which
    breaks `ArmySpeedUnitsPerTick` as a real constant (D-27).
  - Settled at kickoff: the whole send's units leave the source garrison once, at `Execute`, as
    today — so capturing the source mid-column cannot recall or strand a later wave, keeping
    `Army`'s doc comment and `UnitCountExceedsGarrison`'s meaning intact. Draining the garrison wave
    by wave is MW2-faithful but a materially larger change; settled against for this phase.
  - Acceptance: `Match.Execute(SendArmyCommand)` splits an accepted send of `n` units into
    `ceil(n / 8)` ordinary `Army` objects — full 8-unit waves plus a final wave carrying the
    remainder, so 20 units becomes 8, 8, 4 in that order (D-33). No new `Army` subtype.
  - Acceptance: a send of 8 units or fewer produces exactly one `Army` launched on the submission
    tick, with the same id, count, launch tick and arrival tick it produces today — bit-identical,
    asserted by a test pinning all five values. `SendArmyCommand`'s shape and every
    `SendArmyOutcome` member are unchanged, and every existing rejection still leaves all state
    untouched, including leaving no pending wave behind.
  - Acceptance: a pending wave's launch tick is an `Advance` boundary alongside arrivals and
    construction completions, evaluated after construction completion and before tower fire and
    arrivals — so a wave is a legitimate tower target from the tick it launches, never before. A
    test with an enemy tower covering the sending base proves pending waves take no losses while
    they wait. Nothing launches once `Outcome` leaves `InProgress` (phase 2 FR-7's freeze).
  - Acceptance: `Army` gains get-only `SendId`, `WaveIndex` (1-based), and `WaveCount`. Every wave
    from one `Execute` shares one `SendId`, drawn from a counter separate from the army-id counter;
    a single-wave send still gets one and reports `1/1`. No code that resolves an army branches on
    `WaveIndex` or `WaveCount` — not `ResolveArrival`, `CombatResolver`, `EvaluateTowerFireAtTick`,
    or `EvaluateOutcome` (ARCHITECTURE.md §5, "a wave is not a special case").
  - Acceptance: each wave resolves through the existing `ResolveArrival` and `CombatResolver` with
    no change to either. A capturing wave demotes the base and later waves **of the same send** then
    reinforce it rather than fight; the recapture grace works mid-column, proved by a test where
    wave 1 captures, the defender's reinforcement recaptures inside `RecaptureGraceTicks`, and wave
    2 arrives against the restored owner.
  - Acceptance: above 8 units, waves are **deliberately weaker for the attacker** than a single
    arrival, and this is asserted rather than discovered — `Bu = (a/d) × Wu` floors per wave, so ten
    8-unit waves against a 140% defender do `10 × floor(800/140) = 50` damage where one 80-unit
    arrival would do `floor(8000/140) = 57`. The test computes the single-arrival figure from
    `CombatResolver` rather than hardcoding it.
  - Acceptance: a test proves the tower mechanism specifically (§3 success criterion 2).
    `EvaluateTowerFireAtTick` fires one shot at the nearest enemy army per period, so the column's
    advantage to the defender is its longer total time in range, **not** a higher fire rate: 80
    units sent past a level-1 tower lose strictly more in transit than the
    `floor(range ÷ ArmySpeedUnitsPerTick ÷ firePeriod)` shots a single army covering the same
    distance could take, and the defending base is still held at the end.
  - Acceptance: `WaveSizeUnits = 8` and `WaveIntervalTicks = 5` are named `MW3.Core` constants with
    no literal at any call site, held by a new `SendWaveCalculator` that mirrors
    `SendStrengthCalculator` — pure, engine-free, exposing `WaveCount(int)`,
    `UnitsInWave(int, int)`, and `LaunchTickOffset(int)`, returning scalars by index and **never a
    collection**, so splitting allocates nothing beyond the `Army` objects. A parameterized test
    covers 1, 7, 8, 9, 16, 20, 80, and 100 units and asserts the per-wave units sum back to the
    input.
  - Acceptance: splitting is a pure function of the command and the submission tick (D-12, D-14,
    D-15) — launch tick is `submissionTick + (waveIndex - 1) × WaveIntervalTicks`. Advancing a match
    containing a multi-wave send in one call and in arbitrary chunks produces identical state,
    including which wave captured and on which tick, following `AiTowerRoutingDeterminismTests`'
    pattern with the three new fields added to its projection. Nothing allocates per tick: the
    per-boundary pending-launch scan is an index loop with no LINQ.
  - Acceptance: `--dump-state`'s per-army line gains three fields with existing fields unchanged in
    name, order, and meaning —
    `Army 3: Owner=Human Source=1 Target=3 Count=8 Launch=120 Arrival=154 Send=2 Wave=1/3`, with a
    single-arrival send reading `Wave=1/1`. **No new script directive and no new command-line
    flag**; `ScriptParser` is untouched.
  - Acceptance: every existing `MW3.Core.Tests` assertion that a send yields exactly one army is
    re-authored against the new behaviour, **never weakened** — the standing rule since FR-3a. This
    covers the `ArmiesInFlight.Single()` sites in `TowerFireTests`, `RecaptureGraceTests`,
    `CombatTests`, `CaptureDemotionTests`, `ConvertTests`, `ConstructionTests`, and `AiBrainTests`;
    a test whose send is ≤ 8 units keeps `.Single()` legitimately. No test is deleted and no
    assertion is relaxed to leave a send's shape unobserved.
  - Acceptance: `AiBrain` is not modified — its three `ArmiesInFlight` readers are already wave-safe
    (two boolean any-checks; `AssessThreat` sums `UnitCount` rather than counting armies). A test
    pins the accepted consequence that `AssessThreat` now weighs a whole column's units against the
    garrison predicted at wave 1's arrival, making the AI slightly more defensive.
  - Acceptance: two new `qa/scripts/` scripts — one waiting for the human base to reach its level-1
    cap of 20 then dragging to a neutral, showing two armies with the same `Send=`, `Wave=1/2` and
    `Wave=2/2`, counts 8 and 2, and launch ticks exactly 5 apart; one pinning `Wave=1/1` for a send
    of 8 or fewer. Every existing committed script passes unchanged in its budget, or has the budget
    re-derived with the reason recorded in the PR — never raised merely to turn a red script green.
  - Acceptance: `ARCHITECTURE.md` records D-35 with its rejected alternatives, replaces D-33's "the
    wave interval is deliberately not fixed here" paragraph with a pointer to the table below, and
    corrects §2a's per-army dump description to the field names actually shipped.
    `docs/reference/MW2-PARITY.md`'s **G-2** row becomes closed-for-rules by FR-3 with the drawn
    column owed by FR-4, recording that the interval is MW3's own number.
  - Acceptance: `./gate.ps1` passes locally, CI is green, and `MW3.Core` still targets
    `netstandard2.1` with no `Microsoft.Xna` or `MonoGame` text.
  - Out of scope: the drawn tapered column and visible tower fire (FR-4); the strength picker,
    snaking, and the `Strength:` dump line (FR-2, #58); any new script directive or command-line
    flag; row density (parity G-20, needs persistence S-9); the AI varying its send strength (parity
    G-21 — `AiBrain` keeps sending at `Half`); multiselect and converging multi-building attacks
    (§6); morale, energy, heroes, forges (G-1, G-4, G-5, G-6); draining the source garrison wave by
    wave instead of once at `Execute`.

FR-4 (wf: a3e0351a6c4b): The player can see a multi-wave send drawn as a tapered column rather than
a single marker, with a tower visibly firing into it as waves pass. `MW3.Game` only; adds no rule of
its own, reading FR-3's wave-grouping metadata.

### Tuning values

Every simulation number this phase introduces, per D-22's routing rule: a constant lives in a table
settled at `/kickoff`, never inline at a call site. Phase 3's own table
(`docs/base-upgrades-and-types/REQUIREMENTS.md` §4) is unchanged and still in force — this phase adds
to it rather than replacing anything.

| Constant | Value | Where | Source and derivation |
|---|---|---|---|
| `WaveSizeUnits` | 8 units | `SendWaveCalculator` | MW2's published wave size (`MW2-RULES.md` §3.3, `[S]`). A larger send arrives as several waves of this size, remainder last (D-33). |
| `WaveIntervalTicks` | 5 ticks (250 ms) | `SendWaveCalculator` | **MW3's own number** — MW2 publishes no interval anywhere found, only that a passive skill shortens it (`MW2-RULES.md` §3.3, §10; parity **G-20**). Settled at FR-3's kickoff, 30-07-2026. |

The interval's derivation, recorded because the number is invented rather than sourced and a later
phase may need to re-derive it against a different tick rate or map:

- **Lower bound — it must be worth having.** `EvaluateTowerFireAtTick` fires one shot at the nearest
  enemy army per fire period, and the tower ladder's periods are 6/5/4/3 ticks (levels 1–4). An
  interval at or below 3 would let several waves pass between shots at every tower level, leaving
  the mechanic close to inert. 5 sits above the fastest tower's period, so every wave gap admits a
  fresh shot at any level.
- **Upper bound — a big attack must still read as one attack.** On this map, army speed
  `0.01`/tick makes the shortest base-to-base travel 30 ticks, home to nearest neutral 34, and home
  to home 76. An ordinary mid-game send (40 units, 5 waves) finishes launching in 20 ticks, inside
  every edge on the map. Only the largest reachable commitment — a level-4 village at its cap of 80,
  sent at 100%, so 10 waves — stretches to 45 ticks, which is the intended "a big attack is a long
  visible column you can react to" feel rather than a slow trickle. The modelled maximum (a level-5
  village's cap of 100, 13 waves) spans 60 ticks.
- **Precedent.** This is the same treatment FR-4 gave tower range and fire period in phase 3 where
  MW2 was equally silent (parity **G-13**, **G-22**): derive against MW3's own tick and speed
  constants, record the reasoning, and mark it in the parity file as MW3's number rather than a
  parity claim.

This supersedes `ARCHITECTURE.md` D-33's closing paragraph, which deliberately left the interval
unfixed for this kickoff to settle. Its guidance — calibrate against `TickDurationMilliseconds` and
`ArmySpeedUnitsPerTick`, and keep a maximum send's launch span comfortably inside travel time — was
followed, with one deliberate departure: that paragraph's arithmetic implied an interval of 2 or
less, which the lower bound above rules out as inert. The binding constraint is the *reachable*
maximum send (10 waves), not the modelled one (13).

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Determinism remains a hard requirement** (D-12, S-8). Splitting a send into staggered waves at
  `Execute` time must be a pure function of the command and the tick it is submitted on — no
  wall-clock read, no accumulated state that behaves differently across a chunked `Advance`.
- **Tuning values enter only through a kickoff-settled table** (D-22, `CLAUDE.md`). The wave
  interval is MW3's own number — MW2 never publishes it, only that a passive skill (out of scope,
  parity **G-20**) shortens it — so FR-3's kickoff derived and recorded it the way FR-4 derived tower
  range and fire period against MW3's own tick and speed constants. **Settled 30-07-2026 at 5 ticks
  (250 ms); see §4 "Tuning values" for the derivation.**
- **No allocation per tick.** Even a maximum send (a capped level-5 village's 100 units, 13 waves)
  must not allocate a collection per tick on the wave-splitting or per-wave resolution path — the
  same standing rule phase 3's tower fire established.
- **The engine-free rules layer still binds** (S-2, D-2). Wave splitting, the strength calculator,
  and wave grouping metadata all live in `MW3.Core` with no engine type.
- **Unattended verifiability without new mechanisms.** `--script`, `--dump-state`, and
  `--screenshot` (D-17) already carry everything this phase needs — a strength control is a new
  screen element read the same way the action menu's buttons are, and waves are additional
  in-flight armies the existing per-army dump line already reports.
- **Device QA is available and device criteria are blocking** (as it has been since phase 3 FR-2),
  following the lesson from follow-up #28: rebuild and reinstall from `main` before trusting any
  device observation that contradicts a passing headless test.
- No auth, no persistence, no network, and no accessibility or performance targets this phase.

## 6. Out of scope

Explicit non-goals for this phase — these are what stop `/autopilot` drifting.

> Read these as sequencing, not design positions (the standing convention since phase 3's
> mid-phase correction — `docs/base-upgrades-and-types/REQUIREMENTS.md` §6). Where a bullet
> excludes something MW2 has, it is excluded **from this phase** and owed by a later one, tracked
> as a numbered gap in `docs/reference/MW2-PARITY.md` §2.

- **Morale, energy, and heroes** (parity **G-1**, **G-4**, **G-5**). A send's strength and wave
  shape do not change based on either player's morale this phase; the combat resolver's morale term
  stays fixed at identity exactly as phase 3 FR-3b left it.
- **Forges** (parity **G-6**). Still two base types.
- **Row density** (the passive skill that shortens the wave interval, parity **G-20**). This phase
  ships one fixed baseline interval; a passive-skill system to modify it is a later phase's job and
  needs persistence this project does not have (S-9).
- **The AI varying its send strength.** FR-1's shared calculator is available to `AiBrain`, but the
  AI keeps sending at Half this phase — teaching it to choose a strength situationally is parity
  **G-21** territory (MW2's AI is undocumented) and belongs to a dedicated AI feature.
- **Multiselect and converging attacks.** MW2's multi-building send that times several attacks to
  land together (`MW2-RULES.md` §3.3) is not taken this phase — there is still exactly one drag
  gesture per send.
- **A second map, campaign structure, art, sound, or animation.** Unchanged from phase 3's
  exclusions.
- **Anything server, account, login, or multiplayer** (S-7).

## 7. Open questions

None. The one question raised in discovery — whether "row density" is a base-level mechanic or a
passive skill — was resolved with the user (30-07-2026): it is a passive skill (§2, `MW2-
ITEMS-AND-PROGRESSION.md`), so it is out of scope here (§6) and this phase ships a single fixed
baseline wave interval instead, settled as a tuning value at FR-3's kickoff.
