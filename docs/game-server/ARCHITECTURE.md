# Architecture — Game server (phase 8)

> What **this phase** adds or changes. Everything else defers to the repo-wide
> `docs/ARCHITECTURE.md` (standing decisions S-1..S-9) and to the earlier phases' files, which
> remain current. Decisions are numbered continuously across phases; phase 7 ended at **D-56**, so
> this phase opens at **D-57**.

## 1. Overview

Phase 8 splits the running game into two processes and puts the simulation in the far one. The
dependency graph gains a fourth library and a second executable:

```
                    MW3.Desktop ---+
                                   +--> MW3.Game --------+
                    MW3.Android ---+    (renderer only)  |
                                                         +--> MW3.Protocol
                    MW3.Server ------> MW3.Core ---------+    (snapshot, events, JSON)
                    (ASP.NET Core)     (rules + AI)
```

`MW3.Protocol` is the new engine-free, `netstandard2.1` library at the bottom: the serializable
snapshot of a match, the event types, and the JSON contract. `MW3.Core` gains a builder that produces
a snapshot from a live `Match`, and a differ/applier pair over snapshots. `MW3.Game` **loses** its
reference to `MW3.Core` entirely and depends only on `MW3.Protocol` — that is the phase's headline
structural change and the thing success criterion 3 checks mechanically.

A client reaches a match through one interface with two implementations:

```
MatchScreen ──> IMatchGateway ──┬── LoopbackMatchGateway  (in-process; owns Match + MatchRunner + AiBrain)
                                └── RemoteMatchGateway    (WebSocket + JSON to MW3.Server)
```

Loopback is the default and is what every `qa/scripts/` run and every offline Android session uses.
Both implementations run the **same** diff/apply pipeline (D-61), so local play is not a shortcut
around the protocol — it exercises it.

Server-side, one process holds many matches:

```
MW3.Server
  WebSocket endpoint  ──> resolves matchId ──> MatchSession
  MatchSessionRegistry: Dictionary<matchId, MatchSession>
  TickScheduler (one 50 ms hosted service, walks every live session)
     MatchSession = Match + MatchRunner + AiBrain + inbox + last snapshot + connections + log
```

## 2. Stack

Unchanged from `docs/ARCHITECTURE.md` §2 except:

- **New**: `MW3.Server` — ASP.NET Core on `net10.0`, referencing `MW3.Core`. In-box only; no NuGet
  beyond the framework.
- **New**: `MW3.Protocol` — `netstandard2.1`, no dependencies, no engine types (S-2).
- Serialization: **`System.Text.Json`**, in-box on every target. Source-generated contexts, so the
  Android head stays trimming- and AOT-safe.
- Transport: **WebSocket** — `ClientWebSocket` on the heads, ASP.NET Core's WebSocket middleware on
  the server.
- Data: still none. **No database, no persistence** beyond FR-6's append-only log files.
- Hosting target: **localhost only** this phase. No cloud, no TLS, no recurring cost.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a and every phase's §2a since remain current, including the
repo-wide `-m:1` build rule, the `down` / `up` / `wait` scripted-input vocabulary, `--smoke`,
`--screenshot`, `--script`, `--time-scale`, `--dump-state`, and phase 7's `--map`. `qa-verifier`
follows this section literally.

**This phase is the first where a feature may need two processes.** The default remains one.

Local play — unchanged, and still the default with no flags. This is the loopback path, and it is
what all 55 committed `qa/scripts/` exercise:

```powershell
dotnet run --project src/MW3.Desktop -- --map small
```

Start the server (foreground; prints the listening URL and exits non-zero on a port clash):

```powershell
dotnet run --project src/MW3.Server
```

Play against it from the desktop head — `--server` is this phase's one new client flag, and it
composes with every existing flag including `--map`, `--script` and `--dump-state`:

```powershell
dotnet run --project src/MW3.Desktop -- --server http://localhost:5180 --map small
```

An unreachable or malformed `--server` value writes the offending value to stderr and exits 1 before
any graphics device is created, mirroring how `--map` and `--time-scale` already behave.

Android connects to a development host by address rather than by flag (the Android head accepts no
command-line arguments — phase 7 `docs/maps/ARCHITECTURE.md` §2a); FR-5 settles where that address is
entered. With no server reachable it falls back to loopback and plays offline.

Many concurrent matches are verified **headlessly**, not by launching many clients — the same
approach phase 6 FR-3 took for the forge cap. `MW3.Server`'s session registry and scheduler are
driven directly from a test.

## 3. Project layout

Two new projects; existing ones keep their roles.

| Project | Target | Role | Changes this phase |
|---|---|---|---|
| `MW3.Protocol` | `netstandard2.1` | **New.** Snapshot, events, diff/apply, JSON contract | FR-1, FR-2, FR-3 |
| `MW3.Core` | `netstandard2.1` | Rules, AI. Gains snapshot building and the loopback gateway | FR-1, FR-3 |
| `MW3.Game` | `net10.0;net10.0-android` | **Renderer only.** Loses its `MW3.Core` reference | FR-3, FR-5 |
| `MW3.Server` | `net10.0` | **New.** ASP.NET Core host, sessions, scheduler, log | FR-4, FR-6 |
| `MW3.Desktop` | `net10.0` | Head, and composition root from FR-3 (D-74). Gains `--server` | FR-3, FR-4 |
| `MW3.Android` | `net10.0-android` | Head, and composition root from FR-3 (D-74). Gains network config and an address | FR-3, FR-5 |

Tests follow the existing convention: `MW3.Core.Tests` keeps snapshot/diff coverage, and a new
`MW3.Server.Tests` covers sessions, the scheduler and the wire. `MW3.Core.Tests` also holds the
source-generated `JsonSerializerContext` and the one converter the snapshot needs, until FR-4 gives
the codec a shipped home — see **D-72** for why they cannot live in `MW3.Protocol` itself.

## 4. Key decisions

**D-57: the protocol is its own `netstandard2.1` project, not a folder in `MW3.Core`.** Considered:
putting the DTOs in `MW3.Core` and letting `MW3.Game` keep referencing it. Chosen because success
criterion 3 — "the client contains no rules" — is only mechanically checkable if the client *cannot*
see the rules, and a project reference is the only boundary the compiler enforces. A folder
convention would be re-litigated by every future feature; a missing reference is a build error.
`MW3.Protocol` therefore holds data and serialization only: no behaviour, no tables, no `Match`.

**D-58: events are derived by diffing two snapshots, not emitted by `Match`.** Considered:
instrumenting `Match.Advance` to raise events as state changes, which is what most engines do.
Rejected on two grounds. `Match` is 1368 lines with mutation spread across production, combat, tower
fire, capture, construction, morale and forge recomputation, so an emission path would have to be
threaded through all of it and would be silently incomplete the first time a later phase added a
mutation without a matching event. And it would create a **second source of truth** that can disagree
with the state — exactly the resolver/prediction desync class that follow-up #68 closed once for
building defence, phase 5 patched again for morale, and phase 6 D-45 had to guard a third time for
forges. Diffing makes the events a *pure function* of the snapshot, so they cannot disagree with it,
and makes `apply(diff(a, b), a) == b` a property test rather than a hope. The cost is honest: a diff
is O(bases + armies) per tick and computes changes the emitter would have known for free. At nine
slots and a few dozen armies that is irrelevant.

**D-59: a command applies when it arrives; there is no prediction, no rollback, and no scheduled
input delay.** Considered: MW3-side delay-based scheduling (`currentTick + inputDelay`), which is
what lockstep RTS traditionally does, and client-side prediction with server reconciliation.
Chosen because with one human and a local server the round trip is invisible, and because a send has
no immediate visual consequence beyond an army leaving a base — roughly 100 ms before the column
appears is acceptable for this genre. Both alternatives are real work that buys nothing measurable at
this phase's scale. **This reopens in the Multiplayer project** if PvP feel demands it; the gateway
interface is where it would land, so nothing here forecloses it.

**D-60: determinism becomes an enforced property, not an accident.** `MW3.Core` today contains no
`DateTime`, no `Random`, no wall-clock read, and no floating-point operation outside `+ - * /` and
`Math.Sqrt` — all of which IEEE-754 requires be correctly rounded, so the simulation is
bit-reproducible across x64 and ARM. Nothing protects that. This phase adds an enforcement mechanism
(a banned-API check, an analyzer rule, or a golden-hash test over a fixed command script — the
feature that first needs it settles which at its kickoff). Considered: leaving it undefended, on the
grounds that a thin client needs no client-side determinism. Rejected because FR-6's log and the
Game logs / replays project both depend on it, and the failure mode is a replay that silently
diverges rather than an error.

**D-61: loopback runs the same diff/apply pipeline as the wire.** Considered: letting the in-process
gateway hand the renderer a snapshot directly, since it has one and there is no bandwidth to save.
Rejected because it would make local play a code path the network path does not share, so the 50
`qa/scripts/`, the Android head and every developer run would all exercise the *easy* path and leave
the protocol tested only by whatever explicitly targeted it. Running the same pipeline costs one diff
per tick in-process and buys protocol coverage on every single run of the game. Loopback still skips
serialization to bytes; it does not skip diff, apply, or the snapshot types.

**D-62: the server owns time.** `--time-scale` becomes a per-session server-side property rather than
a client one, because the client no longer advances anything. The desktop head's existing flag
therefore has to reach the session at creation. `Match.TickDurationMilliseconds` stays 50 ms (phase 3
D-27) and the server does **not** get a tick rate of its own — one clock, one value, one place.

**D-63: one scheduler for all sessions, not a thread or timer per match.** Considered: a task per
`MatchSession`, which reads more naturally. Rejected because a tick on a nine-slot board is
microseconds while a context switch is not, so per-match threads would cost more in scheduling than
the simulation costs in work. A single hosted service walking the registry every 50 ms scales to
hundreds of sessions in one process before the tick budget binds. This is only true because `Match`
has **no statics and no ambient clock** — `MapCatalog` and the `*Table` types are read-only data — so
two sessions share nothing. Horizontal scale, when it is ever needed, is more processes with the
gateway routing by `matchId`; a session never migrates mid-match, because that would require state
serialization this phase does not build.

**D-64: WebSocket + JSON, with the codec behind an interface.** Considered: raw TCP with custom
framing (leanest, but reconnection and handshake are work WebSocket gives free), gRPC bidirectional
streaming (typed contracts, but HTTP/2 on Android MonoGame is fragile and it adds dependency weight
this project has avoided), and MessagePack over WebSocket (~4× smaller). Chosen because
`System.Text.Json` and `ClientWebSocket` are both in-box on all three targets, so **S-5** costs
nothing, and because a readable payload is worth real money when `qa-verifier` and `code-reviewer`
have to diagnose a protocol failure unattended — a JSON snapshot is close to what `--dump-state`
already emits. The codec seam exists so binary can be swapped in without touching the protocol; it is
explicitly not exercised this phase.

**D-65: the AI runs server-side, and a disconnect substitutes it for the missing player.** `AiBrain`
is a player, and players are server-side; `MatchRunner` already consults an `IPlayerBrain`, so
substituting one on disconnect is swapping an interface implementation rather than new machinery.
This is the cheapest disconnect policy available and it is only cheap because of phase 2's D-16.
Considered: pausing the match (griefable once PvP exists) and immediate forfeit (hostile to a dropped
connection). The grace period before substitution is a tuning value settled at FR-4's kickoff.

**D-66: available actions ship in the snapshot and stay authoritative on the server.**
`Match.AvailableActions` has two callers after this phase: the snapshot builder, so the client can
grey out menu entries without knowing the rules, and the server's command validation, because a
client can lie. Same code, two callers — the same shape `CombatResolver.WouldCapture` took when #68
made it the one shared capture predicate. A command the client believed valid may still be rejected;
the gateway carries a command result for exactly this reason, and the client must render a rejection
rather than assume success.

**D-67: the pure value types move down into `MW3.Protocol`; they are not duplicated.** `MapPoint`,
`MapObstacle`, `ArmyPath`, `BaseType`, `BaseActionKind`, `BaseActionAvailability`, `MatchOutcome`,
`SendStrength` and `PlayerControllerKind` are data with no rules attached, and the snapshot needs all
of them. Considered: declaring a parallel set in `MW3.Protocol` and mapping at the builder, which is
the conventional answer and needs no churn in existing files. Rejected because it is two definitions
of every enum kept in step by hand plus a mapping layer that must be extended for every future enum
member — and a value that means one thing on one side of a mapping and another on the other side is
precisely the drift this repo has already paid to close three times (follow-up #68 for building
defence, phase 5's `WouldCapture` patch for morale, D-45 for forges). Also considered: leaving them
in `MW3.Core` and having `MW3.Protocol` reference it, which inverts the dependency D-57 exists to
create and would let `MW3.Game` reach the rules transitively. The churn objection is largely
answered by `ImplicitUsings` already being on repo-wide, so `<Using Include="MW3.Protocol" />` absorbs
the move: the bar set at FR-1 is that **no test file changes except its using directives**.

**D-68: army position and progress are shared pure functions, not duplicated geometry.** They become
functions of `(path, launch tick, arrival tick, current tick)` in `MW3.Protocol`, and
`Match.PositionOf`/`ProgressOf` delegate to them. Considered: leaving the math in `Match` and letting
the client reimplement it at FR-3, which touches nothing now. Rejected for the same reason as D-67 —
two implementations of one calculation drift, and here the symptom would be armies *drawn* somewhere
other than where tower range and arrival actually resolve, which is a bug that looks like a rendering
glitch and isn't. Also considered: putting the position in the snapshot so only the server computes
it, which was rejected because it discards the phase's cheapest property (launch data alone renders
an army forever) and would make smooth motion depend on the send rate. Same one-shared-implementation
shape as `CombatResolver.WouldCapture` after #68 and `TravelTimeCalculator` after D-53.

**D-69: `--dump-state` renders from the snapshot, and must stay byte-identical.** Settled at FR-1's
kickoff on the finding that `MatchScreen.WriteStateDump` is already a snapshot serializer in a
bespoke text format, writing very nearly the exact field list FR-1 has to define. Rewiring it makes
all 55 committed `qa/scripts/` evidence that the snapshot is complete and faithful, which is a far
stronger standard than unit tests written by the same session that decided what "complete" means.
The `Menu:` and `Strength:` lines keep coming from the screen, because menu and selected strength are
presentation state under D-26 and are not part of the match — that this boundary falls out cleanly is
itself evidence the snapshot's scope is right. The cost is that a feature scoped as pure foundation
now edits a client file; that is accepted, and it is the first instalment of FR-3's job.

**D-70: an event is a complete delta carrying a semantic label.** Settled at FR-2's kickoff on a
finding about what the client actually consumes: `MatchScreen` renders from state and derives its one
change-driven animation from a state field (`Base.LastFireTick`), and nothing in `MW3.Game` holds a
previous frame to compare against. So the client needs an efficient *delta*, not a domain-event
vocabulary; the semantic value is for FR-6's log and for features that do not exist yet. Considered:
pure structural field deltas, which are smallest and make the apply invariant nearly trivial, but
reduce FR-6's log to a stream of "base 3 garrison 4→5" lines that no human or replay tool can read
without re-inferring meaning. Also considered: rich semantic domain events modelled on what happened
in the game, which read best but require the differ to *infer* intent from state changes — and any
event that does not carry every affected field silently breaks `apply(diff(a, b), a) == b`, the one
property D-58's whole argument rests on. The chosen shape takes both: every event carries every
changed field of its entity, so reconstruction is exact, and the kind is a label derived from *which*
fields changed rather than an inference that permits dropping one. Where a label cannot be derived
with certainty it is **not invented** — `ArmyRemoved` deliberately carries no arrived-vs-destroyed
reason, because the obvious rule (`ArrivalTick <= toTick`) is wrong for an army whose strength
reaches zero on exactly its arrival tick.

**D-71: determinism is enforced by a banned-API source scan and a golden snapshot hash, together.**
This closes D-60, which deferred the mechanism to whichever feature first depended on it — FR-2,
whose property test only means anything if a run is reproducible. Considered: either half alone. The
scan alone catches only what it knows to look for and would miss a nondeterministic iteration order
or a change inside a referenced library; the hash alone catches any divergence whatever its cause but
reports only "the hash changed", leaving an autonomous build-mode session to bisect unaided. Together
the scan names the file and line for the common case and the hash backstops everything else. Two
things the implementation must get right or the test is theatre: the scan covers **`MW3.Protocol` as
well as `MW3.Core`**, because D-68 moved the position and progress math there; and the hash must not
be built on `string.GetHashCode` or `object.GetHashCode`, since .NET randomizes string hashing per
process, so such a hash would differ between two runs on one machine. The hash is defined over a
canonical serialization and its cross-process stability is asserted by the test rather than assumed.
It also gives FR-4 a desync detector for free.

**D-72: the source-generated `JsonSerializerContext` lives with whoever targets `net10.0`, not in
`MW3.Protocol`.** Found at FR-1's build, and it is a genuine collision between two of that feature's
own criteria rather than a preference. `MW3.Protocol` must target `netstandard2.1` (so `MW3.Core`,
which is `netstandard2.1` under S-2/D-2, can reference it) and must carry **no `PackageReference`**,
because a dependency-free project is the cheapest possible proof of D-57's boundary. But
`System.Text.Json` is in-box only from `net6.0`: on `netstandard2.1` it is a NuGet package. So the
snapshot types stay in `MW3.Protocol` as plain JSON-shaped data with no serialization attributes at
all, and the context that serializes them lives in the nearest project that targets `net10.0` —
`MW3.Core.Tests` at FR-1, since nothing *ships* a serialized snapshot until FR-4, which owns the
codec seam (D-64) and gives it a permanent home. Considered and rejected: taking the package (breaks
the rule the project exists to hold, and puts a trimming dependency in the Android head's transitive
graph for nothing); multi-targeting `MW3.Protocol` as `netstandard2.1;net10.0` (works, but
`MW3.Core`'s reference resolves the `netstandard2.1` asset while a head would resolve the `net10.0`
one, and two assemblies of one identity in a copy-local set is a build conflict, not a design);
declaring the snapshot types twice (D-67 rejects exactly this). One consequence FR-4 inherits:
`MapObstacle` needs a converter, because it is a struct with get-only extents and a validating
constructor, and `System.Text.Json` reaches for a struct's parameterless constructor and then assigns
settable properties — so without one an obstacle deserializes as four silent zeroes. Teaching the
codec to rebuild the type through its constructor costs the type nothing; loosening its properties to
`init` would let any caller build an invalid obstacle, and `[JsonConstructor]` is unavailable for the
same package reason.

**D-73: a match knows which map it is.** `MapDefinition` gains an optional `MapId?`, `MapCatalog`
stamps its three entries with theirs, and `Match` exposes it — null for a definition a caller
assembled itself, which only a test does. The snapshot needs a map identity (a client has to know
which board it is drawing) and searching `MapCatalog` for a definition matching by value would be a
lookup that can fail on a legitimate custom layout. The snapshot carries the map as a **name**, not
as the `MapId` enum: `MapId` belongs to the rules' catalogue and stays there, since D-49 leaves the
map file format to the Campaigns project and the wire should not be holding an enum whose members
that project will add to.

**D-74: the heads become the composition root, and the client names a map by name.** Settled at
FR-3's kickoff. `LoopbackMatchGateway` needs `MW3.Core`, and D-57 takes `MW3.Core` away from
`MW3.Game` — so something above `MW3.Game` has to build the gateway, and the only things above it are
`MW3.Desktop` and `MW3.Android`. They gain the `MW3.Core` reference and inject a gateway factory into
`MW3Game`, which passes it to `WelcomeScreen`. Considered: leaving `MW3.Game` referencing `MW3.Core`
and enforcing "no rules read" by review, which is what D-57 already rejected; and putting the loopback
implementation in `MW3.Protocol`, which would drag `Match` into the protocol and invert the whole
dependency. The second consequence is the useful one: `MapId` and `MapCatalog` leave the client
entirely, because the factory takes a map **name** and exposes the available names in catalogue
order. That is the same name `MatchSnapshot.MapId` already carries under D-73, so the client holds
one map identity concept rather than two, and the Campaigns project can add a map without the
renderer learning about it.

**D-75: `diff` and `apply` live in `MW3.Protocol`, not `MW3.Core`.** §1 above says `MW3.Core` gains
the differ/applier pair; that line is superseded here. A client applies event batches to its own
snapshot, and after FR-3 the client cannot see `MW3.Core` — so an applier in `MW3.Core` would make
success criterion 3 unreachable. Both are pure functions over snapshots with no rule in them, so
`MW3.Protocol` is where they belong on the merits too, next to `ArmyPathMath` for the reason D-68
gives. Raised at FR-3's kickoff while FR-2 was still unbuilt, and the user settled that **FR-2's
issue (#112) would not be edited** for it — the correction cost FR-3 a file move it could absorb, and
rewriting a settled contract to save that move is the more expensive habit. FR-2 then merged the same
day having placed `SnapshotDiffer`, `SnapshotApplier` and `SnapshotHash` in `MW3.Protocol` of its own
accord, so no move is needed and this decision is a **standing constraint** rather than a task: they
stay there, and §1's line is the thing that was wrong.

**D-76: a gateway command carries a send strength and no player id.** Two things settled at FR-3's
kickoff about the command vocabulary, which is new protocol data and therefore free to differ from
Core's `SendArmyCommand`. It carries **no issuing player**: the gateway attributes every command to
its session's local player, so there is no field a client could set to submit on the AI's behalf —
validation at the boundary by making the bad state unrepresentable rather than by checking for it.
And it carries a **`SendStrength`**, not a unit count, because `MatchScreen.cs:316` calls
`SendStrengthCalculator` today and that is a rule executing on the client. Considered: keeping the
count and having the server re-validate it, which preserves Core's command shape but leaves the
arithmetic duplicated on both sides of the seam — the drift shape this repo has paid to close three
times. Resolving the strength inside the gateway leaves `AiBrain` and `SendArmyCommand` untouched.

## 5. Cross-cutting conventions

**Every message is versioned.** The protocol carries a version field from FR-1, and a mismatch is a
clean refusal with both versions named — never a partial parse. There is no compatibility guarantee
between phases; the requirement is only that a mismatch is diagnosable.

**Snapshots are values.** Everything in `MW3.Protocol` is immutable, has no behaviour, and is
constructible from JSON alone. Nothing in it may reference `Match`, a `*Table`, or any type in
`MW3.Core` — that is the direction of the dependency, and it is enforced by there being no reference
to invert.

**The client never computes a rule, but it does compute geometry.** Army position from path and ticks
(D-51, D-39), hit-testing, and layout are the client's job and stay there. Anything that decides
*what is legal or what happens* is the server's. When the boundary is unclear, ask whether two
clients disagreeing about it would change the match: if yes, it is server-side.

**Validate at the boundary.** Every inbound message is untrusted input, in both directions, and is
validated where it is deserialized — never cast into shape and used. A malformed or out-of-range
command closes the connection with a reason; it never reaches `Match`.

**No secret, address, or port is hardcoded in the client.** The server address arrives by flag
(desktop) or configuration (Android), and defaults to loopback.

**A test that asserts only that a message was sent is hollow** — the standing rule from
`docs/CONVENTIONS.md`. Protocol tests assert on the reconstructed snapshot, not on the wire traffic.

**Gameplay must not change.** Any behavioural difference between a match played on `main` and the
same match played through this phase's gateway is a defect. The strongest available evidence is a
`--dump-state` diff proving byte-identity, and it is the standard phase 7 FR-2 set for a change of
this kind.
