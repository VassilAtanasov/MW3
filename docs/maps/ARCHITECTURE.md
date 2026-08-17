# Architecture — Maps (phase 7)

> What **this phase** adds or changes. Everything else defers to `docs/ARCHITECTURE.md` (the system
> baseline, S-1..S-9) and to the earlier phases' files, which stay in force:
> `docs/welcome-screen/`, `docs/core-gameplay-loop/`, `docs/base-upgrades-and-types/`,
> `docs/army-sending/`, `docs/morale/`, `docs/forges/`. Decision numbering continues the repo-wide
> sequence — phase 6 ended at **D-48**, so this phase starts at **D-49**.

## 1. Overview

Phase 7 looks like content work and is mostly structural work, in the opposite proportion to phase 6.
Three layouts are nearly free: `Match` has accepted an injectable layout since D-44, so Small,
Medium and Big are data, and Small is a transcription of what phases 2–5 already shipped.

The expensive half is one sentence in the requirements: *an army routes around an obstacle*. Today an
`Army` stores **no path at all** — its own doc comment says its position "is a pure function of
`LaunchTick`, `ArrivalTick`, and the two bases it travels between — recomputed each tick, never
accumulated". That model cannot express a detour. So this phase gives an army a route, computes that
route once at submission, and moves the definition of "how far is it" out of straight-line distance
and into path length. Everything downstream that assumed a straight line is then either untouched
(tower fire, which reads a position), re-derived (the drawn wave spine), or corrected (the AI's
threat estimator, whose doc comment currently states the no-pathfinding assumption in so many words).

Two smaller structural changes ride along. The home screen stops being a single `Play` button, which
breaks the opening line of all 50 committed QA scripts at once (D-56). And the base radius that every
piece of match presentation is measured from shrinks by roughly half, which is issue #94, and which is
sequenced late deliberately so it is re-derived once against the final board rather than twice.

```
MW3.Desktop ---+
               +--> MW3.Game (MonoGame) --> MW3.Core (rules, no engine) <-- MW3.Core.Tests
MW3.Android ---+
                    FR-2 chooses, FR-4/FR-5 draw     FR-1, FR-3, FR-6 live here
```

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds no package reference, no content-pipeline asset, and no platform capability. In
particular it adds **no serialization dependency**, because maps are C# and not files (D-49). See
`docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a and the §2a of every phase since are complete and current;
everything there applies verbatim, including the repo-wide `-m:1` build rule and the `down` / `up` /
`wait` scripted-input vocabulary.

**This phase changes how a match is reached**, which is the first change to that path since phase 2:

- The home screen shows **three buttons** — Small, Medium, Big — stacked from `y = 0.55 *
  viewportHeight` (Small occupies exactly the position `Play` occupied), in place of the single
  `Play` button. There is no longer a default map chosen by the application.
- The desktop head accepts **`--map <small|medium|big>`** (case-insensitive). It does **not** bypass
  the home screen: it pushes the welcome screen first and then a match on the chosen map, so the
  screen stack is identical to a real button tap and `back` from the match returns to the welcome
  screen rather than exiting. An unrecognised or missing value writes the offending value and the
  three valid ones to stderr and exits 1 before any graphics device is created, mirroring
  `--time-scale`. `MW3.Android` accepts no CLI args and is unaffected. This is the phase's one new
  command-line flag; the reason for it, and for pushing welcome first, is D-56.
- Every committed `qa/scripts/` file therefore names a map, via `--map`, **except** the five scripts
  that verify home-screen selection itself (`play-then-back.txt`, `press-then-drag-off.txt`,
  `back-and-forth.txt`, plus the three new per-button scripts) — those tap a real button, never the
  flag. `play.txt` was a sixth until issue #111 retired it into `select-map-small.txt`, which taps
  the identical coordinate. `dismiss-ending.txt` uses the flag: the flag preserves the welcome
  screen beneath the match, which is the property that script depends on.

`--dump-state` gains no new line this phase. A map is not per-tick state, and the chosen map is
already evident from the base count and composition in the existing output. If a later feature finds
otherwise, that feature's kickoff fixes the line's shape, exactly as FR-3's did in phase 6.

**The three-map home screen is the compatibility break**, and it is a wider one than phase 6's eight
bases. All 50 committed scripts opened with the identical line `0 down 0.500000 0.591667` — the
`Play` button. Scripts using `--map` had that line and the following `up` deleted, and every
remaining frame number reduced by 5 (the match starts at frame 0 under the flag, where a real tap
started it at frame 5). D-56 is what keeps that from becoming 50 files of re-derived coordinates —
it removes the *coordinate* churn, not the *expectation* churn: a script whose dump reflects
board-wide state (AI scripts, victory/defeat, the forge and neutral-tower scripts) still moved,
because the shipped eight-slot board is retired and neither Small (6 slots) nor Big (9) reproduces
it. Every re-homed script whose target map's slots 0–5 sufficed to run it unchanged was proved
byte-identical against its own dump on `main`; the five Big-map scripts (`capture-neutral-forge`,
`forge-buff-decides-an-exchange`, `morale-forge-capture`, `neutral-tower-fire`, `ai-contests-forge`)
were not, since Big's composition genuinely differs, and their headers were re-derived instead. A
second break rides with FR-5: every tap that was only valid because it landed inside the *current,
larger* base radius must be re-derived, which #94 already calls out. A script weakened to pass
rather than re-authored is a defect.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  MapDefinition.cs      NEW - a named map: its slots and its obstacles (D-49)
  MapCatalog.cs         NEW - exactly three definitions: Small, Medium, Big. Not a registry
                         that grows at runtime, not a loader (D-49)
  MapObstacle.cs        NEW - an axis-aligned rectangle in normalized 0..1 space (D-50)
  MapLayout.cs          folded into MapCatalog; the phase-6 eight-slot layout survives as a
                         TEST FIXTURE, not as a shipped map (D-49)
  MapSlotKind.cs        unchanged - stays HumanStart | AiStart | Neutral (two players, §6)
  Match.cs              takes a MapDefinition rather than a bare slot list, extending D-44;
                         exposes its obstacles so presentation can draw them
  PathCalculator.cs     NEW - visibility graph over inset obstacle corners, shortest path,
                         deterministic tie-break (D-52). Pure geometry, like
                         TowerThreatEstimator
  ArmyPath.cs           NEW - an ordered polyline plus its total length; the value an Army
                         carries and every wave in a send shares (D-51)
  Army.cs               carries its ArmyPath; position walks the polyline instead of
                         interpolating between two base positions
  TravelTimeCalculator  takes a path length rather than two endpoints (D-53)
  TowerThreatEstimator  FR-6 only: sums the chord over every segment of a polyline, and loses
                         the "the map has no pathfinding" premise in its doc comment

src/MW3.Game/
  WelcomeScreen.cs      three map buttons in place of Play; hands the chosen MapDefinition on
  MatchScreen.cs        constructed with a MapDefinition; draws obstacles (FR-4); its
                         _radiusFraction and everything measured off it shrink (FR-5, #94)
  BaseActionMenu.cs     FR-5 only: arc radius and button placement re-derived at the new radius
  WaveColumnPresentation FR-4 only: D-36's shared spine follows the polyline
  MW3Game.cs / heads    --map parsing (D-56)
```

## 4. Key decisions

**D-49: a map is a `MapDefinition` in C#, and there is no map file format.** `MapCatalog` holds
exactly three, named. This is the smallest thing that satisfies "three maps and a chooser", and the
alternative — JSON or a content-pipeline asset — would introduce the repo's first data layer for no
present gain. `docs/ARCHITECTURE.md` §2 still reads "Data: none yet", and the first genuine consumer
of authored map data is the **Campaigns** project, which needs missions anyway. G-18's map-format half
therefore stays open on purpose. `MapLayout` is folded in; the phase-6 eight-slot layout it currently
holds does not survive as a *shipped* map — Big replaces it at nine slots — but is preserved as a test
fixture so phase 6's tests keep an eight-slot board to assert against.

**D-50: an obstacle is an axis-aligned rectangle.** Circles were the alternative and are rejected on
arithmetic: a circle's detour runs along tangent points, which is fiddlier to get exactly right in a
determinism-critical path, whereas a rectangle contributes four corner nodes and a closed-form
segment-intersection test. A rectangle is also drawable with the stretch-a-1x1-texture trick
`MatchScreen` already uses for every other shape. A map may hold more than one; they are all
rectangles.

**D-51: a path is computed once at submission and carried by the army.** `Army` stores no path today
because it never needed one. It gets one now, fixed at the moment the send is accepted, and every wave
in the send shares it. This mirrors D-39's speed lock exactly, and for two reasons of the same kind: a
path re-derived later could differ from the one the arrival tick was computed against — letting a
later wave overtake an earlier one — and capturing an army's source base mid-flight must not re-route
something already in the air, which is the phase-2 rule (D-15) that capture changes nothing about an
army already launched.

**D-52: routing is a visibility graph over inset corners, with an explicit tie-break.** Nodes are the
two endpoints plus every obstacle corner pushed outward by a fixed inset; an edge exists between two
nodes whose connecting segment crosses no obstacle; the route is the shortest path through that graph.
At this scale — at most nine bases and a handful of corners — the algorithm's efficiency is irrelevant
and its *predictability* is the whole point. **The tie-break is a correctness requirement, not a
tidiness one:** on a symmetric map with a centred rectangle, passing above and passing below the
obstacle are exactly equal in length, so the tie is guaranteed rather than merely possible, and S-8
forbids resolving it by whatever order a collection happened to enumerate in. Ties are broken by node
index, which is stable because the node list is built from the map definition in declaration order.

**D-53: `TravelTimeCalculator` takes path length, not two endpoints.** One place computes how long a
journey takes, and both the resolver and the AI's prediction go through it — the pattern follow-up
**#68** established when it extracted `CombatResolver.WouldCapture` as the single shared capture
predicate, and that **D-45** repeated for the forge term. A second copy of the arithmetic is precisely
how the simulation and the AI come to disagree silently, which this repo has now paid for twice. It is
also what makes FR-6 small: correcting one calculator corrects every AI prediction that reads it, and
FR-6 is left with only the threat estimator's own straight-segment geometry.

**D-54: obstacles block movement and nothing else.** Not tower fire, not tower range, not line of
sight, not base placement. A tower shoots an army it is within range of, whether or not a rock sits
between them. This is a scope wall rather than a claim about realism: each of those would be a
separate mechanic with its own tests, and line-of-sight in particular would reopen the tower-fire path
that phase 6 D-47 has just finished changing.

**D-55: detour routing is a deliberate divergence from MW2, recorded in `MW2-PARITY.md` §4.**
`MW2-RULES.md` §1 currently lists "straight line, base to base. No pathfinding, no fog of war" as a row
where *both games already agree*, and §10 lists MW2's terrain behaviour as unpublished. So adding
detours is not closing a gap — it is choosing to differ, which `MW2-PARITY.md` §0 permits only with the
user's agreement and only as a §4 entry with its reasoning. That agreement was given 07-08-2026. The
movement row moves out of §1 accordingly. Like the AI (G-21), this is MW3's own design and must never
be described as a port.

**D-56: map choice reaches the simulation two ways — a home-screen button and a `--map` flag.** The
flag is not a convenience. All 50 committed QA scripts open by tapping a Play button that this phase
deletes, and without a bypass every one of them would have its opening coordinates re-derived against
a new home screen, and re-derived again at the next home-screen change. The flag decouples the scripted
QA surface from home-screen layout permanently. It comes with an obligation, or it would hollow the
suite out: **the buttons themselves must still be verified by scripts that tap them**, so selection is
never proven only by the path no player takes. Phase 6 shipped no new flag and said so; this phase
ships exactly one and says why.

> **Amended at FR-2's kickoff (07-08-2026).** This decision claimed the flag "re-homes them instead
> of re-coordinating them". That is half right, and the optimistic half was the important one — the
> same failure mode phase 6's §2a hit at FR-3's kickoff, recorded here rather than left for build
> mode to discover. The flag does remove the *coordinate* churn. It does **not** remove the
> *expectation* churn: today's eight-slot board is retired and neither Small (6 slots) nor Big (9)
> reproduces it, so every script whose result reflects board-wide state — the AI scripts,
> victory/defeat, the forge and neutral-tower scripts, anything running past the AI's first decision
> at tick 40 — has expectations that move whichever map it lands on. Those are re-derived and their
> headers annotated, never weakened. The user was offered a QA-only fourth "legacy" map that would
> have preserved roughly 45 scripts byte-for-byte and declined it: it would contradict FR-1's shipped
> criterion that the catalog holds exactly three, and would leave the suite verifying a board no
> player can select — precisely the hollowing-out this decision's own obligation guards against.
>
> Two mechanics follow, both settled at that kickoff. The flag **pushes the welcome screen and then
> the match screen**, so the screen stack is identical to a real tap and a `back` still returns home
> instead of exiting — without which `dismiss-ending.txt` could not use the flag at all. And the
> bypass **shifts every script's timeline by exactly five frames**, since the match is pushed at
> frame 0 where the tap pushed it at frame 5; because all 50 scripts open with the identical two
> lines, the correction is uniform — delete them and subtract 5 from every remaining frame number —
> and it is proved by a byte-identical dump diff against `main` rather than assumed.

## 5. Cross-cutting conventions

Everything in the earlier phases' §5 sections stays in force. This phase adds:

- **Never measure a journey in straight-line distance again.** After FR-3, `MapPoint`-to-`MapPoint`
  distance is an implementation detail of `PathCalculator`. Any other caller that wants "how far" or
  "how long" asks for the path — otherwise the straight-line assumption reappears somewhere the tests
  do not look, which is exactly how the AI/resolver disagreements in #68 and D-45 arose.
- **A map is data, and data has no behaviour.** `MapDefinition` and `MapObstacle` are values; the
  rules for what an obstacle *does* live in `PathCalculator` and `Match`, not on the obstacle. This is
  what keeps a future flying mover (heroes, G-4) to a skipped consultation rather than a new obstacle
  subtype.
- **Small is the regression anchor.** Any change that alters behaviour on the Small map is a
  regression against phases 2–5 until proven otherwise, because Small *is* their board.
- **Every number goes through the kickoff-settled "Tuning values" table** (D-22) — map coordinates,
  the obstacle rectangle, the corner inset, and the base radius fraction included. None of them
  appears inline at a call site.
