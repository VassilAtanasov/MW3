# Requirements — Maps

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`3f7156d826aa`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 7 gives the game **a board worth choosing between**. Every match since phase 2 has been played
on exactly one hardcoded layout — six slots until phase 6 FR-2 appended two more — so the only
variety a player has ever had is the AI's behaviour. Three maps, picked from three buttons on the
home screen:

| Map | Slots | Composition |
|---|---|---|
| **Small** | 6 | 2 starts, 4 neutral producers |
| **Medium** | 8 + 1 obstacle | 2 starts, 6 neutral producers, one obstacle in the middle |
| **Big** | 9 | 2 starts, 4 neutral producers, 2 neutral towers with a forge between them |

Small is not new work: 2 starts and 4 neutrals is **bit-identical to the layout phases 2–5 shipped**,
before phase 6 FR-2 appended the neutral forge and tower. That is what makes it the phase's
regression anchor, and it is why most of the existing `qa/scripts/` become valid again by being
pointed at it rather than re-coordinated (§4 FR-2).

The one new *mechanic* is **terrain that armies route around**. An obstacle makes the straight line
between two bases unavailable, and a send takes the shortest way past it. This was chosen by the user
on 07-08-2026 over the cheaper alternative — refusing a send whose line is blocked — with the cost
stated at the time: it roughly doubles the phase, because it is the first thing in this project's
history to make an army's position something other than a linear interpolation between two base
positions.

It is also a **deliberate divergence from MW2**, not a parity closure. `MW2-RULES.md` §1 lists
"straight line, base to base, no pathfinding" as *already at parity in both games*, and §10 lists
terrain behaviour as unpublished. Per `MW2-PARITY.md` §0, a new difference from MW2 needs the user's
agreement first and then belongs in **§4 of that file with its reasoning** — never as a §2 gap. That
agreement was given 07-08-2026 and the row moves out of §1 accordingly.

**Player count stays at two on every map.** The original Workflowy note for this project asked for
layouts flexible enough for PvP, 3–4 players and coop campaigns; the user narrowed that on
07-08-2026 to three fixed two-player maps. This is the single largest thing the narrowing bought:
`Match.HumanPlayer`/`AiPlayer`, `MatchOutcome.HumanVictory`/`HumanDefeat`, the two `MoraleState`
fields and `MatchRunner`'s single `IPlayerBrain` all stay exactly as they are — a refactor touching
33 call sites in `src/` and roughly 40 test files, which this phase therefore does not carry.

The phase also folds in follow-up issue **#94** (reduce base shape sizes by about half on both heads)
as FR-5. Its own issue body asked for a separate `/kickoff` "since it changes phases 2–5's shipped
look, not just this phase's forge work", and this is that kickoff's home: nine elements plus an
obstacle on Big is precisely why the shapes must shrink now.

Rules stay in the engine-free `MW3.Core` and stay headlessly testable; presentation stays
deliberately plain. This phase adds **three layouts, one terrain mechanic and a chooser** — not a map
system: no file format, no authored data outside C#, no geolocated map, no tunnel or air lane, no
zone, no N-player layout.

## 2. Target users

- **The player** — the developer, on their own Android device. The question this phase answers for
  them is "does this game have more than one match in it". Three boards that reward different play:
  Small is the tight opening-speed game they already know, Medium makes the centre impassable so
  tempo runs along the flanks, and Big puts the most valuable prize in the game dead centre under two
  neutral guns.
- **The developer** — still the implementer, and still the reason every rule must be verifiable
  headlessly on the desktop head without a device or a human (S-4). This phase's `--map` flag exists
  for exactly that reason (§4 FR-2).

## 3. Success criteria

Observable outcomes, not features:

1. From the home screen on a physical Android device, each of the three buttons starts a match on the
   right board, and each of the three can be played to victory or defeat with no crash and no dead
   end.
2. On Medium, an army sent between two bases whose straight line crosses the obstacle **visibly
   travels around it** and arrives later than the straight-line distance would predict — provable
   both headlessly (arrival tick against path length) and on screen.
3. On Medium, the route chosen for a given send is **identical on every run**, including for the
   symmetric case where passing above and below the obstacle are exactly the same length (§5, D-52).
4. A match on **Small** behaves bit-for-bit as the six-base map does today, so the phase regresses no
   phase 2–5 test or `qa/scripts/` budget.
5. At the reduced base size (FR-5), no two action-menu buttons overlap and no morale meter or
   `Forges:` readout overlaps a base — at both 1280x720 and the MI PAD 4's ~1808x1018 viewport.
6. The AI plays Medium without being systematically wrong about arrival times: it does not commit to
   attacks it loses only because it costed a straight line the army never flies (§4 FR-6).

## 4. Functional requirements

One entry per Workflowy level-3 feature, in dependency order. Acceptance criteria are settled by
`/kickoff <feature>` and written into the GitHub issue, which is the contract; the criteria are
deliberately **not** duplicated here.

**FR-1 (wf: `da7ae6122744`): three named maps and obstacles as core map data.** Kicked off
07-08-2026 as **issue #98** on board **24**, which carries the full acceptance criteria.
A `MapDefinition` — slots plus obstacles — a `MapCatalog` holding exactly Small, Medium and Big, a
`MapId` naming them, and `MapObstacle` as an axis-aligned rectangle. `Match` takes a definition
rather than a bare slot list, extending D-44's injectable-layout seam. The obstacle is **data only**
here: nothing reads it for movement until FR-3, so Medium ships briefly with an inert obstacle that
armies fly straight through.

Settled at kickoff: the feature is **deliberately invisible**. `MapLayout.Slots` and `Match`'s
parameterless constructor are untouched, both heads still boot to today's eight-slot board, and all
50 committed `qa/scripts/` pass unedited — the whole compatibility break stays inside FR-2 rather
than leaking across two features. It therefore adds **no new `qa/scripts/` file**, deliberately:
nothing it adds is reachable from the running app until FR-2 wires selection, so a new script could
only re-prove the board the existing 50 already cover. That exception is stated in the issue so a
later reader knows it was decided rather than forgotten.

The criteria are grouped as: the types and their validation; the three maps' exact slots; **the
geometry, asserted for all three maps** (§5); `Match` taking a definition while keeping one
bases-building code path; and a "nothing else changes" group requiring every existing test to pass
*unchanged* rather than edited.

**All three maps share slots 0–5**, identical to today's first six — the append-don't-insert
discipline phase 6 FR-2 established, which is what makes any script or test keyed on bases 0–5 valid
on every map, and most of what FR-2 has to re-home.

**FR-2 (wf: `475b7d607239`): the home screen offers three maps, plus a `--map` flag.** Kicked off
07-08-2026 as **issue #99** on board **24**, which carries the full acceptance criteria.
Three buttons replace the single `Play` button; `MatchScreen` takes a `MapDefinition` instead of
building its own; `MapLayout` is retired to a test fixture; the desktop head gains
`--map <small|medium|big>` (D-56).

**This is the phase's compatibility break, and it is atomic.** All 50 committed `qa/scripts/` open
with the identical line `0 down 0.500000 0.591667` — a tap on the button this feature replaces. A
branch that changes the home screen without migrating the suite leaves QA red, so the migration
cannot be split out.

Settled at kickoff:

- **Three name-only buttons**, stacked from today's `y = 0.55 * viewportHeight` using the existing
  240x64 reference geometry and a 24-unit gap, so **Small occupies exactly the position `Play`
  occupies today**. Normalized centres are 0.5944 / 0.7167 / 0.8389, stable to within 0.001 at both
  1280x720 and the MI PAD 4's ~1808x1018, and the stack fits inside both.
- **`--map` pushes the welcome screen and then the match screen**, so the stack is identical to what
  a real tap produces and a `back` still returns home rather than exiting. Without this,
  `dismiss-ending.txt` — which exists to prove dismissal is a real pop — could never use the flag.
- **Scripts are assigned by need**: a script requiring a neutral forge or tower goes to Big
  (`capture-neutral-forge`, `forge-buff-decides-an-exchange`, `morale-forge-capture`,
  `neutral-tower-fire`, `ai-contests-forge`); everything else goes to Small, whose slots 0–5 are
  identical to the board it was authored against.
- **The timeline shifts by exactly five frames** and is corrected mechanically: delete the two
  opening tap lines and subtract 5 from every remaining frame number, because `--map` starts the
  match at frame 0 where the tap started it at frame 5. The equivalence is **proved by a
  byte-identical `--dump-state` diff against `main`**, not assumed — anything that fails that is
  re-derived with an annotated header.
- **Four scripts keep tapping real buttons** and must not use the flag: `play`, `play-then-back`,
  `press-then-drag-off`, `back-and-forth`. Three new scripts tap each button and assert the base
  count (6 / 8 / 9), which is D-56's own condition that selection never be verified solely by the
  path no player takes.

**FR-3 (wf: `c4bd0f438bd1`): armies detour around obstacles on a computed path.** Kicked off
08-08-2026 as **issue #102** on board **24**, which carries the full acceptance criteria.
Visibility-graph routing over outward-inset obstacle corners; the army carries the polyline it was
given at submission; `TravelTimeCalculator` returns path length rather than straight-line distance.
Tower fire is untouched — it reads an army's current position, not its line.

Settled at kickoff:

- **The corner inset is 0.02**, confirming the tuning table's own estimate. Medium's start-to-start
  send is 0.912 routed against 0.760 straight — 92 ticks against 76 — and the 92-vs-76 pair is what
  makes the detour externally observable at all.
- **`--dump-state` gains no new line and no new field.** Appending the waypoints to the existing
  `Army …` line was offered and **declined by the user**. The consequence is recorded in the issue
  rather than left implicit: a verifier can confirm the detour's *cost* from `Launch=`/`Arrival=`,
  but **not which side of the obstacle was taken**, until FR-4 draws it. D-52's tie-break is
  therefore proven by unit test only, by decision. This preserves §2a's claim that the phase adds
  no dump-state change.
- **D-52's tie-break is stated concretely enough to assert**: nodes are `from`, `to`, then each
  obstacle's four inset corners in the order `(minX,minY)`, `(minX,maxY)`, `(maxX,minY)`,
  `(maxX,maxY)`, obstacles in map-definition order; exact ties resolve to the lexicographically
  smaller sequence of node indices. On Medium that is `[0, 2, 4, 1]` — the lower-y route via
  (0.40, 0.28) and (0.60, 0.28). The tie there is *guaranteed*, not merely possible.
- **The crossing test uses the obstacle's strict interior**, so a segment that only touches a
  boundary edge or corner is not blocked. Without this the inset corner nodes would themselves
  register as blocked on a graze and the graph would have no usable nodes.
- **A blocked send is never rejected**, and no `SendArmyOutcome` value is added: an unroutable pair —
  reachable only from a test-injected layout that walls a base in, never from a shipped map — falls
  back to the straight two-waypoint path. Refusing blocked sends is the alternative the user rejected
  on 07-08-2026 (§1).
- **`TravelTimeCalculator`'s two-point overload is deleted, not kept alongside** the length-taking
  one, per §5's "never measure a journey in straight-line distance again".
- **Both `AiBrain` travel-time sites** (`AiBrain.cs:136` and `:462`) move to `PathCalculator` here
  rather than at FR-6, which is where D-53 places them, with a test pinning the AI's predicted
  arrival tick to the one `Match` actually assigns for the same send on Medium.
  `TowerThreatEstimator` stays untouched and is named out of scope, so it is not helpfully corrected
  early.

Unlike FR-1, this feature **does** owe a new `qa/scripts/` file: one on `--map medium` showing
`Arrival − Launch = 92`, paired with the same send costing 76 where nothing intervenes.

**FR-4 (wf: `377dd9b78a0e`): obstacles and detoured paths drawn on both heads.** Kicked off
08-08-2026 as **issue #104** on board **24**, which carries the full acceptance criteria.
The obstacle rendered as a filled rectangle, and D-36's shared wave spine following the polyline
instead of a straight line.

**This entry previously claimed army markers "come along for free, since they already draw at
whatever position the rules report". That was wrong, and the kickoff disproved it against the code.**
`MatchScreen.cs:673` does not read a rules-reported position — it recomputes one by interpolating
between the source and target base positions, while `Match.cs:651`'s private `PositionAtTick` walks
`Army.Path` by arc length and is what tower fire resolves against. So since FR-3 merged, an army on
`--map medium` visibly flies **through** the obstacle while the simulation has it routing around,
disagreeing by up to the full detour. That is the fourth instance of the split follow-up **#68**,
**D-45** and **D-53** each closed, this time between the simulation and its own renderer, and it is
what §5's "never measure a journey in straight-line distance again" exists to forbid. Correcting it
is FR-4's first criterion rather than a separate follow-up, since FR-4 is the feature that would
have fixed it regardless.

Settled at kickoff:

- **`Match` exposes the position and the screen stops computing one.** `Match` gains
  `PositionOf(Army)` and `ProgressOf(Army)` — the clamped-fraction polyline walk it already performs,
  plus the fraction itself, which the spine needs to know which waypoints lie between two waves.
  `MatchScreen`'s own interpolation is **deleted, not left alongside**, exactly as FR-3 deleted
  `TravelTimeCalculator`'s two-point overload. Rejected: moving the arc-length walk onto `ArmyPath`
  (which hands the screen the tick→fraction arithmetic that caused this bug, and puts behaviour on a
  value), and duplicating the walk inside `WaveColumnPresentation`.
- **An obstacle is a solid `SaddleBrown` rectangle**, distinct from every colour already on the match
  screen at both 1280x720 and the MI PAD 4's ~1808x1018, drawn **first** so terrain hides nothing.
  Outline-only was rejected: a hollow rectangle reads as a zone, and this phase ships no zones.
- **The obstacle colour is a presentation constant, not a D-22 tuning value** — the call phase 4 FR-4
  made for its flash durations and FR-2 made for its button geometry. No "Tuning values" row is owed.
- **The spine's bend is pure geometry in `WaveColumnPresentation`** (D-25): a new
  `ComputeSpinePoints` emits the point run and `MatchScreen` draws it with the existing
  `DrawSpineSegment`, so no new texture is created.
- **`--dump-state` gains no line and no field**, holding `ARCHITECTURE.md` §2a's claim that this
  phase adds none. Verification is therefore a screenshot plus a device check: one new script
  `qa/scripts/obstacle-and-spine-medium.txt` on `--map medium`, a 100% start-to-start send caught at
  a frame where wave 1 has passed the inset corner at (0.40, 0.28) and wave 2 has not, with its PNG
  committed.

**FR-5 (wf: `d3b78a2ca229`): base shapes shrink by about half on both heads.**
Folds in **#94** whole: the radius fraction, `BaseActionMenu`'s arc geometry, the level and
construction rings, morale-meter and `Forges:` readout clearance, a `qa/scripts/` tap sweep, and a
device check. #94 explicitly warns against assuming "half" means exactly `0.075` — the fraction is a
kickoff decision.

**FR-6 (wf: `e3277c8adba6`): the AI opponent routes and weighs threats around obstacles.**
`TowerThreatEstimator` becomes polyline-aware, and the premise in its own doc comment — "the map has
no pathfinding … so 'routing around' a tower means preferring a different source/target pair" — is
deleted by name rather than left to rot.

### Tuning values

Settled per feature at `/kickoff` and routed through a table there, never written inline at a call
site (D-22). This phase owes at least:

**Settled at FR-1's kickoff (07-08-2026)** — all three layouts. Slots 0–5 are shared by every map and
are today's first six, unchanged:

| Slot | Position | Kind | Garrison | Type |
|---|---|---|---|---|
| 0 | (0.12, 0.50) | HumanStart | 10 | Producer, L1 |
| 1 | (0.88, 0.50) | AiStart | 10 | Producer, L1 |
| 2 | (0.35, 0.25) | Neutral | 5 | Producer, L1 |
| 3 | (0.35, 0.75) | Neutral | 5 | Producer, L1 |
| 4 | (0.65, 0.25) | Neutral | 5 | Producer, L1 |
| 5 | (0.65, 0.75) | Neutral | 5 | Producer, L1 |

| Map | Adds | Obstacles |
|---|---|---|
| **Small** | nothing — exactly slots 0–5 | none |
| **Medium** | 6: (0.50, 0.15) and 7: (0.50, 0.85), neutral producers, garrison 5 — the gates | one: x 0.42–0.58, y 0.30–0.70 |
| **Big** | 6: (0.50, 0.32) Tower L1, 7: (0.50, 0.68) Tower L1, 8: (0.50, 0.50) Forge — all neutral, garrison 10 | none |

Medium's gates carry an ordinary neutral's 5, not 10: phase 6 reserved the doubled garrison for
centre-line *prizes*, and a gate is a producer in a contested spot rather than a prize.

**Settled at FR-3's kickoff (08-08-2026)** — the one value that feature owed:

| Value | Setting | Notes |
|---|---|---|
| Corner inset for routing nodes | **0.02** | How far outside a corner a waypoint sits, so paths do not graze the obstacle. Confirms the estimate this row carried: Medium's start-to-start detour is 0.912 against a straight line's 0.760 — 20% longer, 92 ticks against 76. Medium's inset corners land at (0.40, 0.28), (0.40, 0.72), (0.60, 0.28), (0.60, 0.72), inside 0..1 and no nearer than 0.07 to any slot |

Still owed:

| Value | Owed by | Notes |
|---|---|---|
| Base radius fraction | FR-5 | Currently `0.15` of the viewport's smaller dimension; #94 says confirm, don't assume `0.075` |
| Action-menu arc radius and step | FR-5 | Measured off the base radius, so it moves with it |

## 5. Non-functional requirements

- **Determinism is a correctness requirement here, not a nicety** (S-8). On a symmetric map with a
  centred rectangle, routing above and below the obstacle are *exactly* equal in length, so a tie is
  guaranteed to occur rather than merely possible. It is resolved by an explicit rule, never by
  whatever order a collection happened to enumerate in (D-52).
- **An army's position is computed in one place too, and presentation is one of its readers** (FR-4).
  The rule above was written about the AI and the resolver, and FR-4 found the renderer breaking it
  in exactly the same way — `MatchScreen` interpolating between two base positions while `Match`
  walked the polyline. From FR-4, `Match.PositionOf`/`ProgressOf` are the only source of an army's
  drawn position, and nothing outside `Match` derives one from `LaunchTick`, `ArrivalTick` and two
  base positions. A renderer is not exempt from S-8 because it draws rather than decides.
- **Path length is computed in one place** and shared by resolution and AI prediction (D-53). This is
  the pattern follow-up #68 established for capture prediction and D-45 for the forge term: a second
  copy of the arithmetic is how the simulation and the AI quietly come to disagree.
- **A path is immutable once submitted** (D-51), like a send's unit speed (D-39). Every wave in a
  send flies the same route, and capturing an army's source base mid-flight re-routes nothing.
- The rules layer stays engine-free and headlessly testable (S-2, S-3); obstacles and paths are
  `MW3.Core` types, never engine geometry (D-2).
- Small must stay a **bit-for-bit** reproduction of today's six-base behaviour. A test or script
  weakened to pass rather than re-authored is a defect — the standing rule since phase 3 FR-3a.

### Geometry every shipped map must satisfy

Settled at FR-1's kickoff and asserted there for **all three** maps, because two of discovery's own
layout proposals turned out to violate them:

1. **No tower range at any level reaches a start base.** Ranges are 0.20 / 0.22 / 0.25 / 0.28
   (`LevelTable.Tower.RangeUnits`), and on every map the nearest non-start slot to a start is
   **0.3397**. This is the one invariant phase 6 FR-2 kept when it retired its two weaker siblings,
   and it exists so no player can park an upgraded tower that permanently shells a home base.
   *Discovery's Medium sketch — three neutral producers per flank — violated it*: a neutral at
   (0.35, 0.50) is 0.23 from the human start, inside a level-4 tower's 0.28.
2. **No slot sits closer than 0.12 to any map edge.** `MoraleMeter` and `MoraleMeterTests` already
   depend on this, but only as an observation about `MapLayout`; from FR-1 it is asserted of every
   shipped map.
3. **A map's own claims about coverage are asserted with the range read from `LevelTable`,** never
   with a literal — so changing the range ladder fails a map's test rather than silently un-guarding
   what it was meant to guard. *Discovery's Big proposal violated the spirit of this*: towers at
   (0.50, 0.20) and (0.50, 0.80) sit 0.30 from a centre forge, beyond even a level-4 tower's 0.28, so
   the "two towers guarding a forge" design would have shipped as two towers that never fire in its
   defence. Corrected to y 0.32 / 0.68, giving 0.18.

These bind **shipped** maps only. Test-injected layouts stay unvalidated on purpose — four existing
test files inject deliberately extreme boards (a slot at (0.20, 0.95), starts 0.20 apart) and must
keep working, so the constructor validates only obstacle well-formedness and the slot-inside-obstacle
rule.

## 6. Out of scope

Explicit non-goals for this phase — these are what stop `/autopilot` drifting.

> Read these as sequencing, not design positions (the standing convention since phase 3's mid-phase
> correction). Where a bullet excludes something MW2 has, it is excluded **from this phase** and owed
> by a later one, tracked as a numbered gap in `docs/reference/MW2-PARITY.md` §2.

- **More than two players per map** (**G-17**). Every map here has exactly one human start and one AI
  start. PvP, 1v1v1, 1v1v1v1 and 2v2 need a server (S-7) and belong to the **Multiplayer server**
  project; the `MapSlotKind` enum stays two-player until then.
- **A map file format, or any authored map data outside C#** (**G-18**, partly). Three
  code-defined maps behind a catalog (D-49). There is no data layer in this repo at all yet
  (`docs/ARCHITECTURE.md` §2, "Data: none yet"), and the first thing that genuinely needs authored
  maps is the **Campaigns** project.
- **Geolocated maps.** They remain the **Branding** project's territory (§5 of the parity file, S-6).
- **Tunnels, and over-the-air direct paths for heroes.** Both were in the original project note. Air
  paths have no possible consumer: heroes (**G-4**) do not exist and are phase 8 at the earliest.
  Nothing here may foreclose them — an obstacle is consulted by the path calculator, so a future
  flying mover simply skips that consultation.
- **Zones, and terrain that is anything other than a blocker** (**G-18**, partly). No capture zones,
  no slow fields, no damage fields.
- **Obstacles affecting anything but movement** (D-54): not tower fire, not tower range, not line of
  sight, not base placement.
- **More than one obstacle shape.** Axis-aligned rectangles only (D-50). A map may hold several; they
  are all rectangles.
- **A fourth, QA-only map.** Offered at FR-2's kickoff as a way to preserve roughly 45 scripts
  byte-for-byte — a retired-eight-slot board reachable by `--map` but absent from the home screen —
  and **declined by the user**. It would contradict FR-1's shipped criterion that the catalog holds
  exactly three, and would leave the suite verifying a board no player can select.
- **A map preview, thumbnail, or description text on the home screen.** Name-only buttons, settled
  at FR-2's kickoff; descriptive strings about each board are content the Branding project owns.
- **Remembering the last chosen map, or any persistence of the choice** (S-9). Every launch opens on
  the home screen.
- **Fog of war.** At parity today (`MW2-RULES.md` §1: neither game has it) and untouched.
- **Domination and King of the Hill objective buildings** (**G-15**). Still a modes phase's job, and
  still not a map concern despite living on maps.
- **Energy, heroes and Rush Mode** (**G-4**, **G-5**, **G-16**), which the forges phase named as
  phase 7 before this project was sequenced ahead of them. They are now phase 8.
- **Art, sound, or animation beyond making an obstacle legible and the shapes smaller.** Unchanged
  from phases 3–6.
- **Anything server, account, login, or multiplayer** (S-7).

## 7. Open questions

None. Every question this discovery raised was settled with the user on 07-08-2026:

- The obstacle's behaviour — **armies detour around it**, chosen over refusing blocked sends, with
  the doubled phase cost stated before the choice.
- Big's composition — **9 slots**, two neutral towers with a forge between them, chosen over reusing
  today's 8-slot map unchanged.
- The QA surface — a **`--map` flag**, chosen over re-coordinating all 50 scripts against a new home
  screen.

Questions deliberately deferred to `/kickoff`, which is where they belong rather than here: every
row of the tuning table above, and in particular #94's exact radius fraction.
