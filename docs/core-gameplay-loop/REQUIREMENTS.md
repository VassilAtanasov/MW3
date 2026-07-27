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
  - Acceptance: a player is a stable in-match id plus a `Human`/`Ai` controller kind and nothing
    else — no name, display string, colour, score, or persistence field (D-11, S-9).
  - Acceptance: a base carries an id, garrison count, normalized position, and owner, with neutral
    modelled as the *absence* of an owner in the type system — never a reserved id or sentinel.
  - Acceptance: the hardcoded map has exactly six bases — human `(0.12, 0.50)`, AI `(0.88, 0.50)`,
    neutrals `(0.35, 0.25)`, `(0.35, 0.75)`, `(0.65, 0.25)`, `(0.65, 0.75)` — and a test asserts
    every coordinate lies within `0.0..1.0` (D-14).
  - Acceptance: starting garrisons are 10 (human), 10 (AI), and 5 for each neutral.
  - Acceptance: the tick duration (100 ms) and production period (10 ticks per unit) are named
    Core constants, so heads and tests read one source rather than each hardcoding the number.
  - Acceptance: an owned base gains exactly one unit per 10 ticks — 100 ticks from a fresh match
    leaves the human's and the AI's bases holding exactly 20 each.
  - Acceptance: partial production carries — 7 ticks then 3 equals 10 in one call, and 9 ticks from
    a fresh match adds no unit.
  - Acceptance: neutral bases never produce — after 1000 ticks each still holds exactly 5.
  - Acceptance: `Advance(0)` changes nothing, and a negative tick count throws
    `ArgumentOutOfRangeException` rather than rewinding or silently doing nothing.
  - Acceptance: determinism — two matches advanced to the same total tick count, one in a single
    call and one in irregular chunks, end with identical garrisons for every base (D-12).
  - Acceptance: no file under `src/MW3.Core` references `DateTime`, `DateTimeOffset`, `Stopwatch`,
    `Environment.TickCount`, or `Random`.
  - Acceptance: `MW3.Core` still targets `netstandard2.1` and contains no `Microsoft.Xna` or
    `MonoGame` text — the position type is a Core type, not `Vector2` (D-2, D-14).
  - Acceptance: the aggregate exposes no settable property and no mutable collection; bases are
    reachable only as a read-only view and garrisons change only via `Advance` (D-13).
  - Acceptance: `dotnet test MW3.slnx` passes with tests that advance a match over many ticks, and
    `./gate.ps1` exits 0.

FR-2 (wf: f68a4d876cb3): The player can press `Play` and arrive at a match screen, and return from
it, so that the app has more than one destination and `Play` stops being inert. Independent of
FR-1 — buildable in either order.
  - Acceptance: `MW3.Game` defines an `IScreen` abstraction and a manager owning a screen stack;
    the host game class routes lifecycle calls through it and no longer names `WelcomeScreen`
    (D-16).
  - Acceptance: the match screen draws a background colour different from the welcome screen's plus
    the placeholder text `Match`, laid out from the viewport, adding no new content asset.
  - Acceptance: press-and-release within the `Play` button pushes the match screen; a back request
    pops it. Press inside and release outside does **not** navigate.
  - Acceptance: a back request on the welcome screen exits with code 0 rather than popping an empty
    stack.
  - Acceptance: `MW3.Game` reads pointer position, pressed state, and back requests through one
    interface — production wraps the platform APIs, a scripted implementation replays a file, and
    no screen touches `Mouse`, `TouchPanel`, or `Keyboard` directly (D-17).
  - Acceptance: `--script <file>` is accepted on both heads: one directive per line,
    `<frame> <directive> [args]` where directives are `down x y`, `up x y`, `back`, `#` comments,
    and pointer coordinates are normalized `0..1` so a script is resolution-independent.
  - Acceptance: with `--script` the run ends a fixed 10 frames after the last directive, writing
    the screenshot if asked and exiting 0; an unparseable script exits non-zero naming the line.
  - Acceptance: committed scripts under `qa/scripts/` give byte-comparable evidence — tapping
    `Play` yields a screenshot *not* identical to the welcome baseline; `Play` then `back`, a
    press-then-drag-off, and five push/pop cycles each yield one that *is* byte-identical to it.
  - Acceptance: phase 1's `--smoke` and `--smoke --screenshot` commands behave exactly as before.
  - Acceptance: no file is added under `src/MW3.Core`; `dotnet build MW3.slnx -warnaserror -m:1`
    and `./gate.ps1` both pass; §2a documents `--script` and works verbatim on a clean clone.
  - Acceptance (device, blocking — a device is attached): `adb shell input tap` at the `Play`
    button's normalized position scaled to the device resolution, then `screencap`, shows the match
    screen; `adb shell input keyevent 4` returns to welcome with `pidof` still alive; a second
    `keyevent 4` leaves the app without a crash dialog.

FR-3 (wf: fc6dfb3d8695): The player can see the map, every base, who owns it, and its garrison
count rising live, so that the match state is legible before it is interactive.
  - Acceptance: `IScreen.Update` receives the frame's elapsed milliseconds from `MW3Game` (never a
    `GameTime`), and `MatchScreen` advances its `Match` through a `FixedStepClock` built from
    `Match.TickDurationMilliseconds`, passing whole ticks only (D-12).
  - Acceptance: pushing the match screen starts a fresh match each time (10/10/5 again), and no
    match advances while the welcome screen is active.
  - Acceptance: all six bases are drawn as filled circles positioned and sized from the viewport by
    scaling their normalized `MapPoint` — no fixed pixel coordinate in the layout (D-14).
  - Acceptance: the circle texture is generated procedurally at `LoadContent` and disposed with the
    screen; no image asset joins the content pipeline (D-5).
  - Acceptance: human, AI, and neutral bases carry three distinct tints, visibly different from
    each other and the background; each base's garrison count is drawn on its circle with the
    bundled SpriteFont and equals the model's value.
  - Acceptance: at 1280x720 and at 1920x1200, all six circles and numbers are fully within the
    viewport, unclipped and non-overlapping.
  - Acceptance: the script format gains `<frame> wait` (no arguments) as a timeline marker;
    `down`, `up`, `back` are unchanged.
  - Acceptance: `--dump-state <path>` writes, at the final frame, the match's elapsed ticks and one
    line per base (id, owner as human/AI/neutral, garrison), exits 0, works with or without
    `--screenshot`, and writes nothing when omitted.
  - Acceptance: committed `qa/scripts/match-early.txt` and `match-late.txt` both exit 0 within 30
    seconds; the late screenshot is **not** byte-identical to the early one, and re-running either
    reproduces its own screenshot byte-for-byte.
  - Acceptance: the late dump reports ≥ 40 elapsed ticks and is internally consistent — every owned
    base holds exactly `10 + elapsedTicks / 10`, every neutral base exactly 5; the early dump is
    consistent the same way for its own elapsed count.
  - Acceptance: the FR-2 scripts still behave as before, and `--smoke` alone still exits 0 within
    30 seconds writing no file.
  - Acceptance: no file is added under `src/MW3.Core`; `dotnet build MW3.slnx -warnaserror -m:1`
    and `./gate.ps1` both pass; §2a documents `wait` and `--dump-state` and works verbatim.
  - Acceptance (device, blocking): after `adb shell input tap` on `Play`, a `screencap` shows six
    circles with numbers in three distinguishable colours laid out for the device aspect ratio; a
    second `screencap` ten seconds later shows larger numbers on the human's and AI's bases and
    unchanged neutrals; `pidof` still returns a pid.

FR-4 (wf: 8aa2138b342a): The developer can issue a send-army command that detaches part of a
garrison, travels for a number of ticks, and on arrival reinforces a friendly base or fights for a
neutral or enemy one — flipping ownership when it wins — so that the core mechanic exists as
deterministic rules.
  - Acceptance: the send command carries the issuing player, source base id, target base id, and an
    explicit unit count; `Match.Execute` is the only way to submit one, and it returns a result
    distinguishing acceptance from each rejection reason in the type system — never a bool, an
    exception for an ordinary rejection, or a silent no-op.
  - Acceptance: a send is rejected leaving all state untouched when the source is not owned by the
    issuing player, source and target are the same base, the count is ≤ 0, the count exceeds the
    source's current garrison, or a base id does not exist.
  - Acceptance: an accepted send subtracts the count immediately; sending an entire garrison is
    legal, and a zero-garrison base stays owned, keeps producing, and can be taken by one unit.
  - Acceptance: army speed is a named Core constant; travel time is proportional to the
    straight-line distance between normalized positions, crossing the full map width (1.0) in 5
    seconds (~17 ticks to the nearest neutral, ~38 to the AI base), never less than one tick.
  - Acceptance: `Match` exposes in-flight armies read-only — owner, source, target, count, launch
    tick, arrival tick — enough for FR-5 to interpolate a position with no drawing knowledge in
    Core. Armies are inert in flight: no interception, recall, or change of owner if their source
    base is captured.
  - Acceptance: arrival at a base owned by the army's owner reinforces it; arrival elsewhere
    resolves 1:1 with no defender advantage (N > M captures with N − M; N ≤ M leaves the defender
    with M − N, so N == M leaves the defender owning zero units) — D-15.
  - Acceptance: resolution uses the target's owner **at arrival**, not at launch.
  - Acceptance: several armies arriving on the same tick at one base resolve one at a time in a
    deterministic documented order, each fully applied before the next — two armies of 6 against a
    base of 10 capture it with 2.
  - Acceptance: an arrival tick passed over by a large `Advance` still resolves exactly once, at
    its due outcome — never twice, never skipped.
  - Acceptance: determinism (D-12) — the same commands at the same tick counts give identical
    owners, garrisons, and in-flight armies whether `Advance` runs in one step or irregular chunks.
  - Acceptance: `MW3.Core` still has no `DateTime`/`Stopwatch`/`Random`, still targets
    `netstandard2.1`, still contains no `Microsoft.Xna` or `MonoGame` text, and the added state
    stays unmutatable from outside the aggregate (D-13).
  - Acceptance: tests cover every rejection reason, reinforcement, capture, a repelled attack,
    N == M, ownership changing mid-flight, same-tick ordering, and a skipped-over arrival tick;
    `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1` both pass.

FR-5 (wf: 06e4c2f2ddb8): The player can drag from a base they own to another base to send an army,
and see it in transit, on both heads, so that the mechanic is actually playable. The interaction is
a **drag**, not two taps: press on the source, release on the target.
  - Acceptance: `MW3.Core` exposes a pure function taking a normalized `MapPoint` and returning the
    base at it or the absence of one in the type system (never `-1`, never a bool-plus-out) — unit
    tested with no graphics device (D-18).
  - Acceptance: the rule is **nearest base within a threshold** — the closest base by distance, and
    only if that distance is at or under a named Core constant; beyond it, no base.
  - Acceptance: a test asserts no two bases in the hardcoded map lie within twice that threshold of
    each other, so the nearest match is never ambiguous and the constant cannot be widened into
    ambiguity unnoticed.
  - Acceptance: hit-test tests cover a base's exact centre, just inside and just outside the
    threshold, a point between two bases resolving to the genuinely nearer one, and all four map
    corners returning no base.
  - Acceptance: a press starting on a base owned by the human player selects it as the source for
    as long as the pointer is down; a press starting on a neutral base, an AI base, or no base
    selects nothing and its release changes no state.
  - Acceptance: releasing over a different base issues one `SendArmyCommand` from the human player
    with a unit count of `garrison / 2` rounded down and **clamped to a minimum of 1**, read from
    the source's garrison **at release** — so production during the drag cannot overdraw it.
  - Acceptance: releasing over the source base itself, or over no base, cancels — no command, no
    garrison change, selection cleared. After any release the selection clears, so a second drag
    immediately after a first behaves identically.
  - Acceptance: the screen submits commands only through `Match.Execute`, never mutates match state
    directly, and submits no command it can determine will be rejected. Production continues during
    a drag, and a back request still pops the screen as in FR-2.
  - Acceptance: no screen reads `Mouse`, `TouchPanel`, or `Keyboard` directly — the drag is driven
    entirely through `IInputSource`, so one code path serves both heads (D-17).
  - Acceptance: the selected source base is drawn visibly differently from its unselected self and
    from the other two owner tints, apparent in a screenshot rather than only in code.
  - Acceptance: each in-flight army is drawn as a filled circle smaller than a base, tinted by
    owner, positioned by interpolating source→target from its launch and arrival ticks, with its
    unit count in the bundled SpriteFont; it sits on its source at the launch tick, on its target at
    the arrival tick, and disappears the moment it resolves.
  - Acceptance: army circles are laid out by scaling normalized `MapPoint` values with no fixed
    pixel coordinate (D-14) and reuse the procedural circle texture — no image asset joins the
    content pipeline (D-5). At 1280x720 and 1920x1200 every circle and number is within the
    viewport and legible, and Draw allocates nothing per frame beyond FR-3's established pattern.
  - Acceptance: **no new script directive** — a drag is `<frame> down <x> <y>` then
    `<frame> up <x> <y>`; `back` and `wait` unchanged. `ARCHITECTURE.md` §2a is corrected to say so.
  - Acceptance: `--dump-state` gains one line per in-flight army (id, owner, source, target, count,
    launch tick, arrival tick); its elapsed-ticks and per-base lines are unchanged, and a dump with
    nothing in flight lists no army.
  - Acceptance: committed `qa/scripts/` scripts each exit 0 within 30 seconds covering a successful
    send, an arrival, a cancel on empty space, a press starting on a base the human does not own,
    and one holding the pointer down to capture the selection highlight.
  - Acceptance: the send script's dump shows the source at half its pre-send garrison (plus any
    production since) and exactly one army in flight with an arrival tick later than its launch
    tick; the arrival script's dump shows zero armies in flight and the target owned by the human
    with a garrison consistent with FR-4's 1:1 arithmetic; the cancel and not-owned dumps show zero
    armies, starting owners intact, and garrisons consistent with production alone.
  - Acceptance: the selection-highlight screenshot is **not** byte-identical to that of an
    otherwise-identical script pressing on empty space; re-running any new script reproduces its own
    screenshot byte-for-byte.
  - Acceptance: the FR-2 and FR-3 scripts still behave as before, `--smoke` alone still exits 0
    within 30 seconds writing no file, `dotnet build MW3.slnx -warnaserror -m:1` and `./gate.ps1`
    both pass, and §2a documents the extended dump and the drag scripts, working verbatim on a
    clean clone.
  - Acceptance (device, blocking): `adb shell input swipe` from the human base to the nearest
    neutral, then `screencap`, shows the human base's count roughly halved and a small owner-tinted
    numbered circle between the two; a later `screencap` shows that neutral in the human's tint with
    no army circle remaining; a swipe starting on the AI's base changes nothing, and
    `adb shell input keyevent 4` returns to welcome with `pidof` still alive.

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
- **A physical Android device is attached from FR-2 onward** (MI PAD 4, Android 11, 1920x1200 panel
  resolution, in the landscape lock — the MonoGame viewport it actually draws into is smaller,
  roughly `1808x1018`, due to Android system chrome; see `ARCHITECTURE.md` §2a "Desktop window
  size"), so device-dependent criteria are verified per feature and block the PR.
  Phase 1 deferred them to follow-up issue #7 for want of hardware; that deferral does not carry
  into this phase (#7 was verified and closed on 26-07-2026). Android input is injected with
  `adb shell input tap` / `keyevent`, which is real OS input and needs none of the D-17 seam.
  From FR-5 onward `adb shell input swipe <x1> <y1> <x2> <y2> <ms>` joins them, because the
  send-army interaction is a drag rather than a tap.
- **Hit-testing distance is computed in normalized space while circles are drawn with a pixel
  radius** (FR-5), so the threshold region is an ellipse in pixel terms on any non-square viewport.
  Accepted, not a defect: for a *nearest*-base rule the closest base in normalized space is the
  closest one on screen everywhere in the map's neighbourhood, because the hardcoded bases are at
  least 0.34 normalized units apart — far wider than the distortion 16:9 or 16:10 introduces. It
  would stop being harmless if a map ever placed two bases close together, which is exactly what
  FR-5's twice-the-threshold separation test exists to catch.
- **Launching for device checks uses plain `adb shell am start`, never `am start -W`.** `-W` waits
  for `Activity.reportFullyDrawn()`, which MonoGame never calls; on the attached device it took
  over two minutes (found while closing #7, commit `22a79a1`). Poll `adb shell pidof` for liveness
  instead, and budget accordingly wherever `Status: ok` genuinely is the evidence wanted.
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
  D-10), and pause. FR-5 settled which of the two the send-army interaction is: **drag only** —
  tap-to-tap is deliberately not offered as a second path to the same command.
- **Choosing how many units a send dispatches.** FR-5 fixes it at half the garrison rounded down,
  never below 1. No slider, percentage picker, multi-tap-to-add, or HUD showing totals.
- **Rally points, army recall, and interception in flight.** FR-4 settled that armies are inert
  once launched; FR-5 does not reopen it.
- **Fog of war and pathfinding around obstacles.** Armies travel base-to-base in a straight line.
- **Nice-to-have, explicitly deferred rather than forgotten**: a HUD totalling a player's units,
  garrison caps, pause, camera pan/zoom, and the build/version info and app icon still owed from
  phase 1.

## 7. Open questions

None. Discovery closed with every question resolved.
