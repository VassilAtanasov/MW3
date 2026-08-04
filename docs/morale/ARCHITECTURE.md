# Architecture — Morale (phase 5)

> What **this phase** adds or changes. Everything else defers to `docs/ARCHITECTURE.md` (the system
> baseline, S-1..S-9) and to the earlier phases' files, which stay in force:
> `docs/welcome-screen/`, `docs/core-gameplay-loop/`, `docs/base-upgrades-and-types/`,
> `docs/army-sending/`. Decision numbering continues the repo-wide sequence — phase 4 ended at
> **D-36**, so this phase starts at **D-37**.

## 1. Overview

Morale is the first system in MW3 that is **per-player and global** rather than per-building or
per-army. Everything the simulation has held until now hangs off a `Base` or an `Army`; morale hangs
off neither, and that is the single fact that shapes every decision below.

It is also unusual in being almost entirely *already specified*. `MW2-RULES.md` §5 carries the
ladder, the gain table, the loss table, and the decay table at `[T]` (transcribed-table) confidence,
and the 50 ms tick (D-27) makes every published second land on a whole 20 ticks. So this phase writes
very few numbers of its own — four, all recorded in `REQUIREMENTS.md` §4 "Tuning values" — and spends
its design effort on *where the state lives* and *how it is read* instead.

The phase is deliberately sequenced so the score exists before anything reads it: FR-1 moves a number
nobody consults, then FR-2 (combat), FR-3 (decay) and FR-4 (speed) each turn one effect on
independently. That keeps every effect's regression surface separable — if a phase-2 combat test
breaks, it broke at FR-2 and not somewhere in a six-feature bundle.

```
MW3.Desktop ---+
               +--> MW3.Game (MonoGame) --> MW3.Core (rules, no engine) <-- MW3.Core.Tests
MW3.Android ---+
                    FR-5 draws the meter        FR-1..FR-4, FR-6 live entirely here
```

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds no package reference, no content-pipeline asset, and no platform capability. See
`docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a, `docs/core-gameplay-loop/ARCHITECTURE.md` §2a,
`docs/base-upgrades-and-types/ARCHITECTURE.md` §2a, and `docs/army-sending/ARCHITECTURE.md` §2a are
all complete and current; everything there applies verbatim, including the repo-wide `-m:1` build
rule and the `down` / `up` / `wait` scripted-input vocabulary.

**No new script directive and no new command-line flag this phase.** Morale needs neither: it is
driven entirely by commands a script can already issue (a send, an upgrade, a convert) and by the
passage of ticks a script can already `wait` for. Decay in particular is verified by *not* acting —
the one thing a script gets for free. This follows phase 4 FR-2's precedent, where a directive was
deliberately not added because the existing vocabulary exercised the real code path.

`--dump-state` gains **one** field, at FR-1, fixed exactly at that feature's kickoff rather than
here: a per-player morale reading, written by `MatchScreen` and never by `MW3.Core` (D-26). Every
existing field keeps its name, order and meaning, so every committed script that keys on a base or
army line is unaffected. FR-3's decay and FR-4's speed multiplier add no field of their own — decay
is observable as the same morale number falling, and speed as an army's `Arrival=` tick moving.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  MoraleState.cs        NEW - one player's morale: points, derived level, and the tick of their
                         last send. Mirrors ProductionState's role as small mutable per-subject
                         simulation state (D-37)
  MoraleTable.cs        NEW - the published ladder and the gain/loss/decay tables as pure lookups,
                         mirroring LevelTable. No call site names a morale number (D-22)
  Match.cs              owns one MoraleState per player and is the only thing that mutates them
                         (D-37); awards and deducts at capture, at unit death, and at upgrade;
                         evaluates decay on its own Advance boundary (D-38)
  CombatResolver.cs     MoraleContributionPercent stops being a fixed 100 and becomes a parameter;
                         ComposePercentages' disclaimer comment is corrected, since a defender now
                         carries two non-identity terms for the first time (D-40)
  TravelTimeCalculator  gains the sender's speed multiplier, threaded through BOTH its Match and
                         its AiBrain call paths so predictions cannot desync (D-39)
  Army.cs               carries the speed locked at its send's submission tick (D-39)
  AiBrain.cs            FR-6 only: weighs morale in the attack decision and avoids idling into
                         decay. Untouched by FR-1..FR-5 beyond the TravelTimeCalculator signature
src/MW3.Game/
  MatchScreen.cs        FR-5 only: draws both players' morale and writes the dump field
```

File names are intent, not contract — `/kickoff` and `/implement` may split or rename them. What
**is** contract: which project each concern lives in.

## 4. Key decisions

**D-37: morale is per-player match state owned by `Match`, not a field on `Player`.**
`Player` is `record Player(int Id, PlayerControllerKind ControllerKind)` — an identity, and S-9 binds
it to staying one ("a player is an in-match id plus a controller kind"). Morale is mutable state that
belongs to a *match*, not to a player: the same player id in a future rematch starts at zero. So
`Match` holds one `MoraleState` per player and is the only thing that mutates them, exactly as it
owns `_armies` and `_pendingWaves` and as `Base` owns its `ProductionState`. Rejected: adding mutable
points to the `Player` record, which would make an identity record unequatable-by-value and quietly
break S-9; and a static or ambient morale service, which would break determinism the moment two
matches existed in one process (the test suite routinely does exactly that).

**D-38: morale points are integers clamped to `[0, 8000]`, and decay applies in whole points on a
fixed 20-tick period — never fractionally per tick.** Three things force this shape. D-24 keeps all
simulation arithmetic on integer ticks, and the published rates are per-second: −10/s is −0.5/tick,
which cannot be represented without either floating-point accumulation (banned — it makes a chunked
`Advance` diverge from a single one) or a fractional remainder field (representable, but it is
per-tick state that serves no other purpose). Applying the whole second's decay on a 20-tick boundary
keeps every value an integer, and it matches how production already applies a whole unit on a
`60/level`-tick boundary rather than accruing fractions.

The ceiling is the more interesting half. MW2 publishes no maximum, but without one a player could
bank far above 8 000 and be immune to decay for minutes, defeating §5.4 entirely. Capping at the
level-5 threshold also **explains the reference's own worked example**: `MW2-RULES.md` §5.4's `[D]`
note says sitting still at morale 5 costs a full level "in about 40 seconds", which reconciles only
if the first decay period drops you out of morale 5 immediately and the rate then falls to morale 4's
−100/s — giving 4 000 ÷ 100 = 40 s to reach morale 3's threshold. So **the decay rate is read from
the current level on every period, and the bleed self-slows as you fall**. That is a behaviour to
assert, not an emergent accident.

**D-39: a send's unit speed is locked once, at the submission tick, for the whole send — not
recomputed per wave and not tracked live in flight.** Two alternatives were rejected for concrete
reasons rather than taste.

*Live speed* — an in-flight army moving faster as its owner's morale climbs — breaks the `Advance`
boundary architecture outright. Arrival ticks are **precomputed** at launch and `Match.Advance` finds
its work through `EarliestArrivalTickUpTo`; if speed can change mid-flight, an arrival tick is no
longer knowable in advance and every boundary scan would have to re-derive positions each tick. It
would also invalidate `TowerThreatEstimator`'s predictions and phase 4's D-35 wave staggering, both
of which treat `ArmySpeedUnitsPerTick` as a genuine constant.

*Per-wave at each wave's own launch tick* is subtler and worse. D-35 has waves 2..N launch on their
own ticks, 5 apart; if each read the sender's morale at its own launch, a morale gain mid-column
would give a later wave a **higher speed than the wave ahead of it**, and it would overtake — a
column that visibly reorders itself, and a capture attributed to the wrong wave index. Locking at
submission means every wave of one send shares one speed and the column keeps its order, which is
also what FR-4's drawn taper (D-36) assumes.

Consequence for FR-4: the multiplier must be threaded through **both** of
`TravelTimeCalculator.ComputeTicks`' call paths — `Match` resolving a send and `AiBrain` predicting
one. That helper exists specifically so the two cannot disagree about travel time; adding a parameter
to only one of them would reintroduce the disagreement it was written to prevent.

**D-40: attack and defence multipliers compose multiplicatively.** `MW2-RULES.md` §4.3 flags this
`[?]`: the sources say only that the terms "combine", and additive stacking is not ruled out. It has
not mattered until now — `CombatResolver.ComposePercentages` already multiplies, and its own comment
correctly notes that with at most one non-identity term the composition "does not itself answer the
stacking question". FR-2 ends that: a defender carries building defence **and** morale defence, both
non-identity, for the first time.

Settled with the user 04-08-2026 as **multiplicative**, on the evidence that the reference's own
worked example (§4.3) multiplies and that the shipped code already does. Two obligations follow.
FR-2 must **correct that comment** rather than leave it disclaiming an answer the code now gives. And
`MW2-PARITY.md` must record this as **MW3's assumption, not a parity claim** — if MW2 is ever
observed stacking additively, this is a gap to reopen, and `MW2-RULES.md` §10's list of things Ivan
should not claim to know keeps the `[?]` standing.

**D-41: only *attacking* units generate morale, in both directions — a defender's dead garrison is
worth nothing to anyone.** This falls out of the tables' exact wording (`MW2-RULES.md` §5.2, §5.3):
"destroying an enemy **attacking** soldier +10" and "your unit dies **attacking** −10". It is not a
rounding of the rule, and it interacts precisely with the combat formula: in `Bu = (a/d) × Wu`, `Bu`
counts units destroyed **inside the defending building**, so the defender's own losses are `Bu` and
generate nothing for either side, while the attacking wave `Wu` is consumed either way.

The implementer therefore needs one derivation, and it is easy to get wrong: **the attacker's dead
count is `Wu` on a failed attack, and `Wu − remaining` on a successful one**, where `remaining` is
`CombatResolver`'s surviving-attacker figure that becomes the captured base's new garrison. A naive
"all `Wu` died" would over-penalise every successful capture, and the error would be invisible
against a table nobody can check by eye.

Two consequences worth stating because they are the design working as intended. Successfully
defending is a **morale engine** — every unit thrown at you and destroyed is +10 to you and −10 to
the attacker, a 20-point swing per unit — which is what makes "morale rewards not losing" true rather
than merely asserted. And **tower fire generates morale**: `EvaluateTowerFireAtTick` destroys
attacking units in transit, so the tower's owner gains and the army's owner loses, on exactly the
same terms. That is a real interaction between this phase and phase 3's towers, and it should be
tested rather than discovered.

**FR-4 confirmed two things rather than discovering surprises.** First, D-39 needed no
restructuring: `Match.Execute(SendArmyCommand)` already built a fully-constructed `Army` for every
wave up front, with `ArrivalTick` computed once at submission and parked waves 2..N in
`PendingWave(army, launchTick)` — the shared-speed lock fell out of reading the sender's morale once
before that loop, rather than requiring any change to the loop's shape. Second, **wave spacing widens
with morale**: a wave's on-screen gap is `speed × WaveIntervalTicks`, so morale 0's 0.05 normalized
units becomes 0.075 at morale 5 (150% speed). Phase 4's **D-36** computed its marker-overlap
arithmetic at morale 0, which is therefore the *worst case* for overlap — the shipped taper stays
valid at every morale level and only becomes more legible as speed rises. Recorded here so a later
reader does not re-open D-36 thinking morale broke it.

## 5. Cross-cutting conventions

Rules build mode must follow, beyond the standing ones in `docs/CONVENTIONS.md`:

- **Morale is never read at a call site as a number.** Every published value goes through
  `MoraleTable` the way every level value goes through `LevelTable` (D-22). A literal `+10` or `225`
  anywhere outside that table is a defect.
- **Morale 0 is exact identity.** Every index at morale 0 must be literally 100%, so a match in
  which neither player scores produces today's numbers bit-for-bit. This is what protects phases
  2–4's tests and QA budgets, and it should be asserted directly rather than assumed.
- **Morale never branches the resolution path.** `ResolveArrival`, `CombatResolver`, and
  `EvaluateTowerFireAtTick` take morale as a *value*, never as a condition — no `if (morale > 3)`
  anywhere. This is the same rule phase 4 applied to waves ("a wave is not a special case") and it
  is what keeps the morale-0 baseline provable.
- **Decay is evaluated on an `Advance` boundary**, alongside arrivals, construction completions, and
  pending-wave launches — not scanned every tick. The per-boundary work is two players' arithmetic
  with no allocation and no LINQ.
- **Every morale mutation is attributable.** A test asserting a player's total after a sequence of
  events proves very little on its own; the gain and loss tables are large enough that a wrong row
  hides easily inside a right-looking total. Prefer per-event assertions over end-state totals.
- **The AI reads the same state the human does** (S-8). FR-6 adds no morale query the human's screen
  could not also make, so nothing the AI knows is privileged.
