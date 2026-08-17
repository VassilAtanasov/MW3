# Requirements — Game server

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`836033c6cb0a`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 8 moves the simulation **off the client and onto an authoritative server**, while the game
itself stays exactly what it is today: one human against the AI, on the three maps phase 7 shipped.
Nothing a player can see is meant to change. What changes is **who owns the truth**. `MW3.Core`,
`MatchRunner` and `AiBrain` all run server-side; `MW3.Game` stops holding rules and becomes a
renderer that draws a snapshot and submits commands.

The phase exists to make the hard parts of multiplayer solvable and verifiable **with one player, no
accounts, and no second human**: authority, transport, serialization, latency, disconnects, and many
concurrent matches are all present, and every one of them can be exercised by `qa-verifier` against a
local process. When PvP arrives it can then be about *players* — identity, matchmaking, fairness, N
of them — rather than about plumbing.

**This is a scope reduction the user made on 17-08-2026.** The Workflowy node was created as
"Multiplayer server" and was renamed to "Game server" here; everything the reduction deferred moved
to a new sibling project, **Multiplayer** (`98f700a52bf7`), which is now what parity gap **G-17**
points at. Phase 8 closes **no parity gap**. That is expected and is not a defect in the phase:
MW2's netcode is entirely unpublished — `MW2-RULES.md` §9 covers only the engine and release facts —
so almost everything here is **MW3's own design**, and build mode must describe it that way and never
as a port. The already-answered rule buys this phase very little, which is why four decisions were
settled explicitly with the user at discovery (§5) rather than cited from the reference.

Two findings from the existing code shape the whole phase, and both are load-bearing rather than
incidental:

**The wire never needs to carry an army position.** `Army`'s own doc comment states that its position
is a pure function of `LaunchTick`, `ArrivalTick` and `Path`, "recomputed each tick, never
accumulated". `ArmyPath` is immutable and locked at the send's submission tick (phase 7 D-51), unit
speed is locked at the same tick (phase 5 D-39), and the single mutable field on `Army` is
`UnitCount`, which changes only when a tower fires. So a client told *"army 47 launched at tick 900,
arrives at 1160, along this polyline"* can render it exactly, forever, with no further updates. This
is what makes a thin client cheap here when it would be expensive in most games, and it is the reason
the event model in FR-2 is viable at all.

**`MW3.Core` is already deterministic.** There is no `DateTime`, no `Random` and no wall-clock read
anywhere in it, and its only non-trivial arithmetic is `Math.Sqrt` — no `Sin`, `Cos`, `Pow` or `Exp`
— which IEEE-754 requires be correctly rounded. The simulation is therefore bit-reproducible across
x64 and ARM. This phase does not *depend* on that property (a thin client needs no client-side
determinism at all), but FR-6's log and the Game logs / replays project do, and **nothing in the repo
protects it today**. §5 records it as a non-functional requirement so a future `Math.Pow` in a tuning
curve is caught rather than discovered.

## 2. Target users

- **The developer, who is also the first player** — unchanged from every phase since 1. Runs the
  desktop head as the QA surface and the Android head on the MI PAD 4.
- **`qa-verifier`, unattended** — newly significant this phase. It is the first phase where a
  feature can only be verified with **two processes running**, so §"How to run it" in
  `docs/game-server/ARCHITECTURE.md` is a harder contract than it has been before.
- **A future second human**, who does not exist yet and for whom nothing is built here. Named only
  because the value of this phase is measured by how little the Multiplayer project has to undo.

## 3. Success criteria

1. **The game is indistinguishable to play.** With the server running, a full match against the AI on
   any of the three maps plays identically to `main` — same production, same combat, same morale,
   same victory and defeat.
2. **All 50 committed `qa/scripts/` pass unedited** after FR-3, on the loopback path. A script
   weakened to pass rather than re-authored is a defect (the standing rule since phase 3 FR-3a).
3. **The client contains no rules.** After FR-3, `MW3.Game` has no reference to `Match`,
   `MatchRunner`, `CombatResolver`, `AiBrain`, or any `*Table` type. This is mechanically checkable
   and should be checked mechanically.
4. **Offline still works.** The Android head, with no network and no server reachable, plays a full
   match.
5. **One server process runs many matches at once**, with no state shared between them, demonstrated
   headlessly rather than by launching many clients.
6. **A finished match leaves a log** from which the sequence of commands and events can be read.

## 4. Functional requirements

Acceptance conditions are deliberately empty here — `/kickoff <feature>` settles them in both this
file and the GitHub issue. **FR order is dependency order**, as it was in phase 7 and unlike phases
3, 4 and 6.

**FR-1 (wf: `d9c8506314b8`): a match can be expressed as a serializable snapshot, so that something
other than the process owning it can render it.**

A new engine-free `MW3.Protocol` project holds the snapshot: bases (owner, type, level, garrison,
pending construction), armies (id, owner, source, target, count, launch tick, arrival tick, path,
send id, wave index and count), obstacles, per-player morale, per-player forge counts and their
derived percentages, elapsed tick, outcome, and the available actions for the human's own bases.
`MW3.Core` gains a builder that produces one from a live `Match`. The JSON contract is defined here.

Deliberately **invisible** — no behaviour change, no client change, no server, and like phase 7 FR-1
it is the rare feature that adds no `qa/scripts/` file. That should be stated as a decision in the
issue rather than left silent.

*Settled at kickoff, 17-08-2026 (issue #109).* The kickoff found that
`MatchScreen.WriteStateDump` is **already a snapshot serializer** in a bespoke text format — it
writes very nearly this feature's exact field list, read from `Match`. So FR-1 rewires `--dump-state`
to render from the snapshot and requires **byte-identical output**, which turns all 50 committed
`qa/scripts/` into proof that the snapshot is complete and correct instead of resting that claim on
tests written by the session that defined "complete". The `Menu:` and `Strength:` lines stay
screen-owned; that the split falls out cleanly is evidence the snapshot's scope is right. Two further
decisions were settled there: the pure value types **move down** into `MW3.Protocol` rather than
being duplicated with a mapping layer (**D-67**), and army position and progress become **shared pure
functions** that `Match.PositionOf`/`ProgressOf` delegate to (**D-68**), so a client and a server
cannot disagree about where an army is. `--dump-state`'s rewiring is **D-69**.

- Acceptance: `src/MW3.Protocol/MW3.Protocol.csproj` targets `netstandard2.1` with no project and no
  package reference; `MW3.Core` references it and nothing references back; no type in it touches
  `Match`, `MatchRunner`, `AiBrain`, `CombatResolver` or any `*Table`.
- Acceptance: `MapPoint`, `MapObstacle`, `ArmyPath`, `BaseType`, `BaseActionKind`,
  `BaseActionAvailability`, `MatchOutcome`, `SendStrength` and `PlayerControllerKind` are declared in
  `MW3.Protocol` and no longer in `MW3.Core`; nothing is declared twice; **no mapping or adapter type
  exists** between a Core type and a Protocol type; in the diff no test file changes except its using
  directives.
- Acceptance: `MatchSnapshot` is immutable, carries a protocol version, and round-trips through a
  source-generated `System.Text.Json` context to an equal value.
- Acceptance: the snapshot carries map id, elapsed ticks, outcome and obstacles; per player id,
  controller kind, morale points/level/attack %/defence % and forge count with its two percentages,
  as a **list** with the local player named by id; per base every field the client reads today,
  including cap, upgrade cost, defence %, ring-thickness fraction, production progress, pending
  construction, last owner change, owner before change and last fire tick; per army its launch data,
  wave fields and full waypoint path.
- Acceptance: the snapshot carries available actions for the local player's own bases only, in
  `Match.AvailableActions` order, and carries **no army position and no army progress**.
- Acceptance: the builder lives in `MW3.Core`, takes a `Match` and a local `Player`, mutates nothing,
  and handles multi-wave sends and detoured paths.
- Acceptance: `Match.PositionOf`/`ProgressOf` delegate to `MW3.Protocol` pure functions and perform
  no arithmetic of their own; every existing test covering them passes unchanged.
- Acceptance: `MatchScreen.WriteStateDump` formats every line from the snapshot except `Menu:` and
  `Strength:` (D-26), and reads no `Match` member.
- Acceptance: all 50 committed `qa/scripts/` pass unedited with `--dump-state` output
  **byte-identical** to `main`; the empty diff is quoted in the PR. Final-frame screenshots on Small,
  Medium and Big are unchanged.
- Acceptance: the feature adds no `qa/scripts/` file, no script directive and no command-line flag,
  stated as a decision — the dump diff across 50 existing scripts is stronger evidence than any new
  script would be. Unit tests cover the round-trip, builder purity and the available-actions rule.

**FR-2 (wf: `8336e1854fd3`): two snapshots can be reduced to an ordered list of events and rebuilt
from them, so that the wire carries changes rather than whole states.**

A differ turning `(previous, next)` into events — army launched, army strength reduced, army arrived
or destroyed, base captured, base garrison changed, level or type changed, construction started or
completed, morale changed, forge count changed, match ended — and an applier that reconstructs
`next` from `previous` plus those events. `apply(diff(a, b), a) == b` is a property test over
generated match histories, not a handful of examples.

Pure and headless. Explicitly **does not** instrument `Match` to emit events (§5, D-58).

*Settled at kickoff, 18-08-2026 (issue #112).* The kickoff established that **the client does not
need semantic events** — `MatchScreen` renders from state and derives its one change-driven
animation from a state field (`Base.LastFireTick`), and nothing in `MW3.Game` holds a previous
frame to compare against. The semantic value of events is for FR-6's log and for features that do not
exist yet, so the shape chosen is **complete deltas carrying a semantic label** (**D-70**): every
event carries every changed field of its entity, and the kind is derived from *which* fields changed
rather than inferred in a way that would let a field be dropped and break the apply invariant. This
feature also closes **D-60**, which the phase deferred to whichever feature first depended on
determinism; the enforcement is two-part and recorded as **D-71**.

- Acceptance: events live in `MW3.Protocol`, are immutable, and each carries every changed field of
  its entity. Kinds: `BaseCaptured`, `BaseChanged`, `ConstructionStarted`, `ConstructionCompleted`,
  `ArmyLaunched`, `ArmyChanged`, `ArmyRemoved`, `MoraleChanged`, `ForgeCountChanged`,
  `AvailableActionsChanged`, `MatchEnded`.
- Acceptance: `ArmyRemoved` carries **no reason field** — labelling it arrived-vs-destroyed from
  `ArrivalTick <= toTick` is wrong for an army whose strength reaches zero on exactly its arrival
  tick, and the issue names the trap explicitly.
- Acceptance: a batch names its **from-tick and to-tick**; no separate sequence counter exists,
  because elapsed ticks are already monotonic. Gap detection is `batch.FromTick == currentTick`; the
  policy on a gap is FR-4's.
- Acceptance: `diff` works on **non-adjacent** snapshots, since FR-4 may send below the tick rate;
  `diff(a, a)` is empty; `diff` is deterministic to a byte-identical serialized batch with a
  documented canonical order; it throws across differing map ids; one event per entity per batch.
- Acceptance: `apply` mutates neither argument and **throws when the batch's from-tick does not equal
  the snapshot's elapsed ticks**, naming both.
- Acceptance: the property test runs complete matches on Small, Medium and Big capturing a snapshot
  per tick, asserting `apply(diff(a, b), a) == b` for every adjacent pair and for gaps of 2, 5, 20
  and 100 plus first-to-last, with equality structural over the whole snapshot. The generated
  histories are asserted to contain a multi-wave send, a detour, a capture, a construction, tower
  fire, a morale level change and a forge count change, so the test cannot pass on a match where
  nothing happened.
- Acceptance: a source scan over `MW3.Core` **and `MW3.Protocol`** fails on `DateTime`,
  `DateTimeOffset`, `Stopwatch`, `Environment.TickCount`, `Random`, `Guid.NewGuid` and
  `Math.Pow`/`Sin`/`Cos`/`Tan`/`Exp`/`Log`/`Atan2`/`Cbrt`, **naming the file and line**, and passes
  against today's tree unchanged.
- Acceptance: a stable snapshot hash exists in `MW3.Protocol` over a canonical serialization, pinned
  by a golden-hash test; it does **not** use `string.GetHashCode` or `object.GetHashCode` (.NET
  randomizes string hashing per process), and the test asserts two separate processes agree.
- Acceptance: `./gate.ps1` passes; no `qa/scripts/` file changes and all 50 still pass.

**FR-3 (wf: `11478629af65`): the client renders a match it does not own, so that the rules can live
somewhere else.**

The gateway seam — submit a command and get its result, receive an initial snapshot and a stream of
events — with an **in-process loopback implementation** that runs the same diff/apply pipeline the
wire will. `MatchScreen` reads a snapshot instead of `Match`, and computes army positions itself from
path and ticks. `MatchRunner` and `AiBrain` move behind the gateway.

**This is the phase's compatibility break and its largest feature.** All 50 `qa/scripts/` run through
this path and must pass unedited. If it proves oversized at kickoff, the split to reach for is *bases
and morale first, armies and the action menu second* — but it is not pre-split here on a guess.

**FR-4 (wf: `2f0804afb96f`): many matches run on one server process and a client plays one of them
over a network, so that the simulation is genuinely remote.**

`MW3.Server` (ASP.NET Core, `net10.0`, referencing `MW3.Core`), a WebSocket endpoint, one
`MatchSession` per match, a **single 50 ms scheduler walking all live sessions**, match lifecycle and
eviction, the remote gateway implementation, and a `--server <url>` flag on the desktop head.

Many concurrent matches **from the start**, not as a later feature: a `Dictionary<matchId,
MatchSession>` plus a timer that walks it is the same code as a single session, so restricting it
would be artificial work. `Match` has no statics and no ambient clock, which is what makes this true.

**FR-5 (wf: `38ffe9924312`): the Android head plays against a server and still plays without one.**

Android network security config permitting cleartext to a development host; somewhere to enter or
configure the server address, given the Android head accepts no command-line arguments (phase 7
`docs/maps/ARCHITECTURE.md` §2a); and a clean fallback to loopback when no server is reachable, so
success criterion 4 holds.

**FR-6 (wf: `30450bdd69ee`): a finished match leaves a record of what happened.**

The server appends every accepted command with its tick, and the resulting events, to a per-match
log, in a documented format. Gives `qa-verifier` and the circuit breaker a post-mortem artifact, and
hands the **Game logs, game replays** project a finished input format. No playback, no seeking, no
viewer — those are that project's content.

### Tuning values

Per D-22 no number reaches a call site inline; each enters through this table, settled at the
kickoff of the feature that first needs it.

| Name | Value | Feature | Source |
|---|---|---|---|
| Server tick period | 50 ms | FR-4 | `Match.TickDurationMilliseconds`, phase 3 D-27 — the server does not get its own tick rate |
| Snapshot/event send rate | *settled at FR-4 kickoff* | FR-4 | MW3's own; MW2 publishes nothing |
| Session idle eviction | *settled at FR-4 kickoff* | FR-4 | MW3's own |
| Disconnect grace before AI substitution | *settled at FR-4 kickoff* | FR-4 | MW3's own |
| Max concurrent sessions per process | *settled at FR-4 kickoff* | FR-4 | MW3's own |

## 5. Non-functional requirements

**The four decisions settled with the user at discovery, 17-08-2026.** These are binding, not
build-mode calls:

1. **Thin client, event-driven.** The server owns the simulation and sends a snapshot on connect,
   then events. The client holds no rules. Chosen over a full snapshot every tick (simpler, but makes
   every future state addition a bandwidth question) and over a command relay with a client-side
   mirror (tiny bandwidth, but leaves the rules on the client — so it would not move logic to the
   server at all — and makes any future non-determinism a desync bug).
2. **Local in-process mode survives.** One gateway interface, two implementations, loopback the
   default. Requiring a server for single-player would be a product regression for an Android-first
   game and would invalidate all 50 `qa/scripts/`.
3. **WebSocket + JSON.** `System.Text.Json` is in-box, so no NuGet and no cost to **S-5**;
   `ClientWebSocket` works on both heads; payloads stay readable in logs and QA diffs, which matters
   when `qa-verifier` and `code-reviewer` diagnose failures unattended. The codec sits behind an
   interface so a binary encoding can replace it without touching the protocol.
4. **The server records a command and event log** (FR-6), but no playback and no viewer.

**Determinism is now a protected property.** `MW3.Core` must remain free of `DateTime`, `Random`,
wall-clock reads, and any floating-point operation IEEE-754 does not require to be correctly rounded
(`Math.Pow`, `Sin`, `Cos`, `Exp`, `Log`). It holds today by accident; this phase makes it a
requirement, because FR-6's log and the replays project depend on it. How it is enforced is D-60.

**Cost stays at zero.** **S-5** governs the pipeline, not the product, but the user's constraint here
is explicit: **localhost only this phase**, no cloud hosting, no recurring charge, no paid runner. A
deployment target is the Multiplayer project's problem.

**No new client dependencies.** `MW3.Game` gains no NuGet package; `ClientWebSocket` and
`System.Text.Json` are both in-box on `net10.0` and `net10.0-android`.

**`MW3.Protocol` is engine-free and `netstandard2.1`**, like `MW3.Core` — **S-2**. Dependency
direction stays one-way per **S-3**: `Protocol` ← `Core` ← `Game` ← heads, and `Protocol` ← `Core` ←
`Server`.

## 6. Out of scope

Explicit non-goals. These protect the backlog from drift and several of them are the scope reduction
itself.

- **PvP and any second human player.** Moved to the **Multiplayer** project (`98f700a52bf7`).
- **Making `Match` N-player.** `Match.HumanPlayer`/`AiPlayer`, `MatchOutcome.HumanVictory`/
  `HumanDefeat` and the two `MoraleState` fields stay exactly as they are — roughly 33 call sites and
  40 test files this phase does **not** refactor, the same ones phases 6 and 7 declined. Parity
  **G-17** stays open and belongs to Multiplayer.
- **Accounts, authentication, identity, matchmaking, lobbies, and Fame.** **S-9** does not relax this
  phase. A player remains an in-match id plus a controller kind; the server maps nothing onto it
  because there is nothing to map.
- **Cloud hosting, deployment, TLS, and any recurring cost.** Localhost only.
- **Client-side prediction, rollback, and input delay scheduling.** A command applies on arrival at
  the server (D-59). One human against a local server cannot feel the latency that would justify
  them.
- **Reconnecting into a running match**, and match persistence across a server restart.
- **Replay playback, seeking, or a viewer** — the **Game logs, game replays** project.
- **Binary wire encoding.** The seam is built (§5.3); the codec is not.
- **Game modes** — Domination and King of the Hill, parity **G-15**.
- **Spectators, chat, and any social surface.**
- **Any gameplay change at all.** If a match plays differently after this phase, that is a defect,
  not a feature. This phase adds no rule, no building, no map, and no number that a player can feel.

## 7. Open questions

None. All four decisions raised at discovery were settled with the user on 17-08-2026 and are
recorded in §5.
