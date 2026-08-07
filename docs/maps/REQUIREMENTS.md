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

**FR-1 (wf: `da7ae6122744`): three named maps and obstacles as core map data.**
A `MapDefinition` — named slots plus obstacles — and a `MapCatalog` holding exactly Small, Medium and
Big. `Match` takes a definition rather than a bare slot list, extending D-44's injectable-layout seam.
The obstacle is **data only** here: nothing reads it for movement until FR-3, so Medium ships briefly
with an inert obstacle that armies fly straight through. That is the same separable-regression
ordering phases 5 and 6 used, and it is invisible because nothing draws an obstacle until FR-4.

**FR-2 (wf: `475b7d607239`): the home screen offers three maps, plus a `--map` flag.**
Three buttons replace the single `Play` button; `MatchScreen` runs the chosen map. The desktop head
gains `--map <small|medium|big>`, booting straight into a match. This is the phase's QA-surface
decision (§5, D-56) and the reason the 50 existing scripts are **re-homed rather than
re-coordinated**.

**FR-3 (wf: `c4bd0f438bd1`): armies detour around obstacles on a computed path.**
Visibility-graph routing over outward-inset obstacle corners; the army carries the polyline it was
given at submission; `TravelTimeCalculator` returns path length rather than straight-line distance.
Tower fire is untouched — it reads an army's current position, not its line.

**FR-4 (wf: `377dd9b78a0e`): obstacles and detoured paths drawn on both heads.**
The obstacle rendered, and D-36's shared wave spine following the polyline instead of a straight
line. Army markers come along for free, since they already draw at whatever position the rules
report.

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

| Value | Owed by | Notes |
|---|---|---|
| Small's 6 slot positions and garrisons | FR-1 | Must reproduce the phases 2–5 layout exactly — this is a copy, not a redesign |
| Medium's 8 slot positions and garrisons | FR-1 | New layout; 3 neutral producers per flank is the shape to beat |
| Medium's obstacle rectangle | FR-1 | Centre, and large enough that flank routes are meaningfully longer |
| Big's 9 slot positions and garrisons | FR-1 | Proposal: towers at (0.50, 0.20) and (0.50, 0.80) — today's centre-line positions — forge at (0.50, 0.50) |
| Big's new tower and forge starting garrisons | FR-1 | Phase 6 set the centre-line prizes at 10, double an ordinary neutral's 5 |
| Corner inset for routing nodes | FR-3 | How far outside a corner a waypoint sits, so paths do not graze the obstacle |
| Base radius fraction | FR-5 | Currently `0.15` of the viewport's smaller dimension; #94 says confirm, don't assume `0.075` |
| Action-menu arc radius and step | FR-5 | Measured off the base radius, so it moves with it |

## 5. Non-functional requirements

- **Determinism is a correctness requirement here, not a nicety** (S-8). On a symmetric map with a
  centred rectangle, routing above and below the obstacle are *exactly* equal in length, so a tie is
  guaranteed to occur rather than merely possible. It is resolved by an explicit rule, never by
  whatever order a collection happened to enumerate in (D-52).
- **Path length is computed in one place** and shared by resolution and AI prediction (D-53). This is
  the pattern follow-up #68 established for capture prediction and D-45 for the forge term: a second
  copy of the arithmetic is how the simulation and the AI quietly come to disagree.
- **A path is immutable once submitted** (D-51), like a send's unit speed (D-39). Every wave in a
  send flies the same route, and capturing an army's source base mid-flight re-routes nothing.
- The rules layer stays engine-free and headlessly testable (S-2, S-3); obstacles and paths are
  `MW3.Core` types, never engine geometry (D-2).
- Small must stay a **bit-for-bit** reproduction of today's six-base behaviour. A test or script
  weakened to pass rather than re-authored is a defect — the standing rule since phase 3 FR-3a.

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
