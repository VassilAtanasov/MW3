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

FR-3 (wf: `8554c22a4421`): The player can hold forges and hit harder and defend better everywhere, so
that the trade of a producer for a multiplier actually pays. Makes
`CombatResolver.ForgeContributionPercent` live against the count of forges their owner holds, capped
at four. Only the count matters — forges have a single tier and no position component. **Closes G-6
and completes G-7.** This is where the first reachable arithmetic remainder appears (D-46).

FR-4 (wf: `eb92138da99f`): The player gains and loses morale for taking and losing forges, so that
the phase's new building participates in phase 5's tempo system instead of sitting outside it.
`MoraleTable`'s forge rows are absent today and commented as such; this adds them from
`MW2-RULES.md` §5.2 and §5.3. Unlike villages and towers the values do not vary by level, because a
forge has none. Depends on FR-2: the neutral-forge value is unexercisable until a neutral forge
exists on the board.

FR-5 (wf: `06341f0fa15b`): The player can convert a base into a forge from the action menu and can
see how many forges they hold, so that the type is reachable and the buff is legible. Extends phase 3
FR-5's action menu with the third type, which means the menu stops being a two-way toggle in the UI
as well as in the command. A forge must be distinguishable at a glance from a producer and a tower on
the MI PAD 4's ~1808x1018 viewport, and the owner's forge count must be legible somewhere, because
the buff is global and therefore invisible at the building that grants it.

FR-6 (wf: `b78d24560dd7`): The AI opponent builds, contests and defends forges, so that a human
playing the phase meets an opponent that plays it too. Extends `AiBrain`, which already upgrades and
respects caps (phase 3 FR-6), builds towers and routes around enemy ranges (FR-7), and prefers the
winnable target with the best predicted morale swing (phase 5 FR-6). MW2's published heuristics are
*one forge per four unit-producing buildings* and *convert before committing to an attack*
(`MW2-RULES.md` §2.4). Also contests the neutral forge and tower — an AI that ignores a free global
multiplier sitting in the middle of the map is not playing the phase. **G-21 territory**: no source
describes how MW2's AI plays, so this is original work and must be described as such, never as a
port.

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
  (the standing rule since phase 3 FR-3a doubled every tick count).
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

- The exact placement and starting garrison of the two new slots (§4 Tuning values proposes
  centre-line positions and a garrison of 10).
- Whether the forge count is drawn beside phase 5's morale meter as a second global indicator or per
  building (FR-5 — the meter is the better home, since both are per-player globals).
- **Morale attribution for a neutral tower's kill** (FR-2). The existing path awards the killer's
  owner and charges the victim; an unowned tower has nobody to award, while the victim still pays.
  The recommendation is to keep exactly that — it is consistent with D-41, and it prices routing
  through the contested middle — but FR-2's kickoff must state it deliberately rather than let it
  fall out of a null check.
