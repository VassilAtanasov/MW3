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
control on both input heads before dragging to send, and can snake a garrison by repeatedly tapping
a target at 25%. Reads FR-1's command shape; adds no rule of its own.

FR-3 (wf: ed9c0ead836c): The developer can have a send split into successive 8-unit waves that
arrive and resolve independently — so the defender regenerates, an owned tower gets multiple shots,
and capture (with the recapture grace) is decided per wave rather than for the whole send at once.
`MW3.Core` only. This is the phase's structural feature; the wave interval is a tuning value
settled at this feature's kickoff, since MW2 does not publish it (see `MW2-RULES.md` §3.3, §10).

FR-4 (wf: a3e0351a6c4b): The player can see a multi-wave send drawn as a tapered column rather than
a single marker, with a tower visibly firing into it as waves pass. `MW3.Game` only; adds no rule of
its own, reading FR-3's wave-grouping metadata.

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Determinism remains a hard requirement** (D-12, S-8). Splitting a send into staggered waves at
  `Execute` time must be a pure function of the command and the tick it is submitted on — no
  wall-clock read, no accumulated state that behaves differently across a chunked `Advance`.
- **Tuning values enter only through a kickoff-settled table** (D-22, `CLAUDE.md`). The wave
  interval is MW3's own number — MW2 never publishes it, only that a passive skill (out of scope,
  parity **G-20**) shortens it — so FR-3's kickoff derives and records it the way FR-4 derived tower
  range and fire period against MW3's own tick and speed constants.
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
