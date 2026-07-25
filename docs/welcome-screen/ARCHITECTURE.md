# Architecture — Welcome screen (phase 1)

> Records what this phase adds or changes; the repo-wide `docs/ARCHITECTURE.md` holds the system
> baseline shared by every phase. Because this is the first phase, most decisions below **are** the
> baseline and have been promoted there.

## 1. Overview

A single .NET solution. Game rules live in an engine-free library; the MonoGame game code sits on
top of it; two thin platform heads launch that game code. No server, no database, no network.

```
        +---------------------+
        |  MW3.Desktop        |  Windows head - the QA surface (D-3)
        |  (MonoGame          |
        |   DesktopGL)        |
        +----------+----------+
                   |            +---------------------+
                   +----------> |  MW3.Game           |  shared game + presentation
                   |            |  (MonoGame)         |  screens, input, draw loop
        +----------+----------+ +----------+----------+
        |  MW3.Android        |            |
        |  (MonoGame Android) |            v
        +---------------------+ +---------------------+     +---------------------+
                                |  MW3.Core           | <-- |  MW3.Core.Tests     |
                                |  rules, no engine   |     |  (xUnit)            |
                                |  netstandard2.1     |     +---------------------+
                                +---------------------+
```

The arrow that matters is the one that is **missing**: nothing points from `MW3.Core` outward. It
knows nothing about MonoGame, rendering, or platforms.

## 2. Stack

- Client framework: **MonoGame 3.8.5** (`MonoGame.Framework.DesktopGL`, `MonoGame.Framework.Android`)
- Language/runtime: C# on the .NET 10 SDK (10.0.301 locally); `MW3.Core` targets `netstandard2.1`
- Android: the `android` .NET SDK workload (verified available on this SDK)
- Tests: xUnit
- Data: **none this phase.** When settings/saves arrive, a local JSON file on the device — no
  database until a server exists
- Server / API / auth: **none.** Single-player only until the multiplayer phase
- Hosting target: none — the app ships as an APK to a physical device

## 2a. How to run it

`qa-verifier` follows this section literally.

One-time prerequisites on a clean machine:

```powershell
dotnet new install MonoGame.Templates.CSharp
```

Desktop (the routine QA path — no device or emulator required):

```powershell
dotnet run --project src/MW3.Desktop
```

Desktop smoke check (unattended — one update/draw cycle, then exit 0):

```powershell
dotnet run --project src/MW3.Desktop -- --smoke
```

Android (physical device with USB debugging enabled):

```powershell
dotnet workload install android
dotnet build src/MW3.Android -t:Run
```

Quality gate (build, format check, tests):

```powershell
./gate.ps1
```

## 3. Project layout

```
MW3.slnx                  solution at repo root (gate.ps1 detects *.sln/*.slnx here or one level down)
src/MW3.Core/             rules and game model. No engine references. netstandard2.1
src/MW3.Game/             shared MonoGame code: screens, input, draw loop
src/MW3.Desktop/          DesktopGL head - the QA surface
src/MW3.Android/          Android head - the shipping target
tests/MW3.Core.Tests/     xUnit tests over MW3.Core
docs/                     product truth (this file, REQUIREMENTS.md)
```

Rule: **if logic can live in `MW3.Core`, it must.** Anything in `MW3.Game` is presentation and is
assumed disposable in a future engine migration (D-2).

## 4. Key decisions

**D-1: MonoGame, not Godot or Unity.** Considered: Unity (what Mushroom Wars 2 itself uses), Godot 4
with C#, .NET MAUI, Avalonia. Chosen because:
(a) *Capability is not the constraint.* Mushroom Wars 2 is 2D sprite work whose published minimum
spec is DirectX 9.0c / Intel HD 3000 / 2 GB RAM, and it "runs fine on low end devices". Its visual
quality comes from art and animation, not from rendering technology, and thousands of sprites are
routine for MonoGame's `SpriteBatch` with atlases and batching.
(b) *The codebase is agent-authored.* Godot's and Unity's editor-authored scenes, prefabs, and
animation files diff and review poorly and are awkward for an agent to edit; MonoGame's
everything-is-C# is text, diffable, reviewable, and testable. The tooling advantage of an editor
assumes a human in it, and there is none here.
(c) *Cost and speed are the stated priority.* Pure `dotnet` — no engine binary in CI, no license
server, free GitHub-hosted minutes on a public repo.
Rejected MAUI/Avalonia outright: app UI frameworks, not game frameworks; they would make phase 1
trivial and then have to be thrown away.

**D-2: `MW3.Core` is engine-free, `netstandard2.1`, conservative C#.** Considered: putting the
model directly in the MonoGame project. Chosen because it keeps the rules unit-testable without a
graphics device — which is what makes the autonomous gate meaningful — and keeps a Unity migration
real: Unity consumes a `netstandard2.1` DLL, but its C# language support lags the .NET SDK, so the
newest language features are avoided here. No MonoGame type (`Vector2`, `Texture2D`, `GameTime`)
may appear in `MW3.Core`; the moment one does, portability is gone. A migration would rewrite
`MW3.Game` and the heads and keep `MW3.Core`.

**D-3: the desktop head exists to make QA unattended.** Considered: Android-only, verified on a
device. Rejected because driving an Android device or emulator unattended is slow and flaky, there
is no emulator on GitHub-hosted runners, and success criterion 4 requires routine verification with
no human in the loop. `qa-verifier` therefore exercises `MW3.Desktop` on every feature, and Android
is verified on real hardware at feature boundaries (FR-2) and by the CI artifact (FR-4).

**D-4: no data store and no server in this phase.** Considered: standing up an ASP.NET Core server
early "because it will be needed". Rejected: the enhancement driving MW3 is *single-player*
cooperative campaigns, so nothing needs a server until multiplayer, and an unused server would be
pure cost. The constraint this leaves behind: the architecture must not *assume* single-player
forever — keeping rules in a pure, deterministic library is exactly what allows a future
authoritative server to reuse them.

**D-5: original art only.** Mechanics may follow Mushroom Wars 2; all assets are recreated. The
repository is public, and copied art assets — not cloned mechanics — are where the real IP
exposure sits.

**D-6: `MW3.Game` and the heads target `net10.0`.** Considered: pinning `net8.0` (the TFM MonoGame's
own NuGet package is built for). Chosen because MonoGame documents .NET 9 as the recommended
minimum SDK with **.NET 10 supported**, so `net10.0` is a supported configuration rather than a
gamble, and it keeps one runtime version across the repo and CI. The package shipping `lib/net8.0`
is not a conflict — package TFM and required SDK are different things, and the newer runtime
consumes the older library normally. Fallback if a runtime problem appears on Android: drop the
heads to `net8.0`; `MW3.Core` is unaffected either way.
Source: https://docs.monogame.net/articles/tutorials/building_2d_games/02_getting_started/

## 5. Cross-cutting conventions

- **Dependency direction is one-way**: `Core` <- `Game` <- heads. Nothing flows back.
- **The heads stay thin.** A platform head wires up the game and nothing else; anything with logic
  in it belongs in `MW3.Game`, and anything with rules in it belongs in `MW3.Core`.
- **Tests target `MW3.Core`.** Presentation is verified by `qa-verifier` against the running app,
  not by unit tests over drawing code.
- **The gate is the standard**: `dotnet build -warnaserror`, `dotnet format --verify-no-changes`,
  `dotnet test`. Warnings are errors; formatting is not a matter of taste.
- **Nothing may enter the pipeline that costs money or needs a license** (NFR in REQUIREMENTS §5).
