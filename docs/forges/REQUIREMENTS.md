# Requirements — Forges

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`3900095949a7`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 6 adds the **forge**: the first building in MW3 that pays off *everywhere* rather than where it
stands. A village produces locally. A tower shoots locally. A forge does neither — it produces no
units and never fires — but every forge its owner holds multiplies that player's attacks and
defences across the whole map, up to a cap of four with sharply diminishing returns (the first forge
is worth +50% attack, the fourth only +10%).

That inversion is the phase's whole content. Because a forge produces nothing, buying one costs a
producer, so the question it puts to the player is a standing economic bet rather than a tactical
one: *trade output for a multiplier, or don't*. **Forges are optional.** Nothing in the phase
requires a player to run one, and a match in which neither side builds a forge must play exactly as
it does today.

Left alone, that bet would be solitary — each player quietly deciding how much of their own economy
to convert. So the map grows from six bases to eight, adding a **neutral forge** and a **neutral
tower** placed on the centre line where both players can reach them equally. The forge becomes a
prize on the board rather than a private accounting decision, and the low end of MW2's ladder becomes
reachable without a player gutting their economy to get there. This matters concretely: MW2's own
rule of thumb is *one forge per four unit-producing buildings*, which implies maps with roughly
sixteen producers, while MW3 has six bases in total for both players. Without a contested forge, the
ladder's upper half would be either unreachable or a degenerate all-in.

Mechanically the phase closes parity **G-6** and completes **G-7**, the combat formula
`Bu = (a/d) × Wu`, which has stood open since phase 3 FR-3b built it. `CombatResolver` has carried a
`ForgeContributionPercent` constant pinned at identity, with a comment naming G-6, since that
feature shipped; phase 5 FR-2 populated the morale term and left the forge term as the last one
outstanding. This phase populates it, and G-7 closes.

Rules stay in the engine-free `MW3.Core` and stay headlessly testable; presentation stays
deliberately plain. This phase adds **one building type and two map slots, not a new subsystem**: no
energy, no heroes, no forge levels, no second map.

## 2. Target users

- **The player** — the developer, on their own Android device. The question this phase answers for
  them is "is a global multiplier worth a producer, and is the one in the middle of the map worth
  fighting for". A player who takes the neutral forge early attacks and defends better everywhere;
  a player who over-converts wins every exchange while running out of units to send.
- **The developer** — still the implementer, and still the reason every rule must be verifiable
  headlessly on the desktop head without a device or a human (S-4).

## 3. Success criteria

Observable outcomes, not features:

1. A match on a physical Android device can be played through with forges on the board — the neutral
   forge captured, converted, lost and retaken — with no crash and no dead end.
2. A player holding forges wins an exchange they would have lost holding none, provable headlessly as
   a board-state comparison against the *same* send, not merely asserted.
3. The cap is observable: a fifth forge changes no index, provable as an identical combat outcome at
   four and five forges.
4. A match in which **neither** player ever owns a forge behaves **bit-for-bit as it does today** on
   the six original bases, so the phase regresses no existing test or `qa/scripts/` budget at the
   baseline. The two new map slots are additive, and every existing script that keys on a base index
   must be re-checked against the eight-base layout rather than assumed safe.
5. Determinism (D-12, S-8) survives forges: replaying the same commands against the same starting
   state produces the same outcome every time, including every composed index on every tick.
6. `CombatResolver` composes three non-identity terms — building defence, morale, forge — and the
   first arithmetic remainder reachable in the project's history is handled explicitly and pinned by
   a test, not discovered later as drift (D-46).
7. The neutral tower shoots **both** players' armies in range and neither player's tower shoots
   their own — provable headlessly as a symmetric board-state assertion, not one side only.
8. The AI contests the neutral forge in real play rather than ignoring it, and its capture
   predictions agree with the resolver once the forge term is live (the third occurrence of the
   hazard follow-up #68 closed).
9. `qa-verifier` confirms each feature unattended through the existing `--script` / `--dump-state` /
   `--screenshot` mechanisms (D-17). A new `qa/scripts/` file per feature is expected; a new script
   *directive* or command-line flag is not.
10. `./gate.ps1` passes locally and in CI throughout, and `MW3.Core` still contains no engine type.

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the user
and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: `69b8d6032657`): The developer can have a third building type exist in the rules, so that
everything after it has a forge to place, convert into, capture and buff from. Adds
`BaseType.Forge`: produces no units, never fires, and has exactly one tier — no levels and no upgrade
path (`MW2-RULES.md` §2, §2.4). Carries two structural changes the rest of the phase depends on.
**Conversion becomes explicit**: `Match.cs:368` currently reads
`target.Type == BaseType.Producer ? BaseType.Tower : BaseType.Producer`, a binary toggle a third type
breaks outright, so `ConvertCommand` gains a target type (D-43). **The map layout becomes a value**
`Match` accepts, defaulting to the shipped layout, and `MapSlot` carries a `BaseType` (D-44) — without
this, no rule about a *neutral* forge is testable, because `MapLayout` is `internal static` with a
hardcoded array and `Match` has no injection point. Build time and the one-second recapture grace
reuse phase 3 FR-3c unchanged.

**Settled at kickoff 05-08-2026 — issue [#82](https://github.com/VassilAtanasov/MW3/issues/82),
which carries the verbatim acceptance criteria.** Beyond the tuning-table entries above, four things
were decided that build mode must not re-open:

1. **`LevelTable.GarrisonCap(Forge, 1)` returns null**, following the tower precedent — see the §4
   row, which this kickoff corrected.
2. **`Match.AvailableActions` returns `Upgrade` plus one `Convert` action per `BaseType` other than
   the base's own**, in `BaseType` declaration order — always exactly three actions for an owned
   base (**D-48**). This is what deletes D-43's toggle rather than extending it, and it moves the
   menu to three buttons in *this* feature: eight existing menu QA scripts are re-authored for the
   new geometry, and `--dump-state`'s `Menu:` line replaces its single
   `Convert=/ConvertCost=/ConvertTo=` triple with one `Convert:<TargetType>=<Availability>@<cost>`
   token per action. FR-5 still owns per-type labels, the forge glyph, and the forge count.
3. **`MapSlot`, `MapSlotKind` and `MapLayout` become public** (D-44, amended). A public `Match`
   constructor cannot take an internal parameter type, and `MW3.Core` has no `InternalsVisibleTo`,
   so the seam forces the surface. This is directionally right for G-18 and does not introduce a
   second map.
4. **Two guards that ask "is this a tower?" actually mean "does this produce?"** — `Match.cs:877`
   and `AiBrain.cs:600` — and both invert to "is this a producer?" rather than growing a third
   special case. The tower *fire* guards are untouched here; FR-2 owns those.

FR-2 (wf: `65f7360af81d`): The player can fight over a forge that already exists on the map, so that
the multiplier is a contested objective rather than a private conversion decision. Grows the shipped
layout from six bases to eight: one **neutral forge** and one **neutral tower**, both on the centre
line and therefore equidistant from both starts, preserving the positional fairness `MapLayout`'s own
comment records for the six-base version. The **neutral tower fires** — at any player's army in
range, and never at neutral units (D-47, settled 05-08-2026). That is a behavioural feature, not a
layout edit: both of today's ownership guards on the firing path change, the optimisation that skips
tower evaluation early in a match dies because a tower now exists from tick 0, and a kill by an
unowned tower charges the victim morale while awarding none. **Exactly one neutral forge and one
neutral tower** — this phase adds no other slot and no other terrain concept.

**Now depends on FR-4** (see FR-4's entry): a capturable forge on the shipped map reaches
`MoraleTable.CaptureGain(BaseType.Forge, …)`, which throws until FR-4 supplies the rows.

**Settled at kickoff 06-08-2026 — issue [#86](https://github.com/VassilAtanasov/MW3/issues/86),
which carries the verbatim acceptance criteria.** Two things were settled while investigating this
at FR-2's aborted kickoff on 05-08-2026 and stand unchanged:

- **`AiBrain`'s tower-threat filter is FR-2's job, not FR-6's.** `AiBrain.cs:423` skips any tower
  whose `Owner is null`, so a firing neutral tower would be invisible to every route the AI weighs —
  its predictions would be systematically wrong about the map FR-2 itself creates. Dropping that one
  clause is the same class of fix as follow-up #68. FR-6 still owns the AI *contesting, capturing and
  defending* the forge and tower.
- **The neutral tower's kill charges the victim and awards nobody** — the §7 recommendation,
  consistent with D-41, confirmed rather than left to fall out of a null check.

A third was **understated** by the earlier draft and is corrected here. That draft said the neutral
tower's coverage of both bottom-flank neutrals was "an accident of two independently derived numbers"
that FR-2 should "pin with a test". It is not a latent fact — it is a **live test failure**, and the
invariant behind it cannot be preserved:

- `LevelTableTests.Tower_EveryRange_StaysWithinTheMapsOwnGeometry` asserts every tower range is
  `<= closestPairDistance` over the real map. Today that distance is 0.30. The neutral tower at
  (0.50, 0.80) sits 0.158 from the bases at (0.35, 0.75) and (0.65, 0.75), so the closest pair drops
  to 0.158 and **all four ranges** (0.20, 0.22, 0.25, 0.28) violate it.
- **The invariant is unpreservable with two drawable centre-line slots.** For a point on `x = 0.5` to
  sit at least 0.28 (the level-4 range) from the flank bases at `y = 0.25` and `y = 0.75` it needs
  `|Δy| ≥ sqrt(0.28² − 0.15²) = 0.2364` from each, i.e. `y ≤ 0.0136`, `y ≥ 0.9864`, or
  `y ∈ [0.4864, 0.5136]`. Two slots at least 0.28 apart can only take the first and last of those
  bands — jammed against the map edges, where the base circles clip off-screen and `MoraleMeter`'s
  own "no base sits closer than 0.12 to any edge" premise stops holding. Moving the four flank bases
  instead was rejected: it would rewrite positions phases 2–5's tests and scripts were authored
  against, a far larger break than appending two slots, and it contradicts §5's promise that the six
  original bases are untouched.
- **Decision (user, 06-08-2026): relax the blanket claim deliberately and replace it.** It was an
  observation about the six-base map frozen into a test, never a stated design goal; the new map's
  point is a contested, hazardous middle. The test is **re-authored, not deleted or loosened**, into
  three narrower claims that are true and worth protecting: **(a)** no tower range at any level
  reaches either start base (the nearest base to a start is 0.34 away, the widest range is 0.28);
  **(b)** exactly bases **3** (0.35, 0.75) and **5** (0.65, 0.75) fall inside the neutral tower's
  level-1 range of 0.20, at 0.158 each, with the exclusions asserted as well as the inclusions;
  **(c)** a level-1 tower converted at base **2** (0.35, 0.25) or base **4** (0.65, 0.25) covers the
  neutral forge slot, 0.158 away, so a forge holder can guard it with a flank tower.
- **Decision (user, 06-08-2026): the flank asymmetry is the intended texture.** Expansion to the two
  bottom-flank neutrals is taxed from tick 0 while the forge flank stays free — the prize is free and
  the hazard guards itself. It remains mirror-symmetric about `x = 0.5`, so positional fairness
  *between players* holds, and (b) and (c) pin it as a designed fact rather than an accident.

FR-3 (wf: `8554c22a4421`): The player can hold forges and hit harder and defend better everywhere, so
that the trade of a producer for a multiplier actually pays. Makes
`CombatResolver.ForgeContributionPercent` live against the count of forges their owner holds, capped
at four. Only the count matters — forges have a single tier and no position component. **Closes G-6
and completes G-7.** This is where the first reachable arithmetic remainder appears (D-46).

**Settled at kickoff 06-08-2026 — issue [#87](https://github.com/VassilAtanasov/MW3/issues/87),
which carries the verbatim acceptance criteria.** Beyond the §4 ladder, seven things were decided
that build mode must not re-open:

1. **The ladder lives in a new `ForgeTable`** in `MW3.Core`, the sole home of every forge percentage
   literal (D-22), mirroring `LevelTable` and `MoraleTable`. Its cap is a named constant, never a
   literal `4` at a call site.
2. **The cap clamps rather than throws.** `DefencePercentage(n)` and `AttackPercentage(n)` return the
   `n = 4` values for every `n >= 5` — holding a fifth forge is legal play, not an error — and throw
   only for `n < 0`.
3. **`ComposeAttackerIndex` and `ComposeDefenderIndex` gain a required forge parameter**, so a
   two-argument call stops compiling. A defaulted parameter was rejected for the reason D-43 gives
   about the conversion toggle: it would leave the two-term assumption alive in call sites that never
   get updated. `CombatResolver.ForgeContributionPercent` is deleted outright rather than repurposed.
4. **The attacker's forge count is read live at the arrival tick**, exactly as the attacker's morale
   already is (`Match.ResolveArrival`). Nothing about a forge locks at submission — D-39's lock is
   specific to unit speed, because a send's arrival tick is precomputed from it.
5. **D-46's remainder is pinned by name**: `ComposeDefenderIndex(110, 125, 145)` returns exactly
   `19937`, and the test states that truncation is kept, that the error is under one basis point, and
   that a truncated defender index biases the outcome toward the attacker.
6. **The desync test is constructed to fail if either path drops the term** — a non-zero forge count
   on both sides, chosen so that omitting the forge percentage on the resolve path *or* on either
   `AiBrain` prediction path flips the predicted capture. This is the third occurrence of the hazard
   follow-up #68 closed against building defence and phase 5 FR-2 was patched for against morale
   (D-45).
7. **`--dump-state` gains one line**, directly after `Morale:`:
   `Forges: Human=<n> HumanAtk=<%> HumanDef=<%> Ai=<n> AiAtk=<%> AiDef=<%>`. Count plus the two
   resulting percentages is how `MW2-RULES.md` §2.4 itself expresses a forge holding; the reference
   documents no MW2 HUD, and `--dump-state` has no MW2 counterpart, so §2.4's own shape is the
   closest MW2-grounded answer available. Written by `MatchScreen`, never `MW3.Core` (D-26); every
   existing line and field stays byte-identical.

**The cap is proven headlessly, not in a `qa/scripts/` file** (user, 06-08-2026), which amends
`ARCHITECTURE.md` §2a's expectation that FR-3 carry a scripted cap scenario. Five forges in real play
on the shipped map means five captures and 150 units of conversion cost under a firing neutral tower
and an expanding AI — a long script whose every tap depends on AI behaviour FR-6 is about to change.
The cap is therefore asserted as an identical `CombatResult` at four and five forges against an
injected layout (D-44), and FR-3's new `qa/scripts/` file proves the *ladder* live in real play at one
forge — a send that would be repelled at `a = 10000` capturing at `a = 15000` — with its header naming
the headless test, exactly the precedent FR-4's `morale-forge-capture.txt` set for the +200
neutral-forge value it could not exercise either.

**FR-3 must not be built before FR-2 (#86) merges.** Its QA script captures the neutral forge, which
is not on the shipped map until then. The core rules and every headless test here would build without
it — FR-1's injectable layout sees to that — but the feature is not done until the script runs on the
eight-base map.

FR-4 (wf: `eb92138da99f`): The player gains and loses morale for taking and losing forges, so that
the phase's new building participates in phase 5's tempo system instead of sitting outside it.
`MoraleTable`'s forge rows are absent today and commented as such; this adds them from
`MW2-RULES.md` §5.2 and §5.3. Unlike villages and towers the values do not vary by level, because a
forge has none.

**Resequenced ahead of FR-2 at kickoff 05-08-2026 — issue
[#83](https://github.com/VassilAtanasov/MW3/issues/83), which carries the verbatim acceptance
criteria.** This entry previously read "Depends on FR-2: the neutral-forge value is unexercisable
until a neutral forge exists on the board." FR-1's injectable layout (D-44) dissolved that — a test
can place a neutral forge without touching the shipped map. The dependency in fact runs the *other*
way and only that way: `Match.ResolveArrival` (`Match.cs:1037`) and `AiBrain.PredictedMoraleSwing`
(`AiBrain.cs:459`) both call `MoraleTable.CaptureGain(target.Type, …)`, which **throws** for
`BaseType.Forge`. FR-2 puts a capturable forge on the shipped map, so without these rows it throws
the first time the AI merely scores the centre forge as a target. **The build order is therefore
FR-1 → FR-4 → FR-2 → FR-3 → FR-5 → FR-6**, and the FR numbers are left alone rather than renumbered,
exactly as phase 3's FR-3a/3b/3c were.

Three things were settled at that kickoff and bind build mode:

1. The forge arms **validate the level rather than ignore it** — `CaptureGain` and `CaptureLoss`
   throw for a `BaseType.Forge` at any level other than `LevelTable.MinLevel`, making
   §5's "a forge is never given a level" an enforced invariant rather than a comment.
2. `UpgradeGain(BaseType.Forge, …)` **throws** rather than returning 0. It is unreachable in play,
   and a silent zero would read as a deliberate "forges earn no upgrade morale" rule nobody decided.
3. **Completing a conversion into or out of a forge awards no morale.** Conversion is not an
   upgrade; `Match`'s conversion-completion branch awards nothing today and must still award nothing.
   Pinned by a test rather than left as an absence.

The **+200 neutral-forge value is not verifiable in real play in FR-4** — no forge is on the shipped
map until FR-2 — so it is proven headlessly against an injected layout, and FR-4's new QA script says
so in its header rather than leaving `qa-verifier` to chase an impossible scenario.

FR-5 (wf: `06341f0fa15b`): The player can tell a forge apart from a village and a tower at a glance,
can tell the two convert buttons apart before pressing one, and can see how many forges each side
holds. A forge must be distinguishable on the MI PAD 4's ~1808x1018 viewport, and the owner's forge
count must be legible somewhere, because the buff is global and therefore invisible at the building
that grants it.

**Settled at kickoff 06-08-2026 — issue [#89](https://github.com/VassilAtanasov/MW3/issues/89),
which carries the verbatim acceptance criteria.** The feature was **renamed** at that kickoff, from
"Convert-to-forge in the action menu, and forges drawn on both heads" to its present name, in
Workflowy as well as here. Discovery wrote the old name on 05-08-2026, before FR-1's kickoff moved
the third menu button forward under **D-48**; convert-to-forge is now a working command with its own
`qa/scripts/convert-to-forge.txt`, so the old title advertised merged work as if it were pending and
invited an implementer to re-open D-48's ground. What D-48 actually left to FR-5 is the three things
the player *sees*, and the new name says so. Six things were decided that build mode must not
re-open:

1. **A forge draws as an upward-pointing triangle** — a third generated texture beside
   `CreateCircleTexture`/`CreateSquareTexture`, stretched by `Rectangle` exactly as those two are, so
   the level ring, construction ring and selection highlight all reuse it for free. A diamond was
   rejected as too easily confused with the tower's square at a glance on an eight-base map — the two
   types whose effects are least alike would be the two shapes hardest to tell apart. Its ring
   thickness comes from `LevelTable.RingThicknessFractionOfRadius(Forge, …)` (0.05), never a literal
   in `MW3.Game` (D-22). `MatchScreen.cs:459` currently special-cases only `Tower`, which is why a
   forge draws as a producer's circle today.
2. **The count is drawn as plain text, `Forges: <n>`, for both players**, in owner colour, mirrored
   on phase 5's morale meters — human immediately right of the fifth bottom-left sun, AI immediately
   left of the fifth top-right sun. This settles §7's open question in favour of the meter over
   per-building, both being per-player globals. Four filled pips mirroring the sun ladder were
   offered and rejected by the user in favour of the plainer readout.
3. **The readout is not clamped at four.** FR-3's cap governs the arithmetic, not the holding, and
   holding a fifth forge is legal play — so five forges reads `Forges: 5`. A readout showing `4`
   while the player holds `5` would be false; the cap stays legible in `--dump-state`'s `Forges:`
   line, which FR-3 already specifies.
4. **It is always drawn, reading `Forges: 0` at match start**, never hidden at zero — otherwise
   "drawn, count zero" and "not implemented" are indistinguishable, which would cost `qa-verifier`
   the ability to check the criterion at all.
5. **Each convert button is labelled with its target type and cost alone** — `Producer: 30`,
   `Tower: 30`, `Forge: 30` — so no two buttons on one menu ever carry the same text, which is the
   defect FR-1 knowingly left behind (both convert buttons read the identical `Convert: 30` today).
   Under construction reads `<TargetType>: Building`, keeping the existing pattern in which the cost
   slot is replaced by that word. The verb is dropped deliberately, for label width; the user
   accepted the resulting asymmetry with `Upgrade: <cost>`, which is byte-identical to today. Button
   order and geometry are untouched (D-48), so committed menu scripts' tap coordinates still hold.
6. **The buff percentage is deliberately not drawn.** The readout is the count alone; the
   percentages live in FR-3's `--dump-state` line. This is why FR-5 reads no `ForgeTable` and has no
   code dependency on FR-3.

**`--dump-state` gains nothing at FR-5**, and neither does the script vocabulary: the `Forges:` line
is FR-3's and the `Menu:` line's `Convert:<TargetType>=<Availability>@<cost>` tokens are FR-1's, so
every existing line stays byte-identical. The glyph, the labels and the readout are therefore
verified by **screenshot**, and `--screenshot` captures only the *final* frame — so FR-5's new
`qa/scripts/` file is authored to end in a single frame carrying the whole feature at once: the
human's converted forge as an owner-tinted triangle, FR-2's neutral forge as a grey triangle,
`Forges: 1` bottom-left and `Forges: 0` top-right, and the action menu open on the forge showing
`Producer: 30` and `Tower: 30` as two distinguishable buttons. The neutral forge is on the board from
tick 0, so no capture is needed to get it into frame.

**FR-5 must not be built before FR-2 (#86) merges.** No forge sits on the shipped map until then, so
the final-frame screenshot the feature is verified by cannot be produced. It carries no dependency on
FR-3 (#87), which precedes it only in the phase's build order.

FR-6 (wf: `b78d24560dd7`): The AI opponent builds, contests and defends forges, so that a human
playing the phase meets an opponent that plays it too. Extends `AiBrain`, which already upgrades and
respects caps (phase 3 FR-6), builds towers and routes around enemy ranges (FR-7), and prefers the
winnable target with the best predicted morale swing (phase 5 FR-6). MW2's published heuristics are
*one forge per four unit-producing buildings* and *convert before committing to an attack*
(`MW2-RULES.md` §2.4). Also contests the neutral forge and tower — an AI that ignores a free global
multiplier sitting in the middle of the map is not playing the phase. **G-21 territory**: no source
describes how MW2's AI plays, so this is original work and must be described as such, never as a
port.

**Settled at kickoff 07-08-2026 — issue [#93](https://github.com/VassilAtanasov/MW3/issues/93),
which carries the verbatim acceptance criteria.** The kickoff's first finding was how much of this
feature is *already done*, which narrows it sharply. Three of `AiBrain`'s five clauses need no
change at all:

- **The WATCH note on the Workflowy stub is closed.** It warned that a live forge term makes the
  winnability and threat predictions wrong until they read it — the third occurrence of the hazard
  follow-up #68 closed. FR-3 already did it: `AiBrain.cs:379` and `AiBrain.cs:582` both compose
  `Match.ForgeAttackPercentFor`/`ForgeDefencePercentFor` into the shared `WouldCapture`. FR-6
  inherits D-45's guarantee rather than owing it.
- **"Convert before committing to an attack" is already the structure.** `Decide` evaluates convert
  (clause 3) before attack (clause 4), so `MW2-RULES.md` §2.4's ordering heuristic falls out of the
  existing priority order for free rather than needing a rule.
- **Clause 4 already contests the neutral forge.** `PredictedMoraleSwing` tiebreaks winnable targets
  on `MoraleTable.CaptureGain(target.Type, …)`, which prices a neutral forge at **+200** against a
  neutral level-1 producer's **+40**; base 6 sits on the *unguarded* flank (D-47 put the tower at
  (0.50, 0.80), the forge at (0.50, 0.20)), so `expectedTowerLoss` ties at 0 and the morale key
  decides. Clause 2 needs nothing either — a forge's `GarrisonCap` is null (§4), so
  `IsUpgradeCandidate` already rejects it through the empty case rather than a type test.

Six things were decided that build mode must not re-open. All are **G-21 territory** — MW2's AI is
unpublished, so each is MW3's original work and must be described as such in code and docs, never as
a port. The one published input is §2.4's rule of thumb.

1. **The build ratio transfers literally**: a forge is owed when
   `ForgeCountFor(player) < producerCount / ForgeTable.ProducersPerForge`, integer division,
   evaluated *before* the conversion. Halving the ratio to suit the smaller map was offered and
   rejected — it would invent a number where MW2 published one, and the phase transfers MW2's
   literals everywhere else. The rule is **stable by construction**: 4 producers / 0 forges converts,
   and the resulting 3 producers / 1 forge is not a converting state, so it cannot oscillate. It also
   makes the last-producer hazard unreachable on the forge path, since the gate needs four.
2. **`ForgeTable` gains `ProducersPerForge = 4`, distinct from `MaxContributingForges`.** The two are
   numerically equal by coincidence and mean different things — a buff cap and a build ratio — so
   conflating them would be a latent defect the moment either moves. D-22 routes both; no `AiBrain`
   call site names either literal.
3. **The forge converts the rear-most base, the tower keeps the front.** Among convert candidates,
   greatest `NearestNotOwnedDistance`, ties by lowest id — the rule `TryUpgrade` already uses for
   "safest", so **D-31's one distance rule gains a third reader** rather than a fourth rule. A forge
   has no local defence (100%, D-42) and pays off globally, so it belongs where it will not be taken;
   the front/rear split is the design in one line.
4. **Forge conversion is tried first within clause 3**, falling through to today's unchanged tower
   conversion when no forge is owed. Tower-first was rejected on inspection: `IsConvertCandidate` is
   effectively unconditional once any front producer holds 30, so the forge branch would never be
   reached and the AI would convert everything to towers.
5. **A threatened forge outranks any threatened non-forge**, whatever the ids; among forges and among
   non-forges today's lowest-id order is unchanged. Ordering all threatened bases by
   `MoraleTable.CaptureLoss` instead was rejected as a phase-5 behaviour change smuggled into a forge
   feature. A forge is the only building whose loss weakens its owner *everywhere*.
6. **Clause 4 gains no new comparison key**, and the existing tiebreak is pinned by a test
   constructed to **fail if it stops reading `target.Type`** — not merely to pass today. Inventing a
   second forge preference on top of a morale table that already prices it 5:1 would be unfounded
   original work in the one area where the reference cannot check us.

**The ratio-gated conversion and the forge-first defence are proven headlessly, not by a scripted
scenario**, following FR-3's amendment to `ARCHITECTURE.md` §2a. Building a forge needs four owned
producers, which on the eight-base map means three uncontested captures — a long run whose every tick
depends on the AI behaviour this feature is itself changing. Both are asserted against injected
layouts (D-44), and FR-6's one new `qa/scripts/ai-contests-forge.txt` proves the *contest* live: tap
Play, the human does nothing, and the AI takes base 6 unassisted. Its header names the headless
tests, exactly the precedent FR-3's `forge-buff-decides-an-exchange.txt` and FR-4's
`morale-forge-capture.txt` set.

**One pre-existing defect was found and deliberately left out of scope.** Clause 3 can convert the
AI's *last* producer into a tower — `ownBases.Count >= 2` counts bases, not producers, so an AI
holding one producer and one tower can convert away its only source of units. The forge path cannot
reach it (decision 1), so it stays a tower-path defect predating this phase and is filed as
follow-up [#95](https://github.com/VassilAtanasov/MW3/issues/95) rather than widened into FR-6. It
has been reachable since phase 3 FR-7 (#55) added tower conversion.

### Tuning values

Every simulation number this phase introduces, per D-22's routing rule: a constant lives in a table
settled at `/kickoff`, never inline at a call site. Phase 3's table
(`docs/base-upgrades-and-types/REQUIREMENTS.md` §4), phase 4's (`docs/army-sending/REQUIREMENTS.md`
§4) and phase 5's (`docs/morale/REQUIREMENTS.md` §4) are unchanged and still in force — this phase
adds to them.

**The forge ladder** (`MW2-RULES.md` §2.4, `[T]`) — MW2's published values transferred literally:

| Forges owned | Defence | Attack |
|---|---|---|
| 0 | 100% | 100% |
| 1 | 125% | 150% |
| 2 | 135% | 175% |
| 3 | 145% | 190% |
| 4 or more | 150% | 200% |

A fifth forge does nothing — four is the cap, stated explicitly in the source.

**The build ratio** (`MW2-RULES.md` §2.4, `[S]`) — `ForgeTable.ProducersPerForge` = **4**, MW2's
published rule of thumb *one forge per four unit-producing buildings*, added at FR-6's kickoff
(07-08-2026) as the AI's build threshold. It is **numerically equal to `MaxContributingForges` by
coincidence and means something different** — that one is the buff cap, this one is a build ratio —
so the two are separate named constants and neither is ever spelled as a literal at a call site
(D-22). MW2 states the ratio as player advice; using it as the AI's own threshold is MW3's original
work (**G-21**), not a parity claim.

**Forge morale rows** (`MW2-RULES.md` §5.2 and §5.3, `[T]`) — the rows phase 5 deliberately omitted:

| Event | Morale points |
|---|---|
| Capture neutral forge | +200 |
| Capture **opponent's** forge | +300 |
| Lose forge | −100 |

These do not vary by level. The standing asymmetry still holds: losing costs less than the enemy
gains, so a forge trade is net-positive for the aggressor.

**MW3's own numbers**, where MW2 publishes nothing — each derived here and marked so `MW2-PARITY.md`
records them as MW3's rather than as parity claims:

| Constant | Value | Source and derivation |
|---|---|---|
| Forge conversion price | **30 units**, the existing `LevelTable.ConversionCost` | MW2 prices *all* conversions identically at 30 (§2.1) and MW3 kept that literal at phase 3 FR-3a's realignment. The forge inherits the single existing price rather than introducing a second one — MW2 gives no basis for pricing forge conversion differently from tower conversion. **Corrected at FR-1's kickoff (05-08-2026):** this row previously read "10 units", which never matched the code — `LevelTable.cs:33` has always been 30, and `convert-pending.txt`, `convert-completed.txt` and `greyed-convert-does-nothing.txt` all say so. The row's reasoning was right; only its number was wrong |
| A forge's own building defence | **100%** | Not published (**G-22** territory: MW2 states building defence only as percentages of unstated bases). Set to level-1 village defence, i.e. no bonus. A forge trades *local* defence for a *global* multiplier, which is what keeps the neutral forge genuinely contestable and stops a forge doubling as a fortress. It also keeps `LevelTable`'s forge arm a single value rather than a fabricated ladder |
| A forge's garrison cap | **none** — `LevelTable.GarrisonCap(Forge, 1)` returns null | **Settled at FR-1's kickoff (05-08-2026), replacing this row's earlier "the level-1 producer cap".** `GarrisonCap` is documented as a *production* ceiling, not a storage limit (D-21), and the tower arm returns null precisely because a tower never produces. A forge never produces either, so a cap could never bind: a value of 20 would only surface as a drawn denominator that nothing can ever move toward. Following the tower precedent also keeps the "every reader handles the empty case explicitly" contract intact rather than adding a second kind of meaningless cap |
| Neutral forge and tower positions | **(0.50, 0.20)** and **(0.50, 0.80)** | Both on the centre line, so each is exactly equidistant from the human start (0.12, 0.50) and the AI start (0.88, 0.50) — the mirror symmetry about `x = 0.5` that `MapLayout`'s comment records as the reason neither side starts with a positional advantage. Two off-axis slots could not preserve that |
| Neutral forge and tower starting garrison | **10** | Double an ordinary neutral's 5. These are prizes, not expansion room; a 5-unit prize would be taken in the opening seconds by whoever sends first, which would make the objective a race rather than a contest |
| Neutral tower level | **1** | The weakest tower, and this matters more now that it fires (D-47). Its range and fire period come from `LevelTable.Tower` at level 1 — no new number. A pre-built level-4 tower on the centre line would shoot both players hard from tick 0 and dominate the map on capture; level 1 makes it a hazard and a foothold. Its capture morale value (+80 neutral, +200 opponent's) is already tabled by phase 5 |

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Determinism remains a hard requirement** (D-12, S-8). The forge count, the composed indices, and
  every AI decision that reads them must be pure functions of the command stream and the tick.
- **Tuning values enter only through the table above** (D-22, `CLAUDE.md`).
- **The no-forge baseline must stay bit-identical.** With zero forges every term is 100%, so a match
  in which neither player converts or captures a forge must produce exactly today's numbers on the
  six original bases. This is what protects phases 2–5's tests and `qa/scripts/` budgets, and it is a
  stronger guarantee than "roughly unchanged".
- **The eight-base layout is a breaking change to scripts that index bases.** Unlike the baseline
  guarantee above, this one is not free: every committed `qa/scripts/` file and every test that
  assumes six bases must be re-checked, and a script *weakened* rather than re-authored is a defect
  (the standing rule since phase 3 FR-3a doubled every tick count). The two new slots are **appended**
  rather than inserted, so bases 0–5 keep their ids; what genuinely moves is any expectation touched
  by the neutral tower's fire or by the AI now seeing two more bases, and FR-2's issue requires each
  re-authored file to name which of the two in a header comment.
- **`Tower_EveryRange_StaysWithinTheMapsOwnGeometry` is replaced, not weakened.** The "no tower range
  covers a neighbouring base" invariant is unpreservable on the eight-base map (see FR-2). Its three
  replacement claims are binding from FR-2 onward; deleting the test, or loosening its bound so it
  passes, is a defect rather than a judgement call.
- **No allocation per tick.** The forge count is read on the combat path; it must not allocate a
  collection per evaluation, the same standing rule phase 3's tower fire and phase 4's pending-wave
  scan established.
- **The engine-free rules layer still binds** (S-2, D-2). The type, the ladder, the count and the
  composition all live in `MW3.Core` with no engine type.
- **Unattended verifiability without new mechanisms.** `--script`, `--dump-state` and `--screenshot`
  (D-17) already carry everything this phase needs. Note the standing correction in `CLAUDE.md`:
  "no new QA mechanism" means no new directive and no new flag — it does **not** mean no new
  `qa/scripts/` file, and each feature here should expect to add one.
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

- **Forge levels.** MW2 gives forges exactly one tier (`MW2-RULES.md` §2, §2.4). This is not
  sequencing — it is the rule, and a later phase must not add a forge ladder.
- **Energy** (**G-5**) and **heroes** (**G-4**), now phase 7. G-5 was blocked on morale for its `k`
  index and is unblocked as of phase 5. Four MW2 heroes have forge-coupled abilities — Kenor's Tower
  Defense, Chia's Totem of Rage, Trini's Freeze Building, Ayner's Rage scaling with forges owned —
  which is a second reason forges precede them.
- **Rush Mode** (**G-16**). Depends on energy, so it follows it.
- **Forges affecting tower fire.** MW3's tower fire destroys one unit per shot and is not
  formula-driven, so the forge indices do not touch it. If a later phase makes tower damage
  formula-driven (**G-13**, unclosable from research today), that phase decides whether forges feed
  it.
- **Extended map support — flexible layouts, paths, obstacles and zones — is its own future
  project** (settled with the user 05-08-2026, and the reason FR-2's scope is drawn tightly). This
  phase adds **exactly two slots**: one neutral forge and one neutral tower. It introduces no terrain
  concept, no pathing beyond today's straight-line transit, no blocking geometry and no zone. D-44's
  injectable layout is a testability seam, not the beginning of a map system — a later phase owns
  that, and it will also carry **G-18** and the terrain MW2 implies (Shii'Mori is a terrain-control
  tribe, and Trini's Beehive is documented as *ignoring* terrain, so MW2 has some).
- **A second map, a map file format, or map selection** (**G-18**). FR-1 makes the layout injectable
  so the rules are testable; it does **not** introduce a second shipped map, a map format, or any UI
  for choosing one. Geolocated maps remain the Branding project's territory.
- **Passive skills and artifacts that modify forge effects** (**G-20**). Needs persistence, which
  needs S-9 to relax.
- **Domination and King of the Hill objective buildings** (**G-15**). They are non-convertible and
  spell-immune, which is a different building concept from the forge, and they belong to a modes
  phase.
- **Art, sound, or animation beyond making a forge distinguishable.** Unchanged from phases 3–5.
- **Anything server, account, login, or multiplayer** (S-7).

## 7. Open questions

None. The three questions this discovery raised were settled with the user on 05-08-2026 and are
recorded at the FR entries and decisions they bind: the six-base map cannot hold MW2's forge economy,
so the map grows with a **contested neutral forge and neutral tower** (FR-2); **forges are optional**
and the no-forge baseline must stay bit-identical (§1, §5); and the **map layout becomes injectable**
so neutral-forge rules are testable before the shipped map changes (FR-1, D-44).

A fourth was settled 05-08-2026 after the passes closed and is recorded at D-47: the **neutral tower
fires** at any player's army in range and never at neutral units. An earlier draft of that decision
had it inert; the correction is in FR-2, D-47 and success criterion 7.

Three items are ordinary kickoff work with a recommendation each, not blocking questions:

- ~~The exact placement and starting garrison of the two new slots~~ — **settled at FR-2's kickoff,
  06-08-2026**: (0.50, 0.20) and (0.50, 0.80), garrison 10 each, exactly as §4's Tuning values
  proposed. Settling it surfaced the geometry-invariant failure recorded at FR-2.
- ~~Whether the forge count is drawn beside phase 5's morale meter as a second global indicator or
  per building~~ — **settled at FR-5's kickoff, 06-08-2026**: beside the morale meter, as plain
  `Forges: <n>` text in owner colour, mirrored for both players. Settling it also fixed that the
  readout is uncapped and never hidden at zero, and that the buff percentage stays out of the HUD.
- ~~**Morale attribution for a neutral tower's kill** (FR-2)~~ — **settled at FR-2's kickoff,
  06-08-2026**: the kill charges the victim and awards nobody, exactly as the recommendation
  proposed, stated deliberately at D-47 rather than left to fall out of a null check.

All three are now closed, and **FR-6's kickoff (07-08-2026) raised no new open questions** — every
decision it reached is recorded at FR-6's entry above. Every feature in this phase is now settled and
carries an issue.
