# Architecture — Core gameplay loop (phase 2)

> Records what this phase adds or changes; the repo-wide `docs/ARCHITECTURE.md` holds the system
> baseline shared by every phase, and `docs/welcome-screen/ARCHITECTURE.md` holds phase 1's
> reasoning. Decision numbering **continues** phase 1's sequence (D-11 onward) so that a `D-n`
> reference is unambiguous anywhere in the repo.

## 1. Overview

No new project, no new dependency, no new platform. This phase fills the two boxes phase 1 left
almost empty: the match simulation inside `MW3.Core`, and the screens that draw and drive it inside
`MW3.Game`.

```
  MW3.Game (presentation)                    MW3.Core (rules, no engine)
  +-------------------------+                +-----------------------------+
  |  ScreenManager          |                |  Match                      |
  |   +-- WelcomeScreen     |   commands     |   Advance(ticks)            |
  |   +-- MatchScreen ------|--------------->|   Execute(SendArmyCommand)  |
  |        reads state,     |                |   Players, Bases, Armies    |
  |        never mutates it |<---------------|   Outcome                   |
  +-------------------------+   state read   |  MapLayout (normalized)     |
                                             |  HitTest, Combat            |
                                             |  IPlayerBrain -> AiBrain    |
                                             +-----------------------------+
                                                          ^
                                             MW3.Core.Tests drives whole
                                             matches headlessly (no device)
```

The one-way arrow from phase 1 still holds, and the new arrow that matters is the narrow one:
presentation talks to the simulation **only** by advancing ticks and submitting commands. It never
reaches in and moves a unit.

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds **no** package reference, no content-pipeline asset beyond the existing SpriteFont,
and no platform capability. See `docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a is complete and current for build, run, smoke,
screenshot, Android install/launch, and the gate. Everything there still applies verbatim; note the
repo-wide rule that the solution is built with `-m:1` (see CLAUDE.md).

This phase adds exactly one command, introduced by FR-5 and **not present until that feature
merges** — until then the phase-1 section is the whole story:

```powershell
dotnet run --project src/MW3.Desktop -- --smoke --script <commands.txt> --screenshot out.png
```

Replays a scripted command sequence against a fresh match, then writes one frame and exits 0. This
is how `qa-verifier` asserts interactive and AI behaviour without synthetic input (D-17). The exact
file format is settled by FR-5's `/kickoff` and documented here when it lands.

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  FixedStepClock.cs        (phase 1) drives Match.Advance - unchanged
  Match.cs                 the aggregate: players, bases, armies, outcome
  MapLayout.cs             the one hardcoded map, in normalized coordinates
  MapPoint.cs              Core-side normalized position (no Vector2 - D-14)
  Player.cs                rules-level owner: id + controller kind (D-11)
  SendArmyCommand.cs       the only mutation input (D-12)
  IPlayerBrain.cs          AI seam, implemented by AiBrain (D-16)
src/MW3.Game/
  ScreenManager.cs         minimal screen stack (D-16)
  WelcomeScreen.cs         (phase 1) Play now pushes MatchScreen
  MatchScreen.cs           draws match state, turns input into commands
```

File names above are the intent, not a contract — `/kickoff` and `/implement` may split or rename
them. What **is** a contract: which project each concern lives in.

## 4. Key decisions

**D-11: `Player` is a rules-level owner, not an account.** Considered: modelling a profile now — a
persisted id, display name, avatar — "because multiplayer will need one". Rejected: there is no
server to authenticate against (S-7), so an identity model would be structure built on a guess, and
the wrong guess is expensive to unpick. Chosen: a `Player` is a stable in-match id plus a
controller kind (`Human` / `Ai`), and nothing else. What this protects: a future authoritative
server maps its own accounts onto in-match player ids, which forecloses nothing. What it forbids:
any Core type learning about names, logins, or persistence this phase.

**D-12: the match is a deterministic, command-driven simulation.** Considered: updating the model
directly from the MonoGame update loop with `GameTime` deltas — the obvious MonoGame way.
Rejected: it makes the rules untestable without a device, frame-rate-dependent, and impossible for
a server to re-run. Chosen: the match changes state in exactly two ways — `Advance(ticks)` for
whole ticks produced by the existing `FixedStepClock`, and `Execute(command)` for explicit
commands. No wall-clock read, no `GameTime`, no ambient randomness inside `MW3.Core`. Consequences:
a match is reproducible from a starting state plus a command log, which is what buys headless
tests (success criterion 2), unattended QA of interactive features (D-17), and a future server.
Human input and the AI produce the *same* command type, so neither can do anything the other
cannot — and a server could validate both with one code path.

**D-13: `Match` is an encapsulated mutable aggregate, not a per-tick immutable snapshot.**
Considered: the immutable style `FixedStepClock` already uses (new state returned from every
call), which would make replay, diffing, and undo trivial. Rejected at this scale: the simulation
advances every frame on a phone, and allocating a fresh graph of bases and armies per tick trades a
real GC cost on the target platform for a convenience nothing this phase needs. Chosen: `Match`
mutates in place, exposes no public setters, and permits state changes only through `Advance` and
`Execute`. Determinism (D-12) is what actually delivers replay, and it survives mutability. If a
later phase needs snapshots (rewind, netcode prediction), it adds an explicit serializer rather
than reversing this.

**D-14: map positions are normalized `MapPoint` values in `MW3.Core`, resolved to pixels only in
presentation.** Considered: pixel coordinates in Core (Core would then know the screen size), or
`Microsoft.Xna.Framework.Vector2` (banned by D-2 — it is exactly the kind of engine type whose
first appearance ends portability). Chosen: a Core-side point with X and Y in `0..1`, multiplied by
the viewport when drawn. This continues FR-3's viewport-derived layout precedent, so one map reads
correctly on the desktop window and on a landscape-locked device (D-10) with no per-platform
layout code.

**D-15: combat is deterministic integer arithmetic — no randomness.** Considered: Mushroom Wars
2-style randomized outcomes, which are more authentic and more fun. Rejected for this phase:
random outcomes make acceptance criteria probabilistic and tests flaky by construction, at the
moment when proving the loop matters more than tuning it. Chosen: an army of N attacking a base
holding M resolves by fixed arithmetic. If randomness is wanted later it enters as a seeded PRNG
owned by the match state and advanced only inside `Advance`/`Execute`, so D-12's replay guarantee
survives.

**D-16: screen management is a minimal `IScreen` plus a manager in `MW3.Game` — no library, and the
AI sits behind a Core-side seam.** Considered for screens: a third-party scene/state library, or a
single `Game` class with an enum switch. Chosen: `WelcomeScreen` already has the
LoadContent/Update/Draw/Dispose shape, so the smallest honest step is to name that shape as
`IScreen` and add a manager owning the current one — presentation-only code, assumed disposable in
an engine migration (D-2). Considered for the AI: putting it in `MW3.Game` next to the input that
resembles it. Rejected: the AI is rules-adjacent and must be testable headlessly, so it implements
a Core-side `IPlayerBrain` that observes the match and returns commands. Consequence: the AI is
exercised in unit tests over hundreds of ticks in milliseconds, with no head running at all.

**D-17: interactive and AI features are verified by scripted commands, not synthetic input.**
Considered: injecting synthetic mouse/touch events into the running head, and an image-diff
harness over recorded frames. Rejected: MonoGame reads input from the OS device state, so faking it
means OS-level automation — slow, flaky, and platform-specific, exactly what D-3 exists to avoid.
Chosen: because commands are data (D-12), smoke mode gains `--script <file>` which replays commands
against a fresh match and then honours `--screenshot`. `qa-verifier` asserts the resulting frame,
reusing the D-9 screenshot mechanism instead of inventing a second one. Accepted limitation, stated
plainly: this verifies the rules end-to-end and the drawing of their result, but **not** the step
that converts a physical tap into a command — which is what D-18 shrinks to near-nothing, with the
remainder confirmed by one real tap on hardware at feature boundaries (D-8).

**D-18: hit-testing is a pure function in `MW3.Core`.** Considered: hit-testing inside
`MatchScreen` against pixel rectangles, the natural place for it. Rejected because it would put the
one genuinely untestable part of the game — "which base did the player mean?" — in the one place
tests cannot reach. Chosen: Core answers "which base, if any, is at this normalized point", unit
tested including the miss and the ambiguous-overlap cases. What remains in the head is the
conversion of device coordinates to a normalized point: a couple of lines, verified once on real
hardware rather than mocked forever.

## 5. Cross-cutting conventions

Build-mode Ivan applies these without being asked:

- **Presentation reads, commands write.** A screen may read match state to draw it and may submit
  commands; it may never mutate the model. A reviewer should treat a public setter on a Core match
  type as a defect.
- **One command type for humans and AI.** If input can do something the AI cannot express as a
  command, the command model is wrong — fix the model, do not add a side channel.
- **No engine type in `MW3.Core`, and that includes `Vector2`** (D-2, D-14). No wall-clock read and
  no `Random` in `MW3.Core` either (D-12, D-15).
- **Every rules feature lands with headless tests over whole ticks**, not just over single methods:
  a test that advances a match hundreds of ticks and asserts the outcome is worth more than one
  asserting a getter.
- **One map, one unit type, hardcoded in code.** No config file, no content asset, no map format
  until a phase actually needs a second map (REQUIREMENTS §6).
- **Presentation is verified by screenshot** (D-9) and, for interactive behaviour, by scripted
  commands (D-17) — never by eye as a routine step.
- **The gate is the standard** (`./gate.ps1`, built `-m:1`), and `MW3.Core` staying engine-free is
  checked the same way phase 1 checked it: no `Microsoft.Xna` or `MonoGame` text under
  `src/MW3.Core`.
