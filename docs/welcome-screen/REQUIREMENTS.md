# Requirements — Welcome screen

> One phase of iterative development, mirroring the Workflowy level-2 project of the same name
> (`83e050f507f8`). This file is the source of product truth for the phase; `/kickoff <feature>`
> turns each FR below into a GitHub issue with acceptance criteria.

## 1. Product goal

Phase 1 is the skeleton, not the game. It delivers a .NET solution that builds and deploys to an
Android device and launches to a single placeholder welcome screen — the app name and one inert
entry point. The screen is deliberately unpolished, because the screen is not the deliverable: the
deliverable is the proven chain of solution layout, client framework, Android packaging, quality
gate, CI, and the Ivan build pipeline. Every later phase adds gameplay to a foundation that
already ships, instead of discovering packaging and deployment problems while also debugging a
game. MW3 as a whole is an enhanced clone of Mushroom Wars 2, adding new single-player cooperative
campaigns; none of that exists yet in this phase.

## 2. Target users

- **The developer (only user this phase serves)** — one person, building MW3 with Claude Code as
  the implementer. Needs a clone-to-running loop that is fast, cheap, and scriptable.
- **The player (served from a later phase)** — the same person, on their own Android device. Named
  here only so the phase does not accidentally optimize for an audience that does not exist:
  no onboarding, no accessibility work, no store-readiness in this phase.

## 3. Success criteria

Observable outcomes, not features:

1. `./gate.ps1` passes locally and the same gate passes in CI.
2. The app installs on a physical Android device and launches to the welcome screen without
   crashing.
3. A clean clone builds and runs by following `docs/welcome-screen/ARCHITECTURE.md` §2a alone,
   with no undocumented steps.
4. The `qa-verifier` agent launches the app and confirms the screen **unattended** — no human in
   the loop for routine verification. (See D-3: the desktop head exists to make this possible.)

## 4. Functional requirements

Acceptance conditions are intentionally empty here — `/kickoff <feature>` settles them with the
user and writes them into both the Workflowy note and the GitHub issue.

FR-1 (wf: 3dae1956ad98): The developer can build and run a solution skeleton — engine-free
`MW3.Core` rules library, xUnit test project, and a MonoGame DesktopGL head that opens a window —
so that the quality gate has real code to check and stops passing trivially.
  - Acceptance: `MW3.slnx` at the repo root contains exactly `src/MW3.Core`, `src/MW3.Game`,
    `src/MW3.Desktop`, `tests/MW3.Core.Tests`.
  - Acceptance: `MW3.Core` targets `netstandard2.1`; `MW3.Game` and `MW3.Desktop` target `net10.0`.
  - Acceptance: `MW3.Core` has no MonoGame package reference, and no file under `src/MW3.Core`
    contains the text `Microsoft.Xna` or `MonoGame`.
  - Acceptance: `MW3.Core` contains a deterministic fixed-step game clock with no wall-clock or
    platform dependency.
  - Acceptance: `dotnet test MW3.slnx` runs at least three clock tests — whole ticks, remainder
    carried to the next call, zero elapsed producing zero ticks — and all pass.
  - Acceptance: `./gate.ps1` exits 0 and no longer prints "no application code yet".
  - Acceptance: `dotnet build MW3.slnx -warnaserror` gives zero warnings and errors;
    `dotnet format MW3.slnx --verify-no-changes` exits 0.
  - Acceptance: `dotnet run --project src/MW3.Desktop` opens a window showing one solid clear
    colour and no content, staying open until the user closes it.
  - Acceptance: `dotnet run --project src/MW3.Desktop -- --smoke` runs one update/draw cycle and
    exits 0 within 30 seconds with no user interaction.
  - Acceptance: the commands in `ARCHITECTURE.md` §2a work verbatim on a clean clone.

FR-2 (wf: 089cdeb5df53): The developer can install and launch the Android head on a physical
device so that this phase's packaging and deployment risk is retired early rather than at the end.
  - Acceptance: `src/MW3.Android` targets `net10.0-android`, references
    `MonoGame.Framework.Android`, and is listed in `MW3.slnx`.
  - Acceptance: `src/MW3.Game` targets `net10.0;net10.0-android`, with the DesktopGL package
    conditioned to `net10.0` and the Android package to `net10.0-android` (D-7).
  - Acceptance: `MW3.Core` still targets `netstandard2.1` and contains no `Microsoft.Xna` or
    `MonoGame` text.
  - Acceptance: `MainActivity` declares an explicit activity name (D-8), and the manifest declares
    application id `com.vassilatanasov.mw3` with minimum SDK 21 or higher.
  - Acceptance: `dotnet build MW3.slnx -warnaserror` and `./gate.ps1` both succeed on a machine
    with the `android` workload installed.
  - Acceptance: `dotnet run --project src/MW3.Desktop -- --smoke` still exits 0 within 30 seconds.
  - Acceptance: `ci.yml` installs the `android` workload before the gate, and the PR's CI run
    concludes `success`.
  - Acceptance: with one device attached, the app installs and `adb shell pm list packages`
    includes `com.vassilatanasov.mw3`.
  - Acceptance: `adb shell am start -n com.vassilatanasov.mw3/com.vassilatanasov.mw3.MainActivity`
    prints `Status: ok`, and `adb shell pidof com.vassilatanasov.mw3` returns a pid at least 10
    seconds later.
  - Acceptance: the Android commands in `ARCHITECTURE.md` §2a work verbatim on a clean clone.

FR-3 (wf: 03845bfc494d): The player can launch the app and see a welcome screen with the game
title and one inert entry point, on both heads, so that the shell is visibly the beginning of MW3.
Depends on FR-2 being merged.
  - Acceptance: the desktop head shows the title `MW3` and a button labelled `Play`, both fully
    within the window, unclipped and non-overlapping.
  - Acceptance: layout derives from the viewport — resizing the window or running at a different
    aspect ratio keeps both elements fully visible and centred.
  - Acceptance: clicking or tapping `Play` changes nothing; a screenshot after the click is
    byte-identical to one taken before it, and the app keeps running.
  - Acceptance: an `.mgcb` content project builds as part of the normal build — no manual content
    step on a clean clone.
  - Acceptance: the TTF behind the `SpriteFont` is committed with its licence file, and no
    `.spritefont` references a font by machine-installed name only (D-9).
  - Acceptance: `dotnet run --project src/MW3.Desktop -- --smoke --screenshot out.png` exits 0
    within 30 seconds and writes a valid PNG matching the back-buffer dimensions with more than one
    distinct colour.
  - Acceptance: `--smoke` without `--screenshot` still exits 0 within 30 seconds and writes no file.
  - Acceptance: `dotnet build MW3.slnx -warnaserror` and `./gate.ps1` both succeed.
  - Acceptance: the Android manifest locks the activity to landscape.
  - Acceptance: on an attached device, `adb shell screencap` shows the same title and button laid
    out for the device's aspect ratio, and `pidof` still returns a pid ten seconds after launch.
  - Acceptance: `ARCHITECTURE.md` §2a documents `--screenshot` and it works verbatim.

FR-4 (wf: a536546adb60): The developer can download an installable APK from any CI run so that
"it builds on my machine" stops being the standard of evidence.
  - Acceptance: the existing `gate` job gains an APK build and an upload step **after** `./gate.ps1`
    in the same job — no second job, and the `android` workload installed exactly once per run.
  - Acceptance: the upload step uses no `if: always()` or equivalent, so a red gate produces no
    artifact and a build that failed its own gate can never be installed.
  - Acceptance: the APK is **Debug** configuration, so a CI APK and one from
    `dotnet build src/MW3.Android -t:Run` are the same kind of build; any added `dotnet build`
    passes `-m:1` per the MonoGame content-race rule.
  - Acceptance: both triggers publish — a pull request run and a push-to-`main` run each attach an
    APK; the artifact name embeds the short commit SHA and retention is 30 days.
  - Acceptance: exactly one APK per run — the installable debug-key-signed one, not both it and its
    unsigned sibling.
  - Acceptance: the run's artifact list over REST
    (`/repos/VassilAtanasov/MW3/actions/runs/<id>/artifacts`) holds one matching entry with non-zero
    `size_in_bytes`; unzipping it yields exactly one `.apk`.
  - Acceptance: that `.apk` is a valid zip containing `AndroidManifest.xml` and `classes.dex`, and
    the string `com.vassilatanasov.mw3` appears in its bytes — so the artifact is this application
    and not an empty or placeholder package.
  - Acceptance: `ARCHITECTURE.md` §2a documents getting the APK from CI (where it appears, its
    name, and `adb install -r`) and works verbatim; the local
    `dotnet build src/MW3.Android -t:Run` path stays documented — CI is an addition, not a
    replacement.
  - Acceptance: CI stays green and the added steps cost no more than a few minutes over the current
    baseline (before/after times recorded in the PR); `./gate.ps1` passes locally and in CI.
  - Acceptance (device, blocking): the APK from this feature's own PR run installs with
    `adb install -r` reporting `Success`; `adb shell pm list packages com.vassilatanasov.mw3` lists
    it; launching without `-W` shows the welcome screen in a `screencap`; and `adb shell pidof`
    returns a pid ten seconds later, proving the CI-built APK does not crash on startup.

## 5. Non-functional requirements

Only the ones that genuinely constrain design:

- **Cost and speed of the build/run loop are the primary constraint.** No engine binary, license
  server, or paid runner may enter the pipeline. The whole gate must be `dotnet` commands, and CI
  runs on GitHub-hosted runners on a public repo (free minutes).
- **Unattended verifiability.** Every feature must be exercisable by `qa-verifier` without a human;
  this is what forces the desktop head (D-3).
- **Engine portability of the rules layer.** `MW3.Core` targets `netstandard2.1`, uses conservative
  C#, and references no engine types, so a future migration to Unity remains possible (D-2).
- **Original art only.** Mechanics may follow Mushroom Wars 2; assets must be recreated (D-5).
- **The `android` workload is a prerequisite for building the repository at all** from FR-2 onward,
  not just for building the Android head (D-7). CI installs it before the gate, which makes every
  run slower — accepted so the gate covers the head that actually ships.
- **Android QA needs the SDK platform-tools on `PATH`** so `adb` resolves, plus one attached device
  with USB debugging authorized (D-8).
- No performance, auth, data-retention, or accessibility targets apply to this phase.

## 6. Out of scope

Explicit non-goals for this phase — these are what stop `/autopilot` drifting:

- Gameplay of any kind: no game board, units, resources, combat, campaigns, or AI.
- Any server, API, account, login, or multiplayer. Cooperative campaigns are **single-player** and
  belong to a later phase; multiplayer is later still.
- Real art, sound, music, animation, or menu polish. The welcome screen is placeholder by design.
- Settings, navigation to any destination, localization.
- Release signing, store packaging, or publishing to Google Play.
- iOS, web, or any target other than Android and the Windows desktop QA head.
- **Deferred deliberately, not forgotten** (revisit in a later phase): build/version info displayed
  on the welcome screen, and a placeholder app icon and splash screen.

## 7. Open questions

None. Discovery closed with every question resolved.
