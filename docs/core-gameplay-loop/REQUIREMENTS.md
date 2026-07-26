# Requirements — Core gameplay loop

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`fb2cdf9f2907`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 2 is the first real match. Phase 1 proved the chain that ships an app; this phase makes the
app a game. Players own bases, bases produce units over time, armies are sent base-to-base to
reinforce or capture, an AI opponent pushes back, and the match ends in victory or defeat — all on
one hardcoded map. That is the whole of Mushroom Wars 2's core mechanic reduced to its smallest
honest form: no unit types, no upgrades, no campaign, no art.

"Player" in this phase is a **rules-level concept** — an owner of bases and armies, human- or
AI-controlled — not an account, profile, or identity. Accounts arrive with the server, which S-7
defers to the multiplayer phase.

The deliverable is the *loop*, and it is deliberately ugly: coloured shapes, integer garrison
counts, and text on the SpriteFont phase 1 already bundled. Making it look like a game is a later
phase; making it *be* a game is this one.

## 2. Target users

- **The player (the user this phase finally serves)** — the developer, on their own Android device,
  playing a match end to end. This is the first phase whose output is playable rather than merely
  launchable, so "can I finish a match without confusion" is now a real question.
- **The developer** — still the implementer, and still the reason every rule must be verifiable
  headlessly. No second human enters the picture this phase.

## 3. Success criteria

Observable outcomes, not features:

1. A match can be played from start to victory **and** from start to defeat on a physical Android
   device, with no crash and no dead end.
2. The complete match simulation runs headlessly in tests — production, sending, transit, combat,
   capture, AI, and end conditions — with no graphics device and no wall-clock dependency.
3. Replaying the same commands against the same starting state produces the same outcome, every
   time (determinism, D-12).
4. `qa-verifier` confirms each feature unattended, without synthetic touch events (D-17).
5. `./gate.ps1` passes locally and in CI throughout, and `MW3.Core` still contains no engine type.

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the
user and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: 50ae1a68b773): The developer can construct a match — players, bases with owners and
garrisons, and the hardcoded map — and advance it so that owned bases accrue units over time, so
that the game has a rules foundation before anything is drawn.
  - Acceptance: (set by /kickoff)

FR-2 (wf: f68a4d876cb3): The player can press `Play` and arrive at a match screen, and return from
it, so that the app has more than one destination and `Play` stops being inert.
  - Acceptance: (set by /kickoff)

FR-3 (wf: fc6dfb3d8695): The player can see the map, every base, who owns it, and its garrison
count rising live, so that the match state is legible before it is interactive.
  - Acceptance: (set by /kickoff)

FR-4 (wf: 8aa2138b342a): The developer can issue a send-army command that detaches part of a
garrison, travels for a number of ticks, and on arrival reinforces a friendly base or fights for a
neutral or enemy one — flipping ownership when it wins — so that the core mechanic exists as
deterministic rules.
  - Acceptance: (set by /kickoff)

FR-5 (wf: 06e4c2f2ddb8): The player can tap or click a source base and then a target base to send
an army, and see it in transit, on both heads, so that the mechanic is actually playable.
  - Acceptance: (set by /kickoff)

FR-6 (wf: e4164ec62a52): The player can face an AI-controlled opponent that reinforces and attacks
on its own, so that a match can be lost rather than only slowly won.
  - Acceptance: (set by /kickoff)

FR-7 (wf: 94ecc30a06a5): The player can win by owning every base or lose by owning none, see which
happened, and return to the welcome screen, so that the loop closes instead of running forever.
  - Acceptance: (set by /kickoff)

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Determinism is a hard requirement, not a nicety.** The match must be reproducible from a
  starting state plus a command sequence: no wall-clock reads, no unseeded randomness, no
  dependence on frame rate or platform (D-12). This is what makes headless tests, unattended QA,
  and a future authoritative server all possible from the same code.
- **Unattended verifiability, without synthetic input.** Injecting fake touch events into a
  MonoGame head is not workable, so verifiability is bought by design instead: commands are data,
  hit-testing is a pure function, and QA drives matches through a scripted command path (D-17,
  D-18).
- **Engine portability of the rules layer still binds** (S-2, D-2). The match model, map
  coordinates, combat, and AI all live in `MW3.Core` with no `Microsoft.Xna` type — including
  `Vector2`, which is why map positions use a Core-side normalized point (D-14).
- **Mobile allocation behaviour matters now.** The simulation advances every frame on a phone, so
  per-tick allocation of whole state snapshots is rejected in favour of an encapsulated mutable
  aggregate (D-13).
- **Cost and speed of the build/run loop** remain the primary constraint (S-5): `dotnet` commands
  only, free CI, no engine binary, no paid runner.
- No auth, no persistence, no network, and no accessibility or performance targets this phase.
  Frame-rate work waits until there is art to slow it down.

## 6. Out of scope

Explicit non-goals for this phase — these are what stop `/autopilot` drifting:

- **More than one map.** One hardcoded layout. No map file format, no editor, no map selection.
- **More than one unit type.** No specialists, no buildings beyond the base, no upgrades, no
  tech tree, no resources other than the garrison count itself.
- **Campaign structure**: no level list, progression, stars, score, statistics, or save data. The
  match starts fresh and ends; that is all.
- **Art, sound, music, and animation.** Bases are shapes, armies are shapes, counts are numbers.
  Original art (D-5) arrives in its own phase.
- **Anything server, account, login, or multiplayer.** Cooperative campaigns are single-player and
  still belong to a later phase (S-7).
- **Randomized combat**, difficulty levels, and AI tuning surfaces (D-15, D-16).
- **Gestures beyond a tap or drag**, camera pan/zoom, rotation handling (still landscape-locked,
  D-10), and pause.
- **Fog of war and pathfinding around obstacles.** Armies travel base-to-base in a straight line.
- **Nice-to-have, explicitly deferred rather than forgotten**: a HUD totalling a player's units,
  garrison caps, pause, camera pan/zoom, and the build/version info and app icon still owed from
  phase 1.

## 7. Open questions

None. Discovery closed with every question resolved.
