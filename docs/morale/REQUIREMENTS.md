# Requirements — Morale

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`3401ecb1c7a5`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 5 is tempo. Phases 2, 3 and 4 made a match a loop, then a decision, then an attack — but
nothing yet rewards or punishes *how you play over time*. A cautious player and an aggressive one
face identical arithmetic, and the only thing a match measures is who ran the numbers better.

Morale is Mushroom Wars 2's answer, and the system every source calls its skill differentiator
("always watch your own and your opponent's morale, at all times", `MW2-RULES.md` §5). It is a
**per-player** global multiplier shown as 0–5 suns. You climb it by capturing buildings, upgrading
them, and destroying units that attack you; you lose it by losing units and buildings; and — the
part that makes it a *tempo* stat rather than a stockpile — it **bleeds automatically when you stop
attacking, faster the higher you have climbed**.

Two asymmetries carry the whole design, and both are MW2's, not ours. First, across its full range
morale buys **+125% defence but only +25% attack**, so it rewards *not losing* rather than *winning
harder*. Second, **losing a building costs less than the enemy gains for taking it**, so a trade is
net-positive for the aggressor and morale flows toward whoever is pressing. Together with the
inactivity decay, that is an explicit anti-turtle, anti-snowball dial: it is why MW2 feels frantic,
and it is the single largest thing MW3 is missing.

Mechanically it closes parity **G-1** and most of **G-7**, feeding the attack and defence indices
`CombatResolver` has held at identity since phase 3 FR-3b specifically so this phase could populate
them. It also raises unit speed by up to +50%, the third and last effect in `MW2-RULES.md` §5.1's
table.

Rules stay in the engine-free `MW3.Core` and stay headlessly testable; presentation stays
deliberately plain — the minimum that makes a morale level and its movement legible. This phase adds
**depth to how a match is paced, not breadth**: no energy, no heroes, no forges, no new modes.

## 2. Target users

- **The player** — the developer, on their own Android device. The question this phase answers for
  them is no longer "how do I want to spend this garrison" but "can I afford to stop". A player who
  banks units and upgrades quietly now watches their multiplier bleed away while the opponent's
  climbs; a player who keeps pressing holds a defence bonus that makes their buildings genuinely
  hard to take.
- **The developer** — still the implementer, and still the reason every rule must be verifiable
  headlessly.

## 3. Success criteria

Observable outcomes, not features:

1. A match on a physical Android device shows both players' morale rising and falling during real
   play, with the meter reflecting captures, kills, and decay — no crash and no dead end.
2. A defender at high morale survives an attack that would have taken the building at morale 0 —
   provable headlessly as a specific board-state comparison against the *same* send, not merely
   asserted.
3. A player who stops sending visibly loses morale, and **loses it faster the higher they were** —
   proved as a tick-exact headless assertion against the decay table, including the self-slowing
   consequence that falling a level slows the bleed (§4, D-38).
4. The whole of the new simulation runs headlessly in tests — the point ladder, every gain and loss
   event, decay, the combat indices, and the speed multiplier — with no graphics device and no
   wall-clock dependency.
5. Determinism (D-12, S-8) survives morale: replaying the same commands against the same starting
   state produces the same outcome every time, including each player's morale points on every tick.
6. A match played with both players held at morale 0 behaves **bit-for-bit as it does today**, so
   this phase does not regress any existing test or `qa/scripts/` budget at the baseline.
7. `qa-verifier` confirms each feature unattended through the existing `--script` / `--dump-state` /
   `--screenshot` mechanisms (D-17), without a new verification mechanism being invented.
8. `./gate.ps1` passes locally and in CI throughout, and `MW3.Core` still contains no engine type.

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the
user and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: `c99d42cbc681`, issue #66): The developer can have each player carry a morale score that
moves on every event MW2 says it moves on, so that later features have a real multiplier to read.
Per-player points and the 0–5 sun level; every gain and loss wired — ±10 per **attacking** unit
destroyed (including by tower fire), the capture tables by building type and level and by
neutral-versus-opponent, and the upgrade tables. `MW3.Core` owns the state (D-37); the one
presentation edit is a `Morale:` line on `--dump-state`, which `MatchScreen` writes because Core
never formats output. This feature moves the number and nothing reads it — every *effect* belongs to
FR-2, FR-3 and FR-4. Kicked off 04-08-2026; the 43 verbatim acceptance criteria are on issue #66,
which is the contract `/implement`, `code-reviewer` and `qa-verifier` read.
  - Settled at kickoff: **upgrade rows map by resulting level.** A village reaching level 2 pays
    +100, level 3 pays +150, level 4 pays +200; a tower reaching level 2 pays +200, level 3 pays
    +300, level 4 pays +400. MW2's gain table indexes upgrades as "to level 1 / 2 / 3 / 4" but MW3
    has only three reachable steps per ladder (`LevelTable.Village.MaxUpgradableLevel = 4` with
    `UpgradeCost` defined for levels 1–3), so the table's "level 1" row is **unreachable** — recorded
    as unreachable rather than hidden, since no upgrade ever produces a level-1 building.
  - Settled at kickoff: **a capture reads the level held before phase 3's demotion applies** — it is
    what you took. `ResolveArrival` already demotes after its capture branch, so this is a
    read-ordering requirement rather than a restructuring one.
  - Settled at kickoff: **upgrade morale lands at construction completion, not at command
    acceptance.** Phase 3 FR-3c gave upgrades a build time, so the building is not that level until
    it finishes; a base captured mid-build discards the construction and therefore awards **nobody**
    the upgrade morale. A completed **conversion** awards nothing — MW2's gain table has no
    conversion row.
  - Settled at kickoff: **a retake inside the recapture grace awards morale normally.** The grace
    skips demotion only; each retake still burns units, so the theoretical capture/recapture farm is
    largely self-limiting and does not justify a special case MW2 never describes.
  - Settled at kickoff: **no parity gap closes on this feature.** `MW2-PARITY.md`'s G-1 stays open
    until FR-2, FR-3 and FR-4 have all merged — a score nothing reads is not yet morale. Written as
    an explicit criterion because an implementer following the usual "update the parity row" habit
    would otherwise close it three features early.
  - Noted at kickoff, as the feature's **most likely defect**: the attacker's dead count is `Wu` on
    a failed attack but `Wu − remaining` on a successful capture, where `remaining` is
    `CombatResolver`'s surviving-attacker figure. "All `Wu` died" over-penalises every capture and is
    invisible against a table nobody checks by eye, so both cases are pinned by test with different
    numbers.
  - Worked example recorded at kickoff, because it is counterintuitive and makes a good scripted
    check: on this map **capturing a neutral is morale-negative**. A 100% send of the human's opening
    10 units splits into waves of 8 and 2; wave 1 beats the neutral's 5-unit garrison at 100% defence
    leaving 3 survivors, so 5 units died attacking — `+40` for the capture against `−50` for the
    deaths, **net −10**. That is MW2's design (morale rewards not-losing), not a bug.

FR-2 (wf: `f7b795f0a982`, issue #67): The developer can have morale feed the combat formula's attack
and defence indices, so a high-morale defender is genuinely hard to dislodge. `CombatResolver`'s
`MoraleContributionPercent` stops being a fixed 100 and becomes the real index for each side, read
from FR-1's state — the **attack** column for the arriving army's owner, the **defence** column for
the base's owner. Closes most of parity **G-7** — after this, G-7 stays open on the forge term
alone (**G-6**). `MW3.Core` only. Kicked off 04-08-2026; the 38 verbatim acceptance criteria are on
issue #67, which is the contract `/implement`, `code-reviewer` and `qa-verifier` read.
  - Settled in discovery 04-08-2026: **multipliers compose multiplicatively** (D-40), settling
    `MW2-RULES.md` §4.3's `[?]`. `ComposePercentages` already multiplies and its own comment
    disclaims answering the question; this feature makes the answer real and must correct that
    comment, because a defender now carries two non-identity terms (building defence *and* morale
    defence) for the first time.
  - Settled at kickoff: **composed indices move to basis points (1/10000)**, not percent. Percent
    scale floors a common case — a level-2 village (110%) defended at morale 1 (125%) is 137.5,
    truncated to 137, a ~0.4% bias toward the attacker that can flip a knife-edge capture. At
    1/10000 scale, and with the forge term still at identity, the two-term product is **exact with
    no division loss at all**; only a future third non-identity term floors, and then at 1/10000
    grain. Doing this now with two terms is materially cheaper than after G-6 adds a third. Cost:
    `CombatResolver.Resolve`'s index parameters change scale, so `CombatResolverTests`' literal-index
    cases and `SendWaveTests`' `Compose*` calls are mechanically re-authored.
  - Settled at kickoff: **the attacker's index is read live, at arrival** — the sender's morale when
    the wave lands, not when the send was issued. Matches MW2 treating morale as a live global
    multiplier applied in combat (`MW2-RULES.md` §4.2) and needs no per-army stored state, so `Army`'s
    shape is unchanged. **This is a deliberate asymmetry with FR-4**, which locks speed at the
    submission tick: D-39 locks speed only because precomputed arrival ticks force it, not as a
    general principle. The issue says "do not harmonise them" in as many words, because a
    conscientious implementer would otherwise read the difference as an oversight.
  - Settled at kickoff: **a neutral defender composes at identity.** Neutral is `Owner is null`
    (D-11) and has no morale, so a neutral base defends with its own defence percentage and a 100%
    morale term. Called out explicitly because a null-owner morale lookup is exactly the kind of
    thing that throws or silently returns a wrong default.
  - Settled at kickoff: **the `Morale:` dump line gains four appended fields** carrying the ladder
    percentages (`HumanAtk`, `HumanDef`, `AiAtk`, `AiDef`), with FR-1's four unchanged in name, order
    and meaning. They carry whole-number ladder percentages rather than the composed basis-point
    index, because the composed value is per-*base* (it folds in that base's own defence) while this
    line is per-*player*; a verifier derives any composed index from the ladder percentage plus the
    base's existing `Level=` and `Type=`. This exists because morale realistically sits at level 0
    through a short script — a fully upgraded village is +450 and morale 1 needs 500 — so without it
    the feature's effect would not be scriptable at all.
  - Noted at kickoff as the **load-bearing correctness risk**: the attack and defence columns must not
    be interchangeable. Swapping them still resolves combat, still produces plausible numbers, and
    still crashes nothing — while inverting the system's entire point from rewarding defence to
    rewarding attack. A criterion exists whose only job is to fail if they are exchanged.
  - Noted at kickoff: FR-1 made captures score, so **any full-match test that captures and then fights
    again legitimately changes behaviour here**. Those are re-authored with expected values derived
    from `MoraleTable`/`LevelTable` in the test and a one-line reason each in the PR — never hardcoded
    and never nudged until green. This is the standing rule since FR-3a, and it needs teeth here
    because the honest fix and the dishonest fix look identical from outside.

FR-3 (wf: `eeb19c449be6`, issue #69): The player loses morale for standing still, faster the higher
they have climbed, so turtling costs something. The idle timer per player — 10/9/8/7/6/5 seconds
before decay starts and −10/−20/−25/−50/−100/−200 points per second, both accelerating with morale.
`MW3.Core` only. Kicked off 04-08-2026; the 33 verbatim acceptance criteria are on issue #69, which
is the contract `/implement`, `code-reviewer` and `qa-verifier` read.
  - Settled in discovery 04-08-2026: **only issuing a send resets the idle timer.** Upgrading and
    converting do not, because they are exactly the turtling behaviour this rule exists to punish.
    MW2 says only that morale bleeds "if you stop playing" (`MW2-RULES.md` §5.4) — this is MW3's own
    definition of playing. See "Tuning values" below.
  - Settled in discovery 04-08-2026: decay applies in **whole points on a fixed 20-tick period**
    (D-38), never fractionally per tick, keeping D-24's integer-tick arithmetic intact.
  - Settled at kickoff: **gaining morale does not reset the idle timer either.** A defender whose
    tower is killing attackers gains +10 per kill and **still decays**; kills, captures and completed
    upgrades all leave `lastSendTick` alone. This is the purest form of the anti-turtle rule already
    chosen — sitting behind towers is exactly the play decay exists to punish — and it keeps decay a
    pure function of send history. Rejected: resetting on any morale event (softer, but needs a
    `lastActivityTick` distinct from `lastSendTick` and guts the rule where turtling is most
    attractive) and resetting on kills only (closest to intuition, most complex, and MW3's invention
    on top of a rule that says only "if you stop playing").
  - Settled at kickoff: **only an *accepted* send resets the timer, at the submission tick.** A
    rejected `SendArmyCommand` leaves it untouched — otherwise invalid commands become a way to farm
    activity — and a staggered wave's later launch tick (phase 4 D-35) is not the reset point, since
    the action happened at `Execute`.
  - Settled at kickoff: **decay is evaluated after tower fire and arrivals**, so a wave landing on the
    same tick has already scored and decay applies to the post-combat total. It changes no ownership
    and no outcome, so its position relative to `EvaluateOutcome` is immaterial — but it is fixed and
    documented rather than incidental.
  - **The design property to protect**: decay needs **no new mutable state**. It is a pure function of
    `(lastSendTick, points, currentTick)`, because the threshold is re-checked against the *current*
    level every period, so nothing has to remember that a decay run is in progress. A criterion
    forbids a decay-run flag, an accumulated remainder, and a next-decay-tick cache explicitly — such
    a field works correctly in a single `Advance` call and then diverges under chunking, which is the
    exact failure D-12 exists to prevent and the hardest kind to notice.
  - **The trap, recorded because the reference never states it**: thresholds *lengthen* as morale
    falls (100 ticks at morale 5 up to 200 at morale 0), so a level drop looks like it should pause a
    decay run when the new threshold exceeds the accumulated idle time. It never does, because idle
    time outgrows the threshold — but that is a load-bearing accident rather than a stated rule, so a
    test walks a full run from 8 000 points to 0 and asserts every consecutive period decayed with no
    gap.
  - Noted at kickoff: **an upgrade grants points without resetting the timer, which is what makes
    decay scriptable at all.** The QA script banks ~100 points from a completed upgrade, then sits
    still and watches them drain — no new mechanism, and no waiting for morale to reach a level a
    short script cannot reach (a fully upgraded village is +450 and morale 1 needs 500).

FR-4 (wf: `2e35c45de62c`): The player's units move faster at higher morale, up to +50% at morale 5 —
the third and last effect in `MW2-RULES.md` §5.1's table, corroborated independently by §3.1's
"morale contributes at most +50%". `MW3.Core` only.
  - Settled in discovery 04-08-2026: **speed is locked for the whole send at its submission tick**
    (D-39), not recomputed per wave at each wave's own launch tick and not tracked live in flight.
    Live speed would break precomputed arrival ticks and the `Advance` boundary architecture;
    per-wave-at-launch would let a later wave overtake an earlier one when morale rises mid-column.
  - Note for kickoff: `TravelTimeCalculator` is shared by `Match` (resolving a send) and `AiBrain`
    (predicting one before committing to it), precisely so the two cannot disagree. A speed
    multiplier must be threaded through **both** call paths or the AI's predictions silently desync.

FR-5 (wf: `b0d20abba8ad`): The player can see both players' morale on the match screen, so the
multiplier deciding their fights is not invisible. Presentation only, `MW3.Game`, reading the state
FR-1 shipped. Adds no rule and no `MW3.Core` change.

FR-6 (wf: `1713e24400b9`): The AI opponent weighs morale when choosing to attack — a failed attack
feeds the defender +10 per unit and costs the attacker −10 per unit — and keeps sending rather than
idling into decay. Parity **G-21** territory (MW2's AI is unpublished), so MW3's own heuristic work
rather than a port, and described as such. Depends on FR-1 through FR-4 being live.

### Tuning values

Every simulation number this phase introduces, per D-22's routing rule: a constant lives in a table
settled at `/kickoff`, never inline at a call site. Phase 3's table
(`docs/base-upgrades-and-types/REQUIREMENTS.md` §4) and phase 4's
(`docs/army-sending/REQUIREMENTS.md` §4) are unchanged and still in force — this phase adds to them.

Almost every number below is **MW2's published value transferred literally**, per the 28-07-2026
settlement that MW2's literal numbers are the target (`MW2-PARITY.md` §3). The 50 ms tick makes
every one of them land on a whole tick, so nothing needs recalibrating.

**The ladder** (`MW2-RULES.md` §5.1, `[T]`):

| Morale | Points to reach | Defence | Attack | Unit speed |
|---|---|---|---|---|
| 0 | — | 100% | 100% | 100% |
| 1 | 500 | 125% | 105% | 110% |
| 2 | 1 000 | 150% | 110% | 120% |
| 3 | 2 000 | 175% | 115% | 130% |
| 4 | 4 000 | 200% | 120% | 140% |
| 5 | 8 000 | 225% | 125% | 150% |

**Gains** (`MW2-RULES.md` §5.2, `[T]`). Forge rows are omitted — MW3 has no forge (**G-6**):

| Event | Morale points |
|---|---|
| Destroying an enemy **attacking** unit | +10 each |
| Capture neutral village, level 1 / 2 / 3 / 4 / 5 | +40 / +100 / +160 / +220 / +300 |
| Capture neutral tower, level 1 / 2 / 3 / 4 | +80 / +200 / +320 / +440 |
| Capture **opponent's** village, level 1 / 2 / 3 / 4 / 5 | +100 / +250 / +400 / +550 / +750 |
| Capture **opponent's** tower, level 1 / 2 / 3 / 4 | +200 / +500 / +800 / +1100 |
| Village upgrade to level 1 / 2 / 3 / 4 | +50 / +100 / +150 / +200 |
| Tower upgrade to level 1 / 2 / 3 / 4 | +100 / +200 / +300 / +400 |

**Losses** (`MW2-RULES.md` §5.3, `[T]`):

| Event | Morale points |
|---|---|
| Your unit dies **attacking** | −10 each |
| Lose village, level 1 / 2 / 3 / 4 / 5 | −50 / −120 / −200 / −280 / −380 |
| Lose tower, level 1 / 2 / 3 / 4 | −100 / −250 / −400 / −550 |

**Inactivity decay** (`MW2-RULES.md` §5.4, `[T]`), with the tick conversion at 50 ms. Every second
converts to exactly 20 ticks, so no threshold needs rounding:

| Morale | Idle before decay starts | Idle ticks | Points lost per second | Points per 20-tick decay period |
|---|---|---|---|---|
| 0 | 10 s | 200 | −10 | −10 |
| 1 | 9 s | 180 | −20 | −20 |
| 2 | 8 s | 160 | −25 | −25 |
| 3 | 7 s | 140 | −50 | −50 |
| 4 | 6 s | 120 | −100 | −100 |
| 5 | 5 s | 100 | −200 | −200 |

**MW3's own numbers**, where MW2 publishes nothing — each derived here and marked so the parity file
records them as MW3's rather than as parity claims:

| Constant | Value | Source and derivation |
|---|---|---|
| What resets the idle timer | **Issuing a send, and nothing else** | MW2 says only that morale bleeds if you "stop playing" (§5.4). Settled 04-08-2026: upgrading and converting are the turtling this rule exists to punish, so letting them reset the timer would blunt it. It also keeps the timer symmetrical between human and AI, since both express the same command types (S-8) |
| Morale point ceiling | **8 000** — the level-5 threshold | Not published. Without a ceiling, a player could bank points far above 8 000 and be immune to decay for minutes, which defeats §5.4's entire purpose. Capping at the top threshold also **corroborates the reference's own worked example**: §5.4's `[D]` note says sitting still at morale 5 costs a full level "in about 40 seconds", which only reconciles if the first decay tick drops you out of morale 5 immediately and the rate then falls to morale 4's −100/s, giving 4 000 ÷ 100 = 40 s to reach morale 3's threshold. See D-38 |
| Morale point floor | **0** | Implied throughout — morale 0 is the bottom of the published ladder |
| Decay period | **20 ticks (1 second)** | The published rates are per-second; D-24 keeps simulation arithmetic on integer ticks, and −10/s would be −0.5/tick. Applying the whole second's decay on a 20-tick boundary keeps every value an integer, exactly as production applies a whole unit on a `60/level`-tick boundary |

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Determinism remains a hard requirement** (D-12, S-8). Morale accrual, decay, and the speed
  multiplier must all be pure functions of the command stream and the tick — no wall-clock read, no
  accumulated fractional state that behaves differently across a chunked `Advance`.
- **Tuning values enter only through this table** (D-22, `CLAUDE.md`). The four MW3-own numbers above
  are derived and recorded here; everything else is MW2's literal published value.
- **No allocation per tick.** Decay is evaluated on a period boundary for two players; it must not
  allocate a collection per tick, the same standing rule phase 3's tower fire and phase 4's
  pending-wave scan established.
- **The engine-free rules layer still binds** (S-2, D-2). Morale state, the ladder, decay, and the
  speed multiplier all live in `MW3.Core` with no engine type.
- **The morale-0 baseline must stay bit-identical.** Every index at morale 0 is 100%, so a match in
  which neither player scores must produce exactly today's numbers. This is what protects phases
  2–4's tests and `qa/scripts/` budgets, and it is a stronger guarantee than "roughly unchanged".
- **Unattended verifiability without new mechanisms.** `--script`, `--dump-state`, and
  `--screenshot` (D-17) already carry everything this phase needs — morale is a per-player scalar the
  dump line can report, and every effect is observable as a board-state difference.
- **Device QA is available and device criteria are blocking** (as since phase 3 FR-2), following
  follow-up #28's lesson: rebuild and reinstall from the branch before trusting any device
  observation that contradicts a passing headless test.
- No auth, no persistence, no network, and no accessibility or performance targets this phase.

## 6. Out of scope

Explicit non-goals for this phase — these are what stop `/autopilot` drifting.

> Read these as sequencing, not design positions (the standing convention since phase 3's mid-phase
> correction — `docs/base-upgrades-and-types/REQUIREMENTS.md` §6). Where a bullet excludes something
> MW2 has, it is excluded **from this phase** and owed by a later one, tracked as a numbered gap in
> `docs/reference/MW2-PARITY.md` §2.

- **Energy** (parity **G-5**) and **heroes** (**G-4**). Settled in discovery 04-08-2026: energy ships
  with heroes as phase 6, because energy is a currency with no sink until abilities exist — shipping
  it here would mean a number that accumulates and is spent on nothing, verifiable only as a meter.
  The `k` index that couples energy to morale (`MW2-RULES.md` §6.1) is genuinely morale-dependent, so
  it reads FR-1's state when it arrives rather than requiring anything to be rebuilt.
- **Rush Mode** (parity **G-16**). Depends on energy, so it follows it.
- **Forges** (parity **G-6**). Still two building types. **G-7 therefore stays open** after FR-2, on
  the forge term alone — closing G-6 populates the last term rather than requiring a rewrite, exactly
  as FR-3b left the morale term for this phase.
- **Passive skills and artifacts that modify morale effects** (parity **G-20**,
  `MW2-ITEMS-AND-PROGRESSION.md` §2 lists "morale effects" as an Ordinary-class passive). Needs
  persistence, which needs S-9 to relax.
- **Morale affecting production.** It does not, in either game — `MW2-RULES.md` §5.1's table has
  exactly three effect columns (defence, attack, unit speed) and §3.1 corroborates the speed one
  independently. `MW2-PARITY.md`'s G-1 row claimed morale "touches combat, speed and production at
  once"; that was an error, corrected 04-08-2026 as part of this discovery.
- **Half-suns as a mechanic.** They are cosmetic in MW2, showing only progress toward the next whole
  sun (`MW2-RULES.md` §5). FR-5 may draw partial progress; no rule reads it.
- **A second map, campaign structure, art, sound, or animation.** Unchanged from phases 3 and 4.
- **Anything server, account, login, or multiplayer** (S-7).

## 7. Open questions

None. The three questions this discovery raised were all settled with the user on 04-08-2026 and are
recorded at the FR entries they bind: multipliers compose **multiplicatively** (FR-2, D-40); only a
**send** resets the inactivity timer (FR-3); and unit speed is **locked for the whole send at its
submission tick** (FR-4, D-39). The two items marked "for kickoff to settle" under FR-1 are ordinary
kickoff work with a recommendation each, not blocking questions.
